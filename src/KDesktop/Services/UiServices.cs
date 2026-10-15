using System.Windows.Threading;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Osd;
using KDesktop.QuickMenus;

namespace KDesktop.Services;

/// <summary>Thread-safe implementation of <see cref="IUiServices"/> on top of the WPF dispatcher.</summary>
public sealed class UiServices(Dispatcher dispatcher, Func<AppSettings> settings, Action showMainWindow, Action requestExit) : IUiServices
{
    private OsdWindow? _osd;

    public QuickMenuController? QuickMenus { get; set; }

    private OsdWindow Osd
    {
        get
        {
            _osd ??= new OsdWindow();
            _osd.Position = settings().OsdPosition;
            return _osd;
        }
    }

    public void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        dispatcher.BeginInvoke(() => Osd.ShowMessage(text, kind, duration ?? TimeSpan.FromSeconds(Math.Max(0.5, settings().OsdSeconds))));
    }

    public IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null)
    {
        var generation = dispatcher.Invoke(() => Osd.ShowMessage(text, OsdKind.Info, null, countdownTo));
        return new Callback(() => dispatcher.BeginInvoke(() => Osd.HideMessage(generation)));
    }

    public void ShowQuickMenu(string menuId, ForegroundWindow previousForeground) =>
        dispatcher.BeginInvoke(() => QuickMenus?.Show(menuId, previousForeground));

    public void ShowMainWindow() => dispatcher.BeginInvoke(showMainWindow);

    public void RunOnUi(Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void RequestExit() => dispatcher.BeginInvoke(requestExit);

    private sealed class Callback(Action action) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) action();
        }
    }
}
