using System.Diagnostics;

namespace DotarelkaMusicBot.Services;

internal static class FFmpegService
{
    public static void EnsureAvailable()
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
            Console.Error.WriteLine("FFmpeg не найден. Установите FFmpeg и убедитесь, что он доступен в PATH.");
            Environment.Exit(1);
        }
    }

    public static Process StartTranscoding(string inputUrl, int volume)
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

        var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Не удалось запустить FFmpeg.");

        return process;
    }
}
