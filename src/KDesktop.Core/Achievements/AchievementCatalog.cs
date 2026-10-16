using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Achievements;

/// <summary>
/// Definition of an achievement. Texts come from the language files:
/// <c>ach.{Id}.name</c>, and for counters <c>goal.{Stat}</c> / <c>goal.{Stat}.1</c> (otherwise <c>ach.{Id}.desc</c>).
/// </summary>
/// <param name="Stat">Counter that unlocks it when it reaches <paramref name="Target"/> (null = unlocked by an event).</param>
/// <param name="Secret">Name and description are hidden until it is unlocked.</param>
/// <param name="Kind">"Use this action type" achievements.</param>
public sealed record AchievementDef(string Id, string Category, string? Stat = null, long Target = 1, bool Secret = false, ActionKind? Kind = null)
{
    public string Name => Kind is { } k ? Loc.T("ach.kind.name", ActionCatalog.Get(k).Name) : Loc.T($"ach.{Id}.name");

    public string Description
    {
        get
        {
            if (Kind is { } k) return Loc.T("ach.kind.desc", ActionCatalog.Get(k).Name);
            if (Stat is null || Stat == global::KDesktop.Core.Achievements.Stat.Kinds) return Loc.T($"ach.{Id}.desc");
            return Target == 1 ? Loc.T($"goal.{Stat}.1") : Loc.T($"goal.{Stat}", Target.ToString("N0", Loc.Instance.Culture));
        }
    }

    /// <summary>Fluent symbol of its category.</summary>
    public string Symbol => AchievementCatalog.CategorySymbol(Category);

    /// <summary>Has a progress bar (counters with a target above 1).</summary>
    public bool HasProgress => Stat is not null && Target > 1;
}

public static class AchievementCatalog
{
    public const string Master = "master";
    public const string First = "first";

    public static IReadOnlyList<string> Categories { get; } =
        ["commands", "actions", "menus", "macros", "kboard", "apps", "system", "remote", "voice", "time", "kinds", "misc"];

    public static string CategorySymbol(string category) => category switch
    {
        "commands" => "Flash24",
        "actions" => "Rocket24",
        "menus" => "Navigation24",
        "macros" => "Record24",
        "kboard" => "KeyboardLayoutFloat24",
        "apps" => "WindowApps24",
        "system" => "Wrench24",
        "remote" => "PlugConnected24",
        "voice" => "Mic24",
        "time" => "CalendarStar24",
        "kinds" => "Sparkle24",
        _ => "Star24",
    };

    public static IReadOnlyList<AchievementDef> All { get; } = Build();

    private static readonly Dictionary<string, AchievementDef> ById = All.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public static AchievementDef? Get(string id) => ById.GetValueOrDefault(id);

    private static List<AchievementDef> Build()
    {
        var list = new List<AchievementDef>();
        void Tier(string category, string idPrefix, string stat, params long[] targets)
        {
            foreach (var target in targets) list.Add(new AchievementDef($"{idPrefix}-{target}", category, stat, target));
        }

        // Commands
        Tier("commands", "cmd-create", Stat.CommandsCreated, 1, 10, 100);
        Tier("commands", "cmd-delete", Stat.CommandsDeleted, 1, 10);
        Tier("commands", "cmd-run", Stat.CommandsRun, 1, 10, 100, 1000, 10000);
        Tier("commands", "cmd-owned", Stat.CommandsOwned, 50);
        list.Add(new("multi", "commands"));
        list.Add(new("chord", "commands"));

        // Actions
        Tier("actions", "act-create", Stat.ActionsCreated, 1, 10, 100);
        Tier("actions", "act-delete", Stat.ActionsDeleted, 1, 10);
        Tier("actions", "act-run", Stat.ActionsRun, 1, 10, 100, 1000, 10000);
        list.Add(new("reorder", "actions"));

        // QuickMenus
        Tier("menus", "menu-create", Stat.MenusCreated, 1, 10);
        Tier("menus", "menu-delete", Stat.MenusDeleted, 1, 10);
        Tier("menus", "menu-open", Stat.MenusOpened, 1, 10, 100, 1000);
        list.Add(new("menu-nested", "menus", Stat.MenuDepth, 3));

        // Macros
        Tier("macros", "macro-record", Stat.MacrosRecorded, 1, 10);
        Tier("macros", "macro-play", Stat.MacrosPlayed, 1, 10, 100);
        list.Add(new("macro-long", "macros", Stat.MacroLongest, 100));
        list.Add(new("macro-loop", "macros", Secret: true));

        // K-Board
        Tier("kboard", "kboard-create", Stat.KBoardsCreated, 1, 10);
        Tier("kboard", "kboard-remap", Stat.KBoardRemapped, 100, 1000, 10000, 100000);
        Tier("kboard", "kboard-toggle", Stat.KBoardToggled, 1, 100);

        // Applications, folders, web, scripts
        Tier("apps", "app", Stat.AppsLaunched, 1, 10, 100, 1000);
        Tier("apps", "folder", Stat.FoldersOpened, 1, 10, 100);
        Tier("apps", "web", Stat.WebOpened, 1, 10, 100);
        Tier("apps", "script", Stat.ScriptsRun, 1, 10);
        Tier("apps", "cmd-script", Stat.ScriptsCmd, 1, 10);
        Tier("apps", "ps-script", Stat.ScriptsPowerShell, 1);

        // System
        Tier("system", "shutdown", Stat.Shutdowns, 1, 10, 100);
        Tier("system", "recycle", Stat.RecycleEmptied, 1, 10, 100, 1000);
        Tier("system", "hidden", Stat.HiddenFilesToggled, 1, 10, 100);
        Tier("system", "disabledin", Stat.DisabledInToggled, 1, 10, 100);
        Tier("system", "topmost", Stat.TopMostToggled, 1, 10, 100);
        Tier("system", "volume", Stat.VolumeChanged, 1, 100);
        Tier("system", "audio", Stat.AudioSwitched, 1, 10, 100);
        Tier("system", "icon", Stat.IconsChanged, 1, 10);
        list.Add(new("custom-sound", "system"));

        // Remote
        Tier("remote", "remote", Stat.RemoteRun, 1, 10, 100);

        // Voice
        Tier("voice", "voice", Stat.VoiceOrders, 1, 10, 100, 1000);
        Tier("voice", "wake", Stat.WakeWords, 1, 100, 1000);
        Tier("voice", "meaning", Stat.VoiceMeaning, 1, 10, 100);

        // Time of use
        Tier("time", "days", Stat.Days, 7, 30, 365);
        list.Add(new("night-owl", "time", Secret: true));

        // One per action type
        foreach (var info in ActionCatalog.All)
            list.Add(new($"kind-{info.Kind}", "kinds", Kind: info.Kind));
        list.Add(new("completist", "kinds", Stat.Kinds, ActionCatalog.All.Count));

        // Miscellaneous
        list.Add(new("welcome", "misc", Stat.AppStarted, 1));
        list.Add(new(First, "misc"));
        list.Add(new("about", "misc"));
        Tier("misc", "tips", Stat.TipsSeen, 10, 50);
        list.Add(new("curious", "misc"));
        list.Add(new("export", "misc"));
        list.Add(new("import", "misc"));
        list.Add(new("quick-opener", "misc", Secret: true));
        list.Add(new("polyglot", "misc"));
        list.Add(new("theme", "misc"));
        list.Add(new("cancel", "misc", Secret: true));
        list.Add(new(Master, "misc"));

        return list;
    }
}
