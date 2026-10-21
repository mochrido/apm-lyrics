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
