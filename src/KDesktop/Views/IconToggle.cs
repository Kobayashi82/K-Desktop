using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;

namespace KDesktop.Views;

/// <summary>
/// An option of a command as a small icon button instead of a switch: one symbol when on (in the accent colour) and
/// another when off, and a tooltip with its name, whether it is on, and what it does.
/// </summary>
public sealed class IconToggle : ToggleButton
{
    public static readonly DependencyProperty OnSymbolProperty =
        DependencyProperty.Register(nameof(OnSymbol), typeof(SymbolRegular), typeof(IconToggle), new PropertyMetadata(SymbolRegular.Empty, (d, _) => ((IconToggle)d).Refresh()));

    public static readonly DependencyProperty OffSymbolProperty =
        DependencyProperty.Register(nameof(OffSymbol), typeof(SymbolRegular), typeof(IconToggle), new PropertyMetadata(SymbolRegular.Empty, (d, _) => ((IconToggle)d).Refresh()));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(IconToggle), new PropertyMetadata(string.Empty, (d, _) => ((IconToggle)d).Refresh()));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(IconToggle), new PropertyMetadata(string.Empty, (d, _) => ((IconToggle)d).Refresh()));

    public SymbolRegular OnSymbol { get => (SymbolRegular)GetValue(OnSymbolProperty); set => SetValue(OnSymbolProperty, value); }
    public SymbolRegular OffSymbol { get => (SymbolRegular)GetValue(OffSymbolProperty); set => SetValue(OffSymbolProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    private readonly SymbolIcon _icon = new() { FontSize = 18 };

    public IconToggle()
    {
        // A subclass doesn't get the implicit style of ToggleButton (the theme's): ask for it.
        SetResourceReference(StyleProperty, typeof(ToggleButton));
        Content = _icon;
        Padding = new Thickness(8, 6, 8, 6);
        Margin = new Thickness(6, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = false;
        Checked += (_, _) => Refresh();
        Unchecked += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
        AutomationProperties.SetName(this, Title);
    }

    private void Refresh()
    {
        var on = IsChecked == true;
        _icon.Symbol = on ? OnSymbol : OffSymbol;
        // Checked, the button turns accent-coloured: the icon goes dark on it to stay visible.
        _icon.SetResourceReference(ForegroundProperty, on ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        ToolTip = new ToolTip
        {
            Content = new StackPanel
            {
                MaxWidth = 320,
                Children =
                {
                    new System.Windows.Controls.TextBlock
                    {
                        Text = $"{Title}: {Loc.T(on ? "common.yes" : "common.no")}",
                        FontWeight = FontWeights.SemiBold,
                    },
                    new System.Windows.Controls.TextBlock { Text = Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) },
                },
            },
        };
        AutomationProperties.SetName(this, Title);
    }
}
