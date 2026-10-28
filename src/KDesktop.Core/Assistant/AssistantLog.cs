using System.Globalization;
using KDesktop.Core.Achievements;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Engine.Events;
using KDesktop.Core.Model;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The «Registro», the statistics and the checks of a command or an event for the assistants (B3, 5.0.345): the tools
/// <c>query_log</c>, <c>usage_stats</c> and <c>diagnose_command</c>. Nothing runs or changes. The texts for the model are
/// in English; names and condition descriptions as the user sees them.
/// </summary>
public static class AssistantLog
{
    public const int MaxRuns = 50;

    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>How close a name must be to a command's or event's (FuzzyMatch) to take it.</summary>
    private const int MinScore = 300;

    // ── query_log ────────────────────────────────────────────────────────

    public static LogFoundMessage Query(IReadOnlyList<HistoryEntry> history, IReadOnlyList<EventSkip> skips, LogQueryMessage request)
    {
        var since = ParseTime(request.Since, endOfDay: false);
        var until = ParseTime(request.Until, endOfDay: true);
        var words = Words(request.Name);
        var source = request.Source?.Trim();
        bool InTime(DateTime t) => (since is null || t >= since) && (until is null || t <= until);

        var runs = history
            .Where(e => InTime(e.Time))
            .Where(e => words.Length == 0 || Has(e.Name + " " + e.Detail, words))
            .Where(e => string.IsNullOrEmpty(source) || SourceIs(e.Source, source))
            .Where(e => !request.Problems || e.Outcome != RunOutcome.Ok)
            .Reverse()
            .ToList();
        var found = skips
            .Where(s => InTime(s.Time.ToLocalTime()))
            .Where(s => words.Length == 0 || Has(s.Name, words))
            .Where(_ => string.IsNullOrEmpty(source) || source.Equals("event", StringComparison.OrdinalIgnoreCase))
            .Reverse()
            .Take(MaxRuns)
            .Select(Logged)
            .ToList();
        var max = Math.Clamp(request.Max, 1, MaxRuns);
        return new LogFoundMessage(request.Id, [.. runs.Take(max).Select(Logged)], found, runs.Count,
            history.Count > 0 ? history[0].Time.ToString(TimeFormat, CultureInfo.InvariantCulture) : null);
    }

    public static LoggedRun Logged(HistoryEntry e) => new(e.Time.ToString(TimeFormat, CultureInfo.InvariantCulture), e.Name,
        TriggerSourceNames.Key(e.Source), Camel(e.Outcome.ToString()), e.Detail, e.Message, e.Milliseconds, e.Depth);

    public static LoggedSkip Logged(EventSkip s) => new(s.Time.ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture), s.Name,
        Camel(s.Reason.ToString()), s.Detail, s.Times);

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static bool SourceIs(TriggerSource source, string wanted) =>
        TriggerSourceNames.Key(source).Equals(wanted, StringComparison.OrdinalIgnoreCase) || source.ToString().Equals(wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>«2026-10-28» (from its start, or with <paramref name="endOfDay"/> to its end) or «2026-10-28 08:00»; null: none.</summary>
    public static DateTime? ParseTime(string? text, bool endOfDay)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            return at;
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return endOfDay ? day.AddDays(1).AddTicks(-1) : day;
        return null;
    }

    private static string[] Words(string? text) => FuzzyMatch.Tokens(text ?? string.Empty).Where(w => w.Length > 1).ToArray();

    private static bool Has(string text, string[] words)
    {
        var have = FuzzyMatch.Tokens(new string(text.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()));
        return words.All(w => have.Any(h => h.StartsWith(w, StringComparison.Ordinal)));
    }

    // ── usage_stats ──────────────────────────────────────────────────────

    /// <summary>Counters worth telling (the rest are for the achievements).</summary>
    private static readonly string[] Told =
    [
        Stat.VoiceOrders, Stat.WakeWords, Stat.EventsFired, Stat.MenusOpened, Stat.LauncherUsed, Stat.MacrosPlayed,
        Stat.AppsLaunched, Stat.WebOpened, Stat.ScriptsRun, Stat.KBoardRemapped, Stat.AppStarted,
    ];

    public static UsageDoneMessage Usage(StatsSnapshot stats, IReadOnlyList<HistoryEntry> history, AppConfig config, UsageMessage request, DateTime now)
    {
        var data = stats.Data;
        var days = Math.Clamp(request.Days, 1, 365);
        var top = data.CommandRuns
            .Select(p => (Command: config.FindCommand(p.Key), Count: p.Value))
            .Where(p => p.Command is not null)
            .OrderByDescending(p => p.Count)
            .Take(10)
            .Select(p => new UsageCount(p.Command!.Name, p.Count))
            .ToList();
        var from = now.AddDays(-days);
        var recent = history
            .Where(e => e.Time >= from && e.Depth == 0 && e.Outcome != RunOutcome.NotUnderstood)
            .GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new UsageCount(g.Key, g.Count(), g.Count(e => e.Outcome != RunOutcome.Ok)))
            .OrderByDescending(c => c.Count)
            .Take(10)
            .ToList();
        var sources = data.RunsBySource.OrderByDescending(p => p.Value).Select(p => new UsageCount(p.Key, p.Value)).ToList();
        var counters = Told.Where(k => data.Get(k) > 0).ToDictionary(k => k, data.Get, StringComparer.Ordinal);
        return new UsageDoneMessage(request.Id, data.FirstUse.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), data.Get(Stat.CommandsRun),
            days, top, recent, sources, counters);
    }

    // ── diagnose_command ─────────────────────────────────────────────────

    /// <param name="paused">K-Desktop is paused (nothing runs).</param>
    /// <param name="conditionsNow">Whether an event's «Solo si» are met now (and the one that is not); null: not checked.</param>
    public static async Task<DiagnosedMessage> DiagnoseAsync(AppConfig config, IReadOnlyList<HistoryEntry> history, IReadOnlyList<EventSkip> skips,
        DiagnoseMessage request, bool paused, Func<EventRule, Task<(bool Met, string Failed)>>? conditionsNow = null)
    {
        var words = FuzzyMatch.Tokens(request.What ?? string.Empty);
        var kind = request.Kind?.Trim().ToLowerInvariant();
        var command = kind == "event" ? default : Best(config.Commands, c => c.Name, words);
        var rule = kind == "command" ? default : Best(config.Events, e => e.Name, words);
        if (command.Item is null && rule.Item is null) return new DiagnosedMessage(request.Id, false, string.Empty, string.Empty, [], [], [], []);

        var facts = new List<string>();
        var problems = new List<string>();
        if (paused) problems.Add("K-Desktop is paused: nothing runs until it is resumed.");
        if (rule.Item is { } e && (command.Item is null || rule.Score > command.Score))
        {
            await DescribeEvent(config, e, facts, problems, conditionsNow).ConfigureAwait(false);
            var runs = history.Where(h => h.Source is TriggerSource.Event or TriggerSource.Startup or TriggerSource.Exit
                    && h.Detail is { } d && d.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
                .Reverse().Take(10).Select(Logged).ToList();
            var skipped = skips.Where(s => s.Id == e.Id).Reverse().Take(10).Select(Logged).ToList();
            return new DiagnosedMessage(request.Id, true, "event", e.Name, facts, problems, runs, skipped);
        }

        var c = command.Item!;
        DescribeCommand(config, c, facts, problems);
        var last = history.Where(h => h.CommandId == c.Id).Reverse().Take(10).Select(Logged).ToList();
        return new DiagnosedMessage(request.Id, true, "command", c.Name, facts, problems, last, []);
    }

    private static (T? Item, int Score) Best<T>(IEnumerable<T> items, Func<T, string> name, string[] words) where T : class =>
        items.Where(i => name(i).Trim().Length > 0)
            .Select(i => (Item: (T?)i, Score: FuzzyMatch.Score(words, new SearchKey(name(i)))))
            .Where(p => p.Score >= MinScore)
            .OrderByDescending(p => p.Score)
            .FirstOrDefault();

    private static void DescribeCommand(AppConfig config, Command c, List<string> facts, List<string> problems)
    {
        if (!c.Enabled) problems.Add("The command is turned off (disabled): it never runs until it is turned on again.");
        var actions = c.Actions.Where(a => a.Enabled).ToList();
        facts.Add(actions.Count == 0 ? "It has no enabled actions." : "It does, in order: " + string.Join("; ", actions.Select(a => $"{ActionCatalog.Get(a.Kind).Name}: {a.Summary}")) + ".");
        if (actions.Count == 0) problems.Add("It has no enabled actions: running it does nothing.");
        var off = c.Actions.Count - actions.Count;
        if (off > 0) facts.Add($"{off} of its actions are turned off.");
        facts.Add(c.Hotkeys.Count > 0 ? "Hotkeys: " + string.Join(", ", c.Hotkeys) + "." : "It has no hotkey.");
        var events = config.Events.Where(e => e.Commands.Contains(c.Id) || e.EndCommands.Contains(c.Id)).ToList();
        if (events.Count > 0) facts.Add("Events that run it: " + string.Join(", ", events.Select(e => e.Enabled ? e.Name : e.Name + " (turned off)")) + ".");
        foreach (var action in actions) CheckAction(config, action, problems);
    }

    /// <summary>What can be checked without running it: the program, the folder, the command or event it calls.</summary>
    private static void CheckAction(AppConfig config, CommandAction action, List<string> problems)
    {
        switch (action)
        {
            case RunAppAction run when Local(run.Path) is { } path && !File.Exists(path) && !Directory.Exists(path):
                problems.Add($"The program it opens is not there: {path}");
                break;
            case OpenFolderAction { Special: null } folder when Local(folder.Path) is { } path && !Directory.Exists(path):
                problems.Add($"The folder it opens is not there: {path}");
                break;
            case CommandControlAction { Target: CommandControlTarget.Command } call when call.CommandId.Length > 0:
                if (config.FindCommand(call.CommandId) is not { } called) problems.Add("It calls a command that no longer exists.");
                else if (call.Mode == CommandControlMode.Run && !called.Enabled) problems.Add($"It runs the command «{called.Name}», which is turned off.");
                break;
            case CommandControlAction { Target: CommandControlTarget.Event } call when call.EventId.Length > 0 && config.FindEvent(call.EventId) is null:
                problems.Add("It acts on an event that no longer exists.");
                break;
            case ConditionAction { Else: ConditionElse.RunCommand } condition when condition.ElseCommandId is { Length: > 0 } other && config.FindCommand(other) is null:
                problems.Add("Its «Si…» runs another command when not met, and that command no longer exists.");
                break;
        }
    }

    /// <summary>A full local path without variables (one that can be looked for); null otherwise (a name in PATH, a URL…).</summary>
    private static string? Local(string? text)
    {
        var path = Environment.ExpandEnvironmentVariables((text ?? string.Empty).Trim().Trim('"'));
        if (path.Length < 3 || path.Contains('{', StringComparison.Ordinal) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal)) return null;
        try { return Path.IsPathFullyQualified(path) ? path : null; }
        catch (ArgumentException) { return null; }
    }

    private static async Task DescribeEvent(AppConfig config, EventRule e, List<string> facts, List<string> problems,
        Func<EventRule, Task<(bool Met, string Failed)>>? conditionsNow)
    {
        if (!e.Enabled) problems.Add("The event is turned off (disabled): it never runs until it is turned on again.");
        facts.Add($"When: {e.Summary}.");
        facts.Add(e.LastFired is { } last ? $"It last ran by itself on {last.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}." : "It has not run by itself yet (since it was made or K-Desktop 5 started keeping it).");
        if (e.CooldownSeconds > 0) facts.Add($"It does not repeat within {e.CooldownSeconds} s.");

        var checks = e.Conditions.Items.Where(i => i.Enabled).ToList();
        if (checks.Count > 0)
        {
            facts.Add($"Only if ({(e.Conditions.Any ? "any of" : "all of")}): " + string.Join("; ", checks.Select(i => i.Describe())) + ".");
            if (conditionsNow is not null)
            {
                var (met, failed) = await conditionsNow(e).ConfigureAwait(false);
                if (met) facts.Add("Its «only if» conditions are met right now.");
                else problems.Add($"Its condition «{failed}» is not met right now, so it would not run now.");
            }
        }

        var commands = e.Commands.Where(id => !e.DisabledCommands.Contains(id)).ToList();
        var names = new List<string>();
        foreach (var id in commands)
        {
            if (config.FindCommand(id) is not { } c)
            {
                problems.Add("One of its commands no longer exists.");
                continue;
            }
            names.Add(c.Name);
            if (!c.Enabled) problems.Add($"Its command «{c.Name}» is turned off.");
        }
        facts.Add(names.Count > 0 ? "It runs: " + string.Join(", ", names) + "." : "It runs no command.");
        if (names.Count == 0 && e.Ask is null) problems.Add("It has no command to run (nor an order for an assistant): when it fires nothing happens.");
        if (e.Commands.Count > commands.Count) facts.Add($"{e.Commands.Count - commands.Count} of its commands are turned off in the event.");
        if (e.Ask is { } ask && ask.Text.Length > 0) facts.Add($"Then it asks an assistant: «{ask.Text}».");
    }
}
