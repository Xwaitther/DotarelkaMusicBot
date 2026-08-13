using System.Text.Json;
using DotarelkaMusicBot.Utils;

namespace DotarelkaMusicBot.Configuration;

internal sealed class BotConfig
{
    public string BotToken { get; set; } = string.Empty;
    public string SoundCloudClientId { get; set; } = string.Empty;
    public string CommandPrefix { get; set; } = "!";
    public string YtDlpExecutable { get; set; } = "yt-dlp";

    public static async Task<BotConfig> LoadAsync(string fileName)
    {
        if (!File.Exists(fileName))
        {
            Console.Error.WriteLine($"Конфигурационный файл '{fileName}' не найден.");
            Environment.Exit(1);
        }

        var content = await File.ReadAllTextAsync(fileName);
        var config = JsonSerializer.Deserialize<BotConfig>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Guard.NotNull(config, $"Конфигурация '{fileName}' некорректна.");
        Guard.NotNullOrWhitespace(config.BotToken, "BotToken не может быть пустым.");
        Guard.NotNullOrWhitespace(config.CommandPrefix, "CommandPrefix не может быть пустым.");
        Guard.NotNullOrWhitespace(config.YtDlpExecutable, "YtDlpExecutable не может быть пустым.");

        return config;
    }
}
