using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>Display metadata for an action type (texts come from the language files).</summary>
/// <param name="Symbol">Name of a Fluent <c>SymbolRegular</c> icon (resolved by the UI).</param>
public sealed record ActionInfo(ActionKind Kind, string CategoryKey, string Symbol, Func<CommandAction> Create)
{
    public string Name => Loc.T($"action.{Kind}.name");
    public string Description => Loc.T($"action.{Kind}.desc");
    public string Category => Loc.T($"category.{CategoryKey}");

    /// <summary>It belongs to the assistant module: only offered while the module is on, and outside the achievements.</summary>
    public bool NeedsAssistant => CategoryKey == "assistant";
}

public static class ActionCatalog
{
    public static IReadOnlyList<ActionInfo> All { get; } =
    [
        new(ActionKind.RunApp, "apps", "Open24", () => new RunAppAction()),
        new(ActionKind.OpenFolder, "apps", "FolderOpen24", () => new OpenFolderAction()),
        new(ActionKind.OpenUrl, "apps", "Globe24", () => new OpenUrlAction()),
        new(ActionKind.RunScript, "apps", "WindowConsole20", () => new RunScriptAction()),

        new(ActionKind.AppControl, "windows", "WindowApps24", () => new AppControlAction()),
        new(ActionKind.Window, "windows", "WindowNew24", () => new WindowAction()),
        new(ActionKind.TopMost, "windows", "Pin24", () => new TopMostAction()),

        new(ActionKind.SendInput, "input", "Keyboard24", () => new SendInputAction()),
        new(ActionKind.Clipboard, "input", "ClipboardTextEdit24", () => new ClipboardAction()),
        new(ActionKind.PlayMacro, "input", "Record24", () => new PlayMacroAction()),
        new(ActionKind.KBoard, "input", "KeyboardLayoutFloat24", () => new KBoardAction()),

        new(ActionKind.Volume, "sound", "Speaker224", () => new VolumeAction()),
        new(ActionKind.AudioOutput, "sound", "HeadphonesSoundWave24", () => new AudioOutputAction()),
        new(ActionKind.PlaySound, "sound", "MusicNote224", () => new PlaySoundAction()),
        new(ActionKind.AudioRecord, "sound", "MicPulse24", () => new AudioRecordAction()),

        new(ActionKind.Screenshot, "screen", "Screenshot24", () => new ScreenshotAction()),
        new(ActionKind.Magnifier, "screen", "ZoomIn24", () => new MagnifierAction()),
        new(ActionKind.Display, "screen", "Desktop24", () => new DisplayAction()),
        new(ActionKind.WindowsTheme, "screen", "DarkTheme24", () => new WindowsThemeAction()),

        new(ActionKind.HttpRequest, "network", "ArrowSwap24", () => new HttpRequestAction()),
        new(ActionKind.WakeOnLan, "network", "DesktopPulse24", () => new WakeOnLanAction()),
        new(ActionKind.Radio, "network", "Wifi124", () => new RadioAction()),

        new(ActionKind.HiddenFiles, "system", "EyeOff24", () => new HiddenFilesAction()),
        new(ActionKind.PowerPlan, "system", "Battery1024", () => new PowerPlanAction()),
        new(ActionKind.Power, "system", "Power24", () => new PowerAction()),

        new(ActionKind.ShowMessage, "kdesktop", "Chat24", () => new ShowMessageAction()),
        new(ActionKind.Condition, "kdesktop", "BranchFork24", () => new ConditionAction()),
        new(ActionKind.Wait, "kdesktop", "Timer24", () => new WaitAction()),
        new(ActionKind.CommandControl, "kdesktop", "Flash24", () => new CommandControlAction()),
        new(ActionKind.OpenQuickMenu, "kdesktop", "TextBulletListTree24", () => new OpenQuickMenuAction()),

        new(ActionKind.AssistantExtra, "assistant", "Bot24", () => new AssistantExtraAction()),
        new(ActionKind.AssistantAsk, "assistant", "ChatBubblesQuestion24", () => new AssistantAskAction()),
    ];

    /// <summary>The actions of K-Desktop itself (without the assistant module's): the ones the achievements count.</summary>
    public static IReadOnlyList<ActionInfo> Everyday { get; } = [.. All.Where(a => !a.NeedsAssistant)];

    private static readonly Dictionary<ActionKind, ActionInfo> ByKind = All.ToDictionary(a => a.Kind);

    public static ActionInfo Get(ActionKind kind) => ByKind[kind];
}

/// <summary>Special shell locations available in "Open folder".</summary>
public static class SpecialFolders
{
    /// <param name="Icon">K-Desktop icon ("kd:Symbol:colour") or bundled asset name.</param>
    public sealed record Entry(string Key, string? Icon)
    {
        public string Name => Loc.T($"folder.{Key}");
    }

    public static IReadOnlyList<Entry> All { get; } =
    [
        new("ThisPC", "kd:Laptop24:sky"),
        new("Desktop", "kd:Desktop24:blue"),
        new("Documents", "kd:DocumentText24:blue"),
        new("Downloads", "kd:ArrowDownload24:teal"),
        new("Pictures", "kd:Image24:green"),
        new("Music", "kd:MusicNote224:pink"),
        new("Videos", "kd:VideoClip24:purple"),
        new("UserProfile", "kd:Person24:blue"),
        new("RecycleBin", "kd:Delete24:gray"),
        new("EmptyRecycleBin", "kd:Broom24:orange"),
        new("ControlPanel", "kd:Options24:gray"),
        new("Settings", "kd:Settings24:gray"),
        new("VolumeMixer", "kd:Speaker224:teal"),
        new("SoundSettings", "kd:SoundSource24:teal"),
        new("NetworkConnections", "kd:NetworkCheck24:sky"),
        new("Fonts", "kd:TextFont24:orange"),
        new("StartMenu", "kd:Apps24:indigo"),
        new("CommonStartMenu", "kd:AppsList24:indigo"),
        new("WindowsApps", "kd:WindowApps24:blue"),
        new("GodMode", "kd:Sparkle24:gold"),
        new("TaskScheduler", "kd:CalendarLtr24:orange"),
        new("GroupPolicy", "kd:ShieldLock24:green"),
        new("Registry", "kd:Database24:dark"),
        new("OnScreenKeyboard", "kd:Keyboard24:sky"),
        new("PowerShell", "kd:WindowConsole20:blue"),
        new("TaskView", "kd:TabDesktop24:indigo"),
        new("KDesktopData", "KDesktop"),
    ];

    private static readonly Dictionary<string, Entry> ByKey = All.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);

    public static Entry? Get(string? key) => key is not null && ByKey.TryGetValue(key, out var e) ? e : null;

    public static string DisplayName(string key) => Get(key)?.Name ?? key;

    /// <summary>Maps the Spanish names stored by K-Desktop 4 to the new keys.</summary>
    public static string? FromLegacy(string legacy) => legacy.Trim().ToLowerInvariant() switch
    {
        "equipo" => "ThisPC",
        "panel de control" => "ControlPanel",
        "control de volumen" => "VolumeMixer",
        "ajustes de audio" => "SoundSettings",
        "papelera de reciclaje" => "RecycleBin",
        "papelera (vaciar)" => "EmptyRecycleBin",
        "modo 'god'" => "GodMode",
        "documentos" => "Documents",
        "imagenes" => "Pictures",
        "musicas" => "Music",
        "escritorio" => "Desktop",
        "videos" => "Videos",
        "descargas" => "Downloads",
        "k-desktop" or "k-temp" => "KDesktopData",
        "usuario" => "UserProfile",
        "fuentes" => "Fonts",
        "menu inicio (usuario)" => "StartMenu",
        "menu inicio" => "CommonStartMenu",
        "directivas de grupo" => "GroupPolicy",
        "programador de tareas" => "TaskScheduler",
        "registro de windows" => "Registry",
        "teclado en pantalla" => "OnScreenKeyboard",
        "conexiones de red" => "NetworkConnections",
        "task switcher" => "TaskView",
        "powershell" => "PowerShell",
        "windows apps" => "WindowsApps",
        _ => null,
    };
}

/// <summary>Known browsers for "Open web page".</summary>
public static class Browsers
{
    public sealed record Entry(string Id, string Name, string Executable, string? Icon, string NewWindowArg, string IncognitoArg);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new("chrome", "Google Chrome", "chrome.exe", "Navegador_Chrome", "--new-window", "--incognito"),
        new("firefox", "Mozilla Firefox", "firefox.exe", "Navegador_Firefox", "-new-window", "-private-window"),
        new("edge", "Microsoft Edge", "msedge.exe", "Navegador_Edge", "--new-window", "--inprivate"),
        new("brave", "Brave", "brave.exe", "Navegador_Brave", "--new-window", "--incognito"),
        new("opera", "Opera", "opera.exe", "Navegador_Opera", "--new-window", "--private"),
        new("vivaldi", "Vivaldi", "vivaldi.exe", "Navegador_Vivaldi", "--new-window", "--incognito"),
    ];

    public static Entry? Get(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string? FromLegacy(string name) => name.Trim().ToLowerInvariant() switch
    {
        "google chrome" => "chrome",
        "mozilla firefox" => "firefox",
        "microsoft edge" => "edge",
        "brave" => "brave",
        "opera" => "opera",
        "vivaldi" => "vivaldi",
        _ => null,
    };
}
