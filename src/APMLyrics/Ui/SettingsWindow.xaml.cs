using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using APMLyrics.Config;
using APMLyrics.Core;

namespace APMLyrics.Ui;

public partial class SettingsWindow : Window
{
    private const string ColorTip = "Colour name or #RRGGBB";
    private const string ColorTipInvalid = "Not a colour. Use a name like White or #RRGGBB";

    // A fixed three-line sample so the preview always shows something.
    private static readonly LyricsDoc Sample = new("sample", null, new List<LyricLine>
    {
        new(TimeSpan.Zero, TimeSpan.FromSeconds(3), "And I know it's not much", null),
        new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), "But it's the best I'll ever do", null),
        new(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9), "And I know it's not much", null),
    });

    private readonly AppSettings _initial;

    // Last colour value each box held that the overlay can actually render.
    private string _textColor;
    private string _currentLineColor;
    private string _backdropColor;

    private Brush? _defaultBoxBorder;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _initial = settings;

        ModeMulti.IsChecked = settings.Mode == DisplayMode.MultiLine;
        ModeSingle.IsChecked = settings.Mode == DisplayMode.SingleLine;
        Radius.Value = settings.NeighbourRadius;

        // Font family dropdown: seeded from installed families, plus whatever the
        // saved setting names in case it is no longer installed.
        var families = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!families.Contains(settings.FontFamily, StringComparer.OrdinalIgnoreCase))
            families.Insert(0, settings.FontFamily);
        FontFamilyPicker.ItemsSource = families;
        FontFamilyPicker.SelectedItem = families
            .First(f => string.Equals(f, settings.FontFamily, StringComparison.OrdinalIgnoreCase));

        FontSizeSlider.Value = settings.FontSize;
        LockFont.IsChecked = settings.LockFontSize;
        SyncFontSizeLabel();
        TextColorBox.Text = _textColor = settings.TextColor;
        CurrentLineColorBox.Text = _currentLineColor = settings.CurrentLineColor;
        BackdropColorBox.Text = _backdropColor = settings.BackdropColor;
        NeighbourOpacity.Value = settings.NeighbourOpacity;
        Backdrop.Value = settings.BackdropOpacity;
        Dim.IsChecked = settings.DimNeighbours;
        Through.IsChecked = settings.ClickThrough;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText.Text = $"Version {version}";

        foreach (var box in new[] { TextColorBox, CurrentLineColorBox, BackdropColorBox })
            box.ToolTip = ColorTip;
        _defaultBoxBorder = TextColorBox.BorderBrush;

        // Sidebar navigation: each page is a panel toggled by the selection.
        Nav.SelectionChanged += (_, _) => ShowPage(Nav.SelectedItem as ListBoxItem);
        ShowPage(Nav.SelectedItem as ListBoxItem);

        // Changes apply live; there is no Apply button. Handlers are attached
        // after the initial values are set, so seeding the controls cannot
        // publish a change the user never made.
        ModeMulti.Checked += OnAnyChange;
        ModeSingle.Checked += OnAnyChange;
        Radius.ValueChanged += OnAnyChange;
        FontFamilyPicker.SelectionChanged += OnAnyChange;
        FontSizeSlider.ValueChanged += OnAnyChange;
        LockFont.Checked += OnLockToggled;
        LockFont.Unchecked += OnLockToggled;
        TextColorBox.TextChanged += OnColorTextChanged;
        CurrentLineColorBox.TextChanged += OnColorTextChanged;
        BackdropColorBox.TextChanged += OnColorTextChanged;
        NeighbourOpacity.ValueChanged += OnAnyChange;
        Backdrop.ValueChanged += OnAnyChange;
        Dim.Checked += OnAnyChange;
        Dim.Unchecked += OnAnyChange;
        Through.Checked += OnAnyChange;
        Through.Unchecked += OnAnyChange;

        RefreshPreview();
    }

    public event Action<AppSettings>? Applied;

    private void ShowPage(ListBoxItem? item)
    {
        var tag = item?.Tag as string;
        AppearancePage.Visibility = tag == "AppearancePage" ? Visibility.Visible : Visibility.Collapsed;
        ColorsPage.Visibility = tag == "ColorsPage" ? Visibility.Visible : Visibility.Collapsed;
        BehaviourPage.Visibility = tag == "BehaviourPage" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "AboutPage" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One slider, two meanings: the label reads "Font size (locked)" while the
    /// lock is on, and the slider then edits LockedFontSize directly. The first
    /// time the lock turns on it seeds from the current auto-fit base size.
    /// </summary>
    private void SyncFontSizeLabel() =>
        FontSizeLabel.Text = LockFont.IsChecked == true
            ? "Font size (locked)"
            : "Font size (10 to 96)";

    private void OnLockToggled(object sender, RoutedEventArgs e)
    {
        SyncFontSizeLabel();
        OnAnyChange(sender, e);
    }

    private void OnAnyChange(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
        Applied?.Invoke(Build());
    }

    private void RefreshPreview()
    {
        var s = Build();
        foreach (var panel in new[] { Preview, ColorPreview })
        {
            panel.Apply(s);
            panel.Show(Sample, "But it's the best I'll ever do", string.Empty, loading: false);
            panel.Tick(TimeSpan.FromSeconds(4.5)); // middle line current
        }
    }

    /// <summary>
    /// Colour boxes publish live, so a half-typed value must not reach the
    /// overlay, which parses colours unguarded. Only a renderable value is
    /// published; anything else is flagged and the overlay keeps the last good.
    /// </summary>
    private void OnColorTextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        var candidate = box.Text.Trim();

        if (!CanRender(candidate))
        {
            box.BorderBrush = Brushes.OrangeRed;
            box.ToolTip = ColorTipInvalid;
            return;
        }

        box.BorderBrush = _defaultBoxBorder;
        box.ToolTip = ColorTip;

        if (ReferenceEquals(box, TextColorBox))
            _textColor = candidate;
        else if (ReferenceEquals(box, CurrentLineColorBox))
            _currentLineColor = candidate;
        else
            _backdropColor = candidate;

        OnAnyChange(sender, e);
    }

    /// <summary>
    /// Validates with the same call the overlay uses, so "valid here" and
    /// "renderable there" cannot drift apart.
    /// </summary>
    private static bool CanRender(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            return ColorConverter.ConvertFromString(text) is Color;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private AppSettings Build() => _initial with
    {
        Mode = ModeMulti.IsChecked == true ? DisplayMode.MultiLine : DisplayMode.SingleLine,
        NeighbourRadius = (int)Radius.Value,
        FontFamily = FontFamilyPicker.SelectedItem as string ?? _initial.FontFamily,
        FontSize = FontSizeSlider.Value,
        LockFontSize = LockFont.IsChecked == true,
        // While locked, the slider edits the locked size; while auto-fitting it
        // stays the base size, and turning the lock on next time seeds from it.
        LockedFontSize = LockFont.IsChecked == true ? FontSizeSlider.Value : _initial.LockedFontSize,
        TextColor = _textColor,
        CurrentLineColor = _currentLineColor,
        BackdropColor = _backdropColor,
        BackdropOpacity = Backdrop.Value,
        NeighbourOpacity = NeighbourOpacity.Value,
        DimNeighbours = Dim.IsChecked == true,
        ClickThrough = Through.IsChecked == true,
    };
}
