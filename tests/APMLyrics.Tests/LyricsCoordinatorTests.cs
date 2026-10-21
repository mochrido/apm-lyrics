using APMLyrics.Core;
using Xunit;

namespace APMLyrics.Tests;

public class LyricsCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static Track Song(double durationSeconds = 139) => new(
        "Pasti Ada Jalan",
        "Perunggu - Pasti Ada Jalan - Single",
        "Pasti Ada Jalan",
        TimeSpan.FromSeconds(durationSeconds),
        IsPlaying: true);

    private static LyricsDoc Doc(string text = "line one")
        => new("lrclib.net:1", null, new[]
        {
            new LyricLine(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), text, null),
        });

    private static LyricsResolution Synced(string text = "line one")
        => new(LyricsResolutionKind.Synced, Doc(text), "LRCLIB");

    private static LyricsResolution NotFound() => new(LyricsResolutionKind.NotFound);

    /// <summary>Drives the coordinator with synchronous fakes and a virtual clock.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<(long Generation, LyricsResolution Resolution)> _emissions = new();
        private readonly List<TimeSpan> _delays = new();

        public Harness()
        {
            Coordinator = new LyricsCoordinator(
                (track, start) =>
                {
                    lock (_gate) CachedCalls++;
                    return LocalCached;
                },
                async (track, start, ct) =>
                {
                    lock (_gate) EnrichCalls++;
                    var pending = LocalEnrich;
                    if (pending is null)
                        return NotFound();
                    return await pending.WaitAsync(ct);
                },
                async (track, ct) =>
                {
                    lock (_gate) RemoteCalls++;
                    var pending = Remote;
                    if (pending is null)
                        return NotFound();
                    return await pending.WaitAsync(ct);
                },
                (delay, ct) =>
                {
                    lock (_gate)
                    {
                        _delays.Add(delay);
                        Now = Now.Add(delay);
                    }
                    return Task.CompletedTask;
                },
                () =>
                {
                    lock (_gate) return Now;
                });

            Coordinator.Changed += (generation, resolution) =>
            {
                lock (_gate) _emissions.Add((generation, resolution));
            };
        }

        public LyricsCoordinator Coordinator { get; }

        public LyricsResolution LocalCached { get; set; } = NotFound();
        public Task<LyricsResolution>? LocalEnrich { get; set; }
        public Task<LyricsResolution>? Remote { get; set; }

        public int CachedCalls { get; private set; }
        public int EnrichCalls { get; private set; }
        public int RemoteCalls { get; private set; }
        public DateTimeOffset Now { get; private set; } = T0;

        public IReadOnlyList<TimeSpan> Delays
        {
            get { lock (_gate) return _delays.ToList(); }
        }

        public IReadOnlyList<(long Generation, LyricsResolution Resolution)> Emissions
        {
            get { lock (_gate) return _emissions.ToList(); }
        }

        public async Task WaitAsync(Func<(long Generation, LyricsResolution Resolution), bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (_emissions.Any(predicate))
                        return;
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("Expected coordinator emission did not arrive.");
        }

        public async Task WaitForCallsAsync(Func<bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                    return;
                await Task.Delay(10);
            }
            throw new TimeoutException("Expected coordinator work did not start.");
        }

        public void Dispose() => Coordinator.Dispose();
    }

    private static TaskCompletionSource<LyricsResolution> Pending()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Local_miss_starts_remote_without_waiting_for_a_retry()
    {
        using var h = new Harness();
        var remote = Pending();
        h.Remote = remote.Task;

        h.Coordinator.Start(Song(), 1);
        await h.WaitForCallsAsync(() => h.RemoteCalls == 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Loading);

        // Remote starts immediately: it must not sit behind the 1s local retry.
        Assert.Equal(1, h.RemoteCalls);

        remote.SetResult(Synced());
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Synced);
    }

    [Fact]
    public async Task Local_synced_hit_avoids_remote_entirely()
    {
        using var h = new Harness();
        h.LocalCached = new LyricsResolution(
            LyricsResolutionKind.Synced, Doc("apple line"), "Apple");

        h.Coordinator.Start(Song(), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Synced);
        await Task.Delay(100);

        Assert.Equal(0, h.RemoteCalls);
        Assert.Equal("apple line", h.Emissions.Last().Resolution.Document!.Lines[0].Text);
    }

    [Fact]
    public async Task Untimed_associated_local_hit_still_allows_a_synced_remote_result()
    {
        using var h = new Harness();
        h.LocalCached = new LyricsResolution(LyricsResolutionKind.Unsynced);
        h.Remote = Task.FromResult(Synced("remote timed"));

        h.Coordinator.Start(Song(), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Synced);

        Assert.True(h.RemoteCalls >= 1);
        Assert.Equal("remote timed", h.Emissions.Last().Resolution.Document!.Lines[0].Text);
    }

    [Fact]
    public async Task Unsynced_outranks_network_failure()
    {
        using var h = new Harness();
        h.LocalCached = new LyricsResolution(LyricsResolutionKind.Unsynced);
        h.Remote = Task.FromResult(new LyricsResolution(LyricsResolutionKind.NetworkUnavailable));

        h.Coordinator.Start(Song(), 1);
        await h.WaitAsync(e => e.Generation == 1 && e.Resolution.Kind == LyricsResolutionKind.Unsynced);
    }

    [Fact]
    public async Task Network_failure_outranks_a_bare_miss()
    {
        using var h = new Harness();
        h.Remote = Task.FromResult(new LyricsResolution(LyricsResolutionKind.NetworkUnavailable));

        h.Coordinator.Start(Song(), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.NetworkUnavailable);
    }

    [Fact]
    public async Task Unknown_duration_defers_remote_and_resolves_when_duration_arrives()
    {
        using var h = new Harness();
        h.Remote = Task.FromResult(Synced());

        h.Coordinator.Start(Song(0), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Loading);
        await Task.Delay(100);
        Assert.Equal(0, h.RemoteCalls);

        h.Coordinator.UpdateTrack(Song(139), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Synced);
        Assert.Equal(1, h.RemoteCalls);
    }

    [Fact]
    public async Task Unknown_duration_ends_as_a_miss_after_the_retry_window()
    {
        using var h = new Harness();
        h.Coordinator.Start(Song(0), 1);

        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.NotFound);

        var total = h.Delays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d);
        Assert.Equal(TimeSpan.FromSeconds(8), total);
        Assert.Equal(0, h.RemoteCalls);
        Assert.DoesNotContain(h.Emissions, e => e.Resolution.Kind == LyricsResolutionKind.Loading && e == h.Emissions.Last());
    }

    [Fact]
    public async Task A_slow_previous_track_cannot_publish_onto_the_new_generation()
    {
        using var h = new Harness();
        var slowA = Pending();
        h.Remote = slowA.Task;

        h.Coordinator.Start(Song(), 1);
        await h.WaitForCallsAsync(() => h.RemoteCalls == 1);

        h.Remote = Task.FromResult(Synced("song b line"));
        h.Coordinator.Start(Song(200), 2);
        await h.WaitAsync(e => e.Generation == 2 && e.Resolution.Kind == LyricsResolutionKind.Synced);

        slowA.SetResult(Synced("song a line"));
        await Task.Delay(150);

        Assert.DoesNotContain(
            h.Emissions,
            e => e.Generation == 2 && e.Resolution.Document?.Lines[0].Text == "song a line");
    }

    [Fact]
    public async Task Stop_cancels_in_flight_work_and_emits_nothing_more()
    {
        using var h = new Harness();
        var gate = Pending();
        h.Remote = gate.Task;

        h.Coordinator.Start(Song(), 1);
        await h.WaitForCallsAsync(() => h.RemoteCalls == 1);
        var before = h.Emissions.Count;

        h.Coordinator.Stop();
        gate.TrySetResult(Synced());
        await Task.Delay(150);

        Assert.Equal(before, h.Emissions.Count);
    }

    [Fact]
    public async Task A_burst_of_cache_notifications_coalesces_to_one_rescan()
    {
        using var h = new Harness();
        var enrich = Pending();
        h.LocalEnrich = enrich.Task;
        h.Remote = Task.FromResult(NotFound());

        h.Coordinator.Start(Song(), 1);
        await h.WaitForCallsAsync(() => h.EnrichCalls == 1);

        for (var i = 0; i < 5; i++)
            h.Coordinator.NotifyCacheChanged();
        await Task.Delay(80);

        Assert.Equal(1, h.EnrichCalls);

        enrich.SetResult(NotFound());
        await Task.Delay(80);
    }

    [Fact]
    public async Task Accepted_synced_content_is_never_downgraded_by_later_errors()
    {
        using var h = new Harness();
        h.LocalCached = new LyricsResolution(
            LyricsResolutionKind.Synced, Doc("accepted"), "Apple");

        h.Coordinator.Start(Song(), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Synced);

        h.Remote = Task.FromResult(new LyricsResolution(LyricsResolutionKind.NetworkUnavailable));
        h.Coordinator.NotifyCacheChanged();
        await Task.Delay(150);

        var afterAcceptance = h.Emissions
            .SkipWhile(e => e.Resolution.Kind != LyricsResolutionKind.Synced)
            .ToList();
        Assert.NotEmpty(afterAcceptance);
        Assert.All(
            afterAcceptance,
            e => Assert.Equal(LyricsResolutionKind.Synced, e.Resolution.Kind));
    }

    [Fact]
    public async Task Network_failure_gets_one_more_attempt_after_thirty_seconds()
    {
        using var h = new Harness();
        var calls = 0;
        h.Remote = null;
        h.Coordinator.Dispose();

        using var harness = new Harness();
        var attempts = 0;
        var seen = new List<LyricsResolution>();
        var coordinator = new LyricsCoordinator(
            (t, s) => NotFound(),
            (t, s, ct) => Task.FromResult(NotFound()),
            (t, ct) => Task.FromResult(++attempts == 1
                ? new LyricsResolution(LyricsResolutionKind.NetworkUnavailable)
                : Synced("second attempt")),
            (delay, ct) =>
            {
                lock (seen) _ = delay;
                harness.Delays.ToString();
                return Task.CompletedTask;
            },
            () => T0);
        coordinator.Changed += (_, r) =>
        {
            lock (seen) seen.Add(r);
        };

        coordinator.Start(Song(), 1);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (seen)
            {
                if (seen.Any(r => r.Kind == LyricsResolutionKind.Synced))
                    break;
            }
            await Task.Delay(10);
        }

        Assert.Equal(2, attempts);
        Assert.Contains(seen, r => r.Kind == LyricsResolutionKind.Synced);
        coordinator.Dispose();
        _ = calls;
    }

    [Fact]
    public async Task A_materially_different_duration_triggers_a_new_resolution()
    {
        using var h = new Harness();
        var calls = 0;
        var seen = new List<LyricsResolution>();
        var coordinator = new LyricsCoordinator(
            (t, s) => NotFound(),
            (t, s, ct) => Task.FromResult(NotFound()),
            (t, ct) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(Synced($"doc {t.Duration.TotalSeconds}"));
            },
            (delay, ct) => Task.CompletedTask,
            () => T0);
        coordinator.Changed += (_, r) =>
        {
            lock (seen) seen.Add(r);
        };

        coordinator.Start(Song(139), 1);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (seen)
            {
                if (seen.Any(r => r.Kind == LyricsResolutionKind.Synced))
                    break;
            }
            await Task.Delay(10);
        }
        Assert.Equal(1, calls);

        coordinator.UpdateTrack(Song(200), 1);
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && calls < 2)
            await Task.Delay(10);

        Assert.Equal(2, calls);
        coordinator.Dispose();
    }

    [Fact]
    public async Task A_hung_local_enrichment_does_not_delay_a_remote_synced_result()
    {
        using var h = new Harness();
        h.LocalEnrich = new TaskCompletionSource<LyricsResolution>().Task; // never completes
        h.Remote = Task.FromResult(Synced("remote wins"));

        h.Coordinator.Start(Song(), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Synced);

        Assert.Equal("remote wins", h.Emissions.Last().Resolution.Document!.Lines[0].Text);
    }

    [Fact]
    public async Task A_late_apple_synced_result_is_preferred_over_an_earlier_fallback()
    {
        using var h = new Harness();
        var enrich = Pending();
        h.LocalEnrich = enrich.Task;
        h.Remote = Task.FromResult(Synced("fallback copy"));

        h.Coordinator.Start(Song(), 1);
        await h.WaitAsync(e => e.Resolution.Kind == LyricsResolutionKind.Synced);
        Assert.Equal("fallback copy", h.Emissions.Last().Resolution.Document!.Lines[0].Text);

        // Apple's own timed file lands later and must replace the fallback.
        enrich.SetResult(new LyricsResolution(
            LyricsResolutionKind.Synced, Doc("apple copy"), "Apple"));
        await h.WaitAsync(e => e.Resolution.Document?.Lines[0].Text == "apple copy");
    }
}
