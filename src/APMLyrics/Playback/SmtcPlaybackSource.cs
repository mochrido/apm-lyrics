using APMLyrics.Core;
using Windows.Media.Control;

namespace APMLyrics.Playback;

/// <summary>
/// Wraps the Windows media session API. Polls at 4Hz rather than depending on
/// the event surface alone: SMTC events are irregular for WinUI players, and a
/// poll is the only way to keep the position fresh. Track identity is read from
/// the media properties on every tick and compared, which is cheap and reliable.
/// <para>
/// Reads are serialized by the poll loop, so observations cannot interleave or
/// reorder. Each observation carries the current generation and a monotonic
/// sequence number; <see cref="Generation"/> advances on track replacement,
/// session loss, and reconnection so stale work can be discarded downstream.
/// </para>
/// </summary>
public sealed class SmtcPlaybackSource : IPlaybackSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const string AppleMusicSessionMarker = "AppleMusicWin";

    private readonly CancellationTokenSource _cts = new();
    private readonly ITimeSource _time;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Task? _loop;
    private string _lastIdentity = string.Empty;
    private long _generation;
    private long _sequence;

    public SmtcPlaybackSource(ITimeSource? time = null) => _time = time ?? new SystemTimeSource();

    public event Action<long, Track?>? TrackChanged;
    public event Action<PlaybackSnapshot>? PositionChanged;

    public Track? Current { get; private set; }

    /// <summary>Increments whenever the observed track or session changes.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    public void Start() => _loop ??= Task.Run(PollLoopAsync);

    private async Task PollLoopAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception)
        {
            RaiseTrackLost();
            return;
        }

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync();
            }
            catch (Exception)
            {
                // A transient SMTC failure must not kill the loop.
            }

            try
            {
                await Task.Delay(PollInterval, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollOnceAsync()
    {
        var session = FindAppleMusicSession();
        if (session is null)
        {
            RaiseTrackLost();
            return;
        }

        // Capture the generation BEFORE the awaited read: a track change during
        // the await makes this observation obsolete, and it must be discarded
        // rather than published against the new generation.
        var generation = Generation;
        var observedAt = _time.Now;

        var props = await session.TryGetMediaPropertiesAsync();
        var timeline = session.GetTimelineProperties();
        var playback = session.GetPlaybackInfo();

        if (generation != Generation)
            return;

        var identity = $"{props.Title}\u241F{props.Artist}";
        if (string.Equals(identity, _lastIdentity, StringComparison.Ordinal))
        {
            if (Current is not null)
            {
                Current = Current with
                {
                    Duration = timeline.EndTime,
                    IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                };
            }
        }
        else
        {
            // Set identity before raising so a re-entrant poll cannot publish the
            // same change twice.
            _lastIdentity = identity;
            var next = Interlocked.Increment(ref _generation);
            Current = new Track(
                props.Title ?? string.Empty,
                props.Artist ?? string.Empty,
                props.AlbumTitle ?? string.Empty,
                timeline.EndTime,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            generation = next;
            TrackChanged?.Invoke(next, Current);
        }

        if (Current is null)
            return;

        PositionChanged?.Invoke(new PlaybackSnapshot(
            generation,
            ++_sequence,
            timeline.Position,
            timeline.EndTime,
            timeline.LastUpdatedTime,
            Current.IsPlaying,
            playback.PlaybackRate ?? 1.0,
            observedAt));
    }

    /// <summary>
    /// Session loss clears the identity as well as the track, so reconnecting to
    /// the same song raises a fresh generation instead of being mistaken for a
    /// continuation.
    /// </summary>
    private void RaiseTrackLost()
    {
        if (Current is null && _lastIdentity.Length == 0)
            return;

        _lastIdentity = string.Empty;
        Current = null;
        var next = Interlocked.Increment(ref _generation);
        TrackChanged?.Invoke(next, null);
    }

    private GlobalSystemMediaTransportControlsSession? FindAppleMusicSession()
    {
        var sessions = _manager?.GetSessions();
        if (sessions is null)
            return null;

        foreach (var session in sessions)
        {
            if (session.SourceAppUserModelId.Contains(AppleMusicSessionMarker, StringComparison.OrdinalIgnoreCase))
                return session;
        }
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Shutdown must be safe.
        }
        _cts.Dispose();
    }
}
