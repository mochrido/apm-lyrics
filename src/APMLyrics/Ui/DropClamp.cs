namespace APMLyrics.Ui;

/// <summary>
/// Pure math for the taskbar drag fix: the overlay must never end up buried in
/// the taskbar area. Windows 11's taskbar typically wins the z-order battle
/// even over topmost windows, so the guarantee is positional — a drop that
/// would cross into the taskbar is lifted so the window sits on top of it.
/// </summary>
public static class DropClamp
{
    /// <param name="workAreaBottom">Primary work-area bottom (top of the taskbar).</param>
    /// <param name="virtualBottom">Virtual-screen bottom, for secondary monitors below the primary.</param>
    /// <returns>The clamped (left, top); left always passes through unchanged.</returns>
    public static (double Left, double Top) Clamp(
        double left, double top, double width, double height, double workAreaBottom, double virtualBottom)
    {
        // A window fully contained in the region below the primary work area
        // lives on a secondary monitor with its own taskbar-free edge; leave it
        // alone. It must fit entirely in that region — a window spilling past
        // the virtual bottom is not a secondary-monitor case.
        if (top >= workAreaBottom && top + height <= virtualBottom)
            return (left, top);

        var bottom = top + height;
        if (bottom <= workAreaBottom)
            return (left, top);

        // Lift so the bottom edge clears the taskbar…
        var lifted = workAreaBottom - height;
        // …unless the window is taller than the work area, in which case pin it
        // to the virtual top so it never lands below the work area entirely.
        if (lifted < 0)
            lifted = 0;

        return (left, lifted);
    }
}
