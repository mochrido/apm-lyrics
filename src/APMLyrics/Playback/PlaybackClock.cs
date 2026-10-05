namespace APMLyrics.Playback;

/// <summary>Injectable wall clock so the interpolation can be tested.</summary>
public interface ITimeSource
{
    TimeSpan Now { get; }
}

public sealed class SystemTimeSource : ITimeSource
{
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    public TimeSpan Now => _sw.Elapsed;
}

/// <summary>
/// SMTC position ticks are coarse. This keeps a monotonic anchor and interpolates
/// between ticks, never moving backwards, and holds still while paused.
/// </summary>
public sealed class PlaybackClock
{
    private readonly ITimeSource _time;
    private TimeSpan _anchorPosition;
    private TimeSpan _anchorAt;
    private bool _playing;
    private double _rate = 1.0;
    private TimeSpan _lastReported;

    public PlaybackClock(ITimeSource time) => _time = time;

    public void Sync(TimeSpan position, bool isPlaying, double rate)
    {
        var candidate = _playing && _rate > 0
            ? _anchorPosition + Scale(_time.Now - _anchorAt, _rate)
            : _lastReported;

        // Authoritative position wins, but only forwards. A late coarse tick
        // must never drag the displayed line backwards.
        _anchorPosition = position > candidate ? position : candidate;
        _anchorAt = _time.Now;
        _playing = isPlaying;
        _rate = rate <= 0 ? 1.0 : rate;
        _lastReported = _anchorPosition;
    }

    public TimeSpan Position
    {
        get
        {
            _lastReported = _playing
                ? _anchorPosition + Scale(_time.Now - _anchorAt, _rate)
                : _anchorPosition;
            return _lastReported;
        }
    }

    // The displayed (interpolated) position is committed into the anchor before
    // the play flag flips, so pausing or resuming never jumps the position back
    // to the last coarse SMTC tick.
    public void Pause() => Sync(Position, isPlaying: false, rate: _rate);
    public void Resume() => Sync(Position, isPlaying: true, rate: _rate);

    private static TimeSpan Scale(TimeSpan delta, double rate)
        => TimeSpan.FromTicks((long)(delta.Ticks * rate));
}
