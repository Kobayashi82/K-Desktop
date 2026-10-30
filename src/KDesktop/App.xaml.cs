using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using KDesktop.Core;
using KDesktop.Core.Achievements;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Remote;
using KDesktop.Core.Remote.Web;
using KDesktop.Core.Storage;
using KDesktop.Launcher;
using KDesktop.QuickMenus;
using KDesktop.Services;
using KDesktop.Views;
using Microsoft.Win32;

namespace KDesktop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The tray icon and the instance lock are disposed in OnExit.")]
public partial class App : Application
{
    private readonly DispatcherTimer _indexDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hookRefresh = new() { Interval = TimeSpan.FromMinutes(10) };

    /// <summary>Shift + drag snaps windows into the zones of the disposición put on their monitor (E1b).</summary>
    private Services.ZoneSnapper? _zoneSnapper;

    /// <summary>Automatic backups: first look a couple of minutes after starting, then every hour.</summary>
    private readonly DispatcherTimer _backupTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    private bool _backingUp;
    private TaskbarIcon? _tray;
    private SingleInstance? _instance;
    private MainWindow? _main;
    private bool _exiting;
    private AppTheme _appliedTheme;
    private string? _appliedLanguage;
    private bool _appliedFavicons;

    /// <summary>Favicons arrive in bursts at startup: refresh the icons once per burst.</summary>
    private readonly DispatcherTimer _iconRefresh = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public static new App Current => (App)Application.Current;

    public bool StartedByWindows { get; init; }

    public string[] Arguments { get; init; } = [];

    public bool HooksDisabled => Arguments.Contains("--no-hooks");
    private bool UdpDisabled => Arguments.Contains("--no-udp");
    private bool WebDisabled => Arguments.Contains("--no-web");

    /// <summary>False when UDP was disabled from the command line.</summary>
    public bool UdpAllowed => !UdpDisabled;

    /// <summary>False when the web server was disabled from the command line.</summary>
    public bool WebAllowed => !WebDisabled;

    public ConfigStore Store { get; } = new();
    public HotkeyRouter Router { get; } = new();
    public TextExpander Expander { get; } = new();
    public CommandEngine Engine { get; private set; } = null!;

    /// <summary>Automations (Eventos): watches what they wait for and runs their commands.</summary>
    public Core.Engine.Events.EventService Events { get; private set; } = null!;
    public UdpServer Udp { get; private set; } = null!;
    public WebServer Web { get; private set; } = null!;
    public UiServices Ui { get; private set; } = null!;
    public QuickMenuController QuickMenus { get; private set; } = null!;
    public LauncherController Launcher { get; private set; } = null!;

    /// <summary>The clipboard history (Settings › Portapapeles).</summary>
    public ClipboardController ClipboardUi { get; private set; } = null!;
    public AchievementService Achievements { get; } = new();
    public AchievementNotifier Notifier { get; private set; } = null!;
    public Core.Voice.VoiceService Voice { get; private set; } = null!;

    /// <summary>Always-on wake word detection.</summary>
    public Core.Voice.WakeWordListener WakeListener { get; private set; } = null!;

    public AppConfig Config => Store.Config;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Views.InputPolish.Register();
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Excepción no controlada en la UI", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Excepción no controlada", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Tarea con excepción no observada", args.Exception);
            args.SetObserved();
        };

        AppPaths.EnsureCreated();
        Log.Info($"── {AppPaths.VersionName} iniciando (elevado: {ProcessLauncher.IsElevated}, inicio con Windows: {StartedByWindows})");

        Loc.Instance.Load(null);
        LoadConfiguration();
        _appliedLanguage = Config.Settings.Language;
        if (_appliedLanguage is not null) Loc.Instance.Load(_appliedLanguage);
        Config.RefreshReferenceNames();
        _appliedTheme = Config.Settings.Theme;
        ThemeService.Initialize(_appliedTheme);

        // Website icons: set before anything draws an icon, so a disabled option never touches the network.
        _appliedFavicons = Favicons.Enabled = Config.Settings.WebFavicons;
        _iconRefresh.Tick += (_, _) =>
        {
            _iconRefresh.Stop();
            IconProvider.BumpRevision();
            Web?.NotifyCatalogChanged();
        };
        Favicons.Downloaded += () => Dispatcher.BeginInvoke(() =>
        {
            _iconRefresh.Stop();
            _iconRefresh.Start();
        });
        Favicons.Prefetch(Config.Commands.SelectMany(c => c.Actions));

        Ui = new UiServices(Dispatcher, () => Config.Settings, () => ShowMainWindow(), () => _ = ExitAsync(Config.Settings.ExitSound)) { AssistantExtra = RunExtraFromCommand, AssistantAsk = AskAssistantFromCommand, AssistantAskFor = AskForAnswerFromCommand, AnswerSheet = ShowAnswerSheetFromCommand, AssistantAskScheduled = AskScheduledFromEvent, AssistantSayer = SayFromCommand, AssistantBrainer = BrainFromCommand, AssistantChatter = ChatWindowFromCommand, AssistantToolUser = UseToolFromCommandAsync, Dictator = mode => _ = Voice.DictateAsync(mode) };
        Engine = new CommandEngine(Store, Ui, new ExecutionHistory(ExecutionHistory.DefaultFile));
        Events = new Core.Engine.Events.EventService(Engine, Ui, () => Config);
        CreateCalendar();
        CreateTelegram();
        Events.Calendar = Calendar;
        Engine.Calendar = Calendar;
        Voice = new Core.Voice.VoiceService(Ui, () => Config, cmd => Engine.RunAsync(cmd, TriggerSource.Voice),
            action => Engine.TestActionAsync(action, new Command { Name = Loc.T("settings.nav.media") }));
        Voice.History = Engine.History;
        Engine.SpeechToText = Voice.SpeechToText;
        Voice.AddresseeOf = VoiceAddresseeOf;
        UpdateSleepingWakeWords();
        AssistantChanged += UpdateSleepingWakeWords;
        Store.Changed += (_, _) => Dispatcher.BeginInvoke(UpdateSleepingWakeWords);
        // A word just recorded with an assistant's name is given to it.
        Voice.WakeWords.Changed += () => Dispatcher.BeginInvoke(UpdateSleepingWakeWords);
        Engine.Recording.Changed += () => Dispatcher.BeginInvoke(ApplyTrayIcon);
        Engine.VideoChanged += () => Dispatcher.BeginInvoke(ApplyTrayIcon);
        Engine.Awake.Changed += () => Dispatcher.BeginInvoke(UpdateAwakeTray);
        Engine.Magnifier.Changed += () => Ui.ShowOsd(Engine.Magnifier.IsOn
            ? Loc.T("osd.zoomOn", Engine.Magnifier.Zoom.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture))
            : Loc.T("osd.zoomOff"));
        Voice.StateChanged += () => Dispatcher.BeginInvoke(() =>
        {
            _main?.UpdateStatus();
            ApplyTrayIcon();
        });
        Voice.CancelPending = () =>
        {
            var any = Engine.HasCancellable;
            Engine.CancelAll();
            return any;
        };
        WakeListener = new Core.Voice.WakeWordListener(Voice, () => Config.Settings);
        WakeListener.StateChanged += () => Dispatcher.BeginInvoke(() => _main?.UpdateStatus());
        Voice.WakeHeard += word =>
        {
            Achievements.Increment(Stat.WakeWords);
            Dispatcher.BeginInvoke(() => OnAssistantWakeWord(word.Id));
        };
        Voice.OrderRun += order =>
        {
            Achievements.Increment(Stat.VoiceOrders);
            if (order.Semantic) Achievements.Increment(Stat.VoiceMeaning);
        };
        Voice.Models.Changed += () =>
        {
            if (Voice.Models.IsDownloading) return;
            Dispatcher.BeginInvoke(() => _main?.UpdateStatus());
            WakeListener.Refresh();
            if (Voice.Models.Error is { } error) Ui.ShowOsd(Loc.T("voice.downloadError", error), OsdKind.Warning, TimeSpan.FromSeconds(5));
            else if (Core.Voice.VoiceModels.Complete) Ui.ShowOsd(Loc.T("voice.downloaded"), OsdKind.Success);
        };
        QuickMenus = new QuickMenuController(() => Config, Engine);
        Ui.QuickMenus = QuickMenus;
        QuickMenus.EditRequested += (commandId, menuId) =>
        {
            Achievements.Unlock("quick-opener");
            if (commandId is not null) ShowMainWindow(w => w.OpenCommand(commandId));
            else ShowMainWindow(w => w.OpenQuickMenu(menuId));
        };
        QuickMenus.Opened += () => Achievements.Increment(Stat.MenusOpened);
        QuickMenus.Prewarm();

        // Achievements and statistics
        Notifier = new AchievementNotifier(() => Config.Settings, key => ShowMainWindow(w => w.Navigate(key)));
        Ui.Notifier = Notifier.ShowCustom;
        Achievements.Unlocked += def => Dispatcher.BeginInvoke(() => Notifier.Show(def));
        Achievements.Load();
        WireAccountAchievements();

        // Launcher (search box): its window is created now so the first opening is instant.
        Launcher = new LauncherController(() => Config, Engine, Achievements, Ui);
        Ui.Launcher = Launcher;
        Launcher.Prewarm();
        ClipboardUi = new ClipboardController(() => Config);
        Ui.Clipboard = ClipboardUi;
        // {portapapeles:N}: the history while it is on.
        TextVariables.HistoryText = n => Config.Settings.ClipboardHistory ? ClipboardUi.History.Text(n) : null;
        ClipboardUi.Start();
        ClipboardUi.Prewarm();
        Engine.Executed += Achievements.CommandExecuted;
        Engine.ActionStarting += (action, _) => Achievements.ActionExecuted(action);
        Engine.ActionStarting += AdornAction;
        Engine.Cancelled += () => Achievements.Unlock("cancel");
        Engine.EventFired += (_, end, source) =>
        {
            if (source is not (TriggerSource.Manual or TriggerSource.CommandLine)) Achievements.Increment(end ? Stat.EventsEnded : Stat.EventsFired);
        };

        // Hotkeys
        Router.Triggered += Engine.OnHotkey;
        // Textos: an abbreviation typed anywhere writes its text (never in a password box).
        Expander.IsPasswordBox = PasswordFocus.IsPassword;
        Expander.Expanded += _ => Achievements.Increment(Stat.SnippetsExpanded);
        Router.SnippetTyped += hit => _ = Expander.ExpandAsync(hit);
        Router.BuiltIn += hotkey =>
        {
            // The window in front now, before the launcher takes its place.
            var front = ForegroundWindow.Capture();
            Dispatcher.BeginInvoke(() =>
            {
                switch (hotkey)
                {
                    case BuiltInHotkey.ShowWindow: ShowMainWindow(); break;
                    case BuiltInHotkey.Launcher: Launcher.Show(front); break;
                    case BuiltInHotkey.Clipboard: ClipboardUi.Show(front); break;
                    case BuiltInHotkey.Pause: SetPaused(!Config.Settings.Paused); break;
                    default: _ = ExitAsync(Config.Settings.ExitSound); break;
                }
            });
        };
        Router.HasCancellable = () => Engine.HasCancellable || Voice.IsListening;
        Router.CancelRequested += Engine.CancelAll;
        Router.CancelRequested += Voice.Cancel;
        Router.VoiceRequested += (held, toAssistant) =>
        {
            if (toAssistant == HotkeyIndex.Dictation) _ = Voice.DictateAsync(held);
            else if (toAssistant is not null) Dispatcher.BeginInvoke(() => TalkToAssistant(held, toAssistant));
            else _ = Voice.ListenAsync(held);
        };
        Router.DisabledInToggleRequested += window =>
        {
            Engine.ToggleDisabledIn(window);
            Achievements.Increment(Stat.DisabledInToggled);
        };
        Router.IsPlayingMacro = () => Engine.IsPlayingMacro;
        // Ctrl + wheel changes the lupa's zoom while it is on (off, the wheel goes to the program as always).
        Router.CtrlWheel = delta =>
        {
            if (!Config.Settings.ZoomCtrlWheel || !Engine.Magnifier.IsOn) return false;
            Dispatcher.BeginInvoke(() => Engine.Magnifier.Nudge(Math.Sign(delta)));
            return true;
        };
        Router.StopMacrosRequested += Engine.StopMacros;
        Router.ClickObserver = Engine.VideoClick;
        // Mouse gestures (E4): the stroke on the screen while it is drawn.
        var gestureTrail = new Osd.GestureTrail(Router);
        Router.GestureDrawing += () => Dispatcher.BeginInvoke(() =>
        {
            if (Config.Settings.GestureTrail) gestureTrail.Begin();
        });
        Router.GestureFinished += _ => Dispatcher.BeginInvoke(gestureTrail.End);
        Router.KBoardUsed += () => Achievements.Increment(Stat.KBoardRemapped);
        RebuildIndex();
        _indexDebounce.Tick += (_, _) =>
        {
            _indexDebounce.Stop();
            RebuildIndex();
            Events.Rebuild();
            ApplySettings();
            Achievements.CheckConfig(Config);
            Config.RefreshReferenceNames(); // renamed macros, menus or commands
            SendAssistantExtraKeys(); // the hotkeys of the commands that run assistants' extras
            Favicons.Prefetch(Config.Commands.SelectMany(c => c.Actions)); // new or edited web actions
            Web?.NotifyCatalogChanged();
        };
        Store.Changed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _indexDebounce.Stop();
            _indexDebounce.Start();
        });
        if (!HooksDisabled)
        {
            Router.Start();
            _zoneSnapper = new Services.ZoneSnapper(() => Config);
            _zoneSnapper.Start();
        }
        else Log.Warn("Hooks desactivados por la línea de comandos (--no-hooks)");

        // Keep the hooks healthy: reinstall them periodically and after unlocking the session.
        _hookRefresh.Tick += (_, _) =>
        {
            Router.Reinstall();
            Achievements.CheckTime();
        };
        _hookRefresh.Start();
        _backupTimer.Tick += (_, _) =>
        {
            _backupTimer.Interval = TimeSpan.FromHours(1);
            _ = BackupIfDueAsync();
        };
        _backupTimer.Start();
        SystemEvents.SessionSwitch += (_, args) =>
        {
            if (args.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect) Router.Reinstall();
        };
        SystemEvents.SessionEnding += OnSessionEnding;
        // Disposiciones (E1b): every monitor seen is remembered, to cut zones for it when it is not connected.
        RememberMonitors();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(RememberMonitors);

        // Remote control
        var assistants = new WebAssistants(this);
        Udp = new UdpServer(Engine, Ui) { Assistants = assistants, OnUi = f => Dispatcher.CheckAccess() ? f() : Dispatcher.Invoke(f) };
        Web = new WebServer(new WebHost(Dispatcher, Engine, Achievements, Voice) { Assistants = assistants, Ui = Ui });
        Web.Devices.Paired += _ => Achievements.Unlock("pair");
        Web.PointerMoved += () => Achievements.Unlock("touchpad");
        Web.StateChanged += () => Dispatcher.BeginInvoke(() => _main?.UpdateStatus());
        ApplySettings();
        InitUpdates();
        if (ProcessLauncher.IsElevated) Task.Run(FirewallRule.Ensure);

        // First run with a normal data folder: honour the default "start with Windows" by registering the
        // scheduled task once, so a fresh copy auto-starts without the user having to open Settings.
        if (_firstRun && Config.Settings.StartWithWindows && AppPaths.InstanceSuffix.Length == 0 && !StartedByWindows)
            Task.Run(() =>
            {
                try
                {
                    if (!StartupTask.IsRegistered())
                    {
                        StartupTask.Register();
                        Log.Info("Primer arranque: tarea de inicio con Windows registrada");
                    }
                }
                catch (Exception ex) { Log.Warn("No se pudo registrar la tarea de inicio en el primer arranque: " + ex.Message); }
            });

        _instance = new SingleInstance(
            () => Dispatcher.BeginInvoke(() => ShowMainWindow()),
            args => Dispatcher.BeginInvoke(() => HandleArguments(args)));

        // Startup feedback and the «Al iniciar K-Desktop» events; a restart after an update is not a new start.
        Events.Start(afterUpdate: UpdatedFrom is not null);
        StartCalendar();
        StartTelegram();
        var s = Config.Settings;
        if (UpdatedFrom is null)
        {
            Sounds.Play(s.StartupSound);
            if (!string.IsNullOrWhiteSpace(s.StartupMessage))
                Ui.ShowOsd(s.StartupMessage.Replace("\"version\"", AppPaths.VersionName, StringComparison.OrdinalIgnoreCase)
                                           .Replace("\"ver\"", AppPaths.VersionName, StringComparison.OrdinalIgnoreCase));
            _ = Engine.RunStartupCommandsAsync();
        }

        var requestsSomething = Program.ValueOf(Arguments, "--menu") is not null || Program.ValueOf(Arguments, "--run") is not null ||
                                Program.ValueOf(Arguments, "--event") is not null || Arguments.Contains("--launcher", StringComparer.OrdinalIgnoreCase);
        if ((!StartedByWindows && UpdatedFrom is null && !requestsSomething && s.ShowWindowOnStart)) ShowMainWindow();
        HandleArguments(Arguments);
        Log.Info($"Listo: {Config.Commands.Count} comandos, {Config.QuickMenus.Count} QuickMenus, {Config.Macros.Count} macros");

        // After the window: the first notifications ("welcome"…) appear once everything is on screen. A fresh install
        // opened by hand (not a test copy with --data) shows the welcome over the main window, and the achievements
        // come when it closes.
        Dispatcher.BeginInvoke(() =>
        {
            void CheckAchievements()
            {
                Achievements.Increment(Stat.AppStarted);
                Achievements.CheckTime();
                Achievements.CheckConfig(Config);
            }
            if (_firstRun && AppPaths.InstanceSuffix.Length == 0 && !StartedByWindows) ShowWelcome(CheckAchievements);
            else CheckAchievements();
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Command-line requests: --menu "Name", --run "Name", --show, --install-assistant "file", --import "file", --exit.</summary>
    public void HandleArguments(IReadOnlyList<string> args)
    {
        if (Program.ValueOf(args, "--menu") is { } menu) QuickMenus.Show(menu, ForegroundWindow.Capture());
        if (Program.ValueOf(args, "--run") is { } name)
        {
            var cmd = Config.FindCommand(name) ??
                      Config.Commands.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cmd is not null) _ = Engine.RunAsync(cmd, TriggerSource.CommandLine);
            else Log.Warn($"--run: comando no encontrado '{name}'");
        }
        if (Program.ValueOf(args, "--event") is { } eventName)
        {
            if (Config.FindEvent(eventName) is { } rule) _ = Events.RunNowAsync(rule, TriggerSource.CommandLine);
            else Log.Warn($"--event: evento no encontrado '{eventName}'");
        }
        if (args.Contains("--launcher", StringComparer.OrdinalIgnoreCase)) Launcher.Show(ForegroundWindow.Capture());
        if (args.Contains("--clipboard", StringComparer.OrdinalIgnoreCase)) ClipboardUi.Show(ForegroundWindow.Capture());
        if (args.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowMainWindow();
        if (args.Contains("--welcome", StringComparer.OrdinalIgnoreCase)) ShowWelcome();
        if (Program.ValueOf(args, "--page") is { } page) ShowMainWindow(w => w.Navigate(page.ToLowerInvariant()));
        if (Program.ValueOf(args, PackageAssociation.Argument) is { } package) OpenAssistantPackage(package);
        if (Program.ValueOf(args, "--import") is { } exported) _ = Views.ItemTransferUi.ImportAsync(exported);
        if (Program.ValueOf(args, "--video-test") is { } seconds) _ = VideoTestAsync(args, seconds);
        if (Program.ValueOf(args, "--annotate-test") is { } picture) _ = AnnotateTestAsync(picture, args.Contains("demo", StringComparer.OrdinalIgnoreCase));
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) _ = ExitAsync();
    }

    /// <summary>
    /// <c>--annotate-test picture.png</c>: the capture editor (F1) with that picture instead of the screen; what it gives
    /// back is saved next to it as «… anotada.png» (and logged). With <c>demo</c>, one mark of each kind is already there.
    /// </summary>
    private static async Task AnnotateTestAsync(string path, bool demo)
    {
        try
        {
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.GetFullPath(path)), System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
            var image = new BgraImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
            var cursor = ScreenCapture.MonitorAtCursor();
            var result = await Views.Dialogs.AnnotateWindow.EditAsync(image, cursor, copy: true, save: true, demo);
            if (result is null)
            {
                Log.Info("--annotate-test: cancelado");
                return;
            }
            var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path) + " anotada.png");
            await File.WriteAllBytesAsync(output, await Core.Imaging.ImageFiles.EncodeAsync(result.Image, ImageFileFormat.Png, 90, CancellationToken.None));
            Log.Info($"--annotate-test: {result.Image.Width}×{result.Image.Height}, copiar {result.Copy}, guardar {result.Save} → {output}");
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or ArgumentException)
        {
            Log.Error("--annotate-test", ex);
        }
    }

    /// <summary>
    /// <c>--video-test seconds out.mp4 [monitor|region|window] [audio] [60] [clicks] [gif|webp]</c>: «Grabar vídeo» (F2) without the UI: the monitor
    /// of the mouse, or a 640×360 region in its middle, for that long (logged); <c>clicks</c> marks made-up clicks (never the real mouse); <c>gif</c> / <c>webp</c>
    /// make an animation at 15 fps, 800 wide at most.
    /// </summary>
    private static async Task VideoTestAsync(IReadOnlyList<string> args, string seconds)
    {
        try
        {
            var index = args.ToList().FindIndex(a => a.Equals("--video-test", StringComparison.OrdinalIgnoreCase));
            var output = Path.GetFullPath(args[index + 2]);
            var monitor = ScreenCapture.MonitorAtCursor();
            var region = args.Contains("region", StringComparer.OrdinalIgnoreCase);
            var front = ForegroundWindow.Capture();
            var target = args.Contains("window", StringComparer.OrdinalIgnoreCase) && front.RootHandle != IntPtr.Zero
                ? new VideoTarget(CaptureArea.ActiveWindow, WindowLayout.VisibleBounds(front.RootHandle), front.RootHandle)
                : region
                ? new VideoTarget(CaptureArea.Region, new ScreenRect(monitor.CenterX - 320, monitor.Top + monitor.Height / 2 - 180, 641, 361))
                : new VideoTarget(CaptureArea.Monitor, monitor);
            var format = args.Contains("gif", StringComparer.OrdinalIgnoreCase) ? VideoFormat.Gif
                : args.Contains("webp", StringComparer.OrdinalIgnoreCase) ? VideoFormat.WebP : VideoFormat.Mp4;
            var options = new VideoOptions(format != VideoFormat.Mp4 ? 15 : args.Contains("60") ? 60 : 30, VideoQuality.Normal, true,
                args.Contains("audio", StringComparer.OrdinalIgnoreCase) ? AudioSource.Output : null, 1, args.Contains("clicks", StringComparer.OrdinalIgnoreCase),
                format, format != VideoFormat.Mp4 ? 800 : 0);
            var recorder = ScreenRecorder.Start(target, output, options);
            var limited = false;
            recorder.LimitReached += () => limited = true;
            var until = DateTime.UtcNow.AddSeconds(double.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture));
            for (var i = 0; DateTime.UtcNow < until && !limited; i++)
            {
                if (options.Clicks)
                {
                    // Around an ellipse in the middle of what is recorded: left clicks, and every third a right one.
                    var area = target.Area;
                    var angle = i * 0.9;
                    recorder.AddClick(area.CenterX + (int)(Math.Cos(angle) * area.Width / 3), area.Top + area.Height / 2 + (int)(Math.Sin(angle) * area.Height / 3),
                        i % 3 == 2 ? MouseButton.Right : MouseButton.Left);
                }
                await Task.Delay(600);
            }
            var length = await recorder.StopAsync();
            Log.Info($"--video-test: {length} → {output} ({(File.Exists(output) ? new FileInfo(output).Length / 1024 : 0)} KB)");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or FormatException or System.Runtime.InteropServices.COMException or SharpGen.Runtime.SharpGenException or InvalidOperationException)
        {
            Log.Error("--video-test", ex);
        }
    }

    /// <summary>The welcome (language, theme, the basics, voice, assistants) over the main window.</summary>
    /// <param name="closed">Runs when it closes (the first start's achievements).</param>
    public void ShowWelcome(Action? closed = null) => ShowMainWindow(w => Views.Dialogs.WelcomeWindow.Open(w, closed));

    /// <summary>True when no configuration existed yet (a fresh install).</summary>
    private bool _firstRun;

    private void LoadConfiguration()
    {
        if (Store.Load()) return;
        // K-Desktop 4's data is imported by a program of its own (importer\), not here.
        _firstRun = true;
        Store.SaveNow();
    }

    /// <summary>Swaps in a whole new configuration (imports) and brings everything in line with it.</summary>
    public void ReplaceConfig(AppConfig config)
    {
        Store.Replace(config);
        IconProvider.Invalidate();
        RebuildIndex();
        Events.Rebuild();
        ApplySettings();
        Voice.WakeWords.Reload();
        _main?.RebuildPages();
    }

    private void RebuildIndex()
    {
        var paused = Config.Settings.Paused;
        Router.UpdateIndex(new HotkeyIndex(Config.Commands, Config.Settings, paused));
        Router.UpdateSnippets(new SnippetIndex(Config, paused));
        Router.UpdateKBoard(paused ? KBoardIndex.Empty : new KBoardIndex(Config.KBoards, Config.Commands, Config.Settings.DisabledIn));
    }

    // ── pause ────────────────────────────────────────────────────────────

    /// <summary>Raised (UI thread) when K-Desktop is paused or resumed.</summary>
    public event Action? PausedChanged;

    /// <summary>
    /// Pauses or resumes K-Desktop: while paused no command runs (hotkeys, QuickMenus, K-Board, remote…); only the
    /// show-window and exit hotkeys work. The state is saved, so it survives a restart.
    /// </summary>
    public void SetPaused(bool paused)
    {
        if (Config.Settings.Paused == paused) return;
        Config.Settings.Paused = paused;
        RebuildIndex();
        Log.Info(paused ? "K-Desktop en pausa" : "K-Desktop reanudado");
        if (paused) Achievements.Unlock("pause");
        WakeListener.Refresh(); // paused: the microphone closes
        Ui.ShowOsd(Loc.T(paused ? "pause.osdOn" : "pause.osdOff"), look: OsdLook.Of(paused ? "Pause24" : "Play24"));
        Sounds.Play(paused ? Config.Settings.PauseSound : Config.Settings.ResumeSound);
        if (_tray is not null) _tray.ToolTipText = TrayTooltip();
        _assistant?.Send(new Core.Assistant.Pipe.PauseMessage(paused));
        UpdateAssistantInput();
        PausedChanged?.Invoke();
    }

    private string TrayTooltip() => Config.Settings.Paused ? Loc.T("pause.tooltip", AppPaths.VersionName) : AppPaths.VersionName;

    /// <summary>Reads the chosen language again (its files may have changed) and redoes the texts built in code.</summary>
    public void ReloadLanguage()
    {
        Loc.Instance.Load(Config.Settings.Language);
        foreach (var action in Config.Commands.SelectMany(c => c.Actions)) action.RefreshSummary();
        foreach (var macro in Config.Macros) macro.RefreshTexts();
        foreach (var profile in Config.KBoards) profile.RefreshTexts();
        foreach (var rule in Config.Events) rule.RefreshTexts();
        foreach (var menu in Config.QuickMenus) menu.RefreshTexts();
        foreach (var snippet in Config.Snippets) snippet.RefreshTexts();
        foreach (var arrangement in Config.Arrangements) arrangement.RefreshTexts();
        // The tray menu is built in code: rebuild it in the new language.
        _tray?.Dispose();
        _tray = null;
        UpdateTray();
    }

    /// <summary>The UDP server's key in plain text (null: none).</summary>
    public string? UdpKey => Config.Settings.UdpKey is { } stored ? Core.Platform.Secrets.Unprotect(stored) : null;

    /// <summary>Applies settings that have side effects (theme, tray, UDP).</summary>
    public void ApplySettings()
    {
        var s = Config.Settings;
        // Paused, the replay doesn't keep listening either.
        Engine.Recording.ConfigureReplay(s.AudioReplay && !s.Paused, s.AudioReplaySeconds, s.AudioReplaySource, Engine.RecordOptions.MicGain);
        Engine.Magnifier.Configure(Engine.MagnifierOptions);
        ClipboardUi?.ApplyLimits();
        if (s.Theme != _appliedTheme)
        {
            _appliedTheme = s.Theme;
            ThemeService.Apply(s.Theme);
            Achievements.Unlock("theme");
        }

        if (s.Language != _appliedLanguage)
        {
            _appliedLanguage = s.Language;
            ReloadLanguage();
            Achievements.Unlock("polyglot");
        }

        if (s.WebFavicons != _appliedFavicons)
        {
            _appliedFavicons = Favicons.Enabled = s.WebFavicons;
            IconProvider.BumpRevision();
            Web.NotifyCatalogChanged();
            Favicons.Prefetch(Config.Commands.SelectMany(c => c.Actions));
        }

        UpdateTray();
        ApplyTrayIcon();
        WakeListener?.Refresh(); // the option, the microphone or the pause changed
        Voice?.ApplySettings(s); // voice off: its models leave memory now
        _main?.UpdateStatus(); // e.g. the voice hotkey shown in its tooltip

        if (s.UdpEnabled && !UdpDisabled)
            Udp.Start(new UdpOptions(s.UdpPort, s.UdpScope, UdpKey, s.UdpAllowKeys, s.UdpAllowHidden, s.UdpAllowEvents, s.UdpAllowAssistants));
        else Udp.Stop();

        if (s.WebEnabled && !WebDisabled)
            Web.Start(new WebServerOptions(s.WebPort, WebServer.BasePathOf(s.WebPublicUrl), s.WebScope));
        else Web.Stop();
        Web.InputAllowed = s.WebInput;
        Web.PowerAllowed = s.WebPower;
        Web.RunPower ??= (mode, device) => WebServer.RunPowerAsync(Engine, mode, device);
        ApplyAssistant();
    }

    // ── tray ─────────────────────────────────────────────────────────────

    /// <summary>The icon is there while the window is visible; with the window hidden or closed, the setting decides.</summary>
    private void UpdateTray()
    {
        if (Config.Settings.ShowTrayIcon || _main is { IsVisible: true }) EnsureTray();
        else if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    private void EnsureTray()
    {
        if (_tray is not null) return;
        var menu = new ContextMenu();
        // Show or hide, by whether the window is on screen (minimized counts as hidden).
        var window = TrayItem(string.Empty, (_, _) =>
        {
            if (MainWindowShown) _main!.Close();
            else ShowMainWindow();
        });
        menu.Items.Add(window);
        // «Grabar vídeo»: stop it from here too (only while it records).
        var stopVideo = TrayItem(Loc.T("tray.videoStop"), (_, _) => _ = Engine.StopVideoAsync());
        menu.Items.Add(stopVideo);
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(Loc.T("tray.launcher"), (_, _) => Launcher.Show(ForegroundWindow.Capture())));
        var clipboard = TrayItem(Loc.T("tray.clipboard"), (_, _) => ClipboardUi.Show(ForegroundWindow.Capture()));
        menu.Items.Add(clipboard);
        var clipboardPause = new MenuItem { Header = Loc.T("tray.clipboardPause"), IsCheckable = true };
        clipboardPause.Click += (_, _) => Config.Settings.ClipboardPaused = clipboardPause.IsChecked;
        menu.Items.Add(clipboardPause);
        menu.Items.Add(new Separator());
        var assistant = new MenuItem { Header = Loc.T("nav.assistant") };
        menu.Items.Add(assistant);
        var events = new MenuItem { Header = Loc.T("nav.events") };
        menu.Items.Add(events);
        var kboard = new MenuItem { Header = Loc.T("nav.kboard") };
        menu.Items.Add(kboard);
        menu.Items.Add(new Separator());
        var pause = TrayItem(string.Empty, (_, _) => SetPaused(!Config.Settings.Paused));
        menu.Items.Add(pause);
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(Loc.T("tray.exit"), (_, _) => _ = ExitAsync(Config.Settings.ExitSound)));
        menu.Opened += (_, _) =>
        {
            window.Header = Loc.T(MainWindowShown ? "tray.hide" : "tray.show");
            stopVideo.Visibility = Engine.IsRecordingVideo ? Visibility.Visible : Visibility.Collapsed;
            pause.Header = Loc.T(Config.Settings.Paused ? "pause.resume" : "pause.pause");
            clipboard.Visibility = clipboardPause.Visibility = Config.Settings.ClipboardHistory ? Visibility.Visible : Visibility.Collapsed;
            clipboardPause.IsChecked = Config.Settings.ClipboardPaused;
            FillAssistantTrayMenu(assistant);
            FillToggleTrayMenu(events, Config.Events.Select(e => (e.Name, e.Enabled, (Action<bool>)(on => e.Enabled = on))));
            FillToggleTrayMenu(kboard, Config.KBoards.Select(k => (k.Name, k.Active, (Action<bool>)(on => k.Active = on))));
        };

        _tray = new TaskbarIcon
        {
            ToolTipText = TrayTooltip(),
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        // The icon's id comes from the name; Windows ties it to the exe path, so a run with another data folder needs a
        // name of its own (or its icon cannot be added). Always the same one (5.0.432): one per data folder left Windows
        // a new entry in «Otros iconos de la bandeja» for every test folder. Two such runs at once: the second has no icon.
        if (AppPaths.InstanceSuffix.Length > 0) _tray.CustomName = "KDesktopData";
        _tray.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        _appliedTrayIcon = null;
        ApplyTrayIcon();
        try { _tray.ForceCreate(false); }
        catch (InvalidOperationException ex)
        {
            // Without the icon K-Desktop still works (window, hotkeys, launcher): better than not starting.
            Log.Warn($"No se pudo crear el icono de la bandeja ({ex.Message})");
        }
    }

    /// <summary>Events or K-Board profiles, each one switched on / off with a click; hidden when there are none.</summary>
    private static void FillToggleTrayMenu(MenuItem menu, IEnumerable<(string Name, bool On, Action<bool> Set)> rows)
    {
        menu.Items.Clear();
        foreach (var (name, on, set) in rows)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = on };
            item.Click += (_, _) => set(item.IsChecked);
            menu.Items.Add(item);
        }
        menu.Visibility = menu.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool MainWindowShown =>_main is { IsVisible: true } m && m.WindowState != WindowState.Minimized;

    private string? _appliedTrayIcon;

    /// <summary>K-Desktop's icon or the one chosen in Settings › Appearance.</summary>
    private void ApplyTrayIcon()
    {
        if (_tray is null) return;
        var wanted = Config.Settings.TrayIcon ?? string.Empty;
        // While the voice listens, the icon shows a red dot.
        var listening = Voice?.IsListening == true || Engine?.Recording.IsRecording == true || Engine?.IsRecordingVideo == true;
        var key = wanted + (listening ? "|voice" : "");
        if (_appliedTrayIcon == key) return;
        _appliedTrayIcon = key;
        if (TrayIcons.Custom(wanted, listening) is { } custom) _tray.Icon = custom;
        else _tray.IconSource = TrayIcons.Default();
    }

    private static MenuItem TrayItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = header };
        item.Click += click;
        return item;
    }

    // ── main window ──────────────────────────────────────────────────────

    public void ShowMainWindow(Action<MainWindow>? then = null)
    {
        if (_exiting) return;
        if (_main is null)
        {
            _main = new MainWindow();
            _main.IsVisibleChanged += (_, _) => UpdateTray();
            _main.Closed += (_, _) =>
            {
                _main = null;
                UpdateTray();
                // The window is fully released when closed to keep the resident footprint small.
                GC.Collect(2, GCCollectionMode.Optimized, false);
            };
            _main.Show();
        }
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        WindowTools.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_main).Handle);
        then?.Invoke(_main);
    }

    // ── exit ─────────────────────────────────────────────────────────────

    /// <param name="sound">Played while closing (the exit sound when the user closes it: hotkey, tray or a command); the exit waits until it ends.</param>
    public async Task ExitAsync(string? sound = null)
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("Saliendo…");
        var playing = string.IsNullOrWhiteSpace(sound) ? Task.CompletedTask : Sounds.PlayAndWaitAsync(sound, CancellationToken.None);
        QuickMenus.CloseAll(restoreFocus: false);
        _main?.Close();
        // Leaving is not «the program closed»: the events stop before the exit ones run (no «Al terminar» now).
        Events.Dispose();
        try { await Engine.RunExitCommandsAsync(); } catch (Exception ex) { Log.Error("Error en los comandos de salida", ex); }
        // A video being recorded is closed so it can be played.
        try { await Engine.StopVideoAsync(showOsd: false).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { Log.Warn("Grabación de vídeo: no se cerró a tiempo al salir"); }
        try { await playing; } catch (Exception ex) { Log.Warn("No se pudo reproducir el sonido de salida: " + ex.Message); }
        Shutdown();
    }

    /// <summary>Makes the automatic backup when it is due (off the UI thread; saving first, on it).</summary>
    public async Task<string?> BackupIfDueAsync(bool force = false)
    {
        if (_backingUp || _exiting) return null;
        var settings = Config.Settings;
        if (!force && !AutoBackup.IsDue(settings, DateTime.Now)) return null;
        _backingUp = true;
        try
        {
            return await Task.Run(() => AutoBackup.Create(settings, () => Dispatcher.Invoke(() =>
            {
                Store.SaveNow();
                Achievements.Flush();
            })));
        }
        catch (Exception ex)
        {
            Log.Error("Error en la copia automática", ex);
            if (force) throw;
            return null;
        }
        finally { _backingUp = false; }
    }

    private void RememberMonitors() => WindowArrangements.Remember(Config.Settings.KnownMonitors, WindowArrangements.Monitors());

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        Log.Info($"La sesión termina ({e.Reason})");
        Events.Dispose();
        try { Engine.RunExitCommandsAsync().Wait(TimeSpan.FromSeconds(4)); } catch { /* ignore */ }
        Store.Flush();
        Achievements.Flush();
        Engine.History.Save();
        Engine.Magnifier.Off();
        Engine.Recording.Stop();
        // A video being recorded is closed so it can be played.
        try { Engine.StopVideoAsync(showOsd: false).Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { /* it is closing anyway */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hookRefresh.Stop();
        _backupTimer.Stop();
        StopAssistant();
        SaveAssistantPanels();
        Events?.Dispose();
        Calendar?.Dispose();
        Mail?.Dispose();
        Telegram?.Dispose();
        TelegramAccount?.Dispose();
        ClipboardUi?.Dispose();
        Router.Dispose();
        _zoneSnapper?.Dispose();
        Udp?.Dispose();
        Web?.Dispose();
        Updates.Dispose();
        WakeListener?.Dispose();
        Voice?.Dispose();
        Store.Dispose();
        Achievements.Dispose();
        Engine?.History.Dispose();
        Engine?.Magnifier.Dispose();
        Engine?.Recording.Dispose(); // a recording in progress is closed properly
        Engine?.Awake.Dispose();
        _awakeTray?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        Log.Info("K-Desktop cerrado");
        Log.Flush();
        base.OnExit(e);
    }
}
