using System.Windows.Threading;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Launcher;
using KDesktop.Osd;
using KDesktop.QuickMenus;

namespace KDesktop.Services;

/// <summary>Thread-safe implementation of <see cref="IUiServices"/> on top of the WPF dispatcher.</summary>
public sealed class UiServices(Dispatcher dispatcher, Func<AppSettings> settings, Action showMainWindow, Action requestExit) : IUiServices
{
    private OsdWindow? _osd;

    // For the nested voice OSD (primary-constructor parameters are not members).
    private Dispatcher UiDispatcher => dispatcher;
    private AppSettings Settings => settings();

    public QuickMenuController? QuickMenus { get; set; }

    public LauncherController? Launcher { get; set; }

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

    public IVoiceOsd ShowVoiceOsd(string text)
    {
        var osd = new VoiceOsd(this);
        dispatcher.BeginInvoke(() => osd.Generation = Osd.ShowVoice(text));
        return osd;
    }

    /// <summary>
    /// The voice OSD of one session. Everything goes through the dispatcher; it only touches the OSD while its own
    /// message is the one shown (another message may have replaced it).
    /// </summary>
    private sealed class VoiceOsd(UiServices owner) : IVoiceOsd
    {
        private int _finished;

        /// <summary>Generation of its message on the OSD (UI thread only).</summary>
        public int Generation { get; set; } = -1;

        private bool Current => Generation == owner.Osd.Generation;

        public void SetLevel(float level) => owner.UiDispatcher.BeginInvoke(() =>
        {
            if (Current) owner.Osd.SetLevel(level);
        });

        public void SetText(string text) => owner.UiDispatcher.BeginInvoke(() =>
        {
            if (Current) Generation = owner.Osd.ShowMessage(text, OsdKind.Info, null);
        });

        public void Finish(string text, OsdKind kind)
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0) return;
            owner.UiDispatcher.BeginInvoke(() =>
                owner.Osd.ShowMessage(text, kind, TimeSpan.FromSeconds(Math.Max(2.5, owner.Settings.OsdSeconds))));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0) return;
            owner.UiDispatcher.BeginInvoke(() => owner.Osd.HideMessage(Generation));
        }
    }

    public void ShowQuickMenu(string menuId, ForegroundWindow previousForeground) =>
        dispatcher.BeginInvoke(() => QuickMenus?.Show(menuId, previousForeground));

    public void ShowLauncher(LauncherAction options, ForegroundWindow previousForeground) =>
        dispatcher.BeginInvoke(() => Launcher?.Show(options, previousForeground));

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
