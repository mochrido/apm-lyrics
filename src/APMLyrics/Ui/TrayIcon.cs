using System.Windows;
using APMLyrics.Config;
using APMLyrics.Core;
using H.NotifyIcon;

namespace APMLyrics.Ui;

/// <summary>
/// The only way to control the app once the overlay is click-through.
/// Every item either does something real or is not present.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;

    /// <summary>Update service, attached once by the app; null before AttachUpdates.</summary>
    private UpdateService? _updates;

    /// <summary>Latest known newer release; written from worker threads, read on the UI thread.</summary>
    private volatile ReleaseInfo? _pendingRelease;

    public TrayIcon(OverlayWindow overlay, Func<AppSettings> get, Action<AppSettings> set)
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "APM Lyrics",
            Icon = LoadIcon(),
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
            var window = new SettingsWindow(get(), _updates);
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

    /// <summary>
    /// Wires the update flow into the tray: a balloon when a new release is
    /// found on startup, a "Check for updates…" menu entry, and an "Update
    /// now" entry that appears once a newer release is known.
    /// </summary>
    public void AttachUpdates(UpdateService updates, Action shutdown)
    {
        _updates = updates;

        var menu = (System.Windows.Controls.ContextMenu)_icon.ContextMenu;
        var updateItem = new System.Windows.Controls.MenuItem { Header = "Check for updates…" };

        async void RunUpdate(ReleaseInfo release)
        {
            updateItem.Header = "Downloading update…";
            updateItem.IsEnabled = false;
            var started = await updates.DownloadAndRunInstallerAsync(release, CancellationToken.None);
            if (started)
            {
                shutdown();
                return;
            }

            // Download failed: fall back to the release page in the browser.
            updateItem.Header = "Update available — open download page";
            updateItem.IsEnabled = true;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    release.ReleasePageUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // Best effort; the menu header still tells the user what to do.
            }
        }

        updateItem.Click += async (_, _) =>
        {
            if (updates.State == UpdateState.Available && _pendingRelease is { } release)
            {
                RunUpdate(release);
                return;
            }

            updateItem.Header = "Checking…";
            var found = await updates.CheckAsync(CancellationToken.None);
            if (found && _pendingRelease is { } newer)
            {
                updateItem.Header = $"Version {newer.Tag.TrimStart('v')} available — Update now";
                updateItem.IsEnabled = true;
                RunUpdate(newer);
            }
            else
            {
                updateItem.Header = "Up to date";
            }
        };

        updates.UpdateAvailable += release =>
        {
            _pendingRelease = release;
            // The event may arrive from a worker thread; the tray is a
            // DispatcherObject, so hop to the UI thread.
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                updateItem.Header = $"Version {release.Tag.TrimStart('v')} available — Update now";
                _icon.ShowNotification(
                    title: "APM Lyrics update",
                    message: $"Version {release.Tag.TrimStart('v')} is out. Use the tray menu to update when you're ready.");
            });
        };

        // Insert after the settings entry, before Quit.
        menu.Items.Insert(Math.Max(0, menu.Items.Count - 1), new System.Windows.Controls.Separator());
        menu.Items.Insert(Math.Max(0, menu.Items.Count - 1), updateItem);
    }

    /// <summary>
    /// Loads the tray icon from the embedded resource; the stream is copied
    /// into the Icon and disposed here, not leaked for the process lifetime.
    /// </summary>
    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream);
    }
}
