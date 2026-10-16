using KDesktop.Core.Engine;
using KDesktop.Core.Model;

namespace KDesktop.Core.Achievements;

/// <summary>Names of the statistics counters (they are the keys stored in stats.json).</summary>
public static class Stat
{
    public const string AppStarted = "app.started";

    public const string CommandsCreated = "commands.created";
    public const string CommandsDeleted = "commands.deleted";
    public const string CommandsRun = "commands.run";
    public const string CommandsOwned = "commands.owned";

    public const string ActionsCreated = "actions.created";
    public const string ActionsDeleted = "actions.deleted";
    public const string ActionsRun = "actions.run";
    public const string ActionsReordered = "actions.reordered";

    public const string MenusCreated = "menus.created";
    public const string MenusDeleted = "menus.deleted";
    public const string MenusOpened = "menus.opened";
    public const string LauncherUsed = "launcher.used";
    public const string MenuDepth = "menus.depth";

    public const string RemoteRun = "remote.run";

    /// <summary>Wake words heard (both phases passed).</summary>
    public const string WakeWords = "voice.wakewords";

    /// <summary>Orders run by voice (commands and built-in orders such as «pausa» or «cancela»).</summary>
    public const string VoiceOrders = "voice.orders";

    /// <summary>Orders understood by their meaning, without saying the command's name.</summary>
    public const string VoiceMeaning = "voice.meaning";

    public const string MacrosRecorded = "macros.recorded";
    public const string MacrosPlayed = "macros.played";
    public const string MacroLongest = "macros.longest";
    public const string MacroSteps = "macros.steps";

    public const string AppsLaunched = "apps.launched";
    public const string WebOpened = "web.opened";
    public const string FoldersOpened = "folders.opened";
    public const string ScriptsRun = "scripts.run";
    public const string ScriptsCmd = "scripts.cmd";
    public const string ScriptsPowerShell = "scripts.powershell";
    public const string KeysSent = "input.sent";

    public const string Shutdowns = "power.shutdown";
    public const string PowerUsed = "power.used";
    public const string RecycleEmptied = "recycle.emptied";
    public const string HiddenFilesToggled = "hiddenFiles.toggled";
    public const string DisabledInToggled = "disabledIn.toggled";
    public const string TopMostToggled = "topMost.toggled";
    public const string SoundsPlayed = "sounds.played";
    public const string VolumeChanged = "volume.changed";
    public const string IconsChanged = "icons.changed";
    public const string Cancelled = "countdowns.cancelled";
    public const string MouseChords = "hotkeys.chords";
    public const string AudioSwitched = "audio.switched";
    public const string TipsSeen = "tips.seen";

    public const string EventsCreated = "events.created";
    public const string EventsFired = "events.fired";

    /// <summary>«Al terminar» commands run.</summary>
    public const string EventsEnded = "events.ended";

    public const string KBoardsCreated = "kboard.created";
    public const string KBoardRemapped = "kboard.remapped";
    public const string KBoardToggled = "kboard.toggled";

    // Not real counters: computed by the service.
    public const string Days = "@days";
    public const string Kinds = "@kinds";

    /// <summary>Counters shown on the statistics page, grouped (group key → counters).</summary>
    public static IReadOnlyList<(string Group, string[] Keys)> Display { get; } =
    [
        ("commands", [CommandsRun, CommandsCreated, CommandsDeleted, ActionsRun, ActionsCreated, ActionsDeleted]),
        ("menus", [MenusOpened, MenusCreated, MenusDeleted, LauncherUsed, RemoteRun]),
        ("apps", [AppsLaunched, FoldersOpened, WebOpened, ScriptsRun, KeysSent, MacrosPlayed, MacrosRecorded]),
        ("events", [EventsFired, EventsEnded, EventsCreated]),
        ("kboard", [KBoardRemapped, KBoardToggled, KBoardsCreated]),
        ("voice", [VoiceOrders, VoiceMeaning, WakeWords]),
        ("system", [VolumeChanged, AudioSwitched, SoundsPlayed, TopMostToggled, HiddenFilesToggled, DisabledInToggled, RecycleEmptied, PowerUsed, IconsChanged, TipsSeen, AppStarted]),
    ];
}

/// <summary>Persisted statistics and achievement progress (stats.json).</summary>
public sealed class StatsData
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>First time K-Desktop 5 was used.</summary>
    public DateTime FirstUse { get; set; } = DateTime.Now;

    public Dictionary<string, long> Counters { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Executions per command id.</summary>
    public Dictionary<string, long> CommandRuns { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Executions per origin (hotkey, quickMenu, remote…).</summary>
    public Dictionary<string, long> RunsBySource { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Action types used at least once.</summary>
    public HashSet<string> ActionKinds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Unlocked achievements and when.</summary>
    public Dictionary<string, DateTime> Unlocked { get; set; } = new(StringComparer.Ordinal);


    public long Get(string key) => Counters.GetValueOrDefault(key);

    public StatsData Clone() => Json.Clone(this);
}

/// <summary>A read-only copy of the statistics for the UI.</summary>
public sealed record StatsSnapshot(StatsData Data, int UnlockedCount, int TotalCount);

public static class TriggerSourceNames
{
    public static string Key(TriggerSource source) => source switch
    {
        TriggerSource.Hotkey => "hotkey",
        TriggerSource.QuickMenu => "quickMenu",
        TriggerSource.Remote => "remote",
        TriggerSource.Startup or TriggerSource.Exit => "auto",
        TriggerSource.Command => "command",
        TriggerSource.Launcher => "launcher",
        TriggerSource.Web => "web",
        TriggerSource.Voice => "voice",
        TriggerSource.Event => "event",
        _ => "manual",
    };

    public static readonly string[] All = ["hotkey", "quickMenu", "launcher", "voice", "remote", "web", "event", "manual", "command", "auto"];
}
