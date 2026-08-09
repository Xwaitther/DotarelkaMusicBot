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
}
