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
        EventManager.RegisterClassHandler(typeof(MainWindow), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnWindowMouseDown), true);
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnComboDown), true);
        EventManager.RegisterClassHandler(typeof(ComboBoxItem), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnComboItemUp));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TextBox), TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnWindowSized));
    }

    // ── fixed-size windows ───────────────────────────────────────────────

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Fixed = [];
    private delegate nint SubclassProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    // Kept alive: Windows calls it for as long as the windows live.
    private static readonly SubclassProc FixedProc = FixedHitTest;

    // WPF-UI's windows answer the resize borders themselves (after any HwndSource hook), even with NoResize: the
    // dialogs could be made smaller than their buttons. A fixed window is subclassed, outside all of that, and its
    // resize borders become a plain border (on the first layout of each window).
    private static void OnWindowSized(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Window window || Fixed.TryGetValue(window, out _)) return;
        // WPF-UI's message boxes are sized to their text, but can be resized; their buttons go cancel first.
        if (window is Wpf.Ui.Controls.MessageBox box)
        {
            box.ResizeMode = ResizeMode.NoResize;
            CancelFirst(box);
        }
        if (window.ResizeMode is not (ResizeMode.NoResize or ResizeMode.CanMinimize)) return;
        if (PresentationSource.FromVisual(window) is not System.Windows.Interop.HwndSource source) return;
        Fixed.Add(window, new object());
        SetWindowSubclass(source.Handle, FixedProc, 1, 0);
    }

    private static nint FixedHitTest(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        const int WM_NCHITTEST = 0x84, WM_NCDESTROY = 0x82, HTLEFT = 10, HTBOTTOMRIGHT = 17, HTBORDER = 18;
        if (msg == WM_NCDESTROY) RemoveWindowSubclass(hwnd, FixedProc, id);
        var result = DefSubclassProc(hwnd, msg, wParam, lParam);
        return msg == WM_NCHITTEST && result is >= HTLEFT and <= HTBOTTOMRIGHT ? HTBORDER : result;
    }

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>The buttons of a message box from right to left: the one that does it on the right, «Cancelar» on the left.</summary>
    private static void CancelFirst(Wpf.Ui.Controls.MessageBox box)
    {
        // The grid that holds the buttons of the answers (the title bar's close button is alone in its own).
        var grids = new List<Grid>();
        Collect(box);
        foreach (var grid in grids.Distinct().Where(g => g.ColumnDefinitions.Count > 1 && grids.Count(x => x == g) > 1))
        {
            var last = grid.ColumnDefinitions.Count - 1;
            foreach (UIElement child in grid.Children) Grid.SetColumn(child, last - Grid.GetColumn(child));
            var widths = grid.ColumnDefinitions.Select(c => c.Width).Reverse().ToList();
            for (var i = 0; i <= last; i++) grid.ColumnDefinitions[i].Width = widths[i];
        }

        void Collect(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Wpf.Ui.Controls.Button && VisualTreeHelper.GetParent(child) is Grid grid) grids.Add(grid);
                else Collect(child);
            }
        }
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

    // ── leaving a field: Enter, Escape or a click anywhere else ─────────

    private static void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key is not (Key.Enter or Key.Escape) || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Field(e.OriginalSource as DependencyObject) is not { } field) return;
        // Enter writes a new line in a multi-line box: there only Escape leaves.
        if (e.Key == Key.Enter && e.OriginalSource is System.Windows.Controls.TextBox { AcceptsReturn: true } or System.Windows.Controls.RichTextBox) return;
        if (field is ComboBox { IsDropDownOpen: true }) return;
        e.Handled = true;
        Leave(field);
    }

    // A click on the background (or anything that doesn't take the focus itself) leaves the field being typed in.
    private static void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Field(Keyboard.FocusedElement as DependencyObject) is not { } field || e.OriginalSource is not DependencyObject source) return;
        // Inside the field, or in a drop-down (its own or another window's): nothing to do.
        if (Inside(source, field) || PresentationSource.FromDependencyObject(source) is { } from && !ReferenceEquals(from, PresentationSource.FromVisual(field))) return;
        field.Dispatcher.BeginInvoke(() =>
        {
            // Whatever was clicked took the focus by itself (a button, a list, another box): nothing to do.
            if (Keyboard.FocusedElement is DependencyObject now && (ReferenceEquals(now, field) || Inside(now, field))) Leave(field);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>The text box or combo that has the focus in <paramref name="source"/> (the text part of an editable combo is the combo).</summary>
    private static Control? Field(DependencyObject? source) => source switch
    {
        TextBoxBase box => (box.TemplatedParent as ComboBox) ?? (Control)box,
        ComboBox combo => combo,
        _ => null,
    };

    private static bool Inside(DependencyObject element, DependencyObject field)
    {
        for (var d = element; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, field)) return true;
        return false;
    }

    /// <summary>
    /// Keyboard focus first, then the logical one: a box bound on LostFocus gets its last fix (an emptied number becomes
    /// 0) before it is saved, and WPF-UI hides the X only if the keyboard has already gone when the box loses focus.
    /// </summary>
    private static void Leave(Control field)
    {
        var scope = FocusManager.GetFocusScope(field);
        // Clearing the focus raises LostFocus (which saves) without PreviewLostKeyboardFocus first.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox box) NumberInput.Fill(box);
        Keyboard.ClearFocus();
        if (scope is not null) FocusManager.SetFocusedElement(scope, null);
    }
}
