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

    /// <summary>"AP_1872239909" to "1872239909"; anything else (MX_, empty) to null.</summary>
    public static string? SongIdFromLyricsId(string lyricsId)
    {
        if (string.IsNullOrEmpty(lyricsId) || !lyricsId.StartsWith("AP_", StringComparison.Ordinal))
            return null;

        var id = lyricsId[3..];
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
