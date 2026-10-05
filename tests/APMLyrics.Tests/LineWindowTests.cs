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
    public void Fitted_font_size_is_unchanged_at_the_design_size()
    {
        // The configured size is the size for the design window, so a window at
        // exactly 560x160 renders exactly what the user asked for.
        Assert.Equal(28.0, LineWindow.FitFontSize(28, 560, 160, 3));
    }

    [Fact]
    public void Fitted_font_size_shrinks_with_the_window()
    {
        // Half the height means roughly half the text, so a short strip stays
        // readable instead of clipping the line.
        var small = LineWindow.FitFontSize(28, 280, 80, 3);
        Assert.True(small < 28, $"expected smaller than 28, got {small}");
        Assert.True(small > 6, $"expected still readable, got {small}");
    }

    [Fact]
    public void Fitted_font_size_grows_with_the_window()
    {
        // Roughly double, not exactly: the border padding is a fixed size, so it
        // does not scale with the window. The range is what matters here.
        var large = LineWindow.FitFontSize(28, 1120, 320, 3);
        Assert.InRange(large, 50, 65);
    }

    [Fact]
    public void Fitted_font_size_uses_the_tighter_axis()
    {
        // Wide but very short: the height must win, or the text clips.
        var shortWide = LineWindow.FitFontSize(28, 2000, 80, 3);
        var tallNarrow = LineWindow.FitFontSize(28, 280, 2000, 3);
        Assert.True(shortWide < 28);
        Assert.True(tallNarrow < 28);
    }

    [Fact]
    public void Fitted_font_size_accounts_for_more_neighbour_rows()
    {
        // Five rows must share the same height, so each is smaller than with three.
        var threeRows = LineWindow.FitFontSize(28, 560, 160, 3);
        var fiveRows = LineWindow.FitFontSize(28, 560, 160, 5);
        Assert.True(fiveRows < threeRows, $"expected {fiveRows} < {threeRows}");
    }

    [Fact]
    public void Fitted_font_size_never_returns_something_unreadable()
    {
        // A window dragged as small as it can go still yields usable text.
        var tiny = LineWindow.FitFontSize(28, 40, 32, 3);
        Assert.True(tiny >= 6, $"expected a readable floor, got {tiny}");
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
