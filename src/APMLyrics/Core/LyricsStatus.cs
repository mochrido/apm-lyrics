namespace APMLyrics.Core;

/// <summary>
/// The user-visible wording for each unresolved/blocked state. Kept as plain
/// data so the mapping can be tested without a WPF surface, and so the overlay
/// never invents its own phrasing.
/// </summary>
public static class LyricsStatus
{
    public const string Finding = "Finding synced lyrics…";
    public const string OnlyUnsynced = "Only unsynced lyrics available";
    public const string NoMatch = "No matching synced lyrics found";
    public const string Offline = "Couldn't check online lyrics";

    /// <summary>
    /// The status line for a resolution that is not renderable, or null when the
    /// resolution carries timed lines and needs no status at all.
    /// </summary>
    public static string? For(LyricsResolution resolution) => resolution.Kind switch
    {
        LyricsResolutionKind.Synced when resolution.Document is { IsSynced: true } => null,
        LyricsResolutionKind.Loading => Finding,
        LyricsResolutionKind.Unsynced => OnlyUnsynced,
        LyricsResolutionKind.NetworkUnavailable => Offline,

        // PendingDuration is a miss in progress: the coordinator still shows
        // Loading until the retry window closes, so it maps to the miss text
        // only if it ever reaches the UI directly.
        _ => NoMatch,
    };
}
