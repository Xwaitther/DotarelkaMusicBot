using System.Text.Json;

namespace DotarelkaMusicBot.SoundCloud;

internal sealed class SoundCloudTrack
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string Url { get; init; } = string.Empty;

    public static SoundCloudTrack FromJson(JsonElement trackElement)
    {
        var id = trackElement.GetProperty("id").GetInt64();
        var title = trackElement.GetProperty("title").GetString() ?? "Unknown";
        var author = trackElement.GetProperty("user").GetProperty("username").GetString() ?? "Unknown";
        var durationMs = trackElement.GetProperty("duration").GetInt32();
        var permalinkUrl = trackElement.GetProperty("permalink_url").GetString() ?? string.Empty;

        return new SoundCloudTrack
        {
            Id = id,
            Title = title,
            Author = author,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Url = permalinkUrl,
        };
    }
}
