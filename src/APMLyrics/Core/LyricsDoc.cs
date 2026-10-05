namespace APMLyrics.Core;

/// <summary>One timed lyric line. Part is the optional Apple verse label.</summary>
public sealed record LyricLine(TimeSpan Begin, TimeSpan End, string Text, string? Part);

/// <summary>All timed lines for one song, as Apple cached them.</summary>
public sealed record LyricsDoc(string LyricsId, string? Lang, IReadOnlyList<LyricLine> Lines);
