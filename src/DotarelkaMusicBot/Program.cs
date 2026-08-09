using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using DSharpPlus.VoiceNext.Entities;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

const string ConfigFileName = "config.json";

var config = await Config.LoadAsync(ConfigFileName);
EnsureFfmpegExists();

var discordConfig = new DiscordConfiguration
{
    Token = config.BotToken,
    TokenType = TokenType.Bot,
    Intents = DiscordIntents.Guilds | DiscordIntents.GuildMessages | DiscordIntents.GuildVoiceStates | DiscordIntents.MessageContents,
    AutoReconnect = true,
    MinimumLogLevel = Microsoft.Extensions.Logging.LogLevel.Information,
};

using var client = new DiscordClient(discordConfig);
client.UseVoiceNext();

var soundCloud = new SoundCloudService(config.SoundCloudClientId);
var musicManager = new MusicManager(soundCloud, client);

client.Ready += OnReady;
client.ClientErrored += OnClientError;
client.MessageCreated += async (s, e) => await OnMessageCreatedAsync(s, e, config.CommandPrefix, musicManager);

await client.ConnectAsync();
await Task.Delay(-1);

Task OnReady(DiscordClient sender, ReadyEventArgs e)
{
    sender.Logger.LogInformation("Bot connected as {Username}", sender.CurrentUser.Username);
    return Task.CompletedTask;
}

Task OnClientError(DiscordClient sender, ClientErrorEventArgs e)
{
    sender.Logger.LogError(e.Exception, "Discord client error");
    return Task.CompletedTask;
}

async Task OnMessageCreatedAsync(DiscordClient sender, MessageCreateEventArgs e, string prefix, MusicManager manager)
{
    if (e.Author.IsBot || string.IsNullOrWhiteSpace(e.Message.Content) || !e.Message.Content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return;

    var content = e.Message.Content[prefix.Length..].Trim();
    if (string.IsNullOrWhiteSpace(content))
        return;

    var parts = content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var command = parts[0].ToLowerInvariant();
    var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

    try
    {
        switch (command)
        {
            case "play":
                await manager.HandlePlayCommandAsync(e, argument);
                break;
            case "skip":
                await manager.HandleSkipCommandAsync(e);
                break;
            case "stop":
                await manager.HandleStopCommandAsync(e);
                break;
            case "leave":
                await manager.HandleLeaveCommandAsync(e);
                break;
            case "queue":
                await manager.HandleQueueCommandAsync(e);
                break;
            case "nowplaying":
                await manager.HandleNowPlayingCommandAsync(e);
                break;
            case "volume":
                await manager.HandleVolumeCommandAsync(e, argument);
                break;
            case "pause":
                await manager.HandlePauseCommandAsync(e);
                break;
            case "resume":
                await manager.HandleResumeCommandAsync(e);
                break;
            case "help":
                await manager.HandleHelpCommandAsync(e, prefix);
                break;
            default:
                break;
        }
    }
    catch (Exception ex)
    {
        sender.Logger.LogError(ex, "Unhandled command exception");
        await e.Message.RespondAsync("❌ Произошла ошибка при выполнении команды.");
    }
}

static void EnsureFfmpegExists()
{
    try
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                ArgumentList = { "-version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        process.WaitForExit(2000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException();
    }
    catch
    {
        Console.WriteLine("FFmpeg не найден. Установите FFmpeg и убедитесь, что он доступен в PATH.");
        Environment.Exit(1);
    }
}

public sealed class Config
{
    public string BotToken { get; set; } = string.Empty;
    public string SoundCloudClientId { get; set; } = string.Empty;
    public string CommandPrefix { get; set; } = "!";

    public static async Task<Config> LoadAsync(string fileName)
    {
        if (!File.Exists(fileName))
        {
            Console.WriteLine($"Конфигурационный файл '{fileName}' не найден.");
            Environment.Exit(1);
        }

        var content = await File.ReadAllTextAsync(fileName);
        var config = JsonSerializer.Deserialize<Config>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (config is null || string.IsNullOrWhiteSpace(config.BotToken) || string.IsNullOrWhiteSpace(config.SoundCloudClientId) || string.IsNullOrWhiteSpace(config.CommandPrefix))
        {
            Console.WriteLine($"Конфигурация '{fileName}' некорректна. Проверьте BotToken, SoundCloudClientId и CommandPrefix.");
            Environment.Exit(1);
        }

        return config;
    }
}

public sealed class Track
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string Url { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public string? StreamUrl { get; set; }

    public override string ToString() => $"{Author} — {Title}";
}

public sealed class MusicManager
{
    private readonly SoundCloudService _soundCloud;
    private readonly DiscordClient _client;
    private readonly Dictionary<ulong, GuildPlayer> _players = new();
    private readonly SemaphoreSlim _playersLock = new(1, 1);

    public MusicManager(SoundCloudService soundCloud, DiscordClient client)
    {
        _soundCloud = soundCloud;
        _client = client;
    }

    public async Task HandlePlayCommandAsync(MessageCreateEventArgs e, string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await e.Message.RespondAsync("❌ Укажите ссылку или запрос для поиска.");
            return;
        }

        var memberVoice = e.Member?.VoiceState?.Channel;
        if (memberVoice is null)
        {
            await e.Message.RespondAsync("❌ Вы должны находиться в голосовом канале.");
            return;
        }

        await e.Message.RespondAsync("⏳ Ищу трек...");
        var tracks = await _soundCloud.ResolveSoundCloudQueryAsync(argument, e.CancellationToken);
        if (tracks.Count == 0)
        {
            await e.Message.RespondAsync("❌ Не удалось найти трек.");
            return;
        }

        foreach (var track in tracks)
        {
            track.RequestedBy = e.Author.Username;
        }

        var player = await GetOrCreatePlayerAsync(e.Guild.Id);
        await player.EnsureVoiceAsync(memberVoice, _client, e.Channel);
        await player.EnqueueAsync(tracks);

        if (tracks.Count == 1)
            await e.Message.RespondAsync($"▶️ Добавлено: {tracks[0]}");
        else
            await e.Message.RespondAsync($"▶️ Добавлено {tracks.Count} треков из плейлиста.");
    }

    public async Task HandleSkipCommandAsync(MessageCreateEventArgs e)
    {
        var player = await GetPlayerAsync(e.Guild.Id);
        if (player is null)
        {
            await e.Message.RespondAsync("❌ Нет активного воспроизведения.");
            return;
        }

        if (await player.SkipAsync())
            await e.Message.RespondAsync("⏭️ Трек пропущен.");
        else
            await e.Message.RespondAsync("❌ Нет текущего трека для пропуска.");
    }

    public async Task HandleStopCommandAsync(MessageCreateEventArgs e)
    {
        var player = await GetPlayerAsync(e.Guild.Id);
        if (player is null)
        {
            await e.Message.RespondAsync("❌ Нет активного воспроизведения.");
            return;
        }

        await player.StopAsync();
        await e.Message.RespondAsync("⏹️ Воспроизведение остановлено.");
    }

    public async Task HandleLeaveCommandAsync(MessageCreateEventArgs e)
    {
        var player = await GetPlayerAsync(e.Guild.Id);
        if (player is null)
        {
            await e.Message.RespondAsync("❌ Бот не подключен к голосовому каналу.");
            return;
        }

        await player.LeaveAsync();
        await e.Message.RespondAsync("⏹️ Бот отключился от голосового канала.");
    }

    public async Task HandleQueueCommandAsync(MessageCreateEventArgs e)
    {
        var player = await GetPlayerAsync(e.Guild.Id);
        if (player is null)
        {
            await e.Message.RespondAsync("❌ Очередь пуста.");
            return;
        }

        var response = player.GetQueueMessage();
        await e.Message.RespondAsync(response);
    }

    public async Task HandleNowPlayingCommandAsync(MessageCreateEventArgs e)
    {
        var player = await GetPlayerAsync(e.Guild.Id);
        if (player is null)
        {
            await e.Message.RespondAsync("❌ Сейчас ничего не играет.");
            return;
        }

        var current = player.CurrentTrack;
        if (current is null)
            await e.Message.RespondAsync("❌ Сейчас ничего не играет.");
        else
            await e.Message.RespondAsync($"🎵 Сейчас играет: {current}");
    }

    public async Task HandleVolumeCommandAsync(MessageCreateEventArgs e, string argument)
    {
        if (!int.TryParse(argument, out var value) || value < 0 || value > 100)
        {
            await e.Message.RespondAsync("❌ Укажите громкость от 0 до 100.");
            return;
        }

        var player = await GetOrCreatePlayerAsync(e.Guild.Id);
        player.SetVolume(value);
        await e.Message.RespondAsync($"🔊 Громкость установлена: {value}%.");
    }

    public Task HandlePauseCommandAsync(MessageCreateEventArgs e)
    {
        return e.Message.RespondAsync("❌ Пауза не поддерживается для потокового воспроизведения.");
    }

    public Task HandleResumeCommandAsync(MessageCreateEventArgs e)
    {
        return e.Message.RespondAsync("❌ Продолжение не поддерживается для потокового воспроизведения.");
    }

    public Task HandleHelpCommandAsync(MessageCreateEventArgs e, string prefix)
    {
        var help = new StringBuilder();
        help.AppendLine("Команды:");
        help.AppendLine($"{prefix}play <url|запрос> — добавить трек или плейлист");
        help.AppendLine($"{prefix}skip — пропустить текущий трек");
        help.AppendLine($"{prefix}stop — остановить и очистить очередь");
        help.AppendLine($"{prefix}queue — показать очередь");
        help.AppendLine($"{prefix}nowplaying — показать текущий трек");
        help.AppendLine($"{prefix}volume <0-100> — изменить громкость");
        help.AppendLine($"{prefix}leave — отключиться от голоса");
        help.AppendLine($"{prefix}help — показать команды");
        return e.Message.RespondAsync(help.ToString());
    }

    private async Task<GuildPlayer> GetOrCreatePlayerAsync(ulong guildId)
    {
        await _playersLock.WaitAsync();
        try
        {
            if (!_players.TryGetValue(guildId, out var player))
            {
                player = new GuildPlayer(_soundCloud, _client);
                _players[guildId] = player;
            }

            return player;
        }
        finally
        {
            _playersLock.Release();
        }
    }

    private async Task<GuildPlayer?> GetPlayerAsync(ulong guildId)
    {
        await _playersLock.WaitAsync();
        try
        {
            _players.TryGetValue(guildId, out var player);
            return player;
        }
        finally
        {
            _playersLock.Release();
        }
    }
}

public sealed class GuildPlayer
{
    private readonly SoundCloudService _soundCloud;
    private readonly DiscordClient _client;
    private readonly Queue<Track> _queue = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _playbackCts;
    private CancellationTokenSource? _trackCts;
    private Task? _playbackTask;
    private VoiceNextConnection? _voiceConnection;

    public Track? CurrentTrack { get; private set; }
    public int Volume { get; private set; } = 100;

    public GuildPlayer(SoundCloudService soundCloud, DiscordClient client)
    {
        _soundCloud = soundCloud;
        _client = client;
    }

    public async Task EnsureVoiceAsync(DiscordChannel voiceChannel, DiscordClient client, DiscordChannel responseChannel)
    {
        await _lock.WaitAsync();
        try
        {
            var voiceNext = client.GetVoiceNext();
            if (voiceNext is null)
                throw new InvalidOperationException("VoiceNext не инициализирован.");

            var connection = voiceNext.GetConnection(voiceChannel.Guild);
            if (connection is null || connection.Channel.Id != voiceChannel.Id)
            {
                if (connection is not null)
                {
                    try { await connection.DisconnectAsync(); } catch { }
                }

                connection = await voiceNext.ConnectAsync(voiceChannel);
            }

            _voiceConnection = connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnqueueAsync(IEnumerable<Track> tracks)
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var track in tracks)
            {
                _queue.Enqueue(track);
            }

            if (_playbackTask is null || _playbackTask.IsCompleted)
            {
                _playbackCts = new CancellationTokenSource();
                _playbackTask = Task.Run(() => PlaybackLoopAsync(_playbackCts.Token));
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> SkipAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (CurrentTrack is null)
                return false;

            _trackCts?.Cancel();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _queue.Clear();
            _trackCts?.Cancel();
            _playbackCts?.Cancel();
        }
        finally
        {
            _lock.Release();
        }

        if (_playbackTask is not null)
            await _playbackTask.ConfigureAwait(false);
    }

    public async Task LeaveAsync()
    {
        await StopAsync();

        await _lock.WaitAsync();
        try
        {
            if (_voiceConnection is not null)
            {
                try { await _voiceConnection.DisconnectAsync(); } catch { }
                _voiceConnection = null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void SetVolume(int value)
    {
        Volume = Math.Clamp(value, 0, 100);
    }

    public string GetQueueMessage()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CurrentTrack is null ? "Сейчас ничего не играет." : $"🎵 Сейчас играет: {CurrentTrack}");

        if (_queue.Count == 0)
        {
            builder.AppendLine("Очередь пуста.");
            return builder.ToString();
        }

        builder.AppendLine("Очередь:");
        var index = 1;
        foreach (var track in _queue.Take(10))
        {
            builder.AppendLine($"{index++}. {track}");
        }

        if (_queue.Count > 10)
            builder.AppendLine($"И еще {_queue.Count - 10} треков...");

        return builder.ToString();
    }

    private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Track? track;
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_queue.Count == 0)
                {
                    CurrentTrack = null;
                    return;
                }

                track = _queue.Dequeue();
                CurrentTrack = track;
            }
            finally
            {
                _lock.Release();
            }

            try
            {
                if (_voiceConnection is null)
                    break;

                await PlayTrackAsync(track, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // skip or stop requested
            }
            catch (Exception ex)
            {
                _client.Logger.LogError(ex, "Ошибка воспроизведения трека {Track}", track);
            }
            finally
            {
                await _lock.WaitAsync();
                try
                {
                    if (CurrentTrack == track)
                        CurrentTrack = null;
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
    }

    private async Task PlayTrackAsync(Track track, CancellationToken cancellationToken)
    {
        var voiceConnection = _voiceConnection;
        if (voiceConnection is null)
            throw new InvalidOperationException("Голосовое соединение не установлено.");

        var streamUrl = track.StreamUrl;
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            streamUrl = await _soundCloud.GetTrackStreamUrlAsync(track, cancellationToken);
            if (string.IsNullOrWhiteSpace(streamUrl))
                throw new InvalidOperationException("Не удалось получить audio stream.");

            track.StreamUrl = streamUrl;
        }

        _client.Logger.LogInformation("Начинаю воспроизведение: {Track}", track);

        using var trackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _trackCts = trackCts;

        using var ffmpeg = StartFfmpeg(streamUrl, Volume);
        var errorRead = ffmpeg.StandardError.ReadToEndAsync();
        await using var outputStream = ffmpeg.StandardOutput.BaseStream;
        var transmit = voiceConnection.GetTransmitSink();

        try
        {
            await outputStream.CopyToAsync(transmit, 81920, trackCts.Token);
            await transmit.FlushAsync(trackCts.Token);
        }
        finally
        {
            if (!ffmpeg.HasExited)
            {
                try { ffmpeg.Kill(); } catch { }
            }

            await ffmpeg.WaitForExitAsync();
            var errorText = await errorRead;
            if (ffmpeg.ExitCode != 0)
                _client.Logger.LogError("FFmpeg завершился с кодом {Code}: {Error}", ffmpeg.ExitCode, errorText.Trim());
        }

        if (trackCts.IsCancellationRequested)
            throw new OperationCanceledException(trackCts.Token);

        _client.Logger.LogInformation("Трек завершен: {Track}", track);
    }

    private static Process StartFfmpeg(string inputUrl, int volume)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputUrl);
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add("2");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add("48000");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("s16le");
        psi.ArgumentList.Add("-af");
        psi.ArgumentList.Add($"volume={Math.Clamp(volume, 0, 100) / 100.0:0.00}");
        psi.ArgumentList.Add("pipe:1");

        var ffmpeg = Process.Start(psi);
        if (ffmpeg is null)
            throw new InvalidOperationException("Не удалось запустить FFmpeg.");

        return ffmpeg;
    }
}

public sealed class SoundCloudService
{
    private const int MaxPlaylistTracks = 50;
    private readonly HttpClient _httpClient = new();
    private readonly string _clientId;

    public SoundCloudService(string clientId)
    {
        _clientId = clientId;
    }

    public async Task<List<Track>> ResolveSoundCloudQueryAsync(string input, CancellationToken cancellationToken)
    {
        if (TryParseUrl(input, out var resolvedUrl))
            return await ResolveSoundCloudUrlAsync(resolvedUrl, cancellationToken);

        var result = await SearchTrackAsync(input, cancellationToken);
        return result is null ? new List<Track>() : new List<Track> { result };
    }

    private static bool TryParseUrl(string input, out string url)
    {
        url = string.Empty;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.Contains("soundcloud.com", StringComparison.OrdinalIgnoreCase))
            return false;

        url = uri.ToString();
        return true;
    }

    private async Task<List<Track>> ResolveSoundCloudUrlAsync(string url, CancellationToken cancellationToken)
    {
        var resolveUrl = $"https://api-v2.soundcloud.com/resolve?url={Uri.EscapeDataString(url)}&client_id={_clientId}";
        using var doc = await GetJsonDocumentAsync(resolveUrl, cancellationToken);
        if (doc.RootElement.TryGetProperty("kind", out var kindProperty) && kindProperty.GetString() == "playlist")
        {
            return BuildPlaylistTracks(doc.RootElement);
        }

        if (doc.RootElement.TryGetProperty("kind", out kindProperty) && kindProperty.GetString() == "track")
        {
            return new List<Track> { BuildTrack(doc.RootElement) };
        }

        throw new InvalidOperationException("Неверная ссылка SoundCloud.");
    }

    private List<Track> BuildPlaylistTracks(JsonElement playlist)
    {
        if (!playlist.TryGetProperty("tracks", out var tracksElement) || tracksElement.ValueKind != JsonValueKind.Array)
            return new List<Track>();

        var tracks = new List<Track>();
        foreach (var item in tracksElement.EnumerateArray().Take(MaxPlaylistTracks))
        {
            try
            {
                tracks.Add(BuildTrack(item));
            }
            catch
            {
            }
        }

        return tracks;
    }

    private Track BuildTrack(JsonElement trackElement)
    {
        var id = trackElement.GetProperty("id").GetInt64();
        var title = trackElement.GetProperty("title").GetString() ?? "Unknown";
        var author = trackElement.GetProperty("user").GetProperty("username").GetString() ?? "Unknown";
        var durationMs = trackElement.GetProperty("duration").GetInt32();
        var permalinkUrl = trackElement.GetProperty("permalink_url").GetString() ?? string.Empty;

        return new Track
        {
            Id = id,
            Title = title,
            Author = author,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Url = permalinkUrl,
        };
    }

    private async Task<Track?> SearchTrackAsync(string query, CancellationToken cancellationToken)
    {
        var searchUrl = $"https://api-v2.soundcloud.com/search/tracks?q={Uri.EscapeDataString(query)}&client_id={_clientId}&limit=5";
        using var doc = await GetJsonDocumentAsync(searchUrl, cancellationToken);
        if (!doc.RootElement.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in collection.EnumerateArray())
        {
            if (item.TryGetProperty("kind", out var kind) && kind.GetString() == "track")
                return BuildTrack(item);
        }

        return null;
    }

    public async Task<string?> GetTrackStreamUrlAsync(Track track, CancellationToken cancellationToken)
    {
        var trackUrl = $"https://api-v2.soundcloud.com/tracks/{track.Id}?client_id={_clientId}";
        using var doc = await GetJsonDocumentAsync(trackUrl, cancellationToken);
        return await ExtractStreamUrlAsync(doc.RootElement, cancellationToken);
    }

    private async Task<string?> ExtractStreamUrlAsync(JsonElement trackElement, CancellationToken cancellationToken)
    {
        if (!trackElement.TryGetProperty("media", out var media) || !media.TryGetProperty("transcodings", out var transcodings))
            return null;

        var preferred = transcodings.EnumerateArray()
            .Where(x => x.GetProperty("format").GetProperty("protocol").GetString() == "progressive")
            .FirstOrDefault();

        if (preferred.ValueKind == JsonValueKind.Undefined)
            preferred = transcodings.EnumerateArray().FirstOrDefault(x => x.GetProperty("format").GetProperty("protocol").GetString() == "hls");

        if (preferred.ValueKind == JsonValueKind.Undefined)
            preferred = transcodings.EnumerateArray().FirstOrDefault();

        if (preferred.ValueKind == JsonValueKind.Undefined)
            return null;

        var url = preferred.GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var accessUrl = url.Contains("?") ? $"{url}&client_id={_clientId}" : $"{url}?client_id={_clientId}";
        using var doc = await GetJsonDocumentAsync(accessUrl, cancellationToken);
        if (!doc.RootElement.TryGetProperty("url", out var streamUrlProperty))
            return null;

        return streamUrlProperty.GetString();
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(content);
    }
}
