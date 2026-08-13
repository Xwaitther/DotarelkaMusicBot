using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using System.Text;
using DotarelkaMusicBot.Services;

namespace DotarelkaMusicBot.Music;

internal sealed class GuildPlayer
{
    private readonly ITrackSource _source;
    private readonly DiscordClient _client;
    private readonly Queue<Track> _queue = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _playbackCts;
    private CancellationTokenSource? _trackCts;
    private Task? _playbackTask;
    private VoiceNextConnection? _voiceConnection;

    public Track? CurrentTrack { get; private set; }
    public int Volume { get; private set; } = 100;

    public GuildPlayer(ITrackSource source, DiscordClient client)
    {
        _source = source;
        _client = client;
    }

    public async Task EnsureVoiceAsync(DiscordChannel voiceChannel, DiscordClient client)
    {
        await _lock.WaitAsync();
        try
        {
            var voiceNext = client.GetVoiceNext();
            if (voiceNext is null)
                throw new InvalidOperationException("VoiceNext не инициализирован.");

            var connection = voiceNext.GetConnection(voiceChannel.Guild);
            if (connection is null || connection.TargetChannel.Id != voiceChannel.Id)
            {
                if (connection is not null)
                {
                    try { connection.Disconnect(); } catch { }
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
                _queue.Enqueue(track);

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
                try { _voiceConnection.Disconnect(); } catch { }
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
            builder.AppendLine($"{index++}. {track}");

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
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Ошибка воспроизведения трека {track}: {ex}");
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
            streamUrl = await _source.GetStreamUrlAsync(track, cancellationToken);
            if (string.IsNullOrWhiteSpace(streamUrl))
                throw new InvalidOperationException("Не удалось получить audio stream.");

            track.StreamUrl = streamUrl;
        }

        Console.WriteLine($"Начинаю воспроизведение: {track}");

        using var trackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _trackCts = trackCts;

        using var ffmpeg = FFmpegService.StartTranscoding(streamUrl, Volume);
        var errorRead = ffmpeg.StandardError.ReadToEndAsync();
        await using var outputStream = ffmpeg.StandardOutput.BaseStream;
        var transmit = voiceConnection.GetTransmitSink();

            try
            {
                var buffer = new byte[81920];
                long total = 0;
                var lastLog = DateTime.UtcNow;
                while (true)
                {
                    var read = await outputStream.ReadAsync(buffer.AsMemory(0, buffer.Length), trackCts.Token);
                    if (read == 0)
                        break;

                    await transmit.WriteAsync(buffer.AsMemory(0, read), trackCts.Token);
                    total += read;

                    if ((DateTime.UtcNow - lastLog) > TimeSpan.FromSeconds(2))
                    {
                        Console.WriteLine($"Streaming: sent {total} bytes for {track}");
                        lastLog = DateTime.UtcNow;
                    }
                }

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
                if (!string.IsNullOrWhiteSpace(errorText))
                    Console.Error.WriteLine($"FFmpeg stderr: {errorText.Trim()} (ExitCode={ffmpeg.ExitCode})");
                if (ffmpeg.ExitCode != 0)
                    Console.Error.WriteLine($"FFmpeg завершился с кодом {ffmpeg.ExitCode}");
            }

        if (trackCts.IsCancellationRequested)
            throw new OperationCanceledException(trackCts.Token);

        Console.WriteLine($"Трек завершен: {track}");
    }
}
