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
/// Turns coarse media-session observations into a smooth, seek-aware position.
/// <para>
/// A <em>changed</em> position is authoritative and re-anchors in either
/// direction, so a forward or backward seek is followed. A repeated position is
/// not news: the clock keeps interpolating from its own anchor, so a stale poll
/// can never drag the display backwards. A state-only change (pause, resume,
/// rate change) freezes or restarts at the <em>interpolated</em> position rather
/// than rewinding to the last coarse tick.
/// </para>
/// </summary>
public sealed class PlaybackClock
{
    // A snapshot older than this is treated as buffered latency rather than
    // current playback; extrapolating it would overshoot by the full age.
    private static readonly TimeSpan MaxObservationAge = TimeSpan.FromSeconds(1);

    // Provider timestamps are whole seconds at best. Two positions within this
    // distance are the same reported tick, not a seek.
    private static readonly TimeSpan PositionEpsilon = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond);

    private readonly ITimeSource _time;
    private TimeSpan _anchorPosition;
    private TimeSpan _anchorAt;
    private bool _playing;
    private double _rate = 1.0;

    private long _generation;
    private long _lastSequence = long.MinValue;
    private TimeSpan _lastRawPosition = TimeSpan.MinValue;
    private TimeSpan _duration;

    public PlaybackClock(ITimeSource time) => _time = time;

    /// <summary>
    /// Total reset for a track change. The clock adopts the new generation, so
    /// observations from the previous song (or an earlier connection) can never
    /// be applied again.
    /// </summary>
    public void Reset(long generation = 0)
    {
        _generation = generation;
        _anchorPosition = TimeSpan.Zero;
        _anchorAt = _time.Now;
        _playing = false;
        _rate = 1.0;
        _lastSequence = long.MinValue;
        _lastRawPosition = TimeSpan.MinValue;
        _duration = TimeSpan.Zero;
    }

    public void Sync(PlaybackSnapshot snapshot)
    {
        if (snapshot.Generation < _generation)
            return;

        if (snapshot.Generation > _generation)
            Reset(snapshot.Generation);
        else if (snapshot.Sequence <= _lastSequence)
            return;

        _lastSequence = snapshot.Sequence;

        var now = _time.Now;
        var interpolated = PositionNow(now);
        var positionChanged = _lastRawPosition == TimeSpan.MinValue
                              || (snapshot.Position - _lastRawPosition).Duration() > PositionEpsilon;

        var rate = snapshot.Rate > 0 && double.IsFinite(snapshot.Rate) ? snapshot.Rate : 1.0;
        var stateChanged = snapshot.IsPlaying != _playing || Math.Abs(rate - _rate) > 0.0001;

        TimeSpan anchor;
        if (positionChanged)
        {
            anchor = Clamp(SnapshotPosition(snapshot, now));
        }
        else if (stateChanged)
        {
            // Pause, resume, or a rate change: keep the displayed position. The
            // raw tick may be far behind the interpolated one by now.
            anchor = interpolated;
        }
        else
        {
            // Same tick, same state: the provider has nothing new to say.
            anchor = interpolated;
        }

        _anchorPosition = anchor;
        _anchorAt = now;
        _playing = snapshot.IsPlaying;
        _rate = rate;
        _lastRawPosition = snapshot.Position;

        if (snapshot.Duration > TimeSpan.Zero)
            _duration = snapshot.Duration;
    }

    public TimeSpan Position
    {
        get
        {
            var position = PositionNow(_time.Now);
            return _duration > TimeSpan.Zero && position > _duration ? _duration : position;
        }
    }

    // Interpolation is only ever forward, and only while playing. The anchor is
    // committed at every state change, so pausing never loses progress.
    private TimeSpan PositionNow(TimeSpan now)
    {
        if (!_playing)
            return _anchorPosition;

        var elapsed = now - _anchorAt;
        if (elapsed <= TimeSpan.Zero)
            return _anchorPosition;

        return _anchorPosition + Scale(elapsed, _rate);
    }

    /// <summary>
    /// The reported position, advanced by the time between observation and
    /// application so a queued snapshot lands at the right instant. A snapshot
    /// older than <see cref="MaxObservationAge" /> is stale buffering, not
    /// current playback, and is used as reported.
    /// </summary>
    private TimeSpan SnapshotPosition(PlaybackSnapshot snapshot, TimeSpan now)
    {
        var position = snapshot.Position;
        if (!snapshot.IsPlaying)
            return position;

        var age = now - snapshot.ObservedAt;
        if (age <= TimeSpan.Zero || age > MaxObservationAge)
            return position;

        var rate = snapshot.Rate > 0 && double.IsFinite(snapshot.Rate) ? snapshot.Rate : 1.0;
        return position + Scale(age, rate);
    }

    private TimeSpan Clamp(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            return TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && position > _duration)
            return _duration;
        return position;
    }

    private static TimeSpan Scale(TimeSpan delta, double rate)
        => TimeSpan.FromTicks((long)(delta.Ticks * rate));
}
