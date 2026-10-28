using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KDesktop.Views;

/// <summary>
/// <c>v:NumberInput.IsEnabled="True"</c> on a TextBox bound to a number (milliseconds, seconds…): only digits can be typed
/// or pasted, and leaving it empty puts a 0. <c>v:NumberInput.Maximum</c> brings a bigger number down to it on leaving,
/// <c>v:NumberInput.Decimals="True"</c> also lets one decimal point in (a comma is typed as the point the binding reads)
/// and <c>v:NumberInput.Negative="True"</c> a minus in front.
/// </summary>
public static class NumberInput
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NumberInput), new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
        "Maximum", typeof(double), typeof(NumberInput), new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.RegisterAttached(
        "Decimals", typeof(bool), typeof(NumberInput), new PropertyMetadata(false));

    public static readonly DependencyProperty NegativeProperty = DependencyProperty.RegisterAttached(
        "Negative", typeof(bool), typeof(NumberInput), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    public static double GetMaximum(DependencyObject d) => (double)d.GetValue(MaximumProperty);

    public static void SetMaximum(DependencyObject d, double value) => d.SetValue(MaximumProperty, value);

    public static bool GetDecimals(DependencyObject d) => (bool)d.GetValue(DecimalsProperty);

    public static void SetDecimals(DependencyObject d, bool value) => d.SetValue(DecimalsProperty, value);

    public static bool GetNegative(DependencyObject d) => (bool)d.GetValue(NegativeProperty);

    public static void SetNegative(DependencyObject d, bool value) => d.SetValue(NegativeProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;
        box.MaxLength = 7;
        InputMethod.SetIsInputMethodEnabled(box, false);
        box.PreviewTextInput += (_, args) =>
        {
            if (GetDecimals(box) && args.Text is "," or ".")
            {
                args.Handled = true;
                if (!box.Text.Replace(box.SelectedText, "", StringComparison.Ordinal).Contains('.', StringComparison.Ordinal))
                    box.SelectedText = ".";
                box.CaretIndex = box.SelectionStart + box.SelectionLength;
                box.SelectionLength = 0;
                return;
            }
            if (GetNegative(box) && args.Text == "-")
            {
                args.Handled = box.SelectionStart != 0 || box.Text.Replace(box.SelectedText, "", StringComparison.Ordinal).StartsWith('-');
                return;
            }
            args.Handled = !args.Text.All(char.IsAsciiDigit);
        };
        // A space would get through PreviewTextInput.
        box.PreviewKeyDown += (_, args) => args.Handled |= args.Key == Key.Space;
        DataObject.AddPastingHandler(box, (_, args) =>
        {
            if (args.DataObject.GetData(DataFormats.UnicodeText) is not string text || Parse(box, text.Trim()) is null)
                args.CancelCommand();
        });
        // Before LostFocus, which is when a LostFocus binding saves the text.
        box.PreviewLostKeyboardFocus += (_, _) => Fill(box);
    }

    /// <summary>A number pasted in: digits, and with decimals one point or comma.</summary>
    private static double? Parse(TextBox box, string text)
    {
        if (text.Length == 0) return null;
        if (GetDecimals(box)) text = text.Replace(',', '.');
        var minus = GetNegative(box) && text.StartsWith('-');
        if (minus) text = text[1..];
        if (text.Length == 0 || !text.All(c => char.IsAsciiDigit(c) || c == '.') || text.Count(c => c == '.') > (GetDecimals(box) ? 1 : 0)) return null;
        return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? (minus ? -value : value) : null;
    }

    /// <summary>
    /// An empty number box gets its 0 and one over the maximum comes down to it (also called before the focus is cleared,
    /// which skips PreviewLostKeyboardFocus).
    /// </summary>
    public static void Fill(TextBox box)
    {
        if (!GetIsEnabled(box)) return;
        if (string.IsNullOrWhiteSpace(box.Text) || box.Text is "." or "-" or "-.")
        {
            box.Text = "0";
            return;
        }
        var maximum = GetMaximum(box);
        if (!double.IsNaN(maximum) && double.TryParse(box.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) && value > maximum)
            box.Text = maximum.ToString(CultureInfo.InvariantCulture);
    }
}
