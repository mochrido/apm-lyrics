using APMLyrics.Core;

namespace APMLyrics.Playback;

/// <summary>Where track and position events come from. One implementation today.</summary>
public interface IPlaybackSource : IDisposable
{
    event Action<Track?>? TrackChanged;
    event Action<TimeSpan, bool, double>? PositionChanged; // position, isPlaying, rate
    Track? Current { get; }
    void Start();
}
