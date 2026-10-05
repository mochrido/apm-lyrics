using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using APMLyrics.Config;

namespace APMLyrics.Ui;

public partial class SettingsWindow : Window
{
    private const string ColorTip = "Colour name or #RRGGBB";
    private const string ColorTipInvalid = "Not a colour. Use a name like White or #RRGGBB";

    private readonly AppSettings _initial;

    // Last colour value each box held that the overlay can actually render. Build()
    // reads these, never the raw text: the boxes publish live, so a half-typed
    // value must not reach the overlay, which parses colours unguarded.
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
        TextColorBox.Text = _textColor = settings.TextColor;
        CurrentLineColorBox.Text = _currentLineColor = settings.CurrentLineColor;
        BackdropColorBox.Text = _backdropColor = settings.BackdropColor;
        NeighbourOpacity.Value = settings.NeighbourOpacity;
        Backdrop.Value = settings.BackdropOpacity;
        Dim.IsChecked = settings.DimNeighbours;
        Through.IsChecked = settings.ClickThrough;

        foreach (var box in new[] { TextColorBox, CurrentLineColorBox, BackdropColorBox })
            box.ToolTip = ColorTip;
        _defaultBoxBorder = TextColorBox.BorderBrush;

        // Spec section 6: "Changes apply live." Every control reports a change the
        // moment the user makes it, so the overlay updates without a confirmation
        // step. There is no Apply button: with live updates it would be a control
        // that does nothing (antislop R-26), so the window carries a Close instead.
        // Handlers are attached after the initial values are set, so seeding the
        // controls cannot publish a change the user never made.
        ModeMulti.Checked += OnAnyChange;
        ModeSingle.Checked += OnAnyChange;
        Radius.ValueChanged += OnAnyChange;
        FontFamilyPicker.SelectionChanged += OnAnyChange;
        FontSizeSlider.ValueChanged += OnAnyChange;
        TextColorBox.TextChanged += OnColorTextChanged;
        CurrentLineColorBox.TextChanged += OnColorTextChanged;
        BackdropColorBox.TextChanged += OnColorTextChanged;
        NeighbourOpacity.ValueChanged += OnAnyChange;
        Backdrop.ValueChanged += OnAnyChange;
        Dim.Checked += OnAnyChange;
        Dim.Unchecked += OnAnyChange;
        Through.Checked += OnAnyChange;
        Through.Unchecked += OnAnyChange;

        CloseButton.Click += (_, _) => Close();
    }

    public event Action<AppSettings>? Applied;

    private void OnAnyChange(object sender, RoutedEventArgs e) => Applied?.Invoke(Build());

    /// <summary>
    /// Colour boxes need more care than the other controls. The overlay parses
    /// these strings with ColorConverter, which throws on a partial hex value, so
    /// publishing every keystroke would crash the app mid-word ("Re" is not yet
    /// "Red"). Only a value the overlay can render is published; anything else is
    /// flagged on the box and the overlay keeps the last good colour.
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
        TextColor = _textColor,
        CurrentLineColor = _currentLineColor,
        BackdropColor = _backdropColor,
        BackdropOpacity = Backdrop.Value,
        NeighbourOpacity = NeighbourOpacity.Value,
        DimNeighbours = Dim.IsChecked == true,
        ClickThrough = Through.IsChecked == true,
    };
}
