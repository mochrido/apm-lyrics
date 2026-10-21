using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APMLyrics.Core;

/// <summary>
/// Stores validated fallback lyrics so a song resolved once still shows lyrics
/// offline. One file per recording, named by a hash of the recording identity.
/// <para>
/// Only accepted synchronized documents are stored: a miss or a network failure
/// is never written, because a cached negative would make the song permanently
/// lyric-less. Every read revalidates identity and timestamps, so a damaged or
/// hand-edited file is ignored rather than trusted.
/// </para>
/// </summary>
public sealed class FallbackLyricsCache
{
    private const int SchemaVersion = 1;
    private static readonly TimeSpan EndTolerance = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directory;

    public FallbackLyricsCache(string directory) => _directory = directory;

    /// <summary>Default location, alongside the rest of the app's user data.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "APMLyrics", "lyrics-cache");

    public LyricsDoc? Read(Track track)
    {
        if (!TryKey(track, out var key))
            return null;

        var path = Path.Combine(_directory, key + ".json");
        try
        {
            if (!File.Exists(path))
                return null;

            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path), JsonOptions);
            if (entry is null || entry.Schema != SchemaVersion || entry.Lines is null)
                return null;
            if (!string.Equals(entry.Timing, nameof(LyricsTiming.Line), StringComparison.Ordinal))
                return null;

            // Identity is re-checked, so a renamed or hand-copied file cannot be
            // served for a different recording.
            if (!string.Equals(entry.Title, track.Title, StringComparison.Ordinal)
                || !string.Equals(entry.Artist, track.Artist, StringComparison.Ordinal)
                || !SameSecond(entry.DurationSeconds, track.Duration))
                return null;

            var limit = (track.Duration + EndTolerance).TotalSeconds;
            var lines = new List<LyricLine>(entry.Lines.Count);
            foreach (var row in entry.Lines)
            {
                if (row is null || string.IsNullOrWhiteSpace(row.Text))
                    return null;
                if (!double.IsFinite(row.BeginSeconds) || !double.IsFinite(row.EndSeconds))
                    return null;
                if (row.BeginSeconds < 0 || row.EndSeconds < 0)
                    return null;
                if (row.BeginSeconds > limit || row.EndSeconds > limit)
                    return null;
                if (row.EndSeconds < row.BeginSeconds)
                    return null;

                lines.Add(new LyricLine(
                    TimeSpan.FromSeconds(row.BeginSeconds),
                    TimeSpan.FromSeconds(row.EndSeconds),
                    row.Text,
                    row.Part));
            }

            if (lines.Count == 0)
                return null;

            return new LyricsDoc(entry.LyricsId ?? string.Empty, entry.Lang, lines)
            {
                Timing = LyricsTiming.Line,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public void Write(Track track, LrclibCandidate candidate, LyricsDoc doc)
    {
        // Only a genuinely timed, validated document is ever persisted. Writing
        // an unsynced or malformed one would let a bad payload outlive the
        // process that rejected it.
        if (!doc.IsSynced || doc.Lines.Count == 0 || !IsWellFormed(track, doc))
            return;
        if (!TryKey(track, out var key))
            return;

        var entry = new CacheEntry
        {
            Schema = SchemaVersion,
            Title = track.Title,
            Artist = track.Artist,
            DurationSeconds = track.Duration.TotalSeconds,
            LyricsId = doc.LyricsId,
            Lang = doc.Lang,
            Timing = nameof(LyricsTiming.Line),
            Source = "LRCLIB",
            ProviderId = candidate.Id,
            Lines = doc.Lines
                .Select(l => (CacheLine?)new CacheLine
                {
                    BeginSeconds = l.Begin.TotalSeconds,
                    EndSeconds = l.End.TotalSeconds,
                    Text = l.Text,
                    Part = l.Part,
                })
                .ToList(),
        };

        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, key + ".json");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            // Write-then-move: a crash mid-write leaves the previous entry
            // intact instead of a half-written file.
            File.WriteAllText(temp, JsonSerializer.Serialize(entry, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A cache miss is always safe; a failure to persist never breaks lyric
            // display for the current song.
        }
    }

    /// <summary>
    /// The recording's identity: exact title, exact artist, and duration to the
    /// second. Deliberately not fuzzy: a different take or a different edit has
    /// different timings, and serving one for the other is worse than a miss.
    /// </summary>
    private static bool TryKey(Track track, out string key)
    {
        key = string.Empty;
        if (track.Duration <= TimeSpan.Zero
            || string.IsNullOrWhiteSpace(track.Title)
            || string.IsNullOrWhiteSpace(track.Artist))
            return false;

        var identity = string.Join(
            "\u241F",
            track.Title.Trim(),
            track.Artist.Trim(),
            Math.Round(track.Duration.TotalSeconds, 0).ToString("F0", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        key = Convert.ToHexString(hash)[..32].ToLowerInvariant();
        return true;
    }

    private static bool IsWellFormed(Track track, LyricsDoc doc)
    {
        var limit = (track.Duration + EndTolerance).TotalSeconds;
        foreach (var line in doc.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
                return false;
            if (line.Begin < TimeSpan.Zero || line.End < line.Begin)
                return false;
            if (line.End.TotalSeconds > limit)
                return false;
        }
        return true;
    }

    private static bool SameSecond(double cachedSeconds, TimeSpan duration)
        => double.IsFinite(cachedSeconds)
           && Math.Abs(cachedSeconds - duration.TotalSeconds) < 1.0;

    private sealed class CacheEntry
    {
        [JsonPropertyName("schema")] public int Schema { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("artist")] public string? Artist { get; set; }
        [JsonPropertyName("durationSeconds")] public double DurationSeconds { get; set; }
        [JsonPropertyName("lyricsId")] public string? LyricsId { get; set; }
        [JsonPropertyName("lang")] public string? Lang { get; set; }
        [JsonPropertyName("timing")] public string? Timing { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("providerId")] public long ProviderId { get; set; }
        [JsonPropertyName("lines")] public List<CacheLine?>? Lines { get; set; }
    }

    private sealed class CacheLine
    {
        [JsonPropertyName("beginSeconds")] public double BeginSeconds { get; set; }
        [JsonPropertyName("endSeconds")] public double EndSeconds { get; set; }
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
        [JsonPropertyName("part")] public string? Part { get; set; }
    }
}
