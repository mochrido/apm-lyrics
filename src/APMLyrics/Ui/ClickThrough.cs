using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace APMLyrics.Ui;

/// <summary>
/// Toggles WS_EX_TRANSPARENT so the overlay stops receiving mouse input.
/// The tray menu is the only way back, because a click-through window cannot be clicked.
/// </summary>
public static class ClickThrough
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    public static void Apply(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
            return;

        var style = GetWindowLong(handle, GwlExStyle);

        // Always keep it out of Alt+Tab and non-activating; OBS and gaming
        // capture both behave better with these set.
        style |= WsExToolWindow | WsExNoActivate;
        style = enabled
            ? style | WsExTransparent
            : style & ~WsExTransparent;

        SetWindowLong(handle, GwlExStyle, style);
    }
}
