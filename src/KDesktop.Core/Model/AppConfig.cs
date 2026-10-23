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
        // Until 5.0.93 the natural voice of the assistants was Pocket TTS; now it is Kokoro.
        if (Settings.AssistantVoiceEngine == "pocket")
        {
            Settings.AssistantVoiceEngine = "kokoro";
            changed = true;
        }
        // In 5.0.111 what K-Desktop had of its own (answering without an assistant) went under the empty key; since 5.0.112
        // K-Desktop is an assistant with its id.
        if (Settings.AssistantPersonal.TryGetValue(string.Empty, out var bodiless))
        {
            var personal = Settings.AssistantPersonal.Where(p => p.Key.Length > 0).ToDictionary(p => p.Key, p => p.Value);
            personal.TryAdd(Assistant.AssistantTargeting.KDesktop, bodiless);
            Settings.AssistantPersonal = personal;
            changed = true;
        }
        // Until 5.0.112 there was a word and a key to talk to «the assistant»; since 5.0.113 each one has its own.
        if (Settings.AssistantWakeWords.ContainsKey("*"))
        {
            Log.Info("La palabra «para el asistente» se queda sin dueño (cada asistente tiene la suya)");
            Settings.AssistantWakeWords = Settings.AssistantWakeWords.Where(p => p.Key != "*").ToDictionary(p => p.Key, p => p.Value);
            changed = true;
        }
        if (Settings.AssistantTalkHotkey is { } talk)
        {
            if (talk.Trim().Length > 0)
            {
                var to = Settings.AssistantLastTalkedTo ?? Assistant.AssistantTargeting.KDesktop;
                Settings.AssistantTalkHotkeys = new Dictionary<string, string>(Settings.AssistantTalkHotkeys) { [to] = Input.Hotkey.Normalize(talk) };
                Log.Info($"La tecla para hablar con el asistente pasa a ser la de «{to}»");
            }
            Settings.AssistantTalkHotkey = null;
            changed = true;
        }
        changed |= MigrateBrain();
        changed |= MigrateStartupAndExit();
        if (SchemaVersion < CurrentSchema)
        {
            SchemaVersion = CurrentSchema;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Until 5.0.106 the assistants' brain was one provider; since 5.0.107 the assistant process starts its own llama-servers
    /// and the other servers are a list. The llama-server goes to the list of servers; Ollama, llama-swap or an OpenAI-compatible
    /// one becomes the other server «r1» (Ollama only if something of it was set: it was the default) and its models get its
    /// id in front; «none» is the model «none».
    /// </summary>
    private bool MigrateBrain()
    {
        var s = Settings;
        if (s.AssistantBrainProvider is not { } provider) return false;
        if (s.AssistantBrainServer is { Length: > 0 } server && !s.AssistantBrainServers.Contains(server, StringComparer.OrdinalIgnoreCase))
        {
            s.AssistantBrainServers = [server, .. s.AssistantBrainServers];
            // Its models were looked for around it; now only in the folders: the one next to it goes there.
            if (Assistant.Brain.GgufFiles.ModelsFolderNear(server) is { } folder && !s.AssistantBrainFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                s.AssistantBrainFolders = [.. s.AssistantBrainFolders, folder];
        }
        var endpoint = s.AssistantBrainEndpoint ?? string.Empty;
        if (provider == Assistant.Brain.BrainProvider.None)
        {
            s.AssistantBrainModel = Assistant.Brain.ModelRefs.None;
        }
        else if (provider != Assistant.Brain.BrainProvider.LlamaServer
                 && (provider != Assistant.Brain.BrainProvider.Ollama || endpoint.Length > 0 || s.AssistantBrainModel.Length > 0 || s.AssistantModels.Count > 0))
        {
            const string id = "r1";
            var kind = provider switch
            {
                Assistant.Brain.BrainProvider.LlamaSwap => Assistant.Brain.RemoteKind.LlamaSwap,
                Assistant.Brain.BrainProvider.OpenAICompatible => Assistant.Brain.RemoteKind.OpenAI,
                _ => Assistant.Brain.RemoteKind.Ollama,
            };
            s.AssistantBrainRemotes = [new Assistant.Brain.BrainRemote(id, kind, endpoint, s.AssistantBrainApiKey ?? string.Empty), .. s.AssistantBrainRemotes];
            string Prefixed(string model) => model.Length == 0 || Assistant.Brain.ModelRefs.IsLocal(model) ? model : Assistant.Brain.ModelRefs.Remote(id, model);
            s.AssistantBrainModel = Prefixed(s.AssistantBrainModel);
            s.AssistantModels = s.AssistantModels.ToDictionary(p => p.Key, p => Prefixed(p.Value));
        }
        Log.Info($"Cerebro de los asistentes: «{provider}» pasa al cerebro propio ({s.AssistantBrainServers.Count} llama-server, {s.AssistantBrainRemotes.Count} servidores más)");
        s.AssistantBrainProvider = null;
        s.AssistantBrainEndpoint = null;
        s.AssistantBrainApiKey = null;
        s.AssistantBrainServer = null;
        return true;
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
    /// not limited to some programs (but <paramref name="except"/>), «K-Desktop» when it is one of its own hotkeys, and the
    /// assistant whose key to talk it is (but <paramref name="exceptAssistant"/>).
    /// </summary>
    public IReadOnlyList<string> HotkeyUsers(string hotkey, Command? except = null, string? exceptAssistant = null)
    {
        var key = Input.Hotkey.Normalize(hotkey);
        if (key.Length == 0) return [];
        bool Same(string h) => Input.Hotkey.Comparer.Equals(Input.Hotkey.Normalize(h), key);
        var users = Commands
            .Where(c => !ReferenceEquals(c, except) && c.Enabled && c.Processes.Count == 0 && c.Hotkeys.Any(Same))
            .Select(c => c.Name)
            .ToList();
        if (Settings.BuiltInHotkeys.Any(Same)) users.Add("K-Desktop");
        foreach (var (assistant, talk) in Settings.AssistantTalkHotkeys)
            if (assistant != exceptAssistant && Same(talk))
                users.Add(Assistant.AssistantTargeting.IsSystem(assistant) ? "K-Desktop" : Assistant.Packages.AssistantPackages.Find(assistant)?.Manifest.Name ?? assistant);
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
    /// <summary>K-Desktop's own hotkeys (show the window, exit, voice, «Desactivar en», pause, launcher, the assistants' keys to talk); unset ones are empty.</summary>
    [JsonIgnore]
    public IEnumerable<string> OwnHotkeys => [.. BuiltInHotkeys, .. AssistantEnabled ? AssistantTalkHotkeys.Values : Enumerable.Empty<string>()];

    /// <summary>K-Desktop's own hotkeys but the assistants' keys to talk.</summary>
    [JsonIgnore]
    public IEnumerable<string> BuiltInHotkeys => [ShowWindowHotkey, ExitHotkey, VoiceHotkey, DictationHotkey, DisabledInHotkey, PauseHotkey, LauncherHotkey];

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

    /// <summary>The page of the main window shown when it was last closed (its tag: "commands", "settings"…); null: the first.</summary>
    [ObservableProperty] private string? _windowPage;

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

    /// <summary>
    /// A browser on this same PC must be paired too: otherwise any program of the PC could run commands through the
    /// API. «Abrir en el navegador» pairs it by itself.
    /// </summary>
    [ObservableProperty] private bool _webLocalPairing = true;

    // Voice
    /// <summary>Hold to talk (or tap and talk). Empty: no hotkey.</summary>
    [ObservableProperty] private string _voiceHotkey = string.Empty;

    /// <summary>Hold to dictate (or tap, dictate, and it ends after a silence): stage 10.5. Empty: no hotkey.</summary>
    [ObservableProperty] private string _dictationHotkey = string.Empty;

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

    /// <summary>
    /// The last assistant the user talked to (its wake word, its key, its menu…): the one K-Desktop turns to by itself
    /// (<see cref="Assistant.AssistantTargeting.Pick"/>).
    /// </summary>
    [ObservableProperty] private string? _assistantLastTalkedTo;

    /// <summary>
    /// The wake word of each assistant (K-Desktop too): assistant id → id of a recorded wake word. A word belongs to one
    /// owner at most (<see cref="Voice.WakeOwners"/>). Replaced whole on every change (so it is saved).
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _assistantWakeWords = [];

    /// <summary>
    /// The wake words that call K-Desktop's commands (5.0.109): every recorded word has one owner —K-Desktop, an assistant
    /// (<see cref="AssistantWakeWords"/>) or nobody, and then it is not listened to. Null until moved from before (all the
    /// words no assistant had). Replaced whole.
    /// </summary>
    [ObservableProperty] private List<string>? _kDesktopWakeWords;

    /// <summary>Until 5.0.112, the one key to talk to «the assistant»: only read, to give it to the last one talked to.</summary>
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private string? _assistantTalkHotkey;

    /// <summary>
    /// Hold to talk to one assistant (5.0.113, its card in Options → Assistants): assistant id → hotkey. Only while it is
    /// on the desktop; a key is of one assistant at most. Replaced whole on every change.
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _assistantTalkHotkeys = [];

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
    /// Who says the assistants' answers aloud (Options → Assistant → Voice, stage 5.4): <c>auto</c> (Kokoro if it is
    /// downloaded for the language, else Piper, else Windows' voices, else beeps), <c>kokoro</c>, <c>piper</c> (5.0.110),
    /// <c>windows</c>, <c>beeps</c> or <c>off</c> (only the bubble). Until 5.0.93 the natural voice was Pocket TTS (<c>pocket</c>, read as <c>kokoro</c>).
    /// </summary>
    [ObservableProperty] private string _assistantVoiceEngine = "auto";

    /// <summary>
    /// The voice of Kokoro for the interface language, or a mix of two («ef_dora:0.7,em_alex:0.3»); empty or not of the
    /// language: its first one. Downloaded when chosen.
    /// </summary>
    [ObservableProperty] private string _assistantVoiceKokoro = string.Empty;

    /// <summary>
    /// The Piper voice for the language the assistants speak in («es_ES-davefx-medium», or «key#speaker» for one with several);
    /// empty or not of the language: its default one (5.0.110).
    /// </summary>
    [ObservableProperty] private string _assistantVoicePiper = string.Empty;

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

    /// <summary>Until 5.0.106, where the assistants' AI ran: only read, to move it to the brain of 5.0.107 (see <see cref="AppConfig.Upgrade"/>).</summary>
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private Assistant.Brain.BrainProvider? _assistantBrainProvider;

    /// <summary>Until 5.0.106, the address of that provider: only read, to move it.</summary>
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private string? _assistantBrainEndpoint;

    /// <summary>Until 5.0.106, its API key (DPAPI): only read, to move it.</summary>
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private string? _assistantBrainApiKey;

    /// <summary>Until 5.0.106, the one llama-server.exe: only read, to move it to <see cref="AssistantBrainServers"/>.</summary>
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private string? _assistantBrainServer;

    /// <summary>
    /// The general model (a <see cref="Assistant.Brain.ModelRefs">reference</see>: a .gguf's path or «id/name» of another
    /// server); empty: chosen automatically; «none»: no AI.
    /// </summary>
    [ObservableProperty] private string _assistantBrainModel = string.Empty;

    /// <summary>The llama-server.exe the assistant process starts (the first one is the usual; a model near another one opens with that one). Replaced whole.</summary>
    [ObservableProperty] private List<string> _assistantBrainServers = [];

    /// <summary>Folders where the .gguf models are looked for, besides the downloads one. Replaced whole.</summary>
    [ObservableProperty] private List<string> _assistantBrainFolders = [];

    /// <summary>Where the models downloaded from Hugging Face go; empty: <c>Models\llm</c> in the data folder.</summary>
    [ObservableProperty] private string _assistantBrainDownloads = string.Empty;

    /// <summary>Each model's own options for llama-server, added to the usual ones (its path → «-c 32768»…). Replaced whole.</summary>
    [ObservableProperty] private Dictionary<string, string> _assistantBrainModelArgs = [];

    /// <summary>Minutes a model stays in the graphics card without use; 0: it stays.</summary>
    [ObservableProperty] private int _assistantBrainKeepMinutes = Assistant.Brain.BrainConfig.DefaultKeepMinutes;

    /// <summary>Loading one model unloads the others (without it they stay while they fit in the graphics card).</summary>
    [ObservableProperty] private bool _assistantBrainOneAtATime = true;

    /// <summary>A vision projector (mmproj) next to a model is loaded with it.</summary>
    [ObservableProperty] private bool _assistantBrainVision = true;

    /// <summary>
    /// The model that thinks harder when an order needs it (stage 12): empty, the same one with its reasoning on;
    /// <c>none</c>, never; otherwise a model as <see cref="Assistant.Brain.ModelRefs"/> names it.
    /// </summary>
    [ObservableProperty] private string _assistantBrainThinkModel = string.Empty;

    /// <summary>How much it may think then, in tokens of reasoning.</summary>
    [ObservableProperty] private int _assistantBrainThinkTokens = Assistant.Brain.BrainConfig.DefaultThinkTokens;

    /// <summary>Folders of the user's own with Piper voices (stage 12): they count as downloaded. Replaced whole.</summary>
    [ObservableProperty] private List<string> _assistantPiperFolders = [];

    /// <summary>A game on the whole screen takes the brain's models out of the graphics card (stage 12).</summary>
    [ObservableProperty] private bool _assistantBrainFreeForGames = true;

    /// <summary>Other servers with models (Ollama, llama-swap, OpenAI compatible), their keys protected with DPAPI. Replaced whole.</summary>
    [ObservableProperty] private List<Assistant.Brain.BrainRemote> _assistantBrainRemotes = [];

    /// <summary>The assistants learn what is worth remembering about the user from what they talk about (stage 3.7).</summary>
    [ObservableProperty] private bool _assistantMemoryLearn = true;

    /// <summary>
    /// The model of each assistant that has its own: assistant id → model (absent: the general one, also when the server
    /// does not have it). Replaced whole on every change (so it is saved).
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _assistantModels = [];

    /// <summary>
    /// What each assistant has of its own (personality, language, voice; 5.0.105): assistant id → it (absent or empty: all
    /// the general). Replaced whole on every change (so it is saved).
    /// </summary>
    [ObservableProperty] private Dictionary<string, Assistant.AssistantPersonal> _assistantPersonal = [];

    /// <summary>
    /// Before doing something, an assistant asks: <c>never</c>, <c>risky</c> (only power, scripts…; the default) or
    /// <c>first</c> (also the first time, until «yes, and don't ask again»). <see cref="Assistant.AskPolicy"/>.
    /// </summary>
    [ObservableProperty] private string _assistantAskBefore = Assistant.AskPolicy.Risky;

    /// <summary>
    /// The language the assistants speak in (its answers, its voice, its texts), whatever the user speaks to them in; empty:
    /// K-Desktop's. The assistant process runs in it.
    /// </summary>
    [ObservableProperty] private string _assistantLanguage = string.Empty;

    /// <summary>Where the user is («Málaga»), for the weather when no place is said; empty: Windows' location, if allowed.</summary>
    [ObservableProperty] private string _assistantHomePlace = string.Empty;

    /// <summary>The tools that no longer ask before doing something («yes, and don't ask again»). Replaced whole on every change.</summary>
    [ObservableProperty] private List<string> _assistantTrustedTools = [];

    /// <summary>
    /// Each tool's own choices in Options → Tools (stage 9.3): switched off, or asking otherwise than it says. Replaced
    /// whole on every change.
    /// </summary>
    [ObservableProperty] private Dictionary<string, global::KDesktop.Core.Assistant.ToolPreference> _assistantToolSettings = [];

    /// <summary>Where Options → Tools → «Importar…» looked last (null: Documents).</summary>
    [ObservableProperty] private string? _assistantMcpImportFolder;

    /// <summary>
    /// The assistants' tools for other programs, as an MCP server (phase 12, <c>KDesktop.exe --mcp</c>). Off by default:
    /// the user turns it on and gives the program the key.
    /// </summary>
    [ObservableProperty] private bool _assistantShareTools;

    /// <summary>What they may use: <c>read</c> (only what reads) or <c>all</c> (the rest asks on the PC first).</summary>
    [ObservableProperty] private string _assistantShareScope = Assistant.Sharing.McpShare.ScopeRead;

    /// <summary>The key the programs give (DPAPI, see <see cref="Platform.Secrets"/>); made the first time sharing is turned on.</summary>
    [ObservableProperty] private string? _assistantShareKey;

    /// <summary>
    /// The catalogs of assistants Options → Catalog reads (phase 12), K-Desktop's own first. Replaced whole on every change.
    /// </summary>
    [ObservableProperty] private List<string> _assistantCatalogs = [Assistant.Packages.AssistantCatalog.Official];

    /// <summary>
    /// New versions of assistants the user chose not to be told about again (id → that version; stage 9.5). A later
    /// version is offered again. Replaced whole on every change.
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _assistantSkippedUpdates = [];

    /// <summary>The groups of Options left open (and «assistants» when its assistants are shown); null: never folded yet.</summary>
    [ObservableProperty] private List<string>? _settingsNavOpen;

    /// <summary>The section of Options shown last ("appearance", "assistants/lander"…); null: the first.</summary>
    [ObservableProperty] private string? _settingsSection;

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

    /// <summary>
    /// K-Desktop (<see cref="Assistant.AssistantTargeting.KDesktop"/>, 5.0.112) was made active the first time the module was
    /// on; from then on it is active or not as the user leaves it.
    /// </summary>
    [ObservableProperty] private bool _assistantKDesktopAdded;

    /// <summary>
    /// The assistants on the desktop (active), in the order they were put there; K-Desktop among them is the one without a
    /// figure. Empty: there is no assistant.
    /// </summary>
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
