using APMLyrics.Core;
using Windows.Media.Control;

namespace APMLyrics.Playback;

/// <summary>
/// Wraps the Windows media session API. Polls at 4Hz rather than depending on
/// the event surface alone: SMTC events are irregular for WinUI players, and a
/// poll is the only way to keep the position fresh. Track identity is read from
/// the media properties on every tick and compared, which is cheap and reliable.
/// </summary>
public sealed class SmtcPlaybackSource : IPlaybackSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const string AppleMusicSessionMarker = "AppleMusicWin";

    private readonly CancellationTokenSource _cts = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Task? _loop;
    private string _lastIdentity = string.Empty;

    public event Action<Track?>? TrackChanged;
    public event Action<TimeSpan, bool, double>? PositionChanged;

    public Track? Current { get; private set; }

    public void Start() => _loop ??= Task.Run(PollLoopAsync);

    private async Task PollLoopAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception)
        {
            TrackChanged?.Invoke(null);
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
            if (Current is not null)
            {
                Current = null;
                TrackChanged?.Invoke(null);
            }
            return;
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var timeline = session.GetTimelineProperties();
        var playback = session.GetPlaybackInfo();

        var identity = $"{props.Title}\u241F{props.Artist}";
        if (!string.Equals(identity, _lastIdentity, StringComparison.Ordinal))
        {
            _lastIdentity = identity;
            Current = new Track(
                props.Title ?? string.Empty,
                props.Artist ?? string.Empty,
                props.AlbumTitle ?? string.Empty,
                timeline.EndTime,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            TrackChanged?.Invoke(Current);
        }
        else if (Current is not null)
        {
            Current = Current with
            {
                Duration = timeline.EndTime,
                IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            };
        }

        if (Current is not null)
        {
            PositionChanged?.Invoke(
                timeline.Position,
                Current.IsPlaying,
                playback.PlaybackRate ?? 1.0);
        }
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
