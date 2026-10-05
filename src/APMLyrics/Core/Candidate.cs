namespace APMLyrics.Core;

/// <summary>One indexed cache file, read from disk but not yet parsed.</summary>
public sealed record Candidate(string FilePath, string LyricsId, string Ttml);

/// <summary>What the public catalog says a lyrics id actually is.</summary>
public sealed record TrackInfo(string Title, string Artist, TimeSpan Duration);

/// <summary>A candidate plus everything known about it, ready to be chosen between.</summary>
public sealed record ResolvedCandidate(
    Candidate Candidate,
    TrackInfo? Info,
    TimeSpan BodyDuration,
    DateTimeOffset WrittenAt);
