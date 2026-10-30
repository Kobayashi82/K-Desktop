using System.Globalization;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// The calendar's tools (calendar, phase 4): thin, they ask K-Desktop through the pipe (its CalendarService has the own
/// calendar, the subscriptions and the accounts; their tokens never leave it). What they find is shown to the model with a
/// key, so a change or a delete points at exactly that appearance.
/// </summary>
internal static class CalendarTools
{
    public const string Ranges = "today, tomorrow, yesterday, this_week, next_week, weekend, this_month, next_month, next_7_days, next_30_days, a weekday in English (monday…) or YYYY-MM-DD";

    public static async Task<CalendarAnswer?> AskAsync(ICommandBridge bridge, CalendarRequest request, string? assistant, CancellationToken cancellationToken) =>
        (await bridge.CalendarAsync(new CalendarMessage(string.Empty, request, assistant), cancellationToken).ConfigureAwait(false))?.Answer;

    public static ToolResult NoAnswer() => ToolResult.Fail("K-Desktop did not answer: nothing was done in the calendar.") with { Reply = Loc.T("tool.calendar.noAnswer") };

    /// <summary>One line per appearance for the model: «[key] Title · 2026-11-05 17:00–17:30 · Calendar · place · repeats · online».</summary>
    public static string Lines(IEnumerable<CalendarItem> items)
    {
        var sb = new StringBuilder();
        foreach (var i in items)
        {
            sb.Append("- [").Append(i.Key).Append("] ").Append(i.Title).Append(" · ");
            if (i.AllDay) sb.Append(i.Start).Append(i.End != i.Start ? " to " + i.End : string.Empty).Append(" (all day)");
            else sb.Append(i.Start).Append('–').Append(i.End.Length > 11 && i.End[..10] == i.Start[..10] ? i.End[11..] : i.End);
            if (i.Calendar.Length > 0) sb.Append(" · calendar ").Append(i.Calendar);
            if (!string.IsNullOrWhiteSpace(i.Location)) sb.Append(" · at ").Append(i.Location);
            if (i.Repeats) sb.Append(" · repeats");
            if (i.Meeting is not null) sb.Append(" · online meeting");
            if (!string.IsNullOrWhiteSpace(i.Notes)) sb.Append(" · notes: ").Append(i.Notes.ReplaceLineEndings(" "));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>«Dentista (viernes 17:00)» in the user's language.</summary>
    public static string Spoken(CalendarItem i, bool withDay = true)
    {
        var culture = Loc.Instance.Culture;
        if (!DateTime.TryParse(i.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return ToolText.Clean(i.Title);
        var day = start.Date == DateTime.Today ? Loc.T("calendar.today").ToLower(culture)
            : start.Date == DateTime.Today.AddDays(1) ? Loc.T("calendar.tomorrow").ToLower(culture)
            : start.ToString("dddd d", culture);
        var when = i.AllDay ? (withDay ? day : Loc.T("calendar.allDay").ToLower(culture)) : withDay ? $"{day} {start.ToString("t", culture)}" : start.ToString("t", culture);
        return $"{ToolText.Clean(i.Title)} ({when})";
    }

    /// <summary>What went wrong, for the model and for the user.</summary>
    public static ToolResult Problem(CalendarAnswer answer, string what) => answer.Reason switch
    {
        "calendar" => ToolResult.Fail($"There is no such calendar ({what}). The calendars are: {string.Join(", ", answer.Calendars ?? [])}. Ask the user which one.")
            with { Reply = Loc.T("tool.calendar.noCalendar", string.Join(", ", answer.Calendars ?? [])) },
        "range" => ToolResult.Fail("Say the days as one of: " + Ranges + ", or from/to as YYYY-MM-DD."),
        "start" => ToolResult.Fail("Say when it starts as YYYY-MM-DD HH:MM (or YYYY-MM-DD for all day). Nothing was done."),
        "title" => ToolResult.Fail("Say what the cita is (title). Nothing was done."),
        "notFound" => ToolResult.Fail("No cita fits that: nothing was done. Use calendar_list to look for it.") with { Reply = Loc.T("tool.calendar.notFound") },
        "ambiguous" => new ToolResult("Several citas fit, so nothing was done: ask the user which one, then call again with its key.\n" + Lines(answer.Items ?? []))
        {
            Reply = Loc.T("tool.calendar.which", string.Join(" · ", (answer.Items ?? []).Take(5).Select(i => Spoken(i)))),
            Done = Loc.T("tool.calendar.whichDone"),
        },
        "readOnly" => ToolResult.Fail("That calendar cannot be changed from K-Desktop: " + answer.Message) with { Reply = answer.Message },
        "conflict" => ToolResult.Fail("Someone changed that cita elsewhere meanwhile; nothing was overwritten. Tell the user.") with { Reply = Loc.T("agenda.conflict") },
        "offline" => ToolResult.Fail("The calendar's server could not be reached: nothing was done.") with { Reply = Loc.T("tool.calendar.offline") },
        _ => ToolResult.Fail("It could not be done (" + (answer.Message ?? answer.Reason) + ")."),
    };

    public static CalendarCita Cita(JsonElement a)
    {
        int? Number(string name) => a.GetNumber(name) is { } n ? (int)Math.Round(n) : null;
        bool? Flag(string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        string? Text(string name) => a.GetString(name) is { } s ? s.Trim() : null;
        return new CalendarCita(Text("title"), Text("start"), Text("end"), Flag("all_day"), Number("minutes"), Text("location"), Text("notes"),
            Text("repeat"), Number("reminder"), Text("calendar"));
    }

    public static async Task<bool> UndoAsync(ICommandBridge bridge, ToolUndo undo, CancellationToken cancellationToken) =>
        undo.Data.Length > 0 && await AskAsync(bridge, new CalendarRequest("undo", Undo: undo.Data), null, cancellationToken).ConfigureAwait(false) is { Ok: true };

    public const string CitaProperties = """
        "title":{"type":"string","description":"What it is (Dentist, Meeting with Ana)"},"start":{"type":"string","description":"YYYY-MM-DD HH:MM in the user's time; only YYYY-MM-DD for an all-day cita"},"end":{"type":"string","description":"YYYY-MM-DD HH:MM; or the last day of an all-day cita"},"minutes":{"type":"integer","description":"How long, when there is no end (default 60)"},"location":{"type":"string"},"notes":{"type":"string"},"repeat":{"type":"string","description":"none, daily, weekdays, weekly, biweekly, monthly, yearly, or an iCalendar RRULE"},"reminder":{"type":"integer","description":"Minutes before to remind (0: at the start, -1: none)"},"calendar":{"type":"string","description":"The calendar as the user calls it (Work, Personal); empty: the default one"}
        """;
}

/// <summary>«¿Qué tengo mañana?», «¿cuándo tengo el dentista?», «¿estoy libre el jueves por la tarde?».</summary>
public sealed class CalendarListTool(ICommandBridge bridge) : ITool
{
    public string Name => "calendar_list";

    public string Description => "Reads the user's calendar (their own, Google and subscriptions): the citas of some days, or finds one by its words (when is the dentist?). Says the calendar of each.";

    public string ParametersSchemaJson { get; } =
        """{"type":"object","properties":{"range":{"type":"string","description":"Which days: """ + CalendarTools.Ranges + """. Empty with text: from a month ago to a year ahead; empty without: the next 7 days"},"from":{"type":"string","description":"YYYY-MM-DD (instead of range)"},"to":{"type":"string","description":"YYYY-MM-DD, the last day"},"text":{"type":"string","description":"Words of the cita to look for (dentist)"},"calendar":{"type":"string","description":"Only this calendar, as the user calls it"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.calendar_list.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var request = new CalendarRequest("list", arguments.GetString("range"), arguments.GetString("from"), arguments.GetString("to"),
            arguments.GetString("text"), arguments.GetString("calendar"));
        if (await CalendarTools.AskAsync(bridge, request, context.Assistant, cancellationToken).ConfigureAwait(false) is not { } answer) return CalendarTools.NoAnswer();
        if (!answer.Ok) return CalendarTools.Problem(answer, request.Calendar ?? request.Range ?? string.Empty);
        var items = answer.Items ?? [];
        var span = $"from {answer.From} to {answer.To}";
        if (items.Count == 0)
            return new ToolResult($"No citas {span}" + (string.IsNullOrWhiteSpace(request.Text) ? "." : $" with «{request.Text}».") + " Tell the user they have nothing then.")
            {
                Reply = Loc.T(string.IsNullOrWhiteSpace(request.Text) ? "tool.calendar_list.none" : "tool.calendar_list.notFound"),
                Done = Loc.T("tool.calendar_list.done", 0),
            };
        var oneDay = items.All(i => i.Start[..10] == items[0].Start[..10]);
        return new ToolResult($"{items.Count} citas {span}" + (items.Count >= CalendarAssistant.MaxItems ? " (only the first ones)" : string.Empty) + ":\n" + CalendarTools.Lines(items)
                              + "Answer briefly with what the user asked; times are the user's.")
        {
            Reply = Loc.T("tool.calendar_list.reply", string.Join(", ", items.Take(8).Select(i => CalendarTools.Spoken(i, withDay: !oneDay)))),
            Done = Loc.T("tool.calendar_list.done", items.Count),
        };
    }
}

/// <summary>«Apunta el dentista el viernes a las cinco», «añade al de trabajo una reunión cada lunes a las 9».</summary>
public sealed class CalendarAddTool(ICommandBridge bridge) : ITool
{
    public string Name => "calendar_add";

    public string Description => "Adds a cita to the user's calendar (their own one by default, or another by its name). For something at a time on a day; a reminder alone is set_timer or schedule.";

    public string ParametersSchemaJson { get; } = """{"type":"object","properties":{""" + CalendarTools.CitaProperties + """},"required":["title","start"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) =>
        Loc.T("tool.calendar_add.intent", ToolText.Clean(arguments.GetString("title") ?? "…"), ToolText.Clean(arguments.GetString("start") ?? "…"));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var cita = CalendarTools.Cita(arguments);
        if (await CalendarTools.AskAsync(bridge, new CalendarRequest("add", Cita: cita), context.Assistant, cancellationToken).ConfigureAwait(false) is not { } answer)
            return CalendarTools.NoAnswer();
        if (!answer.Ok) return CalendarTools.Problem(answer, cita.Calendar ?? string.Empty);
        var item = answer.Item;
        var text = item is null ? "Added." : "Added:\n" + CalendarTools.Lines([item]) + "Tell the user in a short sentence.";
        return new ToolResult(text)
        {
            Done = Loc.T("tool.calendar_add.done", item is null ? ToolText.Clean(cita.Title ?? string.Empty) : CalendarTools.Spoken(item)),
            Reply = item is null ? null : Loc.T("tool.calendar_add.reply", CalendarTools.Spoken(item), item.Calendar),
            Undo = answer.Undo is { } undo ? new ToolUndo(Name, undo) : null,
        };
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => CalendarTools.UndoAsync(bridge, undo, cancellationToken);
}

/// <summary>«Mueve el dentista a las seis», «la reunión del lunes que dure dos horas», «pasa la clase de inglés al de trabajo».</summary>
public sealed class CalendarChangeTool(ICommandBridge bridge) : ITool
{
    public string Name => "calendar_change";

    public string Description => "Changes a cita of the user's calendar: its time, title, place, notes, reminder, repetition or calendar. Finds it by words (and day) or by the key calendar_list gave; when several fit it changes none and you ask which.";

    public string ParametersSchemaJson { get; } =
        """{"type":"object","properties":{"which":{"type":"string","description":"Words of the cita to change (the dentist)"},"date":{"type":"string","description":"YYYY-MM-DD: the day it is on now, if said"},"key":{"type":"string","description":"Its key from calendar_list (better than which)"},"scope":{"type":"string","enum":["this","following","all"],"description":"For a cita that repeats: only this one (default), this and the following, or all"},""" + CalendarTools.CitaProperties + "}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.AskOnce;

    public string Intent(JsonElement arguments) => Loc.T("tool.calendar_change.intent", ToolText.Clean(arguments.GetString("which") ?? arguments.GetString("title") ?? "…"));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var request = new CalendarRequest("change", From: arguments.GetString("date"), Text: arguments.GetString("which"), Key: arguments.GetString("key"),
            Scope: arguments.GetString("scope"), Cita: CalendarTools.Cita(arguments));
        if (await CalendarTools.AskAsync(bridge, request, context.Assistant, cancellationToken).ConfigureAwait(false) is not { } answer) return CalendarTools.NoAnswer();
        if (!answer.Ok) return CalendarTools.Problem(answer, request.Cita?.Calendar ?? string.Empty);
        var item = answer.Item;
        return new ToolResult(item is null ? "Changed." : "Changed. Now:\n" + CalendarTools.Lines([item]) + "Tell the user in a short sentence.")
        {
            Done = Loc.T("tool.calendar_change.done", item is null ? ToolText.Clean(request.Text ?? string.Empty) : CalendarTools.Spoken(item)),
            Reply = item is null ? null : Loc.T("tool.calendar_change.reply", CalendarTools.Spoken(item)),
            Undo = answer.Undo is { } undo ? new ToolUndo(Name, undo) : null,
        };
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => CalendarTools.UndoAsync(bridge, undo, cancellationToken);
}

/// <summary>«Borra la cita del dentista», «quita la reunión del jueves». Always asks.</summary>
public sealed class CalendarDeleteTool(ICommandBridge bridge) : ITool
{
    public string Name => "calendar_delete";

    public string Description => "Deletes a cita of the user's calendar, found by words (and day) or by the key calendar_list gave; when several fit it deletes none and you ask which.";

    public string ParametersSchemaJson { get; } =
        """{"type":"object","properties":{"which":{"type":"string","description":"Words of the cita to delete"},"date":{"type":"string","description":"YYYY-MM-DD: the day it is on, if said"},"key":{"type":"string","description":"Its key from calendar_list (better than which)"},"scope":{"type":"string","enum":["this","following","all"],"description":"For a cita that repeats: only this one (default), this and the following, or all"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.AskAlways;

    public bool AlwaysAsks => true;

    public string Intent(JsonElement arguments) =>
        Loc.T("tool.calendar_delete.intent", ToolText.Clean(arguments.GetString("which") ?? arguments.GetString("key") ?? "…"))
        + (arguments.GetString("scope") is "all" ? " " + Loc.T("tool.calendar_delete.allOfThem") : arguments.GetString("scope") is "following" ? " " + Loc.T("tool.calendar_delete.following") : string.Empty);

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var request = new CalendarRequest("delete", From: arguments.GetString("date"), Text: arguments.GetString("which"), Key: arguments.GetString("key"),
            Scope: arguments.GetString("scope"));
        if (await CalendarTools.AskAsync(bridge, request, context.Assistant, cancellationToken).ConfigureAwait(false) is not { } answer) return CalendarTools.NoAnswer();
        if (!answer.Ok) return CalendarTools.Problem(answer, string.Empty);
        var item = answer.Item;
        return new ToolResult(item is null ? "Deleted." : "Deleted:\n" + CalendarTools.Lines([item]) + "Tell the user in a short sentence.")
        {
            Done = Loc.T("tool.calendar_delete.done", item is null ? ToolText.Clean(request.Text ?? string.Empty) : CalendarTools.Spoken(item)),
            Reply = item is null ? null : Loc.T("tool.calendar_delete.reply", CalendarTools.Spoken(item)),
            Undo = answer.Undo is { } undo ? new ToolUndo(Name, undo) : null,
        };
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => CalendarTools.UndoAsync(bridge, undo, cancellationToken);
}
