using System.Windows;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Native;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The box to write to an assistant (stage 3.6, plan §4: the overlay never takes the focus, so the box is a window of its
/// own). Above the character when it is known where it is (its menu, a middle click), at the bottom of the monitor with
/// the mouse otherwise (a hotkey). It takes the foreground; Enter sends and closes, Escape or clicking elsewhere closes.
/// </summary>
public partial class AssistantWriteWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly Action<string> _send;
    private readonly UiNative.POINT? _at;
    private bool _closing;

    /// <param name="placeholder">«Escribe a Lander…».</param>
    /// <param name="at">The point in screen pixels it opens above (the character), or null.</param>
    internal AssistantWriteWindow(string placeholder, ImageSource? icon, Action<string> send, UiNative.POINT? at)
    {
        InitializeComponent();
        _send = send;
        _at = at;
        Input.PlaceholderText = placeholder;
        AutomationProperties.SetName(Input, placeholder);
        AssistantIcon.Source = icon;
        Fallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Appear();
        Deactivated += (_, _) => Leave();
    }

    private void Appear()
    {
        var hwnd = UiNative.Handle(this);
        var at = _at ?? UiNative.CursorPosition();
        var work = UiNative.WorkAreaAt(at);
        if (UiNative.GetWindowRect(hwnd, out var r))
        {
            // The window has a transparent margin around the box (for the shadow): the box itself is inside.
            var scale = UiNative.GetDpiForWindow(hwnd) / 96.0;
            var margin = (int)(Root.Margin.Top * scale);
            int x, y;
            if (_at is { } point)
            {
                x = point.X - r.Width / 2;
                y = point.Y - r.Height + margin - (int)(8 * scale);
                if (y < work.Top) y = point.Y - margin + (int)(8 * scale);
            }
            else
            {
                x = (work.Left + work.Right - r.Width) / 2;
                y = work.Bottom - r.Height - (int)(64 * scale);
            }
            x = Math.Clamp(x, work.Left - margin, Math.Max(work.Left - margin, work.Right - r.Width + margin));
            y = Math.Clamp(y, work.Top - margin, Math.Max(work.Top - margin, work.Bottom - r.Height + margin));
            UiNative.MoveWindow(hwnd, x, y);
        }
        // K-Desktop was not in front (a hotkey, a click on the overlay of the other process): take it to type at once.
        if (!UiNative.SetForegroundWindow(hwnd)) Core.Platform.WindowTools.ForceForeground(hwnd);
        Activate();
        Input.Focus();
        Keyboard.Focus(Input);
        var d = TimeSpan.FromMilliseconds(200);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, d) { EasingFunction = EaseOut });
    }

    private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        SendButton.IsEnabled = Input.Text.Trim().Length > 0;

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Send();
                break;
            case Key.Escape:
                e.Handled = true;
                Leave();
                break;
        }
    }

    private void OnSend(object sender, RoutedEventArgs e) => Send();

    private void Send()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0) return;
        _send(text);
        Leave();
    }

    /// <summary>Closes it, with a short fade.</summary>
    public void Leave()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
    }
}
