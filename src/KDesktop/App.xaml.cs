using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using KDesktop.Core;
using KDesktop.Core.Achievements;
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

public partial class App : Application
{
    private readonly DispatcherTimer _indexDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hookRefresh = new() { Interval = TimeSpan.FromMinutes(10) };

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
    public CommandEngine Engine { get; private set; } = null!;

    /// <summary>Automations (Eventos): watches what they wait for and runs their commands.</summary>
    public Core.Engine.Events.EventService Events { get; private set; } = null!;
    public UdpServer Udp { get; private set; } = null!;
    public WebServer Web { get; private set; } = null!;
    public UiServices Ui { get; private set; } = null!;
    public QuickMenuController QuickMenus { get; private set; } = null!;
    public LauncherController Launcher { get; private set; } = null!;
    public AchievementService Achievements { get; } = new();
    public AchievementNotifier Notifier { get; private set; } = null!;
    public Core.Voice.VoiceService Voice { get; private set; } = null!;

    /// <summary>Always-on wake word detection.</summary>
    public Core.Voice.WakeWordListener WakeListener { get; private set; } = null!;

    public AppConfig Config => Store.Config;

    /// <summary>Set when this run imported the K-Desktop 4 data.</summary>
    public LegacyImporter.Report? ImportReport { get; private set; }

    /// <summary>Running K-Desktop 4 processes (they would duplicate every hotkey).</summary>
    public IReadOnlyList<Process> LegacyInstances => FindLegacyInstances();

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

        Ui = new UiServices(Dispatcher, () => Config.Settings, () => ShowMainWindow(), () => _ = ExitAsync());
        Engine = new CommandEngine(Store, Ui, new ExecutionHistory(ExecutionHistory.DefaultFile));
        Events = new Core.Engine.Events.EventService(Engine, Ui, () => Config);
        Voice = new Core.Voice.VoiceService(Ui, () => Config, cmd => Engine.RunAsync(cmd, TriggerSource.Voice),
            action => Engine.TestActionAsync(action, new Command { Name = Loc.T("settings.nav.media") }));
        Voice.History = Engine.History;
        Engine.Recording.Changed += () => Dispatcher.BeginInvoke(ApplyTrayIcon);
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
        Voice.WakeHeard += _ => Achievements.Increment(Stat.WakeWords);
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
        Achievements.Unlocked += def => Dispatcher.BeginInvoke(() => Notifier.Show(def));
        Achievements.Load();

        // Launcher (search box): its window is created now so the first opening is instant.
        Launcher = new LauncherController(() => Config, Engine, Achievements, Ui);
        Ui.Launcher = Launcher;
        Launcher.Prewarm();
        Engine.Executed += Achievements.CommandExecuted;
        Engine.ActionStarting += (action, _) => Achievements.ActionExecuted(action);
        Engine.Cancelled += () => Achievements.Unlock("cancel");
        Engine.EventFired += (_, end, source) =>
        {
            if (source != TriggerSource.Manual) Achievements.Increment(end ? Stat.EventsEnded : Stat.EventsFired);
        };

        // Hotkeys
        Router.Triggered += Engine.OnHotkey;
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
                    case BuiltInHotkey.Pause: SetPaused(!Config.Settings.Paused); break;
                    default: _ = ExitAsync(Config.Settings.ExitSound); break;
                }
            });
        };
        Router.HasCancellable = () => Engine.HasCancellable || Voice.IsListening;
        Router.CancelRequested += Engine.CancelAll;
        Router.CancelRequested += Voice.Cancel;
        Router.VoiceRequested += held => _ = Voice.ListenAsync(held);
        Router.DisabledInToggleRequested += window =>
        {
            Engine.ToggleDisabledIn(window);
            Achievements.Increment(Stat.DisabledInToggled);
        };
        Router.IsPlayingMacro = () => Engine.IsPlayingMacro;
        Router.StopMacrosRequested += Engine.StopMacros;
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
            Favicons.Prefetch(Config.Commands.SelectMany(c => c.Actions)); // new or edited web actions
            Web?.NotifyCatalogChanged();
        };
        Store.Changed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _indexDebounce.Stop();
            _indexDebounce.Start();
        });
        if (!HooksDisabled) Router.Start();
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

        // Remote control
        Udp = new UdpServer(Engine, Ui);
        Web = new WebServer(new WebHost(Dispatcher, Engine, Achievements, Voice));
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
        if ((!StartedByWindows && UpdatedFrom is null && !requestsSomething && s.ShowWindowOnStart) || ImportReport is not null) ShowMainWindow();
        HandleArguments(Arguments);
        Log.Info($"Listo: {Config.Commands.Count} comandos, {Config.QuickMenus.Count} QuickMenus, {Config.Macros.Count} macros");

        // After the window: the first notifications ("welcome"…) appear once everything is on screen.
        Dispatcher.BeginInvoke(() =>
        {
            Achievements.Increment(Stat.AppStarted);
            Achievements.CheckTime();
            Achievements.CheckConfig(Config);
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Command-line requests: --menu "Name", --run "Name", --show, --exit.</summary>
    public void HandleArguments(IReadOnlyList<string> args)
    {
        if (Program.ValueOf(args, "--menu") is { } menu) QuickMenus.Show(menu, ForegroundWindow.Capture());
        if (Program.ValueOf(args, "--run") is { } name)
        {
            var cmd = Config.FindCommand(name) ??
                      Config.Commands.FirstOrDefault(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            if (cmd is not null) _ = Engine.RunAsync(cmd, TriggerSource.Manual);
            else Log.Warn($"--run: comando no encontrado '{name}'");
        }
        if (Program.ValueOf(args, "--event") is { } eventName)
        {
            if (Config.FindEvent(eventName) is { } rule) _ = Events.RunNowAsync(rule, TriggerSource.Manual);
            else Log.Warn($"--event: evento no encontrado '{eventName}'");
        }
        if (args.Contains("--launcher", StringComparer.OrdinalIgnoreCase)) Launcher.Show(ForegroundWindow.Capture());
        if (args.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowMainWindow();
        if (Program.ValueOf(args, "--page") is { } page) ShowMainWindow(w => w.Navigate(page.ToLowerInvariant()));
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) _ = ExitAsync();
    }

    /// <summary>True when no configuration existed yet (fresh install or first run after a legacy import).</summary>
    private bool _firstRun;

    private void LoadConfiguration()
    {
        if (Store.Load()) return;
        _firstRun = true;
        if (LegacyImporter.IsAvailable())
        {
            try
            {
                var (config, report) = LegacyImporter.Import();
                Store.Replace(config);
                ImportReport = report;
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Error importando K-Desktop 4", ex);
            }
        }
        Store.SaveNow();
    }

    /// <summary>Re-imports K-Desktop 4 data, replacing the current configuration.</summary>
    public LegacyImporter.Report ReimportLegacy()
    {
        Store.SaveNow();
        var (config, report) = LegacyImporter.Import();
        config.Settings.Theme = Config.Settings.Theme;
        ReplaceConfig(config);
        return report;
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
        WakeListener.Refresh(); // paused: the microphone closes
        Ui.ShowOsd(Loc.T(paused ? "pause.osdOn" : "pause.osdOff"));
        if (_tray is not null) _tray.ToolTipText = TrayTooltip();
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
        // The tray menu is built in code: rebuild it in the new language.
        _tray?.Dispose();
        _tray = null;
        UpdateTray();
    }

    /// <summary>Applies settings that have side effects (theme, tray, UDP).</summary>
    public void ApplySettings()
    {
        var s = Config.Settings;
        // Paused, the replay doesn't keep listening either.
        Engine.Recording.ConfigureReplay(s.AudioReplay && !s.Paused, s.AudioReplaySeconds, s.AudioReplaySource);
        Engine.Magnifier.Configure(s.ZoomMode, s.ZoomLensSize);
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
            Web?.NotifyCatalogChanged();
            Favicons.Prefetch(Config.Commands.SelectMany(c => c.Actions));
        }

        UpdateTray();
        ApplyTrayIcon();
        WakeListener?.Refresh(); // the option, the microphone or the pause changed
        _main?.UpdateStatus(); // e.g. the voice hotkey shown in its tooltip

        if (s.UdpEnabled && !UdpDisabled) Udp.Start(s.UdpPort, s.UdpAllowRemote);
        else Udp.Stop();

        if (s.WebEnabled && !WebDisabled)
            Web.Start(new WebServerOptions(s.WebPort, s.WebAllowRemote, s.WebBasePath, s.WebTrustedProxy, s.WebProxyOnly));
        else Web.Stop();
        Web.InputAllowed = s.WebInput;
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
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(Loc.T("tray.launcher"), (_, _) => Launcher.Show(ForegroundWindow.Capture())));
        menu.Items.Add(new Separator());
        var events = new MenuItem { Header = Loc.T("tray.events") };
        events.Items.Add(new MenuItem()); // placeholder so it opens as a submenu; filled when opened
        events.SubmenuOpened += (_, _) => FillEventsMenu(events);
        menu.Items.Add(events);
        var kboard = new MenuItem { Header = "K-Board" };
        kboard.Items.Add(new MenuItem()); // placeholder so it opens as a submenu; filled when opened
        kboard.SubmenuOpened += (_, _) => FillKBoardMenu(kboard);
        menu.Items.Add(kboard);
        menu.Items.Add(new Separator());
        var pause = TrayItem(string.Empty, (_, _) => SetPaused(!Config.Settings.Paused));
        menu.Items.Add(pause);
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem(Loc.T("tray.exit"), (_, _) => _ = ExitAsync()));
        menu.Opened += (_, _) =>
        {
            window.Header = Loc.T(MainWindowShown ? "tray.hide" : "tray.show");
            pause.Header = Loc.T(Config.Settings.Paused ? "pause.resume" : "pause.pause");
        };

        _tray = new TaskbarIcon
        {
            ToolTipText = TrayTooltip(),
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _tray.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        _appliedTrayIcon = null;
        ApplyTrayIcon();
        _tray.ForceCreate(false);
    }

    private bool MainWindowShown => _main is { IsVisible: true } m && m.WindowState != WindowState.Minimized;

    private string? _appliedTrayIcon;

    /// <summary>K-Desktop's icon or the one chosen in Settings › Appearance.</summary>
    private void ApplyTrayIcon()
    {
        if (_tray is null) return;
        var wanted = Config.Settings.TrayIcon ?? string.Empty;
        // While the voice listens, the icon shows a red dot.
        var listening = Voice?.IsListening == true || Engine?.Recording.IsRecording == true;
        var key = wanted + (listening ? "|voice" : "");
        if (_appliedTrayIcon == key) return;
        _appliedTrayIcon = key;
        if (TrayIcons.Custom(wanted, listening) is { } custom) _tray.Icon = custom;
        else _tray.IconSource = TrayIcons.Default();
    }

    /// <summary>One checkable entry per K-Board profile (switches it on / off) and a shortcut to the page.</summary>
    private void FillKBoardMenu(MenuItem parent)
    {
        parent.Items.Clear();
        foreach (var profile in Config.KBoards.OrderBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MenuItem { Header = profile.Name, IsCheckable = true, IsChecked = profile.Active, StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                profile.Active = item.IsChecked;
                Ui.ShowOsd(Loc.T(profile.Active ? "osd.kboardOn" : "osd.kboardOff", profile.Name), profile.Active ? OsdKind.Success : OsdKind.Info);
            };
            parent.Items.Add(item);
        }
        if (Config.KBoards.Count == 0) parent.Items.Add(new MenuItem { Header = Loc.T("kboard.listEmptyShort"), IsEnabled = false });
        parent.Items.Add(new Separator());
        parent.Items.Add(TrayItem(Loc.T("tray.kboard"), (_, _) => ShowMainWindow(w => w.Navigate("kboard"))));
    }

    /// <summary>One checkable entry per event (switches it on / off) and a shortcut to the page.</summary>
    private void FillEventsMenu(MenuItem parent)
    {
        parent.Items.Clear();
        foreach (var rule in Config.Events)
        {
            var item = new MenuItem { Header = rule.Name, IsCheckable = true, IsChecked = rule.Enabled, StaysOpenOnClick = true, ToolTip = rule.Summary };
            item.Click += (_, _) =>
            {
                rule.Enabled = item.IsChecked;
                Ui.ShowOsd(Loc.T(rule.Enabled ? "osd.eventEnabled" : "osd.eventDisabled", rule.Name), rule.Enabled ? OsdKind.Success : OsdKind.Info);
            };
            parent.Items.Add(item);
        }
        if (Config.Events.Count == 0) parent.Items.Add(new MenuItem { Header = Loc.T("tray.noEvents"), IsEnabled = false });
        parent.Items.Add(new Separator());
        parent.Items.Add(TrayItem(Loc.T("tray.openEvents"), (_, _) => ShowMainWindow(w => w.Navigate("events"))));
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

    // ── K-Desktop 4 detection ────────────────────────────────────────────

    private static List<Process> FindLegacyInstances()
    {
        var self = Environment.ProcessId;
        var ours = AppPaths.ExecutablePath;
        var result = new List<Process>();
        foreach (var name in new[] { "KDesktop", "KDesktop4", "KDesktopL" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.Id == self) { p.Dispose(); continue; }
                    // MainModule is denied for an elevated process (a K-Desktop 5 started as administrator): the
                    // limited query works across elevation levels. Not even that: better no warning than a wrong one.
                    var path = WindowTools.ProcessPath(p.Id);
                    if (path is null || path.Equals(ours, StringComparison.OrdinalIgnoreCase)) { p.Dispose(); continue; }
                    if (FileVersionInfo.GetVersionInfo(path).FileMajorPart >= 5) { p.Dispose(); continue; }
                    result.Add(p);
                }
                catch { p.Dispose(); }
            }
        }
        return result;
    }

    // ── exit ─────────────────────────────────────────────────────────────

    /// <param name="sound">Played while closing (the exit hotkey's sound); the exit waits until it ends.</param>
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hookRefresh.Stop();
        _backupTimer.Stop();
        Events?.Dispose();
        Router.Dispose();
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
        _tray?.Dispose();
        _instance?.Dispose();
        Log.Info("K-Desktop cerrado");
        Log.Flush();
        base.OnExit(e);
    }
}
