using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KDesktop.Views;

/// <summary>
/// A colour as «#RRGGBB», or none (null: «Automático», the theme's <see cref="AutomaticResource"/>): the swatch opens the
/// palette of the assistants' colours, and the box takes one typed by hand (saved as soon as it is a valid one).
/// </summary>
public sealed partial class ColorField : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorField)d).Show()));

    public static readonly DependencyProperty AutomaticResourceProperty = DependencyProperty.Register(nameof(AutomaticResource), typeof(string), typeof(ColorField),
        new PropertyMetadata("Kd.AccentText", (d, _) => ((ColorField)d).Show()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(ColorField));

    /// <summary>«#RRGGBB», or null for «Automático».</summary>
    public string? Color
    {
        get => (string?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The brush of the theme it uses while it has no colour of its own.</summary>
    public string AutomaticResource
    {
        get => (string)GetValue(AutomaticResourceProperty);
        set => SetValue(AutomaticResourceProperty, value);
    }

    /// <summary>What it is, for the tooltip and the screen readers.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ColorField()
    {
        InitializeComponent();
        Colors.ItemsSource = Assistant.ColorSetting.Presets;
        Swatch.Loaded += (_, _) => Show();
    }

    /// <summary>The swatch and the box as the colour is now.</summary>
    private void Show()
    {
        if (Swatch.Template?.FindName("Fill", Swatch) is Border fill)
        {
            if (Parse(Color) is { } own)
            {
                var brush = new SolidColorBrush(own);
                brush.Freeze();
                fill.Background = brush;
            }
            else fill.SetResourceReference(Border.BackgroundProperty, AutomaticResource);
        }
        if (!HexBox.IsKeyboardFocusWithin) HexBox.Text = Parse(Color) is null ? string.Empty : Color!.ToUpperInvariant();
    }

    private static Color? Parse(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;

    /// <summary>A valid colour is saved at once; anything else waits until it is one (emptied: «Automático»).</summary>
    private void OnTyped(object sender, TextChangedEventArgs e)
    {
        if (!HexBox.IsKeyboardFocusWithin) return;
        var text = HexBox.Text.Trim();
        if (text.Length == 0)
        {
            Color = null;
            return;
        }
        if (!text.StartsWith('#')) text = "#" + text;
        if (Parse(text) is not null) Color = text.ToUpperInvariant();
    }

    private void OnHexLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Show();

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex }) return;
        Color = hex;
        Swatch.IsChecked = false;
    }

    private void OnAutomatic(object sender, RoutedEventArgs e)
    {
        Color = null;
        Swatch.IsChecked = false;
    }
}
