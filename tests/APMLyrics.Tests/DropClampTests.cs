using APMLyrics.Ui;
using Xunit;

namespace APMLyrics.Tests;

public class DropClampTests
{
    [Fact]
    public void Window_fully_on_taskbar_is_lifted_above_workarea()
    {
        var (left, top) = DropClamp.Clamp(100, 1040, 560, 160, workAreaBottom: 1040, virtualBottom: 1080);
        Assert.Equal(100, left);
        Assert.Equal(880, top);
    }

    [Fact]
    public void Window_already_above_workarea_unchanged()
    {
        // Bottom 960 clears the 1040 work-area bottom, so nothing to lift.
        var (_, top) = DropClamp.Clamp(100, 800, 560, 160, workAreaBottom: 1040, virtualBottom: 1080);
        Assert.Equal(800, top);
    }

    [Fact]
    public void Taller_than_workarea_pins_to_top()
    {
        // A window taller than the work area cannot sit fully above it; pin it
        // to the virtual top so it is never buried below the taskbar.
        var (_, top) = DropClamp.Clamp(100, 2000, 560, 1100, workAreaBottom: 1040, virtualBottom: 1080);
        Assert.Equal(0, top);
    }

    [Fact]
    public void Secondary_monitor_below_workarea_passes_through()
    {
        // WorkArea.Bottom is the primary monitor's; a window already below it on
        // a secondary monitor must not be yanked up.
        var (_, top) = DropClamp.Clamp(100, 1200, 560, 160, workAreaBottom: 1040, virtualBottom: 2160);
        Assert.Equal(1200, top);
    }

    [Fact]
    public void Never_moves_up_when_partially_overlapping()
    {
        // Overlaps the taskbar by 120px: lifted so the bottom clears it.
        var (_, top) = DropClamp.Clamp(100, 1000, 560, 160, workAreaBottom: 1040, virtualBottom: 1080);
        Assert.Equal(880, top);
    }
}
