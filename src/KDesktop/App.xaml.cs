using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Remote;
using KDesktop.Core.Storage;
using KDesktop.QuickMenus;
using KDesktop.Services;
using KDesktop.Views;
using Microsoft.Win32;

namespace KDesktop;

public partial class App : Application
{
    private readonly DispatcherTimer _indexDebounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hookRefresh = new() { Interval = TimeSpan.FromMinutes(10) };
    private TaskbarIcon? _tray;
    private SingleInstance? _instance;
    private MainWindow? _main;
    private bool _exiting;
    private AppTheme _appliedTheme;
    private string? _appliedLanguage;

    public static new App Current => (App)Application.Current;

    public bool StartedByWindows { get; init; }

    public string[] Arguments { get; init; } = [];

    private bool HooksDisabled => Arguments.Contains("--no-hooks");
    private bool UdpDisabled => Arguments.Contains("--no-udp");

    /// <summary>False when UDP was disabled from the command line.</summary>
    public bool UdpAllowed => !UdpDisabled;

    public ConfigStore Store { get; } = new();
    public HotkeyRouter Router { get; } = new();
    public CommandEngine Engine { get; private set; } = null!;
    public UdpServer Udp { get; private set; } = null!;
    public UiServices Ui { get; private set; } = null!;
    public QuickMenuController QuickMenus { get; private set; } = null!;

    public AppConfig Config => Store.Config;

    /// <summary>Set when this run imported the K-Desktop 4 data.</summary>
    public LegacyImporter.Report? ImportReport { get; private set; }

    /// <summary>Running K-Desktop 4 processes (they would duplicate every hotkey).</summary>
    public IReadOnlyList<Process> LegacyInstances => FindLegacyInstances();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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

        Ui = new UiServices(Dispatcher, () => Config.Settings, () => ShowMainWindow(), () => _ = ExitAsync());
        Engine = new CommandEngine(Store, Ui);
        QuickMenus = new QuickMenuController(() => Config, Engine);
        Ui.QuickMenus = QuickMenus;
        QuickMenus.EditRequested += (commandId, menuId) =>
        {
            if (commandId is not null) ShowMainWindow(w => w.OpenCommand(commandId));
            else ShowMainWindow(w => w.OpenQuickMenu(menuId));
        };
        QuickMenus.Prewarm();

        // Hotkeys
        Router.Triggered += Engine.OnHotkey;
        Router.BuiltIn += hotkey => Dispatcher.BeginInvoke(() =>
        {
            if (hotkey == BuiltInHotkey.ShowWindow) ShowMainWindow();
            else _ = ExitAsync();
        });
        Router.HasCancellable = () => Engine.HasCancellable;
        Router.CancelRequested += Engine.CancelAll;
        RebuildIndex();
        _indexDebounce.Tick += (_, _) =>
        {
            _indexDebounce.Stop();
            RebuildIndex();
            ApplySettings();
        };
        Store.Changed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _indexDebounce.Stop();
            _indexDebounce.Start();
        });
        if (!HooksDisabled) Router.Start();
        else Log.Warn("Hooks desactivados por la línea de comandos (--no-hooks)");

        // Keep the hooks healthy: reinstall them periodically and after unlocking the session.
        _hookRefresh.Tick += (_, _) => Router.Reinstall();
        _hookRefresh.Start();
        SystemEvents.SessionSwitch += (_, args) =>
        {
            if (args.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect) Router.Reinstall();
        };
        SystemEvents.SessionEnding += OnSessionEnding;

        // Remote control
        Udp = new UdpServer(Engine, Ui);
        ApplySettings();
        if (ProcessLauncher.IsElevated) Task.Run(FirewallRule.Ensure);

        _instance = new SingleInstance(
            () => Dispatcher.BeginInvoke(() => ShowMainWindow()),
            args => Dispatcher.BeginInvoke(() => HandleArguments(args)));

        // Startup feedback and "run on startup" commands.
        var s = Config.Settings;
        Sounds.Play(s.StartupSound);
        if (!string.IsNullOrWhiteSpace(s.StartupMessage))
            Ui.ShowOsd(s.StartupMessage.Replace("\"version\"", AppPaths.VersionName, StringComparison.OrdinalIgnoreCase)
                                       .Replace("\"ver\"", AppPaths.VersionName, StringComparison.OrdinalIgnoreCase));
        _ = Engine.RunStartupCommandsAsync();

        var requestsSomething = Program.ValueOf(Arguments, "--menu") is not null || Program.ValueOf(Arguments, "--run") is not null;
        if ((!StartedByWindows && !requestsSomething) || ImportReport is not null) ShowMainWindow();
        HandleArguments(Arguments);
        Log.Info($"Listo: {Config.Commands.Count} comandos, {Config.QuickMenus.Count} QuickMenus");
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
        if (args.Contains("--show", StringComparer.OrdinalIgnoreCase)) ShowMainWindow();
        if (args.Contains("--exit", StringComparer.OrdinalIgnoreCase)) _ = ExitAsync();
    }

    private void LoadConfiguration()
    {
        if (Store.Load()) return;
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
        Store.Replace(config);
        IconProvider.Invalidate();
        RebuildIndex();
        ApplySettings();
        return report;
    }

    private void RebuildIndex() => Router.UpdateIndex(new HotkeyIndex(Config.Commands, Config.Settings));

    /// <summary>Applies settings that have side effects (theme, tray, UDP).</summary>
    public void ApplySettings()
    {
        var s = Config.Settings;
        if (s.Theme != _appliedTheme)
        {
            _appliedTheme = s.Theme;
            ThemeService.Apply(s.Theme);
        }

        if (s.Language != _appliedLanguage)
        {
            _appliedLanguage = s.Language;
            Loc.Instance.Load(s.Language);
            foreach (var action in Config.Commands.SelectMany(c => c.Actions)) action.RefreshSummary();
        }

        if (s.ShowTrayIcon) EnsureTray();
        else if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }

        if (s.UdpEnabled && !UdpDisabled) Udp.Start(s.UdpPort, s.UdpAllowRemote);
        else Udp.Stop();
    }

    // ── tray ─────────────────────────────────────────────────────────────

    private void EnsureTray()
    {
        if (_tray is not null) return;
        var menu = new ContextMenu();
        menu.Items.Add(TrayItem("Abrir K-Desktop", (_, _) => ShowMainWindow()));
        menu.Items.Add(new Separator());
        foreach (var qm in Config.QuickMenus.Take(1))
            menu.Items.Add(TrayItem($"Abrir QuickMenu {qm.Name}", (_, _) => QuickMenus.Show(qm.Id, ForegroundWindow.Capture())));
        menu.Items.Add(TrayItem("Carpeta de datos", (_, _) => ShellTools.OpenSpecial("KDesktopData")));
        menu.Items.Add(new Separator());
        menu.Items.Add(TrayItem("Salir", (_, _) => _ = ExitAsync()));

        _tray = new TaskbarIcon
        {
            ToolTipText = AppPaths.VersionName,
            IconSource = BitmapFrame.Create(new Uri("pack://application:,,,/KDesktop;component/Assets/Icons/KDesktop.ico")),
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _tray.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        _tray.ForceCreate(false);
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
            _main.Closed += (_, _) =>
            {
                _main = null;
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
                    var path = p.MainModule?.FileName;
                    if (path is not null && path.Equals(ours, StringComparison.OrdinalIgnoreCase)) { p.Dispose(); continue; }
                    if (FileVersionInfo.GetVersionInfo(path ?? "").FileMajorPart >= 5) { p.Dispose(); continue; }
                    result.Add(p);
                }
                catch (Win32Exception) { result.Add(p); }
                catch { p.Dispose(); }
            }
        }
        return result;
    }

    // ── exit ─────────────────────────────────────────────────────────────

    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("Saliendo…");
        QuickMenus.CloseAll(restoreFocus: false);
        _main?.Close();
        try { await Engine.RunExitCommandsAsync(); } catch (Exception ex) { Log.Error("Error en los comandos de salida", ex); }
        Shutdown();
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        Log.Info($"La sesión termina ({e.Reason})");
        try { Engine.RunExitCommandsAsync().Wait(TimeSpan.FromSeconds(4)); } catch { /* ignore */ }
        Store.Flush();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hookRefresh.Stop();
        Router.Dispose();
        Udp?.Dispose();
        Store.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        Log.Info("K-Desktop cerrado");
        Log.Flush();
        base.OnExit(e);
    }
}
