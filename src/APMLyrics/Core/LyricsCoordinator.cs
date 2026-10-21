using System.Globalization;

namespace APMLyrics.Core;

/// <summary>
/// Owns lyric resolution for one playing track at a time: a cheap local answer
/// first, then local enrichment and the online fallback concurrently, with a
/// bounded retry ladder and a single status stream.
/// <para>
/// Everything is scoped to a generation. A new track cancels the previous
/// generation's work, and a late result is dropped rather than published
/// against a song it does not describe.
/// </para>
/// </summary>
public sealed class LyricsCoordinator : IDisposable
{
    // Local retries are absolute from track start, not cumulative: Apple often
    // writes the lyric file a few seconds into playback.
    private static readonly TimeSpan[] LocalRetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(8),
    };

    private static readonly TimeSpan EnrichmentDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NetworkRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DurationDriftTolerance = TimeSpan.FromSeconds(2);

    // Precedence: a validated Apple timed document beats a validated fallback,
    // which beats identified-but-untimed content, which beats a network failure,
    // which beats a bare miss. A lower rank never replaces a higher one within a
    // generation, so accepted lyrics cannot be taken away by a late error.
    private const int RankLoading = 0;
    private const int RankMiss = 1;
    private const int RankNetworkFailure = 2;
    private const int RankUnsynced = 3;
    private const int RankFallbackSynced = 4;
    private const int RankAppleSynced = 5;

    private readonly Func<Track, DateTimeOffset, LyricsResolution> _localCached;
    private readonly Func<Track, DateTimeOffset, CancellationToken, Task<LyricsResolution>> _localEnrich;
    private readonly Func<Track, CancellationToken, Task<LyricsResolution>> _remote;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _utcNow;

    // Cooldown bookkeeping only: deliberately in memory, so a restart may try
    // the provider again.
    private readonly Dictionary<string, DateTimeOffset> _remoteCooldown = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private Run? _run;

    public LyricsCoordinator(
        Func<Track, DateTimeOffset, LyricsResolution> localCached,
        Func<Track, DateTimeOffset, CancellationToken, Task<LyricsResolution>> localEnrich,
        Func<Track, CancellationToken, Task<LyricsResolution>> remote,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _localCached = localCached;
        _localEnrich = localEnrich;
        _remote = remote;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>One status per generation, in precedence order.</summary>
    public event Action<long, LyricsResolution>? Changed;

    public void Start(Track track, long generation)
    {
        Run run;
        lock (_gate)
        {
            // Cancel but do not dispose: worker tasks may still be observing
            // this token, and a disposed source throws from Token access.
            _run?.Cts.Cancel();
            run = new Run(track, generation, _utcNow());
            _run = run;
        }

        Publish(run, new LyricsResolution(LyricsResolutionKind.Loading), RankLoading, force: true);
        _ = RunAsync(run);
    }

    /// <summary>
    /// A duration or metadata correction for the same track. Position is never
    /// touched here; only a materially different length restarts resolution,
    /// because every duration-gated match made so far is now suspect.
    /// </summary>
    public void UpdateTrack(Track track, long generation)
    {
        lock (_gate)
        {
            var current = _run;
            if (current is null || current.Generation != generation)
            {
                // Fall through to Start outside the lock.
            }
            else
            {
                var drift = (track.Duration - current.Track.Duration).Duration();
                var restart = current.Track.Duration > TimeSpan.Zero
                              && track.Duration > TimeSpan.Zero
                              && drift > DurationDriftTolerance;
                var wasUnknown = current.Track.Duration <= TimeSpan.Zero
                                 && track.Duration > TimeSpan.Zero;

                if (!restart && !wasUnknown)
                {
                    current.Track = track;
                    return;
                }
            }
        }

        // Restart within the same generation: the old attempt and any content it
        // accepted no longer describe a verifiable recording.
        Start(track, generation);
    }

    /// <summary>Apple wrote a new lyric file; only local work is triggered.</summary>
    public void NotifyCacheChanged()
    {
        Run? run;
        lock (_gate)
            run = _run;

        if (run is not null)
            _ = EnrichLoopAsync(run);
    }

    public void Stop()
    {
        Run? run;
        lock (_gate)
        {
            run = _run;
            _run = null;
        }

        if (run is null)
            return;

        // Cancel only: in-flight continuations still hold this token, and a
        // disposed source would throw the moment they touch it.
        run.Cts.Cancel();
    }

    public void Dispose() => Stop();

    private async Task RunAsync(Run run)
    {
        var token = run.Cts.Token;
        try
        {
            // The cached answer is synchronous and cheap: it decides whether the
            // network is needed at all, without waiting on any catalog lookup.
            var cached = SafeLocal(run.Track, run.TrackStart);
            AcceptLocal(run, cached);
            if (run.BestRank >= RankAppleSynced)
                return;

            if (run.BestRank < RankUnsynced)
                _ = EnrichLoopAsync(run);

            Task? remote = run.Track.Duration > TimeSpan.Zero ? RemoteLoopAsync(run) : null;

            await LocalLadderAsync(run, token).ConfigureAwait(false);

            if (remote is not null)
                await remote.ConfigureAwait(false);

            token.ThrowIfCancellationRequested();
            Publish(run, new LyricsResolution(LyricsResolutionKind.NotFound), RankMiss);
        }
        catch (OperationCanceledException)
        {
            // Superseded or stopped; its results are not news.
        }
    }

    /// <summary>
    /// Re-checks the cache at 1, 3 and 8 seconds. Apple's own timed file often
    /// appears after playback starts, and a hit here outranks any fallback that
    /// already arrived.
    /// </summary>
    private async Task LocalLadderAsync(Run run, CancellationToken token)
    {
        var elapsed = TimeSpan.Zero;
        foreach (var target in LocalRetryDelays)
        {
            // Absolute schedule: the retries fire at 1s, 3s and 8s after the
            // track started, regardless of how long an earlier attempt took.
            var wait = target - elapsed;
            if (wait > TimeSpan.Zero)
            {
                await _delay(wait, token).ConfigureAwait(false);
                elapsed += wait;
            }

            token.ThrowIfCancellationRequested();

            if (run.BestRank >= RankFallbackSynced)
                return;

            AcceptLocal(run, SafeLocal(run.Track, run.TrackStart));
            if (run.BestRank >= RankAppleSynced)
                return;
        }
    }

    /// <summary>
    /// Runs local enrichment at most once at a time. A burst of file-watch
    /// events sets one pending flag instead of queueing one rescan each.
    /// </summary>
    private async Task EnrichLoopAsync(Run run)
    {
        lock (_gate)
        {
            if (run.EnrichRunning)
            {
                run.EnrichPending = true;
                return;
            }
            run.EnrichRunning = true;
        }

        try
        {
            while (true)
            {
                if (!IsCurrent(run))
                    return;

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(run.Cts.Token);
                deadline.CancelAfter(EnrichmentDeadline);
                try
                {
                    var result = await _localEnrich(run.Track, run.TrackStart, deadline.Token)
                        .ConfigureAwait(false);
                    AcceptLocal(run, result);
                }
                catch (OperationCanceledException) when (run.Cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // A timeout or catalog problem is not a status: enrichment is
                    // optional, and the answer already on screen stays.
                }

                lock (_gate)
                {
                    if (!run.EnrichPending)
                        return;
                    run.EnrichPending = false;
                }
            }
        }
        finally
        {
            // Always release the gate, including on timeout, so a later cache
            // event can still trigger a rescan.
            lock (_gate)
                run.EnrichRunning = false;
        }
    }

    private async Task RemoteLoopAsync(Run run)
    {
        var identity = RecordingKey(run.Track);
        var token = run.Cts.Token;

        try
        {
            if (CooldownRemaining(identity) is { } remaining)
            {
                await _delay(remaining, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (!IsCurrent(run))
                    return;

                LyricsResolution result;
                try
                {
                    result = await _remote(run.Track, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    result = new LyricsResolution(LyricsResolutionKind.NetworkUnavailable);
                }

                token.ThrowIfCancellationRequested();

                switch (result.Kind)
                {
                    case LyricsResolutionKind.Synced:
                        Publish(run, result, RankFallbackSynced);
                        return;

                    case LyricsResolutionKind.Unsynced:
                        Publish(run, result, RankUnsynced);
                        return;

                    case LyricsResolutionKind.NetworkUnavailable:
                        Publish(run, result, RankNetworkFailure);
                        if (attempt == 0)
                        {
                            // One further logical attempt, then a cooldown that
                            // survives quick away/back track changes.
                            await _delay(NetworkRetryDelay, token).ConfigureAwait(false);
                            token.ThrowIfCancellationRequested();
                            continue;
                        }
                        SetCooldown(identity);
                        return;

                    default:
                        // A completed miss is not retried remotely in this
                        // generation.
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
    }

    private void AcceptLocal(Run run, LyricsResolution resolution)
    {
        switch (resolution.Kind)
        {
            case LyricsResolutionKind.Synced:
                // A local hit is the cache's own timed file, so it outranks the
                // online fallback and may replace it late in the same song.
                Publish(run, resolution, RankAppleSynced);
                break;
            case LyricsResolutionKind.Unsynced:
                Publish(run, resolution, RankUnsynced);
                break;
            case LyricsResolutionKind.NetworkUnavailable:
                Publish(run, resolution, RankNetworkFailure);
                break;
            case LyricsResolutionKind.NotFound:
            case LyricsResolutionKind.PendingDuration:
                // A local miss is not a result: the ladder and remote still owe
                // an answer.
                break;
        }
    }

    private LyricsResolution SafeLocal(Track track, DateTimeOffset start)
    {
        try
        {
            return _localCached(track, start);
        }
        catch (Exception)
        {
            return new LyricsResolution(LyricsResolutionKind.NotFound);
        }
    }

    private void Publish(Run run, LyricsResolution resolution, int rank, bool force = false)
    {
        Action<long, LyricsResolution>? handler;
        lock (_gate)
        {
            if (!IsCurrentLocked(run))
                return;

            if (!force && rank <= run.BestRank)
                return;

            run.BestRank = rank;
            run.Best = resolution;
            handler = Changed;
        }

        handler?.Invoke(run.Generation, resolution);
    }

    private bool IsCurrent(Run run)
    {
        lock (_gate)
            return IsCurrentLocked(run);
    }

    private bool IsCurrentLocked(Run run)
        => ReferenceEquals(_run, run) && !run.Cts.IsCancellationRequested;

    private TimeSpan? CooldownRemaining(string identity)
    {
        lock (_gate)
        {
            if (!_remoteCooldown.TryGetValue(identity, out var until))
                return null;

            var remaining = until - _utcNow();
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    private void SetCooldown(string identity)
    {
        lock (_gate)
            _remoteCooldown[identity] = _utcNow() + NetworkRetryDelay;
    }

    private static string RecordingKey(Track track) => string.Join(
        "\u241F",
        track.Title.Trim(),
        track.Artist.Trim(),
        Math.Round(track.Duration.TotalSeconds, 0).ToString("F0", CultureInfo.InvariantCulture));

    private sealed class Run
    {
        public Run(Track track, long generation, DateTimeOffset startedAt)
        {
            Track = track;
            Generation = generation;
            TrackStart = startedAt;
        }

        public Track Track { get; set; }
        public long Generation { get; }
        public DateTimeOffset TrackStart { get; }
        public CancellationTokenSource Cts { get; } = new();
        public LyricsResolution Best { get; set; } = new(LyricsResolutionKind.Loading);
        public int BestRank { get; set; } = -1;
        public bool EnrichRunning { get; set; }
        public bool EnrichPending { get; set; }
    }
}
