using System.Globalization;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «A las 19:00 mira el tiempo, mándamelo por Telegram y apaga el ordenador»: an order for later, kept as a K-Desktop
/// event (Eventos) that gives it to the assistant at its time, as if said then. What it will need is approved now (asked
/// once, by its riskiest part): at that time nobody is asked, and what was not approved and would ask is not done.
/// </summary>
public sealed class ScheduleTool(ICommandBridge bridge, ToolRegistry registry) : ITool
{
    public string Name => "schedule";

    public string Description => "Programs an order for later: at that time the assistant gets it as if the user said it then, and does it without asking. "
                                 + "For «at 7 do X», «tomorrow at 9…», «every day at 8…» (not to do it now). List the tools and the user's commands it will need: "
                                 + "they are approved now. For a plain reminder use set_timer.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"order":{"type":"string","description":"What to do then, in the user's words, without the time («mira el tiempo, mándamelo por Telegram y apaga el ordenador»)"},"time":{"type":"string","description":"Time of day HH:MM"},"minutes":{"type":"number","description":"Minutes from now, instead of time («in 2 hours» = 120)"},"day":{"type":"string","description":"today, tomorrow, a weekday in English (friday) or YYYY-MM-DD; empty: the next time it is that hour"},"repeat":{"type":"string","enum":["once","daily","workdays","weekend","weekly"],"description":"once (default) or how it repeats"},"tools":{"type":"array","items":{"type":"string"},"description":"Names of the tools it will use then (send_telegram, look_at_screen…)"},"commands":{"type":"array","items":{"type":"string"},"description":"Exact names of the user's K-Desktop commands it will run then"}},"required":["order"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.AskOnce;

    /// <summary>
    /// As risky as the riskiest thing it approves: only reading asks nothing; a command of the user counts as asking once
    /// (K-Desktop knows the rest).
    /// </summary>
    public ToolRisk RiskFor(JsonElement arguments)
    {
        var (tools, commands) = Split(arguments);
        return RiskOf(registry, tools, commands.Count > 0);
    }

    /// <summary>The riskiest of what is approved for later (schedule, create_event): the tools named, and the user's commands as asking once.</summary>
    public static ToolRisk RiskOf(ToolRegistry registry, IEnumerable<string> tools, bool commands)
    {
        var risk = commands ? ToolRisk.AskOnce : ToolRisk.Safe;
        foreach (var tool in tools.Select(registry.FindAny).OfType<ITool>().Where(t => !t.ReadsOnly))
        {
            var its = registry.AlwaysAsks(tool) ? ToolRisk.AskAlways : registry.RiskOf(tool);
            // What K-Desktop asks about itself (the tool is «safe» here): a command, an event, a program closed or ended.
            if (AskedByKDesktop.TryGetValue(tool.Name, out var there) && there > its) its = there;
            if (its > risk) risk = its;
        }
        return risk;
    }

    private static readonly Dictionary<string, ToolRisk> AskedByKDesktop = new(StringComparer.Ordinal)
    {
        ["run_command"] = ToolRisk.AskOnce,
        ["run_event"] = ToolRisk.AskOnce,
        ["control_app"] = ToolRisk.AskOnce,
        ["end_process"] = ToolRisk.AskAlways,
        ["power_pc"] = ToolRisk.AskAlways,
        ["display"] = ToolRisk.AskOnce,
        ["toggle_radio"] = ToolRisk.AskOnce,
    };

    public string Intent(JsonElement arguments)
    {
        var order = ToolText.Clean(arguments.GetString("order"));
        var when = When(arguments, DateTime.Now) is { } at ? Human(at.Date, at.Time, Repeat(arguments)) : "?";
        var (tools, commands) = Split(arguments);
        var acting = tools.Select(registry.FindAny).OfType<ITool>().Where(t => !t.ReadsOnly).Select(t => Title(t.Name))
            .Concat(commands.Select(c => $"«{c}»")).ToList();
        return Loc.T("tool.schedule.intent", when, order) + (acting.Count > 0 ? Loc.T("tool.schedule.intentMay", string.Join(", ", acting)) : string.Empty);
    }

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var order = ToolText.Clean(arguments.GetString("order"));
        if (order.Length == 0) return ToolResult.Fail("Say what to do then (order).");
        var day = arguments.GetString("day");
        if (day is { Length: > 0 } && AssistantSchedule.Day(day, DateOnly.FromDateTime(DateTime.Now)) is null)
            return ToolResult.Fail("Say the day as today, tomorrow, a weekday in English (friday) or YYYY-MM-DD.");
        if (When(arguments, DateTime.Now) is not { } at) return ToolResult.Fail("Say at what time (HH:MM) or in how many minutes.");
        var (tools, commands) = Split(arguments);
        var known = tools.Where(t => registry.FindAny(t) is not null).ToList();
        // A name that is no tool may be one of the user's commands.
        commands = [.. commands.Concat(tools.Where(t => registry.FindAny(t) is null)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (commands.Count > 0 && !known.Contains("run_command")) known.Add("run_command");
        var repeat = Repeat(arguments);
        var done = await bridge.ScheduleAsync(new ScheduleMessage(string.Empty, "add", context.Assistant, order,
            at.Time.ToString("HH:mm", CultureInfo.InvariantCulture), at.Date is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            repeat, known, commands), cancellationToken).ConfigureAwait(false);
        if (done is null) return ToolResult.Fail("K-Desktop did not answer: nothing was programmed.");
        if (!done.Ok || done.Items is not [var entry])
            return done.Reason switch
            {
                "past" => ToolResult.Fail("That time has already passed: nothing was programmed.") with { Reply = Loc.T("tool.schedule.past") },
                "tooMany" => ToolResult.Fail("There are too many programmed orders: remove some first.") with { Reply = Loc.T("tool.schedule.tooMany") },
                _ => ToolResult.Fail("It could not be programmed (" + done.Reason + ")."),
            };
        var (entryDate, entryTime) = Parse(entry.When);
        var human = Human(entryDate, entryTime, entry.Repeat);
        var unknown = done.Unknown ?? [];
        var text = $"Programmed for {entry.When}{(entry.Repeat is { } r ? " (repeats: " + r + ")" : "")}: «{order}». Approved to use without asking: "
                   + $"{(known.Count + commands.Count - unknown.Count > 0 ? string.Join(", ", known.Concat(commands.Except(unknown))) : "nothing that asks")}."
                   + (unknown.Count > 0 ? $" These are not commands of the user and will not be done: {string.Join(", ", unknown)}. Tell the user." : "");
        var reply = Loc.T("tool.schedule.reply", human, order) + (unknown.Count > 0 ? " " + Loc.T("tool.schedule.unknown", string.Join(", ", unknown)) : string.Empty);
        context.Show?.Invoke(ToolGesture.Mark(ToolSpot.Clock, entryTime.ToString("t", Loc.Instance.Culture)));
        return new ToolResult(text)
        {
            Done = Loc.T("tool.schedule.done", human, order),
            Reply = reply,
            Undo = new ToolUndo(Name, entry.Id),
        };
    }

    /// <summary>Removes it again (if it has not run yet).</summary>
    public async Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) =>
        await bridge.ScheduleAsync(new ScheduleMessage(string.Empty, "cancel", Event: undo.Data), cancellationToken).ConfigureAwait(false) is { Ok: true };

    /// <summary>The tools and the commands it names (arrays, or a text with commas from a model that writes them so).</summary>
    private static (List<string> Tools, List<string> Commands) Split(JsonElement arguments) =>
        ([.. Strings(arguments, "tools").Where(t => t is not ("schedule" or "list_scheduled" or "cancel_scheduled"))], [.. Strings(arguments, "commands")]);

    public static IEnumerable<string> Strings(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value)) return [];
        var items = value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()),
            JsonValueKind.String => value.GetString()!.Split(',', ';'),
            _ => [],
        };
        return items.OfType<string>().Select(s => s.Trim().Trim('«', '»', '"')).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static string? Repeat(JsonElement arguments) =>
        arguments.GetString("repeat")?.Trim().ToLowerInvariant() is { } r && r is "daily" or "workdays" or "weekend" or "weekly" ? r : null;

    /// <summary>The day (null: the next time it is that hour) and the time asked for.</summary>
    private static (DateOnly? Date, TimeOnly Time)? When(JsonElement arguments, DateTime now)
    {
        if (arguments.GetNumber("minutes") is > 0 and var minutes)
        {
            var due = now.AddMinutes(Math.Min(minutes, TimeSpan.FromDays(366).TotalMinutes));
            return (DateOnly.FromDateTime(due), TimeOnly.FromDateTime(due));
        }
        if (!TimerStore.TryParseTime(arguments.GetString("time"), out var time)) return null;
        // No day: the next time it is that hour.
        var today = DateOnly.FromDateTime(now);
        return (AssistantSchedule.Day(arguments.GetString("day"), today) ?? (time > TimeOnly.FromDateTime(now) ? today : today.AddDays(1)), time);
    }

    private static (DateOnly? Date, TimeOnly Time) Parse(string when) =>
        DateTime.TryParseExact(when, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? (DateOnly.FromDateTime(at), TimeOnly.FromDateTime(at))
            : (null, TimeOnly.MinValue);

    /// <summary>«hoy a las 19:00», «mañana a las 7:00», «el viernes 30 a las 9:00», «cada día a las 8:00».</summary>
    public static string Human(DateOnly? date, TimeOnly time, string? repeat)
    {
        var culture = Loc.Instance.Culture;
        var clock = time.ToString("t", culture);
        var today = DateOnly.FromDateTime(DateTime.Now);
        return repeat switch
        {
            "daily" or "workdays" or "weekend" => Loc.T("tool.schedule.when." + repeat, clock),
            "weekly" => Loc.T("tool.schedule.when.weekly", clock, (date ?? today).ToString("dddd", culture)),
            _ when date is null || date == today => Loc.T("tool.schedule.when.today", clock),
            _ when date == today.AddDays(1) => Loc.T("tool.schedule.when.tomorrow", clock),
            _ => Loc.T("tool.schedule.when.on", clock, date.Value.ToString(date.Value < today.AddDays(7) ? "dddd d" : "d MMMM", culture)),
        };
    }

    /// <summary>A tool's name for people (its title in Options → Tools, when it has one).</summary>
    private static string Title(string tool)
    {
        var key = "tool." + tool + ".title";
        var title = Loc.T(key);
        return title == key ? tool : title;
    }
}

/// <summary>«¿Qué tienes programado?»: the orders programmed for later.</summary>
/// <summary>
/// «¿Qué tienes pendiente?» (B2, 5.0.344): everything waiting for later at once, the timers, the orders programmed and what
/// notify_when watches.
/// </summary>
public sealed class ListScheduledTool(ICommandBridge bridge, BuiltinTools? builtin = null) : ITool
{
    public string Name => "list_scheduled";

    public string Description => "Lists everything pending for later: the timers, the orders programmed with schedule and what notify_when is watching. Use it for «what do you have pending».";

    public string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.list_scheduled.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var timers = builtin?.Timers.Pending ?? [];
        var watches = builtin?.Watches.Pending ?? [];
        var done = await bridge.ScheduleAsync(new ScheduleMessage(string.Empty, "list"), cancellationToken).ConfigureAwait(false);
        // Without K-Desktop's answer the timers and the watches are still known.
        if (done is not { Ok: true } && timers.Count == 0 && watches.Count == 0) return ToolResult.Fail("K-Desktop did not answer.");
        var items = done is { Ok: true } ? done.Items ?? [] : [];
        if (items.Count == 0 && timers.Count == 0 && watches.Count == 0)
            return new ToolResult("Nothing is pending: no timers, programmed orders or watches.") { Reply = Loc.T("tool.list_scheduled.none") };

        var model = new List<string>();
        var said = new List<string>();
        if (timers.Count > 0)
        {
            var now = builtin!.Timers.Time.GetUtcNow();
            model.Add("Timers: " + SetTimerTool.Describe(timers, now));
            said.AddRange(timers.OrderBy(t => t.Due).Select(t => t.Text.Length > 0
                ? Loc.T("tool.list_scheduled.timerFor", ToolText.Clock(t.Due), t.Text)
                : Loc.T("tool.list_scheduled.timer", ToolText.Clock(t.Due))));
        }
        if (items.Count > 0)
        {
            model.Add("Programmed: " + string.Join("; ", items.Select(e => $"{e.When}{(e.Repeat is { } r ? " (" + r + ")" : "")}: «{e.Order}»")));
            said.AddRange(items.Select(Said));
        }
        if (watches.Count > 0)
        {
            model.Add("Watching (notify_when): " + string.Join("; ", watches.Select(WatchStore.Told)));
            said.AddRange(watches.Select(w => Loc.T("tool.list_scheduled.watch", WatchStore.Told(w))));
        }
        if (done is not { Ok: true }) model.Add("(K-Desktop did not answer: the programmed orders are unknown.)");
        return new ToolResult(string.Join(". ", model)) { Reply = Loc.T("tool.list_scheduled.reply", string.Join("; ", said)) };
    }

    public static string Said(ScheduledEntry entry)
    {
        var at = DateTime.TryParseExact(entry.When, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when) ? when : (DateTime?)null;
        var human = at is { } a ? ScheduleTool.Human(DateOnly.FromDateTime(a), TimeOnly.FromDateTime(a), entry.Repeat) : entry.When;
        return $"{human}: {entry.Order}";
    }
}

/// <summary>«Ya no hace falta lo de las siete»: removes an order programmed for later.</summary>
public sealed class CancelScheduledTool(ICommandBridge bridge) : ITool
{
    public string Name => "cancel_scheduled";

    public string Description => "Removes an order programmed for later (with schedule): the one whose words match, or the next one.";

    public string ParametersSchemaJson => ToolText.Optional(("which", "string", "Words of the order or its time, to find it (optional: the next one)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => ToolText.Clean(arguments.GetString("which")) is { Length: > 0 } which
        ? Loc.T("tool.cancel_scheduled.intentFor", which)
        : Loc.T("tool.cancel_scheduled.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var which = ToolText.Clean(arguments.GetString("which"));
        var done = await bridge.ScheduleAsync(new ScheduleMessage(string.Empty, "cancel", Event: which), cancellationToken).ConfigureAwait(false);
        if (done is null) return ToolResult.Fail("K-Desktop did not answer.");
        if (!done.Ok || done.Items is not [var entry])
            return ToolResult.Fail("There is no such programmed order.") with { Reply = Loc.T("tool.cancel_scheduled.none") };
        var said = ListScheduledTool.Said(entry);
        return new ToolResult($"Removed the programmed order {entry.When}: «{entry.Order}».")
        {
            Done = Loc.T("tool.cancel_scheduled.done", said),
            Reply = Loc.T("tool.cancel_scheduled.reply", said),
        };
    }
}
