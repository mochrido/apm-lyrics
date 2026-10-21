using APMLyrics.Playback;
using Xunit;

namespace APMLyrics.Tests;

public class PlaybackClockTests
{
    private class FakeTime : ITimeSource
    {
        public TimeSpan Now { get; set; }
    }

    private static PlaybackSnapshot Snap(
        long sequence,
        double positionSeconds,
        bool isPlaying = true,
        double rate = 1.0,
        long generation = 0,
        TimeSpan observedAt = default,
        DateTimeOffset? lastUpdated = null,
        double durationSeconds = 0)
        => new(
            generation,
            sequence,
            TimeSpan.FromSeconds(positionSeconds),
            TimeSpan.FromSeconds(durationSeconds),
            lastUpdated ?? DateTimeOffset.UnixEpoch,
            isPlaying,
            rate,
            observedAt);

    [Fact]
    public void Returns_the_synced_position_immediately_after_sync()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10));
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    [Fact]
    public void Interpolates_between_ticks()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10));
        t.Now = TimeSpan.FromMilliseconds(500);
        Assert.Equal(TimeSpan.FromSeconds(10.5), clock.Position);
    }

    [Fact]
    public void Honours_a_non_default_rate()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10, rate: 1.5));
        t.Now = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(13), clock.Position);
    }

    [Fact]
    public void Invalid_rate_is_treated_as_normal_speed()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10, rate: 0));
        t.Now = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(12), clock.Position);

        clock.Sync(Snap(2, 12, rate: -3, observedAt: t.Now));
        t.Now = TimeSpan.FromSeconds(4);
        Assert.Equal(TimeSpan.FromSeconds(14), clock.Position);
    }

    [Fact]
    public void Seek_backwards_reanchors()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 80));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 20, observedAt: t.Now));

        Assert.Equal(TimeSpan.FromSeconds(20), clock.Position);
    }

    [Fact]
    public void Seek_forwards_reanchors()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 20));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 80, observedAt: t.Now));

        Assert.Equal(TimeSpan.FromSeconds(80), clock.Position);
    }

    [Fact]
    public void Repeated_snapshot_does_not_rewind()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        var updated = DateTimeOffset.UnixEpoch.AddSeconds(5);
        clock.Sync(Snap(1, 20, lastUpdated: updated));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 20, observedAt: t.Now, lastUpdated: updated));

        Assert.Equal(TimeSpan.FromSeconds(21), clock.Position);
    }

    [Fact]
    public void Changed_position_with_same_timestamp_is_accepted()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        var sameTimestamp = DateTimeOffset.UnixEpoch.AddSeconds(5);
        clock.Sync(Snap(1, 80, lastUpdated: sameTimestamp));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 20, observedAt: t.Now, lastUpdated: sameTimestamp));

        Assert.Equal(TimeSpan.FromSeconds(20), clock.Position);
    }

    [Fact]
    public void Provider_timestamp_regression_is_not_a_seek()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 20, lastUpdated: DateTimeOffset.UnixEpoch.AddSeconds(10)));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 20, observedAt: t.Now, lastUpdated: DateTimeOffset.UnixEpoch.AddSeconds(4)));

        Assert.Equal(TimeSpan.FromSeconds(21), clock.Position);
    }

    [Fact]
    public void State_only_pause_preserves_interpolated_position()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 20));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 20, isPlaying: false, observedAt: t.Now));

        Assert.Equal(TimeSpan.FromSeconds(21), clock.Position);

        t.Now = TimeSpan.FromSeconds(40);
        Assert.Equal(TimeSpan.FromSeconds(21), clock.Position);
    }

    [Fact]
    public void Paused_seek_with_changed_position_reanchors()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 20));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 20, isPlaying: false, observedAt: t.Now));

        t.Now = TimeSpan.FromSeconds(2);
        clock.Sync(Snap(3, 40, isPlaying: false, observedAt: t.Now));

        Assert.Equal(TimeSpan.FromSeconds(40), clock.Position);
    }

    [Fact]
    public void Pause_resume_excludes_paused_time()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10));

        t.Now = TimeSpan.Zero;
        clock.Sync(Snap(2, 10, isPlaying: false, observedAt: t.Now));
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);

        t.Now = TimeSpan.FromSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);

        clock.Sync(Snap(3, 10, isPlaying: true, observedAt: t.Now));
        t.Now = TimeSpan.FromSeconds(31);
        Assert.Equal(TimeSpan.FromSeconds(11), clock.Position);
    }

    [Fact]
    public void Older_sequence_is_ignored()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(5, 80));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(4, 5, observedAt: t.Now));

        Assert.Equal(TimeSpan.FromSeconds(81), clock.Position);
    }

    [Fact]
    public void Older_generation_is_ignored()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Reset(generation: 7);
        clock.Sync(Snap(1, 30, generation: 7));

        t.Now = TimeSpan.FromSeconds(1);
        clock.Sync(Snap(2, 5, generation: 6, observedAt: t.Now));

        Assert.Equal(TimeSpan.FromSeconds(31), clock.Position);
    }

    [Fact]
    public void Reset_returns_the_clock_to_zero_for_a_new_track()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 200));
        t.Now = TimeSpan.FromSeconds(2);
        _ = clock.Position; // 202s

        clock.Reset(generation: 2);
        t.Now = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void Reset_accepts_the_new_track_first_tick_from_any_position()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 200));
        clock.Reset(generation: 2);
        clock.Sync(Snap(1, 3, generation: 2));
        t.Now = TimeSpan.FromMilliseconds(500);
        Assert.Equal(TimeSpan.FromSeconds(3.5), clock.Position);
    }

    [Fact]
    public void Compensates_observation_age_while_playing()
    {
        // The snapshot was taken at ObservedAt but reaches the clock 300 ms later.
        var t = new FakeTime { Now = TimeSpan.FromMilliseconds(300) };
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10, observedAt: TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(10.3), clock.Position);
    }

    [Fact]
    public void Does_not_compensate_a_paused_snapshot()
    {
        var t = new FakeTime { Now = TimeSpan.FromMilliseconds(300) };
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10, isPlaying: false, observedAt: TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    [Fact]
    public void Ignores_absurd_observation_age()
    {
        // A stale buffered snapshot must not be extrapolated by minutes.
        var t = new FakeTime { Now = TimeSpan.FromSeconds(90) };
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 10, observedAt: TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    [Fact]
    public void Unknown_duration_does_not_clamp_position()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 50, durationSeconds: 0));

        Assert.Equal(TimeSpan.FromSeconds(50), clock.Position);
    }

    [Fact]
    public void Negative_position_clamps_to_zero()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, -5));

        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void Position_beyond_known_duration_clamps_to_duration()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(Snap(1, 200, durationSeconds: 139));

        Assert.Equal(TimeSpan.FromSeconds(139), clock.Position);
    }

    [Fact]
    public void System_time_source_advances()
    {
        var time = new SystemTimeSource();
        var first = time.Now;
        Thread.Sleep(20);
        Assert.True(time.Now > first);
    }
}
