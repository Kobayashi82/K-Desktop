using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KDesktop.Core.Model;

/// <summary>Root of the persisted configuration (config.json).</summary>
public sealed class AppConfig
{
    /// <summary>3: «Ejecutar al iniciar / al salir» became events.</summary>
    public const int CurrentSchema = 3;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Command> Commands { get; set; } = [];
    public ObservableCollection<QuickMenu> QuickMenus { get; set; } = [];
    public ObservableCollection<Macro> Macros { get; set; } = [];
    public ObservableCollection<KBoardProfile> KBoards { get; set; } = [];

    /// <summary>Automations: «when X happens, run these commands».</summary>
    public ObservableCollection<EventRule> Events { get; set; } = [];

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
        // The launcher is no longer an action: it lives in Settings › Lanzador with its own hotkey. The first hotkey of a
        // command that opened it becomes that hotkey, its options become the settings, and an emptied command goes away.
        foreach (var cmd in Commands.ToList())
        {
            var launchers = cmd.Actions.OfType<LauncherAction>().ToList();
            if (launchers.Count == 0) continue;
            changed = true;
            var first = launchers[0];
            if (string.IsNullOrWhiteSpace(Settings.LauncherHotkey) && cmd.Enabled && first.Enabled &&
                cmd.Hotkeys.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) is { } hotkey)
            {
                Settings.LauncherHotkey = Input.Hotkey.Normalize(hotkey);
                Settings.LauncherApps = first.IncludeApps;
                Settings.LauncherWeb = first.WebSearch;
            }
            foreach (var launcher in launchers) cmd.Actions.Remove(launcher);
            Log.Info($"La acción «Lanzador» de «{cmd.Name}» pasa a Opciones › Lanzador");
            if (cmd.Actions.Count == 0 && !IsReferenced(cmd.Id)) Commands.Remove(cmd);
        }
        // The built-in listening sound was called «Escuchar» in 5.0.11–5.0.14.
        if (Settings.VoiceListenSound == "Escuchar")
        {
            Settings.VoiceListenSound = "Toc";
            changed = true;
        }
        // Until 5.0.34 the update address was the whole manifest; now it is the server and «kdesktop.json» is added.
        if (Settings.UpdateUrl == "https://kobayashi82.net/kdesktop/latest.json")
        {
            Settings.UpdateUrl = AppSettings.DefaultUpdateUrl;
            changed = true;
        }
        // Until 5.0.55 there was one assistant on the desktop at most; now there may be several.
        if (Settings.AssistantActive is { } active)
        {
            if (!Settings.AssistantsOnDesktop.Contains(active)) Settings.AssistantsOnDesktop.Add(active);
            Settings.AssistantActive = null;
            changed = true;
        }
        changed |= MigrateStartupAndExit();
        if (SchemaVersion < CurrentSchema)
        {
            SchemaVersion = CurrentSchema;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// «Ejecutar al iniciar» and «Ejecutar al salir» are no longer options of a command: each becomes one event
    /// («Al iniciar K-Desktop» / «Al salir de K-Desktop») with those commands in the order of the list. A disabled
    /// command goes in too: it didn't run because it was disabled and it still won't, but enabling it brings it back.
    /// </summary>
    private bool MigrateStartupAndExit()
    {
        var startup = Commands.Where(c => c.RunOnStartup).ToList();
        var exit = Commands.Where(c => c.RunOnExit).ToList();
        if (startup.Count == 0 && exit.Count == 0) return false;
        if (startup.Count > 0) Events.Add(MigratedEvent(Text("event.migrated.startup"), new AppStartTrigger(), startup));
        if (exit.Count > 0) Events.Add(MigratedEvent(Text("event.migrated.exit"), new AppExitTrigger(), exit));
        foreach (var c in startup.Concat(exit)) c.RunOnStartup = c.RunOnExit = false;
        if (startup.Count > 0) Log.Info($"«Ejecutar al iniciar» pasa a un evento con {startup.Count} comandos: {string.Join(", ", startup.Select(c => c.Name))}");
        if (exit.Count > 0) Log.Info($"«Ejecutar al salir» pasa a un evento con {exit.Count} comandos: {string.Join(", ", exit.Select(c => c.Name))}");
        return true;
    }

    /// <summary>A text in the language of this configuration (it loads before its language is applied).</summary>
    private string Text(string key) =>
        Settings.Language is { } code && Localization.Loc.Strings(code, key).TryGetValue(key, out var text) ? text : Localization.Loc.T(key);

    /// <summary>An event created from the old options (also by the K-Desktop 4 import).</summary>
    public static EventRule MigratedEvent(string name, EventTrigger trigger, IEnumerable<Command> commands)
    {
        var rule = new EventRule { Name = name, Trigger = trigger, Icon = trigger is AppStartTrigger ? "kd:Play24:green" : "kd:Power24:red" };
        foreach (var c in commands) rule.Commands.Add(c.Id);
        return rule;
    }

    /// <summary>Whether a menu, another command, a K-Board, an event or the options use the command.</summary>
    public bool IsReferenced(string id) =>
        QuickMenus.SelectMany(m => m.Descendants()).Any(i => i.CommandId == id) ||
        Commands.SelectMany(c => c.Actions).OfType<CommandControlAction>().Any(a => a.CommandId == id) ||
        KBoards.SelectMany(k => k.Remaps).Any(r => r.CommandId == id) ||
        Events.Any(e => e.Commands.Contains(id) || e.EndCommands.Contains(id)) ||
        Settings.VoiceMusicOnCommand == id || Settings.VoiceMusicOffCommand == id;

    /// <summary>The events that run the command (when it fires or when it ends).</summary>
    public IEnumerable<EventRule> EventsUsing(string id) => Events.Where(e => e.Commands.Contains(id) || e.EndCommands.Contains(id));

    public EventRule? FindEvent(string? idOrName) =>
        string.IsNullOrEmpty(idOrName)
            ? null
            : Events.FirstOrDefault(e => e.Id == idOrName) ??
              Events.FirstOrDefault(e => e.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public Command? FindCommand(string? id) =>
        string.IsNullOrEmpty(id) ? null : Commands.FirstOrDefault(c => c.Id == id);

    /// <summary>Fills the display-only names of actions that reference menus/commands.</summary>
    public void RefreshReferenceNames()
    {
        IReadOnlyList<Platform.MonitorInfo>? monitors = null;
        foreach (var action in Commands.SelectMany(c => c.Actions))
        {
            switch (action)
            {
                case OpenQuickMenuAction q: q.MenuName = FindMenu(q.MenuId)?.Name; break;
                case CommandControlAction c:
                    c.CommandName = FindCommand(c.CommandId)?.Name;
                    c.EventName = FindEvent(c.EventId)?.Name;
                    break;
                case PlayMacroAction p: p.MacroName = FindMacro(p.MacroId)?.Name; break;
                case KBoardAction k: k.ProfileName = FindKBoard(k.ProfileId)?.Name; break;
                case AssistantExtraAction x: x.ExtraName = ExtraName(x.Assistant, x.Extra); break;
                case DisplayAction { Operation: DisplayOperation.Mode } d when d.Monitor.Length > 0:
                    d.MonitorName = (monitors ??= Platform.DisplayTools.Monitors()).FirstOrDefault(m => m.DeviceName == d.Monitor)?.Name;
                    break;
            }
        }
        foreach (var remap in KBoards.SelectMany(k => k.Remaps).Where(r => r.Kind == RemapKind.Command))
            remap.CommandName = FindCommand(remap.CommandId)?.Name;
    }

    /// <summary>«Lander: Modo manual» for an installed assistant's extra, or null when it is not installed (or has no such extra).</summary>
    public static string? ExtraName(string assistant, string extra) =>
        extra.Length == 0 ? null : Assistant.Packages.AssistantExtras.Find(assistant, extra)?.DisplayName(Localization.Loc.Instance.Language);

    /// <summary>
    /// Who else answers <paramref name="hotkey"/> for every program: the names of the enabled commands with it that are
    /// not limited to some programs (but <paramref name="except"/>), and «K-Desktop» when it is one of its own hotkeys.
    /// </summary>
    public IReadOnlyList<string> HotkeyUsers(string hotkey, Command? except = null)
    {
        var key = Input.Hotkey.Normalize(hotkey);
        if (key.Length == 0) return [];
        var users = Commands
            .Where(c => !ReferenceEquals(c, except) && c.Enabled && c.Processes.Count == 0 && c.Hotkeys.Any(h => Input.Hotkey.Comparer.Equals(Input.Hotkey.Normalize(h), key)))
            .Select(c => c.Name)
            .ToList();
        if (Settings.OwnHotkeys.Any(h => Input.Hotkey.Comparer.Equals(Input.Hotkey.Normalize(h), key))) users.Add("K-Desktop");
        return users;
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

/// <summary>
/// Which assistant takes what is not addressed to anyone (a command with gestures run by a hotkey, a notice, the generic
/// wake word, the panel's chat without choosing).
/// </summary>
public enum AssistantTarget
{
    /// <summary>The last one the user talked to (until then, the first one put on the desktop).</summary>
    LastTalkedTo,

    /// <summary>One at random, chosen once per order.</summary>
    Random,

    /// <summary><see cref="AppSettings.AssistantTargetId"/> (when it is not on the desktop, as <see cref="LastTalkedTo"/>).</summary>
    Fixed,
}

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

    // Automatic backups (Storage/AutoBackup): a full .zip every few days, the last ones kept
    [ObservableProperty] private bool _autoBackup = true;

    /// <summary>Days between automatic backups (1 = daily).</summary>
    [ObservableProperty] private int _autoBackupDays = 1;

    /// <summary>How many automatic backups are kept (older ones are deleted).</summary>
    [ObservableProperty] private int _autoBackupKeep = 10;

    /// <summary>Where they go; empty = the Backups folder of the data dir (another disk, OneDrive or a NAS is safer).</summary>
    [ObservableProperty] private string _autoBackupFolder = string.Empty;

    // Captures and recordings (Settings › Capturas)
    /// <summary>Where screenshots go; empty = PicturesK-Desktop.</summary>
    [ObservableProperty] private string _captureFolder = string.Empty;

    /// <summary>Where audio recordings go; empty = MusicK-Desktop.</summary>
    [ObservableProperty] private string _recordFolder = string.Empty;

    /// <summary>Keep the last seconds of audio all the time, to save them with «Grabar audio › Guardar la repetición».</summary>
    [ObservableProperty] private bool _audioReplay;
    [ObservableProperty] private int _audioReplaySeconds = 30;
    [ObservableProperty] private AudioSource _audioReplaySource = AudioSource.Output;

    /// <summary>«Lupa»: how much it zooms (the web app and actions without their own level).</summary>
    [ObservableProperty] private double _zoomLevel = 2;
    [ObservableProperty] private ZoomMode _zoomMode = ZoomMode.Lens;
    [ObservableProperty] private ZoomLensSize _zoomLensSize = ZoomLensSize.Medium;

    /// <summary>Icon of the notification-area (tray) icon, as command icons are stored; null or empty: K-Desktop's own.</summary>
    [ObservableProperty] private string? _trayIcon;

    // General
    /// <summary>K-Desktop paused: no command runs; only its show-window and exit hotkeys work. Kept across restarts.</summary>
    [ObservableProperty] private bool _paused;
    [ObservableProperty] private bool _startWithWindows = true;

    /// <summary>Open the main window when K-Desktop is started by hand (never when Windows starts it).</summary>
    [ObservableProperty] private bool _showWindowOnStart = true;
    [ObservableProperty] private bool _showTrayIcon = true;
    /// <summary>K-Desktop's own hotkeys (show the window, exit, voice, «Desactivar en», pause, launcher, talk to the assistant); unset ones are empty.</summary>
    [JsonIgnore]
    public IEnumerable<string> OwnHotkeys => [ShowWindowHotkey, ExitHotkey, VoiceHotkey, DisabledInHotkey, PauseHotkey, LauncherHotkey,
        AssistantEnabled ? AssistantTalkHotkey : string.Empty];

    [ObservableProperty] private string _showWindowHotkey = "CTRL + ALT + Home";
    [ObservableProperty] private string _exitHotkey = "CTRL + ALT + End";
    [ObservableProperty] private string? _startupSound;
    [ObservableProperty] private string? _startupMessage;

    /// <summary>Played when K-Desktop is closed with <see cref="ExitHotkey"/>.</summary>
    [ObservableProperty] private string? _exitSound;
    [ObservableProperty] private bool _showHiddenCommands;

    /// <summary>Opens the launcher (Settings › Lanzador); empty = no hotkey.</summary>
    [ObservableProperty] private string _launcherHotkey = string.Empty;

    /// <summary>The application in front doesn't get the key of the launcher hotkey.</summary>
    [ObservableProperty] private bool _launcherBlockKey = true;

    /// <summary>The application in front doesn't get the key of the voice hotkey.</summary>
    [ObservableProperty] private bool _voiceBlockKey = true;

    /// <summary>The launcher hotkey works in the applications of «Desactivar en» too.</summary>
    [ObservableProperty] private bool _launcherIgnoreDisabledApps;

    /// <summary>The voice hotkey works in the applications of «Desactivar en» too.</summary>
    [ObservableProperty] private bool _voiceIgnoreDisabledApps;

    /// <summary>The launcher also finds the programs of the Start menu.</summary>
    [ObservableProperty] private bool _launcherApps = true;

    /// <summary>The launcher offers a web search when nothing else answers.</summary>
    [ObservableProperty] private bool _launcherWeb = true;

    /// <summary>"google", "duckduckgo", "bing", "brave" or "custom" (then <see cref="LauncherSearchUrl"/>).</summary>
    [ObservableProperty] private string _launcherSearchEngine = "google";

    /// <summary>Address of a search engine of your own; {0} is where the text goes.</summary>
    [ObservableProperty] private string _launcherSearchUrl = string.Empty;

    /// <summary>The launcher works out sums typed in it («12*3,5»).</summary>
    [ObservableProperty] private bool _launcherCalculator = true;

    /// <summary>Hidden commands appear in the launcher too.</summary>
    [ObservableProperty] private bool _launcherHidden;

    /// <summary>The most used first (and shown before typing anything).</summary>
    [ObservableProperty] private bool _launcherByUse = true;

    /// <summary>
    /// Where the launcher was dragged to: "x;y" as fractions of the work area of its screen (the centre of its top edge),
    /// so it lands in the same place on any monitor. Empty = centred, a bit above the middle.
    /// </summary>
    [ObservableProperty] private string _launcherPosition = string.Empty;

    /// <summary>Which commands the list shows: "none" (those in no QuickMenu), "all", or a QuickMenu id.</summary>
    [ObservableProperty] private string _commandsMenuFilter = "none";

    /// <summary>Main window position and size when it was last closed: "left,top,width,height[,max]" (DIPs, invariant).</summary>
    [ObservableProperty] private string? _windowBounds;

    // Updates
    /// <summary>Off: never checks; Notify: checks and tells; Auto: also downloads and installs (restarting when idle).</summary>
    [ObservableProperty] private UpdateMode _updateMode = UpdateMode.Auto;

    /// <summary>Where the updates are: the manifest is «kdesktop.json» there (see <see cref="ManifestUrl"/>).</summary>
    [ObservableProperty] private string _updateUrl = DefaultUpdateUrl;

    public const string DefaultUpdateUrl = "https://kobayashi82.net";

    /// <summary>The name of the update manifest, added to <see cref="UpdateUrl"/>.</summary>
    public const string ManifestName = "kdesktop.json";

    /// <summary>The manifest to check for an address: «https://server/path» → «https://server/path/kdesktop.json».
    /// A full address of a .json (as older versions saved it) is used as it is.</summary>
    public static string ManifestUrl(string? address)
    {
        var url = string.IsNullOrWhiteSpace(address) ? DefaultUpdateUrl : address.Trim();
        return url.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? url : url.TrimEnd('/') + "/" + ManifestName;
    }

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

    /// <summary>
    /// Internet address of the app through the proxy (https://…, maybe with a path such as /kdesktop, which becomes
    /// the base path), for the pairing QR and the Android app. Empty: the one seen through the proxy.
    /// </summary>
    [ObservableProperty] private string _webPublicUrl = string.Empty;

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

    /// <summary>Sound when it starts listening for the order (built-in name or .wav path); empty for none.</summary>
    [ObservableProperty] private string? _voiceListenSound = "Toc";

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

    // Assistant module (off: K-Desktop starts nothing of it)
    [ObservableProperty] private bool _assistantEnabled;

    /// <summary>Until 5.0.55, the one assistant on the desktop: only read, to move it to <see cref="AssistantsOnDesktop"/>.</summary>
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private string? _assistantActive;

    /// <summary>Who takes what is not addressed to anyone.</summary>
    [ObservableProperty] private AssistantTarget _assistantTarget;

    /// <summary>The assistant of <see cref="AssistantTarget.Fixed"/>.</summary>
    [ObservableProperty] private string? _assistantTargetId;

    /// <summary>The last assistant the user talked to (its wake word, its menu…), for <see cref="AssistantTarget.LastTalkedTo"/>.</summary>
    [ObservableProperty] private string? _assistantLastTalkedTo;

    /// <summary>
    /// The wake word of each assistant: assistant id → id of a recorded wake word (VoiceWakeWords). A word belongs to
    /// one assistant at most; words of none are K-Desktop's. Replaced whole on every change (so it is saved).
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _assistantWakeWords = [];

    /// <summary>
    /// Hold to talk to the assistants (Options → Assistant → General, stage 5.2): what is said goes to the one of
    /// <see cref="AssistantTarget"/>, or to the module without a body. Empty: none.
    /// </summary>
    [ObservableProperty] private string _assistantTalkHotkey = string.Empty;

    /// <summary>
    /// How the assistants carry out orders (stage 6.7): <c>showy</c>, <c>normal</c> or <c>fast</c> (no manoeuvre over half a
    /// second).
    /// </summary>
    [ObservableProperty] private string _assistantManoeuvres = "normal";

    /// <summary>A command with more actions than this shows only those on windows (the rest run at once); 0: all of them.</summary>
    [ObservableProperty] private int _assistantAnimateMax = 5;

    /// <summary>
    /// The assistant also shows the commands launched other ways (hotkeys, menus, K-Desktop's voice, the phone…), quickly
    /// and without holding them back (stage 6.7). Off by default.
    /// </summary>
    [ObservableProperty] private bool _assistantAdorn;

    /// <summary>
    /// Who says the assistants' answers aloud (Options → Assistant → Voice, stage 5.4): <c>auto</c> (Pocket TTS if it is
    /// downloaded for the language, else Windows' voices, else beeps), <c>pocket</c>, <c>windows</c>, <c>beeps</c> or
    /// <c>off</c> (only the bubble).
    /// </summary>
    [ObservableProperty] private string _assistantVoiceEngine = "auto";

    /// <summary>The voice of Pocket TTS («eve»…); downloaded when chosen.</summary>
    [ObservableProperty] private string _assistantVoicePocket = Assistant.Voice.PocketModels.DefaultVoice;

    /// <summary>The Windows voice by name; empty: the first one of the language.</summary>
    [ObservableProperty] private string _assistantVoiceWindows = string.Empty;

    /// <summary>
    /// The voice profile for all of them (stage 5.5: one of K-Desktop's, <c>robot</c>, <c>mission</c>…); empty: each one's
    /// own (the one its package brings, or «Natural»).
    /// </summary>
    [ObservableProperty] private string _assistantVoiceProfile = string.Empty;

    /// <summary>How fast they speak: 1 = normal (0.5 … 2).</summary>
    [ObservableProperty] private double _assistantVoiceSpeed = 1;

    /// <summary>Volume of their voice, 0–100 (under the assistants' volume).</summary>
    [ObservableProperty] private int _assistantVoiceVolume = 100;

    /// <summary>Their voice sounds from where they are (left or right speaker, stage 5.6).</summary>
    [ObservableProperty] private bool _assistantVoicePan = true;

    /// <summary>At night (22:00–8:00) they speak at half the volume.</summary>
    [ObservableProperty] private bool _assistantVoiceNight;

    /// <summary>With something full screen (a film, a game) they only use the bubble.</summary>
    [ObservableProperty] private bool _assistantVoiceFullscreen;

    /// <summary>The assistants' sounds (engines, landings…), mixed by their process.</summary>
    [ObservableProperty] private bool _assistantSound = true;

    /// <summary>Volume of the assistants' sounds, 0–100 (each assistant may have its own on top).</summary>
    [ObservableProperty] private int _assistantVolume = 100;

    /// <summary>Where the assistants' AI runs (Options → Assistant → Brain).</summary>
    [ObservableProperty] private Assistant.Brain.BrainProvider _assistantBrainProvider;

    /// <summary>Its address; empty: the usual one of the provider.</summary>
    [ObservableProperty] private string _assistantBrainEndpoint = string.Empty;

    /// <summary>Its API key (llama-swap, OpenAI compatible), protected with DPAPI (<see cref="Platform.Secrets"/>); empty: none.</summary>
    [ObservableProperty] private string _assistantBrainApiKey = string.Empty;

    /// <summary>The general model; empty: chosen automatically (about 4 B, with tools).</summary>
    [ObservableProperty] private string _assistantBrainModel = string.Empty;

    /// <summary>The assistants learn what is worth remembering about the user from what they talk about (stage 3.7).</summary>
    [ObservableProperty] private bool _assistantMemoryLearn = true;

    /// <summary>
    /// The model of each assistant that has its own: assistant id → model (absent: the general one, also when the server
    /// does not have it). Replaced whole on every change (so it is saved).
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _assistantModels = [];

    /// <summary>
    /// Before doing something, an assistant asks: <c>never</c>, <c>risky</c> (only power, scripts…; the default) or
    /// <c>first</c> (also the first time, until «yes, and don't ask again»). <see cref="Assistant.AskPolicy"/>.
    /// </summary>
    [ObservableProperty] private string _assistantAskBefore = Assistant.AskPolicy.Risky;

    /// <summary>The tools that no longer ask before doing something («yes, and don't ask again»). Replaced whole on every change.</summary>
    [ObservableProperty] private List<string> _assistantTrustedTools = [];

    /// <summary>The commands (ids) an assistant runs without asking first («yes, and don't ask again»). Replaced whole on every change.</summary>
    [ObservableProperty] private List<string> _assistantTrustedCommands = [];

    // OSD
    [ObservableProperty] private OsdPosition _osdPosition = OsdPosition.BottomCenter;
    [ObservableProperty] private double _osdSeconds = 1.6;

    // Mouse
    /// <summary>Block the mouse back/forward buttons in <see cref="BlockSideButtonsIn"/>.</summary>
    [ObservableProperty] private bool _blockSideButtons = true;

    /// <summary>Adds the app in front to <see cref="DisabledIn"/>, or takes it out (it works there too). Empty: none.</summary>
    [ObservableProperty] private string _disabledInHotkey = string.Empty;

    /// <summary>Pauses K-Desktop or resumes it (it works while paused, like show-window and exit). Empty: none.</summary>
    [ObservableProperty] private string _pauseHotkey = string.Empty;

    /// <summary>Processes where K-Desktop hotkeys are ignored ("Desactivar en").</summary>
    public ObservableCollection<string> DisabledIn { get; set { field = value ?? []; field.CollectionChanged += (_, _) => Touch(); } } = [];

    /// <summary>The installed assistants on the desktop, in the order they were put there (empty: the module works without a body).</summary>
    public ObservableCollection<string> AssistantsOnDesktop { get; set { field = value ?? []; field.CollectionChanged += (_, _) => Touch(); } } = [];

    public ObservableCollection<string> BlockSideButtonsIn { get; set { field = value ?? []; field.CollectionChanged += (_, _) => Touch(); } } =
        ["explorer", "chrome", "firefox", "msedge", "brave", "telegram", "systemsettings"];

    public AppSettings()
    {
        DisabledIn.CollectionChanged += (_, _) => Touch();
        BlockSideButtonsIn.CollectionChanged += (_, _) => Touch();
        AssistantsOnDesktop.CollectionChanged += (_, _) => Touch();
    }

    public event EventHandler? Modified;

    private void Touch() => Modified?.Invoke(this, EventArgs.Empty);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Touch();
    }
}
