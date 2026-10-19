using APMLyrics.Playback;
using Xunit;

namespace APMLyrics.Tests;

public class PlaybackClockTests
{
    private class FakeTime : ITimeSource
    {
        public TimeSpan Now { get; set; }
    }

    [Fact]
    public void Returns_the_synced_position_immediately_after_sync()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    [Fact]
    public void Interpolates_between_ticks()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromMilliseconds(500);
        Assert.Equal(TimeSpan.FromSeconds(10.5), clock.Position);
    }

    [Fact]
    public void Stops_interpolating_while_paused()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromMilliseconds(200);
        clock.Pause();
        t.Now = TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(10.2), clock.Position);
    }

    [Fact]
    public void Resumes_from_the_paused_position()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromMilliseconds(200);
        clock.Pause();
        t.Now = TimeSpan.FromSeconds(5);
        _ = clock.Position; // held at 10.2s while paused
        clock.Resume();
        t.Now = TimeSpan.FromMilliseconds(5300);
        Assert.Equal(TimeSpan.FromSeconds(10.5), clock.Position);
    }

    [Fact]
    public void Resuming_while_already_playing_does_not_rewind()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromSeconds(1);
        _ = clock.Position; // 11s
        clock.Resume(); // already playing: must not re-anchor and jump back
        t.Now = TimeSpan.FromSeconds(1.5);
        Assert.Equal(TimeSpan.FromSeconds(11.5), clock.Position);
    }

    [Fact]
    public void System_time_source_advances()
    {
        var time = new SystemTimeSource();
        var first = time.Now;
        Thread.Sleep(20);
        Assert.True(time.Now > first);
    }

    [Fact]
    public void Honours_a_non_default_rate()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.5);
        t.Now = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(13), clock.Position);
    }

    [Fact]
    public void Never_goes_backwards_when_a_coarse_tick_arrives_late()
    {
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(10), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromSeconds(1);
        _ = clock.Position; // advance to 11s
        clock.Sync(TimeSpan.FromSeconds(10.4), isPlaying: true, rate: 1.0); // late tick
        t.Now = TimeSpan.FromSeconds(1);
        Assert.True(clock.Position >= TimeSpan.FromSeconds(11));
    }

    [Fact]
    public void Reset_returns_the_clock_to_zero_for_a_new_track()
    {
        // A track change: the old song sat at 200s and the new one starts at 0.
        // Sync alone cannot go backwards (that rule protects against late coarse
        // ticks), so the reset must be explicit and total.
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(200), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromSeconds(2);
        _ = clock.Position; // 202s

        clock.Reset();
        t.Now = TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.Zero, clock.Position);
    }

    [Fact]
    public void Reset_accepts_the_new_track_first_tick_from_any_position()
    {
        // After Reset, the very next Sync must be able to jump forwards from
        // zero (the normal path) — nothing of the old anchor may survive.
        var t = new FakeTime();
        var clock = new PlaybackClock(t);
        clock.Sync(TimeSpan.FromSeconds(200), isPlaying: true, rate: 1.0);
        clock.Reset();
        clock.Sync(TimeSpan.FromSeconds(3), isPlaying: true, rate: 1.0);
        t.Now = TimeSpan.FromMilliseconds(500);
        Assert.Equal(TimeSpan.FromSeconds(3.5), clock.Position);
    }
}
