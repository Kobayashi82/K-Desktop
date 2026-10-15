using System.Windows;
using System.Windows.Threading;

namespace KDesktop.Views;

/// <summary>
/// Captures the next hotkey (keyboard or mouse) through the global hook, so even combinations that Windows
/// or other apps would normally swallow (Win+X, mouse side buttons…) can be recorded.
/// Escape cancels. Only one capture can be active at a time.
/// </summary>
public sealed class HotkeyCapture
{
    private static HotkeyCapture? _active;
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _preview;
    private readonly Action<string?> _completed;
    private readonly Window? _window;
    private bool _done;

    private HotkeyCapture(Action<string> preview, Action<string?> completed, Window? window)
    {
        _dispatcher = Application.Current.Dispatcher;
        _preview = preview;
        _completed = completed;
        _window = window;
    }

    /// <summary>Starts capturing. <paramref name="completed"/> receives the hotkey, or null when cancelled.</summary>
    public static HotkeyCapture Start(Action<string> preview, Action<string?> completed, Window? window = null)
    {
        _active?.Cancel();
        var capture = new HotkeyCapture(preview, completed, window);
        _active = capture;
        if (window is not null) window.Deactivated += capture.OnDeactivated;
        App.Current.Router.SetCapture(capture.OnInput);
        return capture;
    }

    public static void CancelActive() => _active?.Cancel();

    private bool OnInput(string hotkey, bool final)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_done) return;
            if (!final)
            {
                _preview(hotkey);
                return;
            }
            Finish(hotkey.Equals("Escape", StringComparison.OrdinalIgnoreCase) ? null : hotkey);
        });
        return true;
    }

    private void OnDeactivated(object? sender, EventArgs e) => Cancel();

    public void Cancel() => Finish(null);

    private void Finish(string? hotkey)
    {
        if (_done) return;
        _done = true;
        if (ReferenceEquals(_active, this)) _active = null;
        App.Current.Router.SetCapture(null);
        if (_window is not null) _window.Deactivated -= OnDeactivated;
        _completed(hotkey);
    }
}
