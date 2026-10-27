using System.Globalization;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine.Events;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The orders the assistants program (<see cref="ScheduleMessage"/>): K-Desktop events «A una hora» with «Pedir a un
/// asistente», «Solo una vez» unless they repeat, marked with who made them and what was approved. They show in Eventos
/// like any other, and the user can change or delete them there. Changed on the thread that owns the configuration.
/// </summary>
public static class AssistantSchedule
{
    /// <summary>Programmed orders kept at most (all the assistants together).</summary>
    public const int MaxScheduled = 50;

    /// <summary>The longest name of the event: the start of the order.</summary>
    private const int NameLength = 60;

    public static ScheduleDoneMessage Handle(AppConfig config, ScheduleMessage request, DateTime nowLocal) => request.Op switch
    {
        "add" => Add(config, request, nowLocal),
        "list" => new ScheduleDoneMessage(request.Id, true, [.. Scheduled(config).Select(e => Entry(e, nowLocal))]),
        "cancel" => Cancel(config, request, nowLocal),
        _ => new ScheduleDoneMessage(request.Id, false, Reason: "op"),
    };

    /// <summary>The events the assistants programmed, in the list's order.</summary>
    public static IEnumerable<EventRule> Scheduled(AppConfig config) => config.Events.Where(e => e.CreatedBy is not null && e.Trigger is TimeTrigger);

    private static ScheduleDoneMessage Add(AppConfig config, ScheduleMessage request, DateTime nowLocal)
    {
        var order = string.Join(' ', (request.Order ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (order.Length == 0) return new ScheduleDoneMessage(request.Id, false, Reason: "empty");
        if (EventTime.Parse(request.Time) is not { } time) return new ScheduleDoneMessage(request.Id, false, Reason: "time");
        if (Scheduled(config).Count() >= MaxScheduled) return new ScheduleDoneMessage(request.Id, false, Reason: "tooMany");
        var repeat = request.Repeat?.Trim().ToLowerInvariant() is { Length: > 0 } r && r != "once" ? r : null;
        var date = EventTime.ParseDate(request.Date);
        // Once and without a day: the next time it is that hour (today, or tomorrow if it has passed).
        var today = DateOnly.FromDateTime(nowLocal);
        date ??= repeat is null or "weekly" ? today.ToDateTime(time) > nowLocal ? today : today.AddDays(1) : null;
        if (repeat is null && date is { } day && day.ToDateTime(time) <= nowLocal) return new ScheduleDoneMessage(request.Id, false, Reason: "past");

        var trigger = new TimeTrigger { Time = time.ToString("HH:mm", CultureInfo.InvariantCulture) };
        switch (repeat)
        {
            case null: trigger.Date = EventTime.FormatDate(date!.Value); break;
            case "workdays": trigger.Days = WeekDays.Workdays; break;
            case "weekend": trigger.Days = WeekDays.Weekend; break;
            case "weekly": trigger.Days = WeekDaysExtensions.Of(date!.Value.DayOfWeek); break;
            default: break; // daily
        }

        var (commands, unknown) = FindCommands(config, request.Commands ?? []);
        var assistant = request.Assistant ?? AssistantTargeting.KDesktop;
        var rule = new EventRule
        {
            Name = order.Length <= NameLength ? order : order[..NameLength].TrimEnd() + "…",

            Trigger = trigger,
            Once = repeat is null,
            CreatedBy = assistant,
            Ask = new EventAsk
            {
                Assistant = assistant,
                Text = order,
                Tools = [.. (request.Tools ?? []).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal)],
                Commands = [.. commands.Select(c => c.Id)],
            },
        };
        config.Events.Add(rule);
        Log.Info($"Asistente: «{assistant}» programa «{order}» ({rule.Summary}); aprobado: {string.Join(", ", rule.Ask.Tools.Concat(commands.Select(c => c.Name)))}"
                 + (unknown.Count > 0 ? $"; no existen: {string.Join(", ", unknown)}" : string.Empty));
        return new ScheduleDoneMessage(request.Id, true, [Entry(rule, nowLocal)], Unknown: unknown.Count > 0 ? unknown : null);
    }

    private static ScheduleDoneMessage Cancel(AppConfig config, ScheduleMessage request, DateTime nowLocal)
    {
        var wanted = request.Event?.Trim() ?? string.Empty;
        var scheduled = Scheduled(config).ToList();
        var rule = scheduled.FirstOrDefault(e => e.Id == wanted);
        if (rule is null && wanted.Length > 0)
        {
            // By its words: every word said is in its order or its name; of several, the one due first.
            var words = TextSimilarity.Words(wanted);
            rule = scheduled
                .Where(e => words.All(w => TextSimilarity.Words(e.Name + " " + e.Ask?.Text).Contains(w)))
                .OrderBy(e => Next(e, nowLocal))
                .FirstOrDefault();
        }
        // Nothing said: the next one due.
        if (rule is null && wanted.Length == 0) rule = scheduled.OrderBy(e => Next(e, nowLocal)).FirstOrDefault();
        if (rule is null) return new ScheduleDoneMessage(request.Id, false, Reason: "notFound");
        var entry = Entry(rule, nowLocal);
        config.Events.Remove(rule);
        Log.Info($"Asistente: quitada la orden programada «{rule.Name}»");
        return new ScheduleDoneMessage(request.Id, true, [entry]);
    }

    /// <summary>
    /// The user's commands named (exactly, or by one of their voice phrases, without minding case or accents), and the
    /// names that are none of them.
    /// </summary>
    public static (IReadOnlyList<Command> Found, IReadOnlyList<string> Unknown) FindCommands(AppConfig config, IEnumerable<string> names)
    {
        var available = AssistantCommands.Available(config);
        var found = new List<Command>();
        var unknown = new List<string>();
        foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            var folded = Fold(name);
            var command = available.FirstOrDefault(c => Fold(c.Name) == folded)
                          ?? available.FirstOrDefault(c => c.VoicePhrases.Any(p => Fold(p) == folded));
            if (command is null) unknown.Add(name);
            else if (!found.Contains(command)) found.Add(command);
        }
        return (found, unknown);
    }

    private static string Fold(string text) => string.Join(' ', TextSimilarity.Words(text));

    /// <summary>When it is due next, local (far in the future when never again).</summary>
    public static DateTime Next(EventRule rule, DateTime nowLocal)
    {
        if (rule.Trigger is not TimeTrigger t || EventTime.Parse(t.Time) is not { } time) return DateTime.MaxValue;
        var nowUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nowLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        var next = t.OnDate is { } date ? EventSchedule.Next(time, date, nowUtc) : EventSchedule.Next(time, t.Days, nowUtc);
        return next == DateTime.MaxValue ? DateTime.MaxValue : TimeZoneInfo.ConvertTimeFromUtc(next, TimeZoneInfo.Local);
    }

    public static ScheduledEntry Entry(EventRule rule, DateTime nowLocal)
    {
        var next = Next(rule, nowLocal);
        var repeat = rule.Trigger is TimeTrigger { HasDate: false } t
            ? t.Days switch
            {
                WeekDays.None => "daily",
                WeekDays.Workdays => "workdays",
                WeekDays.Weekend => "weekend",
                _ => "weekly",
            }
            : null;
        return new ScheduledEntry(rule.Id, rule.Name, next == DateTime.MaxValue ? "-" : next.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            rule.Ask?.Text ?? string.Empty, rule.CreatedBy, repeat);
    }

    /// <summary>
    /// «hoy», «mañana», a day of the week or a date as the model writes them (<c>today</c>, <c>tomorrow</c>, <c>friday</c>,
    /// <c>2026-10-30</c>) → the date, from <paramref name="today"/>; a day of the week is the next one (today if it is
    /// today). Null: empty or not understood.
    /// </summary>
    public static DateOnly? Day(string? text, DateOnly today)
    {
        var word = (text ?? string.Empty).Trim().ToLowerInvariant().Replace("next ", string.Empty, StringComparison.Ordinal);
        if (word.Length == 0) return null;
        if (EventTime.ParseDate(word) is { } date) return date;
        if (word == "today") return today;
        if (word == "tomorrow") return today.AddDays(1);
        if (word is "day after tomorrow" or "the day after tomorrow") return today.AddDays(2);
        if (Enum.TryParse<DayOfWeek>(word, ignoreCase: true, out var weekday) && !int.TryParse(word, out _))
            return today.AddDays(((int)weekday - (int)today.DayOfWeek + 7) % 7);
        return null;
    }
}
