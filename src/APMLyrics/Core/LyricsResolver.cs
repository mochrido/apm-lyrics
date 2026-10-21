using System.IO;
namespace APMLyrics.Core;

/// <summary>
/// Turns a playing track into lyrics: scan the cache, identify each candidate,
/// let the matcher choose, then parse the winner. Never returns stale lyrics:
/// no match means null, and the caller shows its empty state.
/// </summary>
public sealed class LyricsResolver
{
    private readonly IAppleLyricsCache _cache;
    private readonly ICatalogClient _catalog;

    // The search fallback's duration gate is the matcher's own tolerance: a
    // file whose body length only "roughly" fits was measured adopting the
    // WRONG song (.Feast at 3:53 shown for Biang Lara at 4:00), which is worse
    // than an honest "no lyrics". Only a near-exact fit may be named.
    internal static readonly TimeSpan SearchDurationTolerance = LyricsMatcher.DurationTolerance;

    public LyricsResolver(IAppleLyricsCache cache, ICatalogClient catalog)
    {
        _cache = cache;
        _catalog = catalog;
    }

    /// <summary>Legacy shape kept for callers that only need the document.</summary>
    public async Task<LyricsDoc?> ResolveAsync(Track track, CancellationToken ct, DateTimeOffset? trackStart = null)
        => (await ResolveLocalAsync(track, ct, trackStart)).Document;

    /// <summary>
    /// Answers from already-known catalog metadata only, with no network at all.
    /// This is the fast path that decides whether an online fallback is needed,
    /// so it must never block on a lookup.
    /// </summary>
    public LyricsResolution ResolveCached(Track track, DateTimeOffset? trackStart = null)
    {
        var candidates = _cache.Scan();
        if (candidates.Count == 0)
            return new LyricsResolution(LyricsResolutionKind.NotFound);

        var resolved = new List<ResolvedCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            _catalog.TryGetCached(candidate.LyricsId, out var info);
            resolved.Add(new ResolvedCandidate(
                candidate, info, BodyDuration(candidate.Ttml), WrittenAt(candidate.FilePath)));
        }

        return Classify(LyricsMatcher.ChooseResolved(track, resolved, trackStart));
    }

    /// <summary>
    /// Full local resolution, including catalog lookups and the name search that
    /// can identify files whose id the catalog cannot resolve. Bounded by the
    /// caller's deadline; cancellation propagates.
    /// </summary>
    public async Task<LyricsResolution> ResolveLocalAsync(
        Track track, CancellationToken ct, DateTimeOffset? trackStart = null)
    {
        var candidates = _cache.Scan();
        if (candidates.Count == 0)
            return new LyricsResolution(LyricsResolutionKind.NotFound);

        var resolved = new List<ResolvedCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // A candidate whose id is already known needs no request, and one
            // whose length is clearly incompatible can never match, so it does
            // not deserve a lookup either. That keeps historic cache files from
            // delaying the answer.
            if (_catalog.TryGetCached(candidate.LyricsId, out var known))
            {
                var body = BodyDuration(candidate.Ttml);
                if (!CouldMatch(track, known, body))
                    continue;

                resolved.Add(new ResolvedCandidate(
                    candidate, known, body, WrittenAt(candidate.FilePath)));
                continue;
            }

            var info = await _catalog.LookupAsync(candidate.LyricsId, ct);
            resolved.Add(new ResolvedCandidate(
                candidate, info, BodyDuration(candidate.Ttml), WrittenAt(candidate.FilePath)));
        }

        if (resolved.Count == 0)
            return new LyricsResolution(LyricsResolutionKind.NotFound);

        await AttachSearchInfoAsync(track, resolved, ct);

        return Classify(LyricsMatcher.ChooseResolved(track, resolved, trackStart));
    }

    /// <summary>
    /// False only when the metadata is complete enough to prove the file cannot
    /// describe the playing track. Unknown values never disqualify.
    /// </summary>
    private static bool CouldMatch(Track track, TrackInfo? info, TimeSpan bodyDuration)
    {
        if (info is null)
            return true;

        if (TrackIdentity.Normalize(info.Title) != TrackIdentity.Normalize(track.Title))
            return false;

        var drift = (info.Duration - track.Duration).Duration();
        var bodyDrift = bodyDuration <= TimeSpan.Zero
            ? TimeSpan.Zero
            : (bodyDuration - track.Duration).Duration();
        var tolerance = LyricsMatcher.DurationTolerance;

        // Either the catalog's own length or the file's body may be the reliable
        // one; a match is possible when either agrees.
        return track.Duration <= TimeSpan.Zero
               || drift <= tolerance
               || bodyDrift <= tolerance;
    }

    private static LyricsResolution Classify(ResolvedCandidate? chosen)
    {
        if (chosen is null)
            return new LyricsResolution(LyricsResolutionKind.NotFound);

        LyricsDoc document;
        try
        {
            document = TtmlParser.Parse(chosen.Candidate.LyricsId, chosen.Candidate.Ttml);
        }
        catch (FormatException)
        {
            return new LyricsResolution(LyricsResolutionKind.NotFound); // miss, not a crash
        }

        if (document.IsSynced)
            return new LyricsResolution(LyricsResolutionKind.Synced, document, "Apple");

        // Untimed lyrics may only be shown as lyrics when a name identified the
        // file. Arrival timing alone is enough to trust timestamps, never to
        // claim that this text belongs to this recording.
        return chosen.Info is not null
            ? new LyricsResolution(LyricsResolutionKind.Unsynced, document, "Apple")
            : new LyricsResolution(LyricsResolutionKind.NotFound);
    }

    /// <summary>
    /// Names the unnameable: for candidates whose id the catalog cannot look
    /// up (MX_, unknown forms), the playing track's own name identifies the
    /// song through the search API. Apple prefetches some lyric files minutes
    /// before the track starts, so the matcher's timing window cannot reach
    /// them; the search names them instead.
    ///
    /// The searched info is attached to exactly ONE candidate — the one whose
    /// body duration is nearest the searched duration — because attaching it
    /// to every candidate inside the tolerance let a same-length neighbour
    /// ("Ini Abadi", 3:52, next to "Biang Lara", 3:53) be renamed into the
    /// playing track and win the match.
    /// </summary>
    private async Task AttachSearchInfoAsync(Track track, List<ResolvedCandidate> resolved, CancellationToken ct)
    {
        var unnamed = resolved.Where(r => r.Info is null).ToList();
        if (unnamed.Count == 0)
            return;

        var searched = await _catalog.SearchAsync(track.Title, TrackIdentity.SplitArtist(track.Artist), ct);
        if (searched is null)
            return;

        // Nearest by duration wins; ties are ambiguous, so nobody is named.
        ResolvedCandidate? best = null;
        var bestGap = double.MaxValue;
        var tie = false;
        foreach (var c in unnamed)
        {
            var gap = Math.Abs((c.BodyDuration - searched.Duration).TotalSeconds);
            if (gap > SearchDurationTolerance.TotalSeconds)
                continue;
            if (gap < bestGap)
            {
                bestGap = gap;
                best = c;
                tie = false;
            }
            else if (gap == bestGap)
            {
                tie = true;
            }
        }

        if (best is not null && !tie)
            resolved[resolved.IndexOf(best)] = best with { Info = searched };
    }

    internal static TimeSpan BodyDuration(string ttml)
    {
        var marker = "<body";
        var start = ttml.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return TimeSpan.Zero;

        var end = ttml.IndexOf('>', start);
        if (end < 0)
            return TimeSpan.Zero;

        var header = ttml[start..end];
        var durIndex = header.IndexOf("dur=\"", StringComparison.Ordinal);
        if (durIndex < 0)
            return TimeSpan.Zero;

        var valueStart = durIndex + 5;
        var valueEnd = header.IndexOf('"', valueStart);
        if (valueEnd < 0)
            return TimeSpan.Zero;

        try
        {
            return TtmlParser.ParseTime(header[valueStart..valueEnd]);
        }
        catch (Exception)
        {
            // A garbage or out-of-range dur is a miss, not a crash: same Zero as
            // every other unparseable shape above, so ResolveAsync keeps its
            // contract that a malformed document never throws.
            return TimeSpan.Zero;
        }
    }

    private static DateTimeOffset WrittenAt(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }
}
