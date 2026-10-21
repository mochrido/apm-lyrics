using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APMLyrics.Core;

/// <summary>One LRCLIB search result, before any validation.</summary>
public sealed record LrclibCandidate(
    long Id,
    string Title,
    string Artist,
    string Album,
    TimeSpan Duration,
    string? SyncedLyrics,
    string? PlainLyrics);

/// <summary>
/// Fetches synchronized lyrics from LRCLIB's public API and accepts a result
/// only when it is demonstrably the same recording.
/// <para>
/// LRCLIB search returns every submission for a title, including covers,
/// karaoke versions, and truncated duplicates. A live search for "Pasti Ada
/// Jalan" returned durations 281, 30, 2 and 139 seconds for a 139-second track,
/// so "first result" is not a match. Identity (title, artist, recording
/// qualifiers) must agree, the duration must be within two seconds, and the
/// timed payload must parse. Anything less is an honest miss.
/// </para>
/// </summary>
public sealed class LrclibClient
{
    public static readonly string UserAgent =
        "APMLyrics/0.2 (+https://github.com/apm-lyrics/apm-lyrics)";

    internal const string SearchEndpoint = "https://lrclib.net/api/search";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(2);
    private const int MaxResponseBytes = 2 * 1024 * 1024;

    private static readonly HttpClient Shared = new() { Timeout = RequestTimeout };

    private readonly HttpClient _client;
    private readonly FallbackLyricsCache? _cache;

    public LrclibClient(HttpClient client, FallbackLyricsCache? cache = null)
    {
        _client = client;
        _cache = cache;
    }

    /// <summary>Production client over a shared pooled connection.</summary>
    public LrclibClient() : this(Shared, new FallbackLyricsCache(FallbackLyricsCache.DefaultDirectory)) { }

    public async Task<LyricsResolution> ResolveAsync(Track track, CancellationToken ct)
    {
        if (track.Duration <= TimeSpan.Zero)
            return new LyricsResolution(LyricsResolutionKind.PendingDuration);

        // A validated local copy avoids the network entirely, including when the
        // provider is down.
        var cached = _cache?.Read(track);
        if (cached is not null)
            return new LyricsResolution(LyricsResolutionKind.Synced, cached, "LRCLIB cache");

        var artist = TrackIdentity.SplitArtist(track.Artist);
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(artist))
            return new LyricsResolution(LyricsResolutionKind.NotFound);

        List<LrclibCandidate> candidates;
        try
        {
            candidates = await SearchAsync(track.Title, artist, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled: distinct from a provider timeout
        }
        catch (OperationCanceledException)
        {
            return new LyricsResolution(LyricsResolutionKind.NetworkUnavailable);
        }
        catch (HttpRequestException)
        {
            return new LyricsResolution(LyricsResolutionKind.NetworkUnavailable);
        }
        catch (JsonException)
        {
            return new LyricsResolution(LyricsResolutionKind.NotFound);
        }

        var (resolution, winner) = Select(track, candidates);
        if (resolution.Kind == LyricsResolutionKind.Synced
            && resolution.Document is not null
            && winner is not null)
        {
            _cache?.Write(track, winner, resolution.Document);
        }

        return resolution;
    }

    /// <summary>
    /// Chooses the one match worth showing. Identity first, then duration, then
    /// album; the timed payload is parsed before ranking so a plain or malformed
    /// submission cannot hide a usable one.
    /// </summary>
    internal static (LyricsResolution Resolution, LrclibCandidate? Candidate) Select(
        Track track, IReadOnlyList<LrclibCandidate> candidates)
    {
        var wantedTitle = RecordingIdentity.NormalizeTitle(track.Title);
        var wantedArtist = TrackIdentity.Normalize(TrackIdentity.SplitArtist(track.Artist));
        var wantedAlbum = TrackIdentity.Normalize(track.Album);

        var eligible = new List<(LrclibCandidate Candidate, bool AlbumAgrees, int Gap)>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Title) || string.IsNullOrWhiteSpace(candidate.Artist))
                continue;
            if (!RecordingIdentity.SameRecording(wantedTitle, candidate.Title))
                continue;
            if (TrackIdentity.Normalize(candidate.Artist) != wantedArtist)
                continue;

            // A provider entry with no length cannot be proven to be this
            // recording; it is not accepted on name alone.
            if (candidate.Duration <= TimeSpan.Zero)
                continue;

            var gap = (candidate.Duration - track.Duration).Duration();
            if (gap > DurationTolerance)
                continue;

            var albumAgrees = wantedAlbum.Length > 0
                              && TrackIdentity.Normalize(candidate.Album) == wantedAlbum;
            eligible.Add((candidate, albumAgrees, (int)Math.Round(gap.TotalMilliseconds)));
        }

        if (eligible.Count == 0)
            return (new LyricsResolution(LyricsResolutionKind.NotFound), null);

        // Best rank: agreeing album first, then the closest duration.
        var best = eligible
            .OrderByDescending(e => e.AlbumAgrees)
            .ThenBy(e => e.Gap)
            .ToList();

        var topRank = best[0];
        var tied = best
            .Where(e => e.AlbumAgrees == topRank.AlbumAgrees && e.Gap == topRank.Gap)
            .ToList();

        var parsed = new List<(LrclibCandidate Candidate, LyricsDoc Document)>();
        var sawPlain = false;
        foreach (var (candidate, _, _) in tied)
        {
            if (!string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
            {
                try
                {
                    var doc = LrcParser.Parse(
                        $"lrclib.net:{candidate.Id}", candidate.SyncedLyrics, track.Duration);
                    parsed.Add((candidate, doc));
                    continue;
                }
                catch (FormatException)
                {
                    // A "synced" field that cannot be trusted is not a match.
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate.PlainLyrics))
                sawPlain = true;
        }

        if (parsed.Count == 0)
        {
            return (new LyricsResolution(
                sawPlain ? LyricsResolutionKind.Unsynced : LyricsResolutionKind.NotFound), null);
        }

        // Equivalent submissions (same text and timing, different formatting)
        // collapse; genuinely different payloads at the same rank are ambiguous,
        // and ambiguous is not a licence to guess.
        var distinct = parsed
            .GroupBy(p => PayloadKey(p.Document))
            .ToList();

        if (distinct.Count > 1)
            return (new LyricsResolution(LyricsResolutionKind.NotFound), null);

        var winner = distinct[0].First();
        return (
            new LyricsResolution(LyricsResolutionKind.Synced, winner.Document, "LRCLIB"),
            winner.Candidate);
    }

    private static string PayloadKey(LyricsDoc doc)
        => string.Join(
            "\n",
            doc.Lines.Select(l => $"{l.Begin.Ticks}|{l.End.Ticks}|{l.Text}"));

    private async Task<List<LrclibCandidate>> SearchAsync(
        string title, string artist, CancellationToken ct)
    {
        var url = $"{SearchEndpoint}?track_name={Uri.EscapeDataString(title)}" +
                  $"&artist_name={Uri.EscapeDataString(artist)}";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RequestTimeout);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

            if (IsRetryable(response.StatusCode) && attempt == 0 && HasRetryHint(response))
            {
                // Only an explicit Retry-After authorises the second attempt; an
                // unexplained 5xx is retried later by the coordinator, not
                // immediately here.
                await Task.Delay(RetryDelay(response), deadline.Token);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.NotFound)
                return new List<LrclibCandidate>();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"LRCLIB returned {(int)response.StatusCode}.");

            return await ReadCandidatesAsync(response, deadline.Token);
        }
    }

    private static bool IsRetryable(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static bool HasRetryHint(HttpResponseMessage response)
        => response.Headers.RetryAfter is not null;

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta <= TimeSpan.FromSeconds(2) ? (delta > TimeSpan.Zero ? delta : TimeSpan.Zero)
                : TimeSpan.FromSeconds(2);
        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait <= TimeSpan.Zero ? TimeSpan.Zero
                : wait <= TimeSpan.FromSeconds(2) ? wait
                : TimeSpan.FromSeconds(2);
        }
        return TimeSpan.Zero;
    }

    private static async Task<List<LrclibCandidate>> ReadCandidatesAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new HttpRequestException("LRCLIB response is too large.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
                throw new HttpRequestException("LRCLIB response is too large.");
            buffer.Write(chunk, 0, read);
        }

        var json = Encoding.UTF8.GetString(buffer.ToArray());
        var rows = JsonSerializer.Deserialize<List<LrclibRow>>(json);
        if (rows is null)
            return new List<LrclibCandidate>();

        return rows
            .Where(r => r.TrackName is not null && r.ArtistName is not null && r.Duration is not null)
            .Select(r => new LrclibCandidate(
                r.Id,
                r.TrackName!,
                r.ArtistName!,
                r.AlbumName ?? string.Empty,
                TimeSpan.FromSeconds(r.Duration!.Value),
                r.SyncedLyrics,
                r.PlainLyrics))
            .ToList();
    }

    private sealed record LrclibRow(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("albumName")] string? AlbumName,
        [property: JsonPropertyName("duration")] double? Duration,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics,
        [property: JsonPropertyName("plainLyrics")] string? PlainLyrics);
}

/// <summary>
/// Identity rules for online fallback. Unlike <see cref="TrackIdentity.Normalize"/>,
/// which is tuned for Apple's own fields and drops "- live", these preserve
/// recording qualifiers: a live take is a different recording from the studio
/// version and its timings would land on the wrong lines.
/// </summary>
public static class RecordingIdentity
{
    private static readonly string[] Qualifiers =
    {
        "live", "remix", "acoustic", "remaster", "remastered", "edit", "instrumental",
        "demo", "karaoke", "sped up", "slowed", "reprise", "extended", "radio edit",
    };

    // Apple appends the release type to the artist, not the title; this is the
    // one suffix that is marketing metadata rather than a recording qualifier.
    private static readonly string[] ReleaseSuffixes = { " - single", " - ep" };

    /// <summary>Case/accent/space-insensitive title, with qualifiers retained.</summary>
    public static string NormalizeTitle(string title)
    {
        var lowered = (title ?? string.Empty).Trim().ToLowerInvariant();
        foreach (var suffix in ReleaseSuffixes)
        {
            if (lowered.EndsWith(suffix, StringComparison.Ordinal))
                lowered = lowered[..^suffix.Length].Trim();
        }

        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// True when the two titles name the same recording: same normalized text
    /// and the same set of recording qualifiers. A bare title never matches a
    /// qualified one, so "(Live)" cannot borrow the studio timings.
    /// </summary>
    public static bool SameRecording(string wantedTitle, string candidateTitle)
    {
        var wanted = NormalizeTitle(wantedTitle);
        var candidate = NormalizeTitle(candidateTitle);
        if (wanted != candidate)
            return false;

        return QualifiersOf(wanted).SetEquals(QualifiersOf(candidate));
    }

    private static HashSet<string> QualifiersOf(string normalizedTitle)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var qualifier in Qualifiers)
        {
            if (normalizedTitle.Contains(qualifier, StringComparison.Ordinal))
                found.Add(qualifier);
        }
        return found;
    }
}
