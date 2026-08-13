using DotarelkaMusicBot.Services;

namespace DotarelkaMusicBot.Music;

internal sealed class Track
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string Url { get; init; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? StreamUrl { get; set; }

    public override string ToString() => $"{Author} — {Title}";

    public static Track FromSoundCloud(SoundCloud.SoundCloudTrack source)
    {
        return new Track
        {
            Id = source.Id,
            Title = source.Title,
            Author = source.Author,
            Duration = source.Duration,
            Url = source.Url,
        };
    }

    public static Track FromYtDlp(YtDlpInfo info)
    {
        var id = 0L;
        if (!string.IsNullOrWhiteSpace(info.Id))
            long.TryParse(info.Id, out id);

        return new Track
        {
            Id = id,
            Title = info.Title ?? string.Empty,
            Author = info.Uploader ?? string.Empty,
            Duration = info.Duration.HasValue ? TimeSpan.FromSeconds(info.Duration.Value) : TimeSpan.Zero,
            Url = info.WebpageUrl ?? info.Url ?? string.Empty,
        };
    }
}
