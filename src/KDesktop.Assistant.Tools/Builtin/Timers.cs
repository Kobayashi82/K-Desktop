using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>A pending timer or reminder.</summary>
/// <param name="Assistant">Who set it (it tells you when it rings); null: the module without a body.</param>
public sealed record TimerItem(string Id, DateTimeOffset Due, string Text, string? Assistant);

/// <summary>
/// The timers of all the assistants, kept in a file so they survive a restart (one that came due while the process was
/// off rings as soon as it starts). <see cref="Rang"/> is raised on a pool thread. Thread-safe.
/// </summary>
public sealed partial class TimerStore : IDisposable
{
    public const int MaxPending = 20;

    public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly string _file;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private List<TimerItem> _items;

    public TimerStore(string file, TimeProvider? time = null)
    {
        _file = file;
        _time = time ?? TimeProvider.System;
        _items = Load(file);
        _timer = _time.CreateTimer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A timer came due (and was removed).</summary>
    public event Action<TimerItem>? Rang;

    public TimeProvider Time => _time;

    public IReadOnlyList<TimerItem> Pending
    {
        get { lock (_gate) return [.. _items.OrderBy(t => t.Due)]; }
    }

    /// <summary>Starts ringing: those already due ring now.</summary>
    public void Start() => Tick();

    /// <summary>Adds one; null with the reason when it cannot (in the past, too far, too many).</summary>
    public (TimerItem? Item, string? Error) Add(DateTimeOffset due, string text, string? assistant)
    {
        var now = _time.GetLocalNow();
        if (due <= now) return (null, "That time has already passed.");
        if (due - now > MaxAhead) return (null, $"Timers can be at most {MaxAhead.TotalDays:0} days ahead.");
        TimerItem item;
        lock (_gate)
        {
            if (_items.Count >= MaxPending) return (null, $"There are already {MaxPending} timers pending.");
            item = new TimerItem(Guid.NewGuid().ToString("N")[..10], due, text.Trim(), assistant);
            _items.Add(item);
        }
        Changed();
        return (item, null);
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate) removed = _items.RemoveAll(t => t.Id == id) > 0;
        if (removed) Changed();
        return removed;
    }

    /// <summary>The one whose text best matches <paramref name="hint"/> (or the next one when it says nothing), removed.</summary>
    public TimerItem? RemoveMatching(string? hint)
    {
        TimerItem? found;
        lock (_gate)
        {
            var words = Words(hint);
            found = _items.OrderBy(t => t.Due)
                .Select(t => (Item: t, Score: words.Length == 0 ? 1 : words.Count(w => Words(t.Text).Contains(w))))
                .Where(p => p.Score > 0)
                .OrderByDescending(p => p.Score)
                .Select(p => p.Item)
                .FirstOrDefault();
            if (found is not null) _items.Remove(found);
        }
        if (found is not null) Changed();
        return found;
    }

    private static string[] Words(string? text) => Core.Engine.FuzzyMatch.Tokens(text ?? string.Empty).Where(w => w.Length > 2).ToArray();

    private void Tick()
    {
        var now = _time.GetLocalNow();
        List<TimerItem> due;
        lock (_gate)
        {
            due = [.. _items.Where(t => t.Due <= now).OrderBy(t => t.Due)];
            if (due.Count > 0) _items.RemoveAll(t => t.Due <= now);
        }
        if (due.Count > 0) Save();
        foreach (var item in due)
        {
            try { Rang?.Invoke(item); }
            catch (Exception ex) { Log.Warn($"Asistente: aviso de temporizador fallido: {ex.Message}"); }
        }
        Schedule();
    }

    private void Changed()
    {
        Save();
        Schedule();
    }

    private void Schedule()
    {
        DateTimeOffset? next;
        lock (_gate) next = _items.Count == 0 ? null : _items.Min(t => t.Due);
        if (next is not { } at)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }
        var wait = at - _time.GetLocalNow();
        _timer.Change(wait < TimeSpan.Zero ? TimeSpan.Zero : wait + TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);
    }

    private static List<TimerItem> Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<List<TimerItem>>(File.ReadAllText(file), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido leer los temporizadores: {ex.Message}");
            return [];
        }
    }

    private void Save()
    {
        string json;
        lock (_gate) json = JsonSerializer.Serialize(_items, Json);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido guardar los temporizadores: {ex.Message}");
        }
    }

    public void Dispose() => _timer.Dispose();

    // ── reading times ────────────────────────────────────────────────────

    /// <summary>The next moment at that time of day (today or tomorrow).</summary>
    public static DateTimeOffset Next(TimeOnly time, DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var today = new DateTimeOffset(local.Date + time.ToTimeSpan(), local.Offset);
        return today > local ? today : today.AddDays(1);
    }

    /// <summary>A time written by the model or the user: «17:30», «17.30», «17h30», «17h», «5:30 pm», «5 pm», «17».</summary>
    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;
        var match = TimePattern().Match((text ?? string.Empty).Trim().ToLowerInvariant());
        if (!match.Success) return false;
        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (match.Groups["ampm"].Value is { Length: > 0 } suffix)
        {
            if (hour is < 1 or > 12) return false;
            hour = suffix.StartsWith('p') ? hour % 12 + 12 : hour % 12;
        }
        if (hour is < 0 or > 24 || minute is < 0 or > 59 || (hour == 24 && minute > 0)) return false;
        time = new TimeOnly(hour % 24, minute);
        return true;
    }

    [GeneratedRegex(@"^(?<h>\d{1,2})(?:\s*[:.h]\s*(?<m>\d{2}))?\s*h?\s*(?<ampm>[ap]\.?\s?m\.?)?$")]
    private static partial Regex TimePattern();
}

/// <summary>«¿Qué hora es?»: the date and time (and the timers pending).</summary>
public sealed class TimeTool(TimerStore timers) : ITool
{
    public string Name => "get_time";

    public string Description => "Gives the current date and time, and the timers that are pending.";

    public string ParametersSchemaJson => ToolText.NoParameters;

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.time.intent");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var now = timers.Time.GetLocalNow();
        var text = $"It is {now.ToString("HH:mm", CultureInfo.InvariantCulture)} on {now.ToString("dddd d MMMM yyyy", Loc.Instance.Culture)}.";
        if (timers.Pending is { Count: > 0 } pending) text += " Timers pending: " + SetTimerTool.Describe(pending, now) + ".";
        return Task.FromResult(new ToolResult(text) { Done = Loc.T("tool.time.done", ToolText.Clock(now)), Reply = Loc.T("tool.time.reply", ToolText.Clock(now), now.ToString("D", Loc.Instance.Culture)) });
    }
}

/// <summary>«Avísame en diez minutos», «recuérdame a las cinco que llame a mamá».</summary>
public sealed class SetTimerTool(TimerStore timers) : ITool
{
    public string Name => "set_timer";

    public string Description => "Sets a timer (in some minutes) or a reminder at a time of day, with an optional reason. It rings and tells the user.";

    public string ParametersSchemaJson => ToolText.Optional(
        ("minutes", "number", "Minutes from now (for «in 10 minutes»)"),
        ("time", "string", "Time of day HH:MM (for «at five»), when there are no minutes"),
        ("reason", "string", "What to remind, in the user's words (optional)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string? Gesture => "communicate";

    public string Intent(JsonElement arguments)
    {
        var intent = arguments.GetNumber("minutes") is > 0 and var minutes
            ? Loc.T("tool.set_timer.intentIn", ToolText.Left(TimeSpan.FromMinutes(minutes)))
            : TimerStore.TryParseTime(arguments.GetString("time"), out var at)
                ? Loc.T("tool.set_timer.intentAt", at.ToString("t", Loc.Instance.Culture))
                : Loc.T("tool.set_timer.intent");
        return ToolText.Clean(arguments.GetString("reason")) is { Length: > 0 } reason ? intent + Loc.T("tool.reason", reason) : intent;
    }

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var now = timers.Time.GetLocalNow();
        DateTimeOffset due;
        if (arguments.GetNumber("minutes") is { } minutes)
        {
            if (minutes <= 0) return Task.FromResult(ToolResult.Fail("The minutes must be more than zero."));
            due = now.AddMinutes(Math.Min(minutes, TimerStore.MaxAhead.TotalMinutes + 1));
        }
        else if (TimerStore.TryParseTime(arguments.GetString("time"), out var at)) due = TimerStore.Next(at, now);
        else return Task.FromResult(ToolResult.Fail("Say in how many minutes or at what time."));

        var reason = ToolText.Clean(arguments.GetString("reason"));
        var (item, error) = timers.Add(due, reason, context.Assistant);
        if (item is null) return Task.FromResult(ToolResult.Fail(error ?? "It cannot be set."));
        var clock = ToolText.Clock(item.Due);
        return Task.FromResult(new ToolResult($"Timer set for {item.Due.ToLocalTime():HH:mm} ({ToolText.Left(item.Due - now)} from now){(reason.Length > 0 ? ": " + reason : "")}.")
        {
            Done = reason.Length > 0 ? Loc.T("tool.set_timer.doneFor", clock, reason) : Loc.T("tool.set_timer.done", clock),
            Reply = reason.Length > 0 ? Loc.T("tool.set_timer.replyFor", clock, reason) : Loc.T("tool.set_timer.reply", clock),
            Undo = new ToolUndo(Name, item.Id),
        });
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(timers.Remove(undo.Data));

    /// <summary>The timers on one line, for the model: «17:42 (in 10 min): take the pizza out; …».</summary>
    public static string Describe(IReadOnlyList<TimerItem> timers, DateTimeOffset now) => string.Join("; ", timers.Select(t =>
        $"{t.Due.ToLocalTime():HH:mm} (in {ToolText.Left(t.Due - now)}){(t.Text.Length > 0 ? ": " + t.Text : "")}"));
}

/// <summary>«Quita el temporizador de la pizza».</summary>
public sealed class CancelTimerTool(TimerStore timers) : ITool
{
    public string Name => "cancel_timer";

    public string Description => "Removes a pending timer: the one whose reason matches, or the next one.";

    public string ParametersSchemaJson => ToolText.Optional(("reason", "string", "Words of its reason, to find it (optional)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => ToolText.Clean(arguments.GetString("reason")) is { Length: > 0 } reason
        ? Loc.T("tool.cancel_timer.intentFor", reason)
        : Loc.T("tool.cancel_timer.intent");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (timers.RemoveMatching(arguments.GetString("reason")) is not { } removed)
            return Task.FromResult(ToolResult.Fail("There is no such timer pending.") with { Reply = Loc.T("tool.cancel_timer.none") });
        var clock = ToolText.Clock(removed.Due);
        return Task.FromResult(new ToolResult($"Removed the timer of {removed.Due.ToLocalTime():HH:mm}{(removed.Text.Length > 0 ? ": " + removed.Text : "")}.")
        {
            Done = removed.Text.Length > 0 ? Loc.T("tool.cancel_timer.doneFor", clock, removed.Text) : Loc.T("tool.cancel_timer.done", clock),
            Reply = removed.Text.Length > 0 ? Loc.T("tool.cancel_timer.replyFor", clock, removed.Text) : Loc.T("tool.cancel_timer.reply", clock),
            Undo = new ToolUndo(Name, JsonSerializer.Serialize(removed)),
        });
    }

    /// <summary>Puts it back (if its time has not passed).</summary>
    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        var item = JsonSerializer.Deserialize<TimerItem>(undo.Data);
        return Task.FromResult(item is not null && timers.Add(item.Due, item.Text, item.Assistant).Item is not null);
    }
}
