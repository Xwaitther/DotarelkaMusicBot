namespace DotarelkaMusicBot.Music;

internal interface ITrackSource
{
    Task<List<Track>> ResolveAsync(string input, CancellationToken cancellationToken);
    Task<string?> GetStreamUrlAsync(Track track, CancellationToken cancellationToken);
}