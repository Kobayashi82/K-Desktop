using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace KDesktop.Views;

/// <summary>
/// Small behaviours every window gets: a pressed button shows K-Desktop's gold text, and Enter in a text box or
/// combo that has nothing else to do with it leaves the field (so its value is applied, like with Tab).
/// </summary>
public static class InputPolish
{
    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.Button), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnButtonLoaded));
        // Only the main window: in the dialogs Enter presses their default button.
        EventManager.RegisterClassHandler(typeof(MainWindow), UIElement.KeyDownEvent, new KeyEventHandler(OnWindowKeyDown));
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnComboDown), true);
        EventManager.RegisterClassHandler(typeof(ComboBoxItem), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnComboItemUp));
    }

    // ── combos that close at once ────────────────────────────────────────

    // A combo near the bottom of the window opens its list upwards, over the mouse: releasing the click that opened
    // it then picked the item under the pointer and closed the list. The release right after opening is ignored.
    private static readonly DependencyProperty OpenedAtProperty = DependencyProperty.RegisterAttached(
        "OpenedAt", typeof(long), typeof(InputPolish), new PropertyMetadata(0L));

    private static void OnComboDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox combo && !combo.IsDropDownOpen && ItemsControl.ContainerFromElement(combo, (DependencyObject)e.OriginalSource) is null)
            combo.SetValue(OpenedAtProperty, Environment.TickCount64);
    }

    private static void OnComboItemUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ItemsControlFromItemContainer((DependencyObject)sender) is ComboBox combo &&
            Environment.TickCount64 - (long)combo.GetValue(OpenedAtProperty) < 400)
            e.Handled = true;
    }

    // ── pressed buttons ──────────────────────────────────────────────────

    // WPF-UI's pressed trigger binds the text colour to the button's PressedForeground through TemplatedParent, which
    // a button that is not itself inside a template doesn't have: the text went black. An animation outranks that
    // trigger, so while the button is pressed its text is held at the accent colour.
    private static readonly DependencyProperty PressedProperty = DependencyProperty.RegisterAttached(
        "Pressed", typeof(bool), typeof(InputPolish), new PropertyMetadata(false, OnPressedChanged));

    private static void OnButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button button && BindingOperations.GetBinding(button, PressedProperty) is null)
            button.SetBinding(PressedProperty, new Binding(nameof(ButtonBase.IsPressed)) { Source = button });
    }

    private static void OnPressedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Wpf.Ui.Controls.Button button) return;
        // Gold text on a gold (or red) button would not read: those keep WPF-UI's own colours.
        if (e.NewValue is true && button.Appearance is ControlAppearance.Secondary or ControlAppearance.Transparent
            && button.TryFindResource("Kd.AccentText") is Brush accent)
        {
            var hold = new ObjectAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
            hold.KeyFrames.Add(new DiscreteObjectKeyFrame(accent, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            button.BeginAnimation(Control.ForegroundProperty, hold);
        }
        else button.BeginAnimation(Control.ForegroundProperty, null);
    }

    // ── Enter leaves the field ───────────────────────────────────────────

    private static void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.OriginalSource is not DependencyObject source) return;
        var field = source as Control;
        if (source is TextBoxBase box)
        {
            if (box is System.Windows.Controls.TextBox { AcceptsReturn: true } or System.Windows.Controls.RichTextBox) return;
            // The text part of an editable combo: the combo is the field.
            field = (box.TemplatedParent as ComboBox) ?? (Control)box;
        }
        else if (source is not ComboBox) return;
        if (field is ComboBox { IsDropDownOpen: true }) return;

        var scope = FocusManager.GetFocusScope(field);
        if (scope is not null) FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
    }
}
