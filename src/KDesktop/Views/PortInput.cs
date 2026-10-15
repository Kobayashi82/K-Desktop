using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KDesktop.Views;

/// <summary>
/// <c>v:PortInput.IsEnabled="True"</c> on a TextBox bound to a port: only digits can be typed or pasted, and leaving
/// it with something that is not a port (1–65535) puts the current value back instead of saving it.
/// </summary>
public static class PortInput
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PortInput), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    public static bool IsValid(string? text) => int.TryParse(text, out var port) && port is >= 1 and <= 65535;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;
        box.MaxLength = 5;
        InputMethod.SetIsInputMethodEnabled(box, false);
        box.PreviewTextInput += (_, args) => args.Handled = !args.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(box, (_, args) =>
        {
            if (args.DataObject.GetData(DataFormats.UnicodeText) is not string text || !text.Trim().All(char.IsAsciiDigit))
                args.CancelCommand();
        });
        // Before LostFocus, which is when the binding saves the text.
        box.PreviewLostKeyboardFocus += (_, _) =>
        {
            if (!IsValid(box.Text)) box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        };
        box.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            if (IsValid(box.Text)) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            else box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        };
    }
}
