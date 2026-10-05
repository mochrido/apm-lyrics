using System.Windows;
using APMLyrics.Config;
using H.NotifyIcon;

namespace APMLyrics.Ui;

/// <summary>
/// The only way to control the app once the overlay is click-through.
/// Every item either does something real or is not present.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIcon(OverlayWindow overlay, Func<AppSettings> get, Action<AppSettings> set)
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "APM Lyrics",
            Icon = System.Drawing.SystemIcons.Application,
        };

        var menu = new System.Windows.Controls.ContextMenu();

        void Add(string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        var overlayItem = new System.Windows.Controls.MenuItem();
        void SyncOverlayLabel()
        {
            overlayItem.Header = overlay.Visibility == Visibility.Visible
                ? "Hide overlay"
                : "Show overlay";
        }

        overlayItem.Click += (_, _) =>
        {
            overlay.Visibility = overlay.Visibility == Visibility.Visible
                ? Visibility.Hidden
                : Visibility.Visible;
            SyncOverlayLabel();
        };
        menu.Items.Add(overlayItem);

        var clickThroughItem = new System.Windows.Controls.MenuItem();
        void SyncClickThroughLabel()
        {
            var enabled = get().ClickThrough;
            clickThroughItem.Header = enabled
                ? "Click-through: ON (overlay ignores the mouse)"
                : "Click-through: OFF";
        }

        clickThroughItem.Click += (_, _) =>
        {
            var next = get() with { ClickThrough = !get().ClickThrough };
            set(next);
            overlay.SetClickThrough(next.ClickThrough);
            SyncClickThroughLabel();
        };
        menu.Items.Add(clickThroughItem);

        var modeItem = new System.Windows.Controls.MenuItem();
        void SyncModeLabel()
        {
            modeItem.Header = get().Mode == DisplayMode.MultiLine
                ? "Display: multi-line"
                : "Display: single-line";
        }

        modeItem.Click += (_, _) =>
        {
            var next = get() with
            {
                Mode = get().Mode == DisplayMode.MultiLine ? DisplayMode.SingleLine : DisplayMode.MultiLine,
            };
            set(next);
            overlay.ApplySettings(next);
            SyncModeLabel();
        };
        menu.Items.Add(modeItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        Add("Settings", () =>
        {
            var window = new SettingsWindow(get());
            window.Applied += updated =>
            {
                set(updated);
                overlay.ApplySettings(updated);
                overlay.SetClickThrough(updated.ClickThrough);
                SyncClickThroughLabel();
                SyncModeLabel();
            };
            window.Show();
        });

        Add("Quit", () => Application.Current.Shutdown());

        _icon.ContextMenu = menu;
        SyncOverlayLabel();
        SyncClickThroughLabel();
        SyncModeLabel();
        _icon.ForceCreate();
    }

    public void Dispose() => _icon.Dispose();
}
