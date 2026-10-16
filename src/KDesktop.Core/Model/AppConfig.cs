using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KDesktop.Core.Model;

/// <summary>Root of the persisted configuration (config.json).</summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 2;
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Command> Commands { get; set; } = [];
    public ObservableCollection<QuickMenu> QuickMenus { get; set; } = [];
    public ObservableCollection<Macro> Macros { get; set; } = [];
    public ObservableCollection<KBoardProfile> KBoards { get; set; } = [];

    /// <summary>Brings a configuration saved by an older version up to date. Returns true when something changed.</summary>
    public bool Upgrade()
    {
        var changed = false;

        // «Desactivar en (on/off)» is no longer an action: it has its own hotkey (Settings › Desactivar en). The first
        // hotkey of the command that had it becomes that hotkey, and a command left without actions goes away.
        foreach (var cmd in Commands.ToList())
        {
            var toggles = cmd.Actions.OfType<ToggleDisabledInAction>().ToList();
            if (toggles.Count == 0) continue;
            changed = true;
            if (string.IsNullOrWhiteSpace(Settings.DisabledInHotkey) && cmd.Enabled && toggles.Any(t => t.Enabled) &&
                cmd.Hotkeys.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) is { } hotkey)
                Settings.DisabledInHotkey = Input.Hotkey.Normalize(hotkey);
            foreach (var toggle in toggles) cmd.Actions.Remove(toggle);
            if (cmd.Actions.Count == 0 && !IsReferenced(cmd.Id)) Commands.Remove(cmd);
        }
        if (SchemaVersion < 2)
        {
            SchemaVersion = 2;
            changed = true;
        }
        return changed;
    }

    /// <summary>Whether a menu, another command, a K-Board or the options use the command.</summary>
    private bool IsReferenced(string id) =>
        QuickMenus.SelectMany(m => m.Descendants()).Any(i => i.CommandId == id) ||
        Commands.SelectMany(c => c.Actions).OfType<CommandControlAction>().Any(a => a.CommandId == id) ||
        KBoards.SelectMany(k => k.Remaps).Any(r => r.CommandId == id) ||
        Settings.VoiceMusicOnCommand == id || Settings.VoiceMusicOffCommand == id;

    public Command? FindCommand(string? id) =>
        string.IsNullOrEmpty(id) ? null : Commands.FirstOrDefault(c => c.Id == id);

    /// <summary>Fills the display-only names of actions that reference menus/commands.</summary>
    public void RefreshReferenceNames()
    {
        foreach (var action in Commands.SelectMany(c => c.Actions))
        {
            switch (action)
            {
                case OpenQuickMenuAction q: q.MenuName = FindMenu(q.MenuId)?.Name; break;
                case CommandControlAction c: c.CommandName = FindCommand(c.CommandId)?.Name; break;
                case PlayMacroAction p: p.MacroName = FindMacro(p.MacroId)?.Name; break;
                case KBoardAction k: k.ProfileName = FindKBoard(k.ProfileId)?.Name; break;
            }
        }
        foreach (var remap in KBoards.SelectMany(k => k.Remaps).Where(r => r.Kind == RemapKind.Command))
            remap.CommandName = FindCommand(remap.CommandId)?.Name;
    }

    public KBoardProfile? FindKBoard(string? idOrName) =>
        string.IsNullOrEmpty(idOrName)
            ? null
            : KBoards.FirstOrDefault(k => k.Id == idOrName) ??
              KBoards.FirstOrDefault(k => k.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public Macro? FindMacro(string? idOrName) =>
        string.IsNullOrEmpty(idOrName)
            ? null
            : Macros.FirstOrDefault(m => m.Id == idOrName) ??
              Macros.FirstOrDefault(m => m.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public QuickMenu? FindMenu(string? idOrName) =>
        string.IsNullOrEmpty(idOrName)
            ? null
            : QuickMenus.FirstOrDefault(m => m.Id == idOrName) ??
              QuickMenus.FirstOrDefault(m => m.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
}

public enum OsdPosition { BottomCenter, TopCenter, Center, BottomRight, TopRight }
public enum AppTheme { System, Dark, Light }

/// <summary>What K-Desktop does about new versions.</summary>
public enum UpdateMode { Off, Notify, Auto }

/// <summary>What «pon música» / «quita la música» do: the default (resume / pause), a command, or a player.</summary>
public enum MusicHandler { Default, Command, Program }

public partial class AppSettings : ObservableObject
{
    // Appearance
    [ObservableProperty] private AppTheme _theme = AppTheme.System;

    /// <summary>Language code ("es", "en"…); null follows the Windows language.</summary>
    [ObservableProperty] private string? _language;

    /// <summary>Show the websites' own icons (favicons) on "Open web" actions; they are downloaded from each site.</summary>
    [ObservableProperty] private bool _webFavicons = true;

    /// <summary>Icon of the notification-area (tray) icon, as command icons are stored; null or empty: K-Desktop's own.</summary>
    [ObservableProperty] private string? _trayIcon;

    // General
    /// <summary>K-Desktop paused: no command runs; only its show-window and exit hotkeys work. Kept across restarts.</summary>
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private bool _startWithWindows = true;

    /// <summary>Open the main window when K-Desktop is started by hand (never when Windows starts it).</summary>
    [ObservableProperty] private bool _showWindowOnStart = true;
    [ObservableProperty] private bool _showTrayIcon = true;
    [ObservableProperty] private string _showWindowHotkey = "CTRL + ALT + Home";
    [ObservableProperty] private string _exitHotkey = "CTRL + ALT + End";
    [ObservableProperty] private string? _startupSound;
    [ObservableProperty] private string? _startupMessage;

    /// <summary>Played when K-Desktop is closed with <see cref="ExitHotkey"/>.</summary>
    [ObservableProperty] private string? _exitSound;
    [ObservableProperty] private bool _showHiddenCommands;

    /// <summary>Which commands the list shows: "none" (those in no QuickMenu), "all", or a QuickMenu id.</summary>
    [ObservableProperty] private string _commandsMenuFilter = "none";

    /// <summary>Main window position and size when it was last closed: "left,top,width,height[,max]" (DIPs, invariant).</summary>
    [ObservableProperty] private string? _windowBounds;

    // Updates
    /// <summary>Off: never checks; Notify: checks and tells; Auto: also downloads and installs (restarting when idle).</summary>
    [ObservableProperty] private UpdateMode _updateMode = UpdateMode.Auto;

    /// <summary>The update manifest (JSON) to check; see UpdateService.</summary>
    [ObservableProperty] private string _updateUrl = DefaultUpdateUrl;

    public const string DefaultUpdateUrl = "https://kobayashi82.net/kdesktop/latest.json";

    // Achievements
    [ObservableProperty] private bool _achievementNotifications = true;
    [ObservableProperty] private bool _achievementSound = true;

    // Macros
    /// <summary>Record mouse movements (not only clicks) when recording a macro.</summary>
    [ObservableProperty] private bool _macroRecordMoves = true;

    // Remote (UDP)
    [ObservableProperty] private bool _udpEnabled = true;
    [ObservableProperty] private bool _udpAllowRemote = true;
    [ObservableProperty] private int _udpPort = 2501;

    // Remote web app (PWA)
    [ObservableProperty] private bool _webEnabled;
    [ObservableProperty] private int _webPort = 2502;

    /// <summary>Accept connections from the network (the proxy on another machine, or devices directly), not only this PC.</summary>
    [ObservableProperty] private bool _webAllowRemote = true;

    /// <summary>Path of the app behind the proxy ("/kdesktop"); empty at the root of its domain.</summary>
    [ObservableProperty] private string _webBasePath = string.Empty;

    /// <summary>Public address of the app (https://…), for the pairing QR. Empty: the one seen through the proxy.</summary>
    [ObservableProperty] private string _webPublicUrl = string.Empty;

    /// <summary>IP of the reverse proxy: its X-Forwarded-* headers are trusted.</summary>
    [ObservableProperty] private string _webTrustedProxy = string.Empty;

    /// <summary>From the network, accept only the proxy (devices must go through HTTPS).</summary>
    [ObservableProperty] private bool _webProxyOnly;

    /// <summary>The web app may move the mouse and type (its touchpad and keyboard).</summary>
    [ObservableProperty] private bool _webInput = true;

    // Voice
    /// <summary>Hold to talk (or tap and talk). Empty: no hotkey.</summary>
    [ObservableProperty] private string _voiceHotkey = string.Empty;

    /// <summary>Microphone for voice orders (endpoint id); null: the Windows default one.</summary>
    [ObservableProperty] private string? _voiceMicId;

    /// <summary>Minutes without use after which the speech model leaves memory.</summary>
    [ObservableProperty] private int _voiceUnloadMinutes = 10;

    /// <summary>Always listen for the wake words (the microphone stays open while there is any active one).</summary>
    [ObservableProperty] private bool _voiceWakeListen = true;

    /// <summary>When the detector is very sure, skip the check with Parakeet (faster, a little riskier).</summary>
    [ObservableProperty] private bool _voiceWakeTrust;

    /// <summary>Take what the PC is playing (music, a film…) out of the microphone: echo cancellation.</summary>
    [ObservableProperty] private bool _voiceEchoCancel = true;

    /// <summary>Turn the other programs down while an order is being said.</summary>
    [ObservableProperty] private bool _voiceDuck = true;

    /// <summary>How far <see cref="VoiceDuck"/> turns them down: % of their volume.</summary>
    [ObservableProperty] private int _voiceDuckLevel = 20;

    // Multimedia (built-in voice orders: pause, next, volume…)
    [ObservableProperty] private bool _voiceMedia = true;

    /// <summary>How much «sube / baja el volumen» moves it, in %.</summary>
    [ObservableProperty] private int _voiceMediaStep = 10;

    [ObservableProperty] private MusicHandler _voiceMusicOn;
    [ObservableProperty] private string? _voiceMusicOnCommand;

    /// <summary>The player «pon música» opens (when it is open already, it only resumes).</summary>
    [ObservableProperty] private string _voiceMusicOnProgram = string.Empty;

    [ObservableProperty] private MusicHandler _voiceMusicOff;
    [ObservableProperty] private string? _voiceMusicOffCommand;

    /// <summary>The process «quita la música» closes; empty: the player of «pon música».</summary>
    [ObservableProperty] private string _voiceMusicOffProcess = string.Empty;

    // OSD
    [ObservableProperty] private OsdPosition _osdPosition = OsdPosition.BottomCenter;
    [ObservableProperty] private double _osdSeconds = 1.6;

    // Mouse
    /// <summary>Block the mouse back/forward buttons in <see cref="BlockSideButtonsIn"/>.</summary>
    [ObservableProperty] private bool _blockSideButtons = true;

    /// <summary>Adds the app in front to <see cref="DisabledIn"/>, or takes it out (it works there too). Empty: none.</summary>
    [ObservableProperty] private string _disabledInHotkey = string.Empty;

    /// <summary>Processes where K-Desktop hotkeys are ignored ("Desactivar en").</summary>
    public ObservableCollection<string> DisabledIn { get; set { field = value ?? []; field.CollectionChanged += (_, _) => Touch(); } } = [];

    public ObservableCollection<string> BlockSideButtonsIn { get; set { field = value ?? []; field.CollectionChanged += (_, _) => Touch(); } } =
        ["explorer", "chrome", "firefox", "msedge", "brave", "telegram", "systemsettings"];

    public AppSettings()
    {
        DisabledIn.CollectionChanged += (_, _) => Touch();
        BlockSideButtonsIn.CollectionChanged += (_, _) => Touch();
    }

    public event EventHandler? Modified;

    private void Touch() => Modified?.Invoke(this, EventArgs.Empty);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Touch();
    }
}
