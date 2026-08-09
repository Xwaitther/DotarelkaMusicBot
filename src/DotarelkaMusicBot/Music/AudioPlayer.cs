using DSharpPlus.VoiceNext;
using DotarelkaMusicBot.Services;

namespace DotarelkaMusicBot.Music;

internal static class AudioPlayer
{
    public static async Task PlayStreamAsync(VoiceNextConnection connection, string streamUrl, int volume, CancellationToken cancellationToken)
    {
        using var ffmpeg = FFmpegService.StartTranscoding(streamUrl, volume);
        var errorRead = ffmpeg.StandardError.ReadToEndAsync();
        await using var outputStream = ffmpeg.StandardOutput.BaseStream;
        var transmit = connection.GetTransmitSink();

        await outputStream.CopyToAsync(transmit, 81920, cancellationToken);
        await transmit.FlushAsync(cancellationToken);
        if (!ffmpeg.HasExited)
            ffmpeg.Kill();

        await ffmpeg.WaitForExitAsync();
        var errorText = await errorRead;
        if (ffmpeg.ExitCode != 0)
            Console.Error.WriteLine($"FFmpeg завершился с кодом {ffmpeg.ExitCode}: {errorText.Trim()}");
    }
}
