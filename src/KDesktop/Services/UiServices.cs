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
    private RingWindow? _ring;

    // For the nested voice OSD (primary-constructor parameters are not members).
    private Dispatcher UiDispatcher => dispatcher;
    private AppSettings Settings => settings();

    public QuickMenuController? QuickMenus { get; set; }

    public LauncherController? Launcher { get; set; }
    public ClipboardController? Clipboard { get; set; }

    /// <summary>Runs «Asistente: acción» (assistant id, extra id) on the UI thread.</summary>
    public Action<string, string, bool>? AssistantExtra { get; set; }

    /// <summary>Runs «Asistente: preguntar» (assistant id, the order or empty, whether it comes out) on the UI thread.</summary>
    public Action<string, string, bool>? AssistantAsk { get; set; }

    /// <summary>«Asistente: preguntar» with an answer (assistant id, the order or empty, whether it comes out), on the UI thread.</summary>
    public Func<string, string, bool, CancellationToken, Task<Core.Assistant.Pipe.RepliedMessage?>>? AssistantAskFor { get; set; }

    /// <summary>Shows an answer on its assistant's reading sheet, on the UI thread.</summary>
    public Action<string, string>? AnswerSheet { get; set; }

    /// <summary>Runs an order an assistant programmed, with what was approved, on the UI thread.</summary>
    public Action<EventAsk>? AssistantAskScheduled { get; set; }

    /// <summary>Runs «Asistente: decir» (assistant id or empty, the text) on the UI thread.</summary>
    public Action<string, string, bool, bool>? AssistantSayer { get; set; }

    /// <summary>Runs «Asistente: cerebro» on the UI thread.</summary>
    public Action<Core.Model.AssistantBrainOp>? AssistantBrainer { get; set; }

    /// <summary>Runs «Dictar» (any thread).</summary>
    public Action<Core.Model.SwitchMode>? Dictator { get; set; }

    /// <summary>Runs «Asistente: chat» (assistant id or empty, what to do) on the UI thread.</summary>
    public Action<string, Core.Model.AssistantChatMode>? AssistantChatter { get; set; }

    private OsdWindow Osd
    {
        get
        {
            _osd ??= new OsdWindow();
            var s = settings();
            _osd.Position = s.OsdPosition;
            _osd.Screen = s.OsdScreen;
            _osd.Size = s.OsdSize;
            return _osd;
        }
    }

    private TimeSpan OsdDuration => TimeSpan.FromSeconds(Math.Max(0.5, settings().OsdSeconds));

    /// <summary>
    /// Whether a message of this kind and topic shows now (UI thread): Options › OSD can hide each group, and what to do
    /// while something covers the OSD's monitor (a film, a game).
    /// </summary>
    private bool Allowed(OsdKind kind, OsdTopic topic)
    {
        var s = settings();
        if (!s.OsdEnabled) return false;
        var wanted = kind == OsdKind.Warning ? s.OsdShowWarnings : topic switch
        {
            OsdTopic.Levels => s.OsdShowLevels,
            OsdTopic.Voice => s.OsdShowVoice,
            _ => s.OsdShowActions,
        };
        if (!wanted || s.OsdFullscreen == OsdFullscreen.Always) return wanted;
        if (!Native.UiNative.FullscreenOn(Native.UiNative.MonitorFor(s.OsdScreen).Monitor)) return true;
        return s.OsdFullscreen == OsdFullscreen.WarningsOnly && kind == OsdKind.Warning;
    }

    /// <summary>
    /// The answer to something done in K-Desktop's window (a name already taken, a backup made…): in the window itself
    /// while it is open; with it closed or minimised, on the OSD.
    /// </summary>
    public void ShowNotice(string text, OsdKind kind = OsdKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        dispatcher.BeginInvoke(() =>
        {
            if (System.Windows.Application.Current.Windows.OfType<Views.MainWindow>().FirstOrDefault(w => w.IsVisible && w.WindowState != System.Windows.WindowState.Minimized) is { } window)
                window.Notice(text, kind);
            else ShowOsd(text, kind);
        });
    }

    public void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null, OsdLook? look = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        dispatcher.BeginInvoke(() =>
        {
            if (Allowed(kind, look?.EffectiveTopic ?? OsdTopic.Actions)) Osd.ShowMessage(text, kind, duration ?? OsdDuration, look: look);
        });
    }

    /// <summary>Generation of the last sample of Options on the OSD (UI thread only).</summary>
    private int _previewGeneration = -1;

    /// <summary>Options › OSD › «Probar»: shows it whatever the groups and full screen say.</summary>
    public void PreviewOsd(string text, OsdKind kind, OsdLook? look = null) =>
        dispatcher.BeginInvoke(() => _previewGeneration = Osd.ShowMessage(text, kind, OsdDuration + TimeSpan.FromSeconds(1.5), look: look));

    /// <summary>Takes the samples of Options off the OSD (with the ones stacked beside them), if one is still the message on show.</summary>
    public void ClearOsdPreview() => dispatcher.BeginInvoke(() => Osd.HideMessage(_previewGeneration));

    private RingWindow Ring
    {
        get
        {
            _ring ??= new RingWindow();
            var s = settings();
            _ring.Screen = s.OsdScreen;
            _ring.Size = s.OsdSize;
            return _ring;
        }
    }

    // Countdowns and «Mayús + Esc para parar» always show: they say how to stop what is going on. A countdown goes in the
    // ring (Options › «Cuentas atrás en un círculo») or on the OSD.
    public IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null, string? icon = null)
    {
        if (countdownTo is { } to && settings().OsdRing)
        {
            var ring = dispatcher.Invoke(() => Ring.ShowCountdown(text, icon, to));
            return new Callback(() => dispatcher.BeginInvoke(() => Ring.HideRing(ring)));
        }
        var generation = dispatcher.Invoke(() => Osd.ShowMessage(text, OsdKind.Info, null, countdownTo, icon is null ? null : OsdLook.Of(icon)));
        return new Callback(() => dispatcher.BeginInvoke(() => Osd.HideMessage(generation)));
    }

    /// <summary>Options › «Probar» of the ring: a countdown of a few seconds.</summary>
    public void PreviewRing(string text) => dispatcher.BeginInvoke(() =>
    {
        var generation = Ring.ShowCountdown(text, null, DateTime.Now.AddSeconds(5));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Ring.HideRing(generation);
        };
        timer.Start();
    });

    // Like the countdowns, it always shows: it says what is going on and that Esc stops it. On the OSD, not in the ring:
    // a long copy would sit in the middle of the screen.
    public IProgressOsd ShowProgressOsd(string text, string icon)
    {
        var osd = new ProgressOsd(this, icon);
        dispatcher.BeginInvoke(() => osd.Generation = Osd.ShowMessage(text, OsdKind.Info, null, look: new OsdLook(icon, 0)));
        return osd;
    }

    /// <summary>A message with a bar that its work keeps updating; it only touches the OSD while it is the one shown.</summary>
    private sealed class ProgressOsd(UiServices owner, string icon) : IProgressOsd
    {
        private int _closed;

        /// <summary>Generation of its message on the OSD (UI thread only).</summary>
        public int Generation { get; set; } = -1;

        public void Update(string text, double level) => owner.UiDispatcher.BeginInvoke(() =>
        {
            if (_closed == 0 && Generation == owner.Osd.Generation)
                Generation = owner.Osd.ShowMessage(text, OsdKind.Info, null, look: new OsdLook(icon, Math.Clamp(level, 0, 1)));
        });

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            owner.UiDispatcher.BeginInvoke(() => owner.Osd.HideMessage(Generation));
        }
    }

    public IVoiceOsd ShowVoiceOsd(string text)
    {
        var osd = new VoiceOsd(this);
        dispatcher.BeginInvoke(() =>
        {
            if (Allowed(OsdKind.Info, OsdTopic.Voice)) osd.Generation = Osd.ShowVoice(text);
            else osd.Hidden = true;
        });
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

        /// <summary>Options › OSD hides the voice messages (or full screen did): not even the result shows (UI thread only).</summary>
        public bool Hidden { get; set; }

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
            {
                // A warning («No te he entendido») still shows if warnings do.
                if (Hidden && !(kind == OsdKind.Warning && owner.Allowed(kind, OsdTopic.Actions))) return;
                owner.Osd.ShowMessage(text, kind, TimeSpan.FromSeconds(Math.Max(2.5, owner.Settings.OsdSeconds)),
                    look: new OsdLook(Topic: OsdTopic.Voice, Icon: kind == OsdKind.Info ? "Mic24" : null));
            });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0) return;
            owner.UiDispatcher.BeginInvoke(() => owner.Osd.HideMessage(Generation));
        }
    }

    public void BlankMonitors(IReadOnlyCollection<string> devices) =>
        dispatcher.BeginInvoke(() => KDesktop.Osd.BlankScreens.Toggle(devices));

    public void ShowQuickMenu(string menuId, ForegroundWindow previousForeground) =>
        dispatcher.BeginInvoke(() => QuickMenus?.Show(menuId, previousForeground));

    public void ShowClipboardHistory(ForegroundWindow previousForeground) =>
        dispatcher.BeginInvoke(() => Clipboard?.Show(previousForeground));

    public void ShowMainWindow() => dispatcher.BeginInvoke(showMainWindow);

    public void RunAssistantExtra(string assistant, string extra, bool bringOut) => dispatcher.BeginInvoke(() => AssistantExtra?.Invoke(assistant, extra, bringOut));

    public void AskAssistant(string assistant, string text, bool bringOut = false) => dispatcher.BeginInvoke(() => AssistantAsk?.Invoke(assistant, text, bringOut));

    public async Task<Core.Assistant.Pipe.RepliedMessage?> AskAssistantForAsync(string assistant, string text, bool bringOut, CancellationToken token) =>
        AssistantAskFor is { } ask ? await (await dispatcher.InvokeAsync(() => ask(assistant, text, bringOut, token)).Task.ConfigureAwait(false)).ConfigureAwait(false) : null;

    public void ShowAnswerSheet(string assistant, string markdown) => dispatcher.BeginInvoke(() => AnswerSheet?.Invoke(assistant, markdown));

    public void AskAssistantScheduled(EventAsk ask) => dispatcher.BeginInvoke(() => AssistantAskScheduled?.Invoke(ask));

    public void AssistantSay(string assistant, string text, bool catchEye, bool bringOut) => dispatcher.BeginInvoke(() => AssistantSayer?.Invoke(assistant, text, catchEye, bringOut));

    /// <summary>The notification cards («Mostrar notificación»): set by the app once it has its notifier.</summary>
    public Func<Notification, Task>? Notifier { get; set; }

    public Task ShowNotificationAsync(Notification notification) =>
        Notifier is { } notify ? dispatcher.InvokeAsync(() => notify(notification)).Task.Unwrap() : Task.CompletedTask;

    public void AssistantBrain(Core.Model.AssistantBrainOp op) => dispatcher.BeginInvoke(() => AssistantBrainer?.Invoke(op));

    public void Dictate(Core.Model.SwitchMode mode) => Dictator?.Invoke(mode);

    public void AssistantChat(string assistant, Core.Model.AssistantChatMode mode) => dispatcher.BeginInvoke(() => AssistantChatter?.Invoke(assistant, mode));

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

    public Task<Core.Platform.PixelColor?> PickColorAsync(Core.Platform.BgraImage desktop, Core.Platform.ScreenRect bounds, Core.Model.ColorFormat format) =>
        dispatcher.Invoke(() => ScreenPicker.PickColorAsync(desktop, bounds, format));

    public Task<(int Width, int Height)?> MeasureAsync(Core.Platform.BgraImage desktop, Core.Platform.ScreenRect bounds) =>
        dispatcher.Invoke(() => ScreenPicker.MeasureAsync(desktop, bounds));

    public void FlashArea(Core.Platform.ScreenRect area) => dispatcher.BeginInvoke(() => CaptureFlash.Show(area));

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
