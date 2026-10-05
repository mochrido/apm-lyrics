using APMLyrics.Core;
using APMLyrics.Ui;
using Xunit;

namespace APMLyrics.Tests;

public class LineWindowTests
{
    [Fact]
    public void Centers_on_the_current_line_when_there_is_room()
    {
        var w = LineWindow.Compute(lineCount: 10, currentIndex: 5, radius: 1);
        Assert.Equal(4, w.First);
        Assert.Equal(6, w.Last);
        Assert.Equal(5, w.Current);
    }

    [Fact]
    public void Clamps_at_the_start_of_the_song()
    {
        var w = LineWindow.Compute(lineCount: 10, currentIndex: 0, radius: 1);
        Assert.Equal(0, w.First);
        Assert.Equal(2, w.Last);
    }

    [Fact]
    public void Clamps_at_the_end_of_the_song()
    {
        var w = LineWindow.Compute(lineCount: 10, currentIndex: 9, radius: 1);
        Assert.Equal(7, w.First);
        Assert.Equal(9, w.Last);
    }

    [Fact]
    public void Handles_a_song_shorter_than_the_window()
    {
        var w = LineWindow.Compute(lineCount: 1, currentIndex: 0, radius: 1);
        Assert.Equal(0, w.First);
        Assert.Equal(0, w.Last);
        Assert.Equal(0, w.Current);
    }

    [Fact]
    public void Handles_an_empty_document()
    {
        var w = LineWindow.Compute(lineCount: 0, currentIndex: -1, radius: 1);
        Assert.Equal(0, w.First);
        Assert.Equal(-1, w.Last);
        Assert.Equal(-1, w.Current);
    }

    [Fact]
    public void Progress_is_zero_at_begin_and_one_at_end()
    {
        var line = new LyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(4), "x", null);
        Assert.Equal(0.0, LineWindow.Progress(line, TimeSpan.Zero));
        Assert.Equal(1.0, LineWindow.Progress(line, TimeSpan.FromSeconds(4)));
        Assert.Equal(0.5, LineWindow.Progress(line, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Progress_is_clamped_outside_the_line()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "x", null);
        Assert.Equal(0.0, LineWindow.Progress(line, TimeSpan.FromSeconds(1)));
        Assert.Equal(1.0, LineWindow.Progress(line, TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void Progress_handles_a_zero_length_line()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "x", null);
        Assert.Equal(1.0, LineWindow.Progress(line, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Current_index_is_the_last_line_that_has_begun()
    {
        var lines = new[]
        {
            new LyricLine(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "one", null),
            new LyricLine(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), "two", null),
            new LyricLine(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9), "three", null),
        };

        Assert.Equal(-1, LineWindow.CurrentIndex(lines, TimeSpan.FromSeconds(0.5)));
        Assert.Equal(0, LineWindow.CurrentIndex(lines, TimeSpan.FromSeconds(1)));
        Assert.Equal(0, LineWindow.CurrentIndex(lines, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, LineWindow.CurrentIndex(lines, TimeSpan.FromSeconds(3)));
        Assert.Equal(2, LineWindow.CurrentIndex(lines, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Current_index_of_an_empty_document_is_minus_one()
    {
        Assert.Equal(-1, LineWindow.CurrentIndex(Array.Empty<LyricLine>(), TimeSpan.FromSeconds(5)));
    }
}
