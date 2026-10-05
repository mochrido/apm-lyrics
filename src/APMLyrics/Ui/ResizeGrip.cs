using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace APMLyrics.Ui;

/// <summary>
/// Restores edge and corner resizing on the borderless overlay.
///
/// Why this exists: with WindowStyle=None and AllowsTransparency=True, WPF
/// draws no non-client frame, so Windows never gets the hit-test answers that
/// normally come from that frame. Measured on the shipped 0.1.0 window, every
/// edge returned HTCLIENT, which is "this is just content" - there was nothing
/// to grab, so the overlay could be dragged (drag is managed code) but never
/// resized. ResizeMode=CanResize sets the WS_THICKFRAME style bit, which is
/// necessary but not sufficient: the bit allows resizing, but the edge
/// detection has to come from somewhere, and on a borderless window it has to
/// come from here.
///
/// Spec section 5 promises "native edge and corner resize, including the 8px
/// hit targets", so the margin below is that promise.
/// </summary>
public static class ResizeGrip
{
    private const int WmNcHitTest = 0x0084;

    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    /// <summary>Size of the grabbable border, in DIPs. Spec section 5 says 8px.</summary>
    private const double Margin = 8.0;

    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
            return;

        HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg != WmNcHitTest)
                return IntPtr.Zero;

            var result = HitTest(window, l);
            if (result == HtClient)
                return IntPtr.Zero; // inside the window: let WPF handle it

            handled = true;
            return new IntPtr(result);
        });
    }

    /// <summary>
    /// Answers WM_NCHITTEST for a point given in physical screen coordinates.
    /// Converted with PointFromScreen so the margin is DIP-correct on any DPI,
    /// then compared against the live window size.
    /// </summary>
    private static int HitTest(Window window, IntPtr lParam)
    {
        // lParam packs x and y as two signed 16-bit values, in screen pixels.
        var screen = new Point((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
        var point = window.PointFromScreen(screen);

        var left = point.X <= Margin;
        var right = point.X >= window.ActualWidth - Margin;
        var top = point.Y <= Margin;
        var bottom = point.Y >= window.ActualHeight - Margin;

        if (top && left) return HtTopLeft;
        if (top && right) return HtTopRight;
        if (bottom && left) return HtBottomLeft;
        if (bottom && right) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;
        return HtClient;
    }
}
