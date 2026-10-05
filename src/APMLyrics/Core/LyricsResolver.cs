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

    public LyricsResolver(IAppleLyricsCache cache, ICatalogClient catalog)
    {
        _cache = cache;
        _catalog = catalog;
    }

    public async Task<LyricsDoc?> ResolveAsync(Track track, CancellationToken ct, DateTimeOffset? trackStart = null)
    {
        var candidates = _cache.Scan();
        if (candidates.Count == 0)
            return null;

        var resolved = new List<ResolvedCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var info = await _catalog.LookupAsync(candidate.LyricsId, ct);
            var body = BodyDuration(candidate.Ttml);
            resolved.Add(new ResolvedCandidate(candidate, info, body, WrittenAt(candidate.FilePath)));
        }

        var chosen = LyricsMatcher.Choose(track, resolved, trackStart);
        if (chosen is null)
            return null;

        try
        {
            return TtmlParser.Parse(chosen.LyricsId, chosen.Ttml);
        }
        catch (FormatException)
        {
            return null; // a malformed document is a miss, not a crash
        }
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
