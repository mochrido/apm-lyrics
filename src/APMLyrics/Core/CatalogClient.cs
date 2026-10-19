using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APMLyrics.Core;

/// <summary>Seam so the resolver can be tested without touching the real cache directory.</summary>
public interface IAppleLyricsCache
{
    IReadOnlyList<Candidate> Scan();
}

/// <summary>Seam so the resolver can be tested without a network.</summary>
public interface ICatalogClient
{
    Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct);

    /// <summary>Finds a track by name through the iTunes search API.</summary>
    Task<TrackInfo?> SearchAsync(string title, string artist, CancellationToken ct);
}

/// <summary>
/// Resolves an AP_ lyrics id to a real track through the public iTunes lookup.
/// Every result is cached to disk, so after the first warm-up the app works offline.
/// A network failure is never fatal: it returns null and the caller degrades.
/// </summary>
public sealed class CatalogClient : ICatalogClient
{
    private static readonly HttpClient Shared = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly HttpMessageHandler? _handler;
    private readonly string _cachePath;
    private readonly Dictionary<string, TrackInfo?> _memory = new();
    private readonly object _gate = new();

    public CatalogClient(HttpMessageHandler? handler = null, string? cachePath = null)
    {
        _handler = handler;
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "APMLyrics", "catalog-cache.json");
        LoadDiskCache();
    }

    /// <summary>"AP_1872239909"/"MO_1481804592" to the numeric id; anything else (MX_, empty) to null.
    /// MO_ (music video) ids are the same iTunes track ids and resolve through the
    /// same lookup, so they must not be skipped.</summary>
    public static string? SongIdFromLyricsId(string lyricsId)
    {
        if (string.IsNullOrEmpty(lyricsId))
            return null;

        int? prefixLength = lyricsId.StartsWith("AP_", StringComparison.Ordinal) ? 3
            : lyricsId.StartsWith("MO_", StringComparison.Ordinal) ? 3
            : null;
        if (prefixLength is null)
            return null;

        var id = lyricsId[prefixLength.Value..];

        // Some ids carry a trailing language tag: "AP_6801077492-id",
        // "AP_1869189802-en". The numeric part before the dash is the same
        // iTunes track id, so the tag is stripped and the lookup proceeds.
        // Exactly two ASCII letters after the dash — anything else (e.g. an
        // MX_-style "46242766-48516696" pair, already excluded by prefix)
        // is not a language tag and must not be mangled.
        var dash = id.IndexOf('-');
        if (dash >= 0)
        {
            var suffix = id[(dash + 1)..];
            if (suffix.Length == 2 && suffix.All(char.IsAsciiLetter))
                id = id[..dash];
        }

        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }

    public async Task<TrackInfo?> LookupAsync(string lyricsId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_memory.TryGetValue(lyricsId, out var cached))
                return cached;
        }

        var songId = SongIdFromLyricsId(lyricsId);
        if (songId is null)
            return null;

        TrackInfo? info = null;
        var completed = false;
        try
        {
            var url = $"https://itunes.apple.com/lookup?id={songId}";
            if (_handler is null)
            {
                // Shared is process-wide: it must NOT be disposed here, or every
                // lookup after the first one would fail with ObjectDisposedException
                // and the app would silently serve nothing but warm entries.
                info = Parse(await Shared.GetStringAsync(url, ct));
            }
            else
            {
                // disposeHandler: false: the handler is owned by the caller
                // (a test seam); disposing it here would kill every later
                // lookup that reuses the same handler.
                using var client = new HttpClient(_handler, disposeHandler: false);
                info = Parse(await client.GetStringAsync(url, ct));
            }
            completed = true; // set on the success path only, never in the catch
        }
        catch (Exception ex)
        {
            // Offline or malformed: degrade, do not throw. This failure is NOT
            // remembered below: a cached null would persist on disk and be
            // restored on every later start, so the track could never resolve
            // again even after the network comes back. It must be retried.
            Debug.WriteLine($"Catalog lookup failed for {lyricsId}: {ex.Message}");
            info = null;
        }

        // Only a COMPLETED lookup is remembered (success, or a clean negative
        // that found nothing). A completed negative must stay cached: an AP_ id
        // that resolves to an empty result set would otherwise be retried over
        // HTTP on every track change, which is worse for the offline path, not
        // better.
        if (completed)
        {
            lock (_gate)
            {
                _memory[lyricsId] = info;
            }
            SaveDiskCache();
        }
        return info;
    }

    /// <summary>
    /// Finds a track by title and artist through the iTunes search API. Used as
    /// a fallback for lyric files whose id cannot be looked up (MX_ and other
    /// unresolvable forms): the playing track's own name identifies the song.
    /// A result is adopted only when both names match, normalized, so the
    /// search can never invent a match.
    /// </summary>
    public async Task<TrackInfo?> SearchAsync(string title, string artist, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(artist))
            return null;

        var key = $"search:{title}|{artist}";
        lock (_gate)
        {
            if (_memory.TryGetValue(key, out var cached))
                return cached;
        }

        TrackInfo? info = null;
        var completed = false;
        try
        {
            var term = Uri.EscapeDataString($"{title} {artist}");
            var url = $"https://itunes.apple.com/search?term={term}&entity=song&limit=5";
            string json;
            if (_handler is null)
            {
                json = await Shared.GetStringAsync(url, ct);
            }
            else
            {
                using var client = new HttpClient(_handler, disposeHandler: false);
                json = await client.GetStringAsync(url, ct);
            }

            // A search response has the same shape as a lookup response; the
            // difference is that it may hold several results, and only one whose
            // names match the query may be adopted.
            if (JsonSerializer.Deserialize<LookupResponse>(json)?.Results is { } results)
            {
                foreach (var r in results)
                {
                    if (r.TrackName is null || r.ArtistName is null)
                        continue;
                    if (TrackIdentity.Normalize(r.TrackName) != TrackIdentity.Normalize(title))
                        continue;
                    if (TrackIdentity.Normalize(r.ArtistName) != TrackIdentity.Normalize(artist))
                        continue;
                    var seconds = (r.TrackTimeMillis ?? 0) / 1000.0;
                    info = new TrackInfo(r.TrackName, r.ArtistName, TimeSpan.FromSeconds(seconds));
                    break;
                }
            }
            completed = true;
        }
        catch (Exception ex)
        {
            // Offline or malformed: degrade, do not throw, and do not cache the
            // failure (same reasoning as LookupAsync).
            Debug.WriteLine($"Catalog search failed for '{title}': {ex.Message}");
            info = null;
        }

        if (completed)
        {
            lock (_gate)
            {
                _memory[key] = info;
            }
            SaveDiskCache();
        }
        return info;
    }

    internal static TrackInfo? Parse(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<LookupResponse>(json);
            var track = doc?.Results?.FirstOrDefault();
            if (track?.TrackName is null || track.ArtistName is null)
                return null;

            var seconds = (track.TrackTimeMillis ?? 0) / 1000.0;
            return new TrackInfo(track.TrackName, track.ArtistName, TimeSpan.FromSeconds(seconds));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void LoadDiskCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return;

            var json = File.ReadAllText(_cachePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedTrack>>(json);
            if (loaded is null)
                return;

            foreach (var (key, value) in loaded)
            {
                _memory[key] = value.Title is null
                    ? null
                    : new TrackInfo(value.Title, value.Artist ?? string.Empty, TimeSpan.FromSeconds(value.Seconds));
            }
        }
        catch (Exception)
        {
            // A corrupt cache is not worth failing over; it refills on next lookup.
        }
    }

    private void SaveDiskCache()
    {
        try
        {
            Dictionary<string, CachedTrack> snapshot;
            lock (_gate)
            {
                snapshot = _memory.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value is null
                        ? new CachedTrack(null, null, 0)
                        : new CachedTrack(kv.Value.Title, kv.Value.Artist, kv.Value.Duration.TotalSeconds));
            }

            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }

    private sealed record CachedTrack(string? Title, string? Artist, double Seconds);

    private sealed record LookupResponse(
        [property: JsonPropertyName("resultCount")] int ResultCount,
        [property: JsonPropertyName("results")] List<LookupTrack>? Results);

    private sealed record LookupTrack(
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("trackTimeMillis")] long? TrackTimeMillis);
}
