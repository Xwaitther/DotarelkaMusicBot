using DotarelkaMusicBot.Services;
using System.Text.Json;
using DotarelkaMusicBot.Music;

namespace DotarelkaMusicBot.SoundCloud;

internal sealed class SoundCloudSource : ITrackSource
{
    private readonly YtDlpService _ytDlp;
    private const int MaxPlaylistTracks = 50;
    private readonly HttpClient _httpClient = new();
    private readonly string? _clientId;

   public SoundCloudSource(
    YtDlpService ytDlp,
    string? clientId = null)
    {
        _clientId = string.IsNullOrWhiteSpace(clientId)
            ? null
            : clientId;

         _ytDlp = ytDlp;
    }   

    public async Task<List<Track>> ResolveAsync(string input, CancellationToken cancellationToken)
    {
        if (TryParseUrl(input, out var resolvedUrl))
            return await ResolveSoundCloudUrlAsync(resolvedUrl, cancellationToken);

        var result = await SearchTrackAsync(input, cancellationToken);
        return result is null ? new List<Track>() : new List<Track> { result };
    }

    private static bool TryParseUrl(string input, out string url)
    {
        url = string.Empty;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.Contains("soundcloud.com", StringComparison.OrdinalIgnoreCase))
            return false;

        url = uri.ToString();
        return true;
    }

    private async Task<List<Track>> ResolveSoundCloudUrlAsync(string url, CancellationToken cancellationToken)
    {
        var resolveUrl = _clientId is null
            ? $"https://api-v2.soundcloud.com/resolve?url={Uri.EscapeDataString(url)}"
            : $"https://api-v2.soundcloud.com/resolve?url={Uri.EscapeDataString(url)}&client_id={_clientId}";
        try
        {
            using var doc = await GetJsonDocumentAsync(resolveUrl, cancellationToken);
            if (doc.RootElement.TryGetProperty("kind", out var kindProperty) && kindProperty.GetString() == "playlist")
            {
                return BuildPlaylistTracks(doc.RootElement);
            }

            if (doc.RootElement.TryGetProperty("kind", out kindProperty) && kindProperty.GetString() == "track")
            {
                var sc = SoundCloudTrack.FromJson(doc.RootElement);
                return new List<Track> { Track.FromSoundCloud(sc) };
            }

            throw new InvalidOperationException("Неверная ссылка SoundCloud.");
        }
        catch (HttpRequestException)
        {
            // If SoundCloud API is unauthorized (no/invalid client_id), try yt-dlp as a fallback
            if (_ytDlp is not null)
            {
                try
                {
                    var info = await _ytDlp.GetInfoAsync(url, cancellationToken);
                    return new List<Track> { Track.FromYtDlp(info) };
                }
                catch
                {
                    // ignore and rethrow original
                }
            }

            throw;
        }
    }

    private List<Track> BuildPlaylistTracks(JsonElement playlist)
    {
        if (!playlist.TryGetProperty("tracks", out var tracksElement) || tracksElement.ValueKind != JsonValueKind.Array)
            return new List<Track>();

        var tracks = new List<Track>();
        foreach (var item in tracksElement.EnumerateArray().Take(MaxPlaylistTracks))
        {
            try
            {
                var sc = SoundCloudTrack.FromJson(item);
                tracks.Add(Track.FromSoundCloud(sc));
            }
            catch
            {
            }
        }

        return tracks;
    }

    private async Task<Track?> SearchTrackAsync(string query, CancellationToken cancellationToken)
    {
        var searchUrl = _clientId is null
            ? $"https://api-v2.soundcloud.com/search/tracks?q={Uri.EscapeDataString(query)}&limit=5"
            : $"https://api-v2.soundcloud.com/search/tracks?q={Uri.EscapeDataString(query)}&client_id={_clientId}&limit=5";
        try
        {
            using var doc = await GetJsonDocumentAsync(searchUrl, cancellationToken);
            if (!doc.RootElement.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in collection.EnumerateArray())
            {
                if (item.TryGetProperty("kind", out var kind) && kind.GetString() == "track")
                {
                    var sc = SoundCloudTrack.FromJson(item);
                    return Track.FromSoundCloud(sc);
                }
            }

            return null;
        }
        catch (HttpRequestException)
        {
            // fallback to yt-dlp search (scsearch) if SoundCloud API is unavailable
            if (_ytDlp is not null)
            {
                try
                {
                    var info = await _ytDlp.GetInfoAsync($"scsearch1:{query}", cancellationToken);
                    return Track.FromYtDlp(info);
                }
                catch
                {
                    return null;
                }
            }

            throw;
        }
    }

    public async Task<string?> GetStreamUrlAsync(Track track, CancellationToken cancellationToken)
    {
        // First, try extracting a direct audio URL via yt-dlp (preferred)
        try
        {
            if (_ytDlp is not null && !string.IsNullOrWhiteSpace(track.Url))
            {
                var info = await _ytDlp.GetInfoAsync(track.Url, cancellationToken);
                if (!string.IsNullOrWhiteSpace(info?.Url))
                    return info.Url;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"yt-dlp extraction failed: {ex.Message}");
        }

        // Fallback to SoundCloud API transcodings
        var trackUrl = _clientId is null
            ? $"https://api-v2.soundcloud.com/tracks/{track.Id}"
            : $"https://api-v2.soundcloud.com/tracks/{track.Id}?client_id={_clientId}";
        using var doc = await GetJsonDocumentAsync(trackUrl, cancellationToken);
        return await ExtractStreamUrlAsync(doc.RootElement, cancellationToken);
    }

    private async Task<string?> ExtractStreamUrlAsync(JsonElement trackElement, CancellationToken cancellationToken)
    {
        if (!trackElement.TryGetProperty("media", out var media) || !media.TryGetProperty("transcodings", out var transcodings))
            return null;

        var preferred = transcodings.EnumerateArray()
            .Where(x => x.GetProperty("format").GetProperty("protocol").GetString() == "progressive")
            .FirstOrDefault();

        if (preferred.ValueKind == JsonValueKind.Undefined)
            preferred = transcodings.EnumerateArray().FirstOrDefault(x => x.GetProperty("format").GetProperty("protocol").GetString() == "hls");

        if (preferred.ValueKind == JsonValueKind.Undefined)
            preferred = transcodings.EnumerateArray().FirstOrDefault();

        if (preferred.ValueKind == JsonValueKind.Undefined)
            return null;

        var url = preferred.GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var accessUrl = _clientId is null
            ? url
            : (url.Contains("?") ? $"{url}&client_id={_clientId}" : $"{url}?client_id={_clientId}");
        try
        {
            using var doc = await GetJsonDocumentAsync(accessUrl, cancellationToken);
            if (!doc.RootElement.TryGetProperty("url", out var streamUrlProperty))
                return null;

            return streamUrlProperty.GetString();
        }
        catch (HttpRequestException)
        {
            // could not access transcoding URL (unauthorized or other); give up and let higher-level fallbacks handle it
            return null;
        }
    }

    private async Task<JsonDocument> GetJsonDocumentAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(content);
    }
}
