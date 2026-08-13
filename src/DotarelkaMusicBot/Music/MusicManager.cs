using DSharpPlus;
using DSharpPlus.EventArgs;
using System.Text;

namespace DotarelkaMusicBot.Music;

internal sealed class MusicManager
{
    private readonly ITrackSource _source;
    private readonly DiscordClient _client;
    private readonly Dictionary<ulong, GuildPlayer> _players = new();
    private readonly SemaphoreSlim _playersLock = new(1, 1);

    public MusicManager(ITrackSource source, DiscordClient client)
    {
        _source = source;
        _client = client;
    }

    public async Task HandlePlayCommandAsync(MessageCreateEventArgs e, string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await e.Message.RespondAsync("❌ Укажите ссылку или запрос для поиска.");
            return;
        }

        if (e.Guild is null)
        {
            await e.Message.RespondAsync("❌ Команда доступна только на сервере.");
            return;
        }

        var member = await e.Guild.GetMemberAsync(e.Author.Id);
        var memberVoice = member.VoiceState?.Channel;
        if (memberVoice is null)
        {
            await e.Message.RespondAsync("❌ Вы должны находиться в голосовом канале.");
            return;
        }

        await e.Message.RespondAsync("⏳ Ищу трек...");
        using var searchCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        List<Track> tracks;
        try
        {
            var result = await _source.ResolveAsync(argument, searchCts.Token);
            tracks = result ?? new List<Track>();
        }
        catch (OperationCanceledException)
        {
            await e.Message.RespondAsync("❌ Поиск трека занял слишком много времени. Попробуйте снова.");
            return;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Search error: {ex}");
            await e.Message.RespondAsync("❌ Произошла ошибка при поиске трека.");
            return;
        }
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
        await player.EnsureVoiceAsync(memberVoice, _client);
        await player.EnqueueAsync(tracks);

        if (tracks.Count == 1)
            await e.Message.RespondAsync($"▶️ Добавлено: {tracks[0]}");
        else
            await e.Message.RespondAsync($"▶️ Добавлено {tracks.Count} треков из плейлиста.");
    }

    public async Task HandleSkipCommandAsync(MessageCreateEventArgs e)
    {
        if (e.Guild is null)
        {
            await e.Message.RespondAsync("❌ Команда доступна только на сервере.");
            return;
        }

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
        if (e.Guild is null)
        {
            await e.Message.RespondAsync("❌ Команда доступна только на сервере.");
            return;
        }

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
        if (e.Guild is null)
        {
            await e.Message.RespondAsync("❌ Команда доступна только на сервере.");
            return;
        }

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
        if (e.Guild is null)
        {
            await e.Message.RespondAsync("❌ Команда доступна только на сервере.");
            return;
        }

        var player = await GetPlayerAsync(e.Guild.Id);
        if (player is null)
        {
            await e.Message.RespondAsync("❌ Очередь пуста.");
            return;
        }

        await e.Message.RespondAsync(player.GetQueueMessage());
    }

    public async Task HandleNowPlayingCommandAsync(MessageCreateEventArgs e)
    {
        if (e.Guild is null)
        {
            await e.Message.RespondAsync("❌ Команда доступна только на сервере.");
            return;
        }

        var player = await GetPlayerAsync(e.Guild.Id);
        if (player is null || player.CurrentTrack is null)
        {
            await e.Message.RespondAsync("❌ Сейчас ничего не играет.");
            return;
        }

        await e.Message.RespondAsync($"🎵 Сейчас играет: {player.CurrentTrack}");
    }

    public async Task HandleVolumeCommandAsync(MessageCreateEventArgs e, string argument)
    {
        if (e.Guild is null)
        {
            await e.Message.RespondAsync("❌ Команда доступна только на сервере.");
            return;
        }

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
                player = new GuildPlayer(_source, _client);
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

    public async Task ShutdownAsync()
    {
        await _playersLock.WaitAsync();
        try
        {
            var leaveTasks = new List<Task>();
            foreach (var kv in _players)
            {
                try
                {
                    leaveTasks.Add(kv.Value.LeaveAsync());
                }
                catch
                {
                }
            }

            await Task.WhenAll(leaveTasks);
            _players.Clear();
        }
        finally
        {
            _playersLock.Release();
        }
    }
}
