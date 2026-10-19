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
        // Loaded never reaches class handlers (WPF only broadcasts it to elements with handlers of their own): the
        // button is hooked when it is about to be pressed instead.
        EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.Button), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, _) => HookPressed(s)), true);
        EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.Button), UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, _) => HookPressed(s)), true);
        // Only the main window: in the dialogs Enter presses their default button.
        EventManager.RegisterClassHandler(typeof(MainWindow), UIElement.KeyDownEvent, new KeyEventHandler(OnWindowKeyDown));
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnComboDown), true);
        EventManager.RegisterClassHandler(typeof(ComboBoxItem), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnComboItemUp));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TextBox), TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
    }

    // ── empty is not an error ────────────────────────────────────────────

    // A box bound to a number gets the red error border as soon as it is emptied (an empty text is not a number),
    // while the user is just about to type another value. An empty box never shows it; leaving it puts a value back.
    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox { Text.Length: 0 } box) return;
        box.Dispatcher.BeginInvoke(() =>
        {
            if (box.Text.Length == 0 && Validation.GetHasError(box) && box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty) is { } binding)
                Validation.ClearInvalid(binding);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // ── wheel of lists that don't scroll ─────────────────────────────────

    /// <summary>
    /// For a list inside a scrolling editor that doesn't scroll by itself: hands the wheel to the editor. Not when it
    /// comes from an open drop-down (a combo of an item): the preview event reaches the list first through the combo,
    /// and taking it there left long drop-downs without a way to reach their last options.
    /// </summary>
    public static void ForwardWheel(object sender, MouseWheelEventArgs e, UIElement scroller)
    {
        if (e.Handled) return;
        if (e.OriginalSource is DependencyObject source && sender is Visual list &&
            PresentationSource.FromDependencyObject(source) is { } from && !ReferenceEquals(from, PresentationSource.FromVisual(list))) return;
        e.Handled = true;
        scroller.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
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

    private static void HookPressed(object sender)
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
