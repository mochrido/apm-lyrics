using APMLyrics.Core;

namespace APMLyrics.Playback;

/// <summary>
/// One observation of the player: which generation (track/session) it belongs
/// to, how late it was observed, and the metrics Windows exposed with it.
/// <para>
/// <see cref="Generation"/> changes on track replacement, session loss, and
/// reconnection, so results from a previous song can never be applied to the
/// current one. <see cref="Sequence"/> increases per poll; it orders
/// observations but says nothing about whether the underlying media data is
/// fresher.
/// </para>
/// </summary>
public sealed record PlaybackSnapshot(
    long Generation,
    long Sequence,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset LastUpdatedTime,
    bool IsPlaying,
    double Rate,
    TimeSpan ObservedAt);

/// <summary>Where track and position events come from. One implementation today.</summary>
public interface IPlaybackSource : IDisposable
{
    event Action<long, Track?>? TrackChanged;

    /// <summary>Every poll, carrying the observation time from the shared monotonic clock.</summary>
    event Action<PlaybackSnapshot>? PositionChanged;

    Track? Current { get; }

    void Start();
}
