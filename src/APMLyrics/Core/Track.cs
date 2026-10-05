namespace APMLyrics.Core;

/// <summary>A snapshot of what the player is doing right now.</summary>
public sealed record Track(
    string Title,
    string Artist,
    string Album,
    TimeSpan Duration,
    bool IsPlaying);
