using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KDesktop.Views;

/// <summary>
/// <c>v:TrimmedTip.IsEnabled="True"</c> on a control showing a text (a hotkey button): when the text does not fit and ends
/// with «…», hovering it shows it whole; when it fits, no tooltip. The text is the first one drawn inside the control.
/// </summary>
public static class TrimmedTip
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TrimmedTip), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        // A tooltip has to be there for it to try opening; it is filled (or stopped) each time.
        element.ToolTip = string.Empty;
        element.ToolTipOpening += (_, args) =>
        {
            var text = TextOf(element);
            if (text is null || !IsTrimmed(text, element))
            {
                args.Handled = true;
                return;
            }
            element.ToolTip = text.Text;
        };
    }

    private static TextBlock? TextOf(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { Text.Length: > 0 } text) return text;
            if (TextOf(child) is { } found) return found;
        }
        return null;
    }

    private static bool IsTrimmed(TextBlock text, FrameworkElement element)
    {
        var whole = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(text).PixelsPerDip);
        // The room: what the text got, and no more than the control has inside its padding and border.
        var room = text.ActualWidth;
        if (element is Control c) room = Math.Min(room, c.ActualWidth - c.Padding.Left - c.Padding.Right - c.BorderThickness.Left - c.BorderThickness.Right);
        return whole.WidthIncludingTrailingWhitespace > room + 0.5;
    }
}
