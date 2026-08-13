using System.Diagnostics;
using System.Text.Json;

namespace DotarelkaMusicBot.Services;

internal sealed class YtDlpService
{
    private readonly string _executable;

    public YtDlpService(string executable = "yt-dlp")
    {
        _executable = executable;
    }

    public async Task<YtDlpInfo> GetInfoAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-J");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("bestaudio");
        startInfo.ArgumentList.Add(url);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"yt-dlp завершился с ошибкой: {error}");
        }

        var info = JsonSerializer.Deserialize<YtDlpInfo>(
            output,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (info is null)
        {
            throw new InvalidOperationException(
                "yt-dlp вернул некорректный JSON.");
        }

        return info;
    }
}