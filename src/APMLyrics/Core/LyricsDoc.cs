namespace APMLyrics.Core;

/// <summary>One timed lyric line. Part is the optional Apple verse label.</summary>
public sealed record LyricLine(TimeSpan Begin, TimeSpan End, string Text, string? Part);

/// <summary>Whether a document's line positions mean anything for playback.</summary>
public enum LyricsTiming
{
    /// <summary>Plain text: it can be shown as a status, never as a moving highlight.</summary>
    None,

    /// <summary>Every line carries a real start time.</summary>
    Line,
}

/// <summary>All lines for one song, with an explicit statement about their timing.</summary>
public sealed record LyricsDoc(string LyricsId, string? Lang, IReadOnlyList<LyricLine> Lines)
{
    /// <summary>
    /// Line-timed by default so existing construction stays compatible; a
    /// parser that knows the content is untimed sets this explicitly.
    /// </summary>
    public LyricsTiming Timing { get; init; } = LyricsTiming.Line;

    public bool IsSynced => Timing == LyricsTiming.Line;
}

/// <summary>What a resolution attempt concluded. Ordering matters for UI precedence.</summary>
public enum LyricsResolutionKind
{
    /// <summary>Still looking; keep the loading state.</summary>
    Loading,

    /// <summary>A validated, line-timed document is available.</summary>
    Synced,

    /// <summary>Lyrics were found and identified, but only as untimed text.</summary>
    Unsynced,

    /// <summary>The search completed and found nothing usable.</summary>
    NotFound,

    /// <summary>The provider could not be reached; a retry may succeed later.</summary>
    NetworkUnavailable,

    /// <summary>The track's duration is not known yet, so no online match can be verified.</summary>
    PendingDuration,
}

/// <summary>One resolution outcome, with the document when there is one.</summary>
public sealed record LyricsResolution(
    LyricsResolutionKind Kind,
    LyricsDoc? Document = null,
    string? Source = null);

