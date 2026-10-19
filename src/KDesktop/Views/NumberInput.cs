using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KDesktop.Views;

/// <summary>
/// <c>v:NumberInput.IsEnabled="True"</c> on a TextBox bound to a whole number (milliseconds…): only digits can be typed
/// or pasted, and leaving it empty puts a 0.
/// </summary>
public static class NumberInput
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NumberInput), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;
        box.MaxLength = 7;
        InputMethod.SetIsInputMethodEnabled(box, false);
        box.PreviewTextInput += (_, args) => args.Handled = !args.Text.All(char.IsAsciiDigit);
        // A space would get through PreviewTextInput.
        box.PreviewKeyDown += (_, args) => args.Handled |= args.Key == Key.Space;
        DataObject.AddPastingHandler(box, (_, args) =>
        {
            if (args.DataObject.GetData(DataFormats.UnicodeText) is not string text || text.Trim().Length == 0 || !text.Trim().All(char.IsAsciiDigit))
                args.CancelCommand();
        });
        // Before LostFocus, which is when a LostFocus binding saves the text.
        box.PreviewLostKeyboardFocus += (_, _) => Fill(box);
    }

    /// <summary>An empty number box gets its 0 (also called before the focus is cleared, which skips PreviewLostKeyboardFocus).</summary>
    public static void Fill(TextBox box)
    {
        if (GetIsEnabled(box) && string.IsNullOrWhiteSpace(box.Text)) box.Text = "0";
    }
}
