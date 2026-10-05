using System.Windows;
using APMLyrics.Config;

namespace APMLyrics.Ui;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _initial;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _initial = settings;

        ModeMulti.IsChecked = settings.Mode == DisplayMode.MultiLine;
        ModeSingle.IsChecked = settings.Mode == DisplayMode.SingleLine;
        FontSizeSlider.Value = settings.FontSize;
        Radius.Value = settings.NeighbourRadius;
        Backdrop.Value = settings.BackdropOpacity;
        Dim.IsChecked = settings.DimNeighbours;
        Through.IsChecked = settings.ClickThrough;

        Apply.Click += (_, _) => Applied?.Invoke(Build());
    }

    public event Action<AppSettings>? Applied;

    private AppSettings Build() => _initial with
    {
        Mode = ModeMulti.IsChecked == true ? DisplayMode.MultiLine : DisplayMode.SingleLine,
        NeighbourRadius = (int)Radius.Value,
        FontSize = FontSizeSlider.Value,
        BackdropOpacity = Backdrop.Value,
        DimNeighbours = Dim.IsChecked == true,
        ClickThrough = Through.IsChecked == true,
    };
}
