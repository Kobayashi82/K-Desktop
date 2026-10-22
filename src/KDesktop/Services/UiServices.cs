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

    /// <summary>Runs «Asistente: acción» (assistant id, extra id) on the UI thread.</summary>
    public Action<string, string>? AssistantExtra { get; set; }

    /// <summary>Runs «Asistente: preguntar» (assistant id or empty, the order or empty) on the UI thread.</summary>
    public Action<string, string>? AssistantAsk { get; set; }

    /// <summary>Runs «Asistente: decir» (assistant id or empty, the text) on the UI thread.</summary>
    public Action<string, string>? AssistantSayer { get; set; }

    /// <summary>Runs «Asistente: cerebro» on the UI thread.</summary>
    public Action<Core.Model.AssistantBrainOp>? AssistantBrainer { get; set; }

    /// <summary>Runs «Dictar» (any thread).</summary>
    public Action<Core.Model.SwitchMode>? Dictator { get; set; }

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

        public void SetVoiceText(string text) => owner.UiDispatcher.BeginInvoke(() =>
        {
            if (Current) Generation = owner.Osd.ShowVoice(text);
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

    public void ShowMainWindow() => dispatcher.BeginInvoke(showMainWindow);

    public void RunAssistantExtra(string assistant, string extra) => dispatcher.BeginInvoke(() => AssistantExtra?.Invoke(assistant, extra));

    public void AskAssistant(string assistant, string text) => dispatcher.BeginInvoke(() => AssistantAsk?.Invoke(assistant, text));

    public void AssistantSay(string assistant, string text) => dispatcher.BeginInvoke(() => AssistantSayer?.Invoke(assistant, text));

    public void AssistantBrain(Core.Model.AssistantBrainOp op) => dispatcher.BeginInvoke(() => AssistantBrainer?.Invoke(op));

    public void Dictate(Core.Model.SwitchMode mode) => Dictator?.Invoke(mode);

    public void RunOnUi(Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void RequestExit() => dispatcher.BeginInvoke(requestExit);

    public bool SetClipboardImage(Core.Platform.BgraImage image) => dispatcher.Invoke(() =>
    {
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(image.Width, image.Height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(bitmap);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    });

    public Task<Core.Platform.ScreenRect?> SelectRegionAsync(Core.Platform.BgraImage desktop, Core.Platform.ScreenRect bounds) =>
        dispatcher.Invoke(() => RegionSelector.SelectAsync(desktop, bounds));

    public bool SetClipboardText(string text) => dispatcher.Invoke(() =>
    {
        // Another program may have the clipboard open for a moment.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (text.Length == 0) System.Windows.Clipboard.Clear();
                else System.Windows.Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    });

    private sealed class Callback(Action action) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) action();
        }
    }
}
