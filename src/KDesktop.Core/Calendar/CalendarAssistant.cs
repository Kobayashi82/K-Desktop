using System.Globalization;
using System.Text.Json;

namespace KDesktop.Core.Calendar;

/// <summary>
/// What an assistant asks of the calendar (calendar, phase 4): <c>list</c>, <c>add</c>, <c>change</c>, <c>delete</c>,
/// <c>undo</c> or <c>calendars</c>. Every field is optional; what does not apply to <see cref="Op"/> is ignored.
/// </summary>
/// <param name="Range">list: <c>today</c>, <c>tomorrow</c>, <c>yesterday</c>, <c>this_week</c>, <c>next_week</c>,
/// <c>weekend</c>, <c>this_month</c>, <c>next_month</c>, <c>next_7_days</c>, a weekday in English, or a date.</param>
/// <param name="From">list, change, delete: <c>YYYY-MM-DD</c> (with <see cref="To"/>, a span; alone, that day).</param>
/// <param name="Text">list: words of the title, place or notes; change, delete: which cita.</param>
/// <param name="Calendar">The calendar as the user calls it («trabajo»); add: where it goes (empty: the default one).</param>
/// <param name="Key">change, delete: the key of one appearance, as <c>list</c> gave it.</param>
/// <param name="Scope">change, delete of a series: <c>this</c>, <c>following</c> or <c>all</c> (empty: this).</param>
/// <param name="Undo">undo: what a change gave back (<see cref="CalendarAnswer.Undo"/>).</param>
public sealed record CalendarRequest(string Op, string? Range = null, string? From = null, string? To = null, string? Text = null, string? Calendar = null,
    string? Key = null, string? Scope = null, CalendarCita? Cita = null, string? Undo = null);

/// <summary>A cita as the model gives it: times as <c>YYYY-MM-DD HH:MM</c> (or only the date: all day).</summary>
/// <param name="Repeat"><c>daily</c>, <c>weekdays</c>, <c>weekly</c>, <c>monthly</c>, <c>yearly</c>, <c>none</c> or an RRULE.</param>
/// <param name="Minutes">How long, when there is no end.</param>
/// <param name="Reminder">Minutes before; -1: none.</param>
public sealed record CalendarCita(string? Title = null, string? Start = null, string? End = null, bool? AllDay = null, int? Minutes = null,
    string? Location = null, string? Notes = null, string? Repeat = null, int? Reminder = null, string? Calendar = null);

/// <summary>One appearance for the model: the key to point at it, its times in the PC's zone and where it is.</summary>
public sealed record CalendarItem(string Key, string Title, string Start, string End, bool AllDay, string Calendar, bool Repeats,
    string? Location = null, string? Meeting = null, string? Notes = null);

/// <param name="Reason">When not done: <c>calendar</c> (that calendar is not there or read only), <c>range</c>,
/// <c>start</c>, <c>title</c>, <c>notFound</c>, <c>ambiguous</c> (<see cref="Items"/> are the candidates),
/// <c>readOnly</c>, <c>conflict</c>, <c>offline</c>, <c>error</c>.</param>
/// <param name="Message">The source's own words for what went wrong, in the user's language.</param>
/// <param name="Undo">What <c>undo</c> needs to put it back as it was.</param>
public sealed record CalendarAnswer(bool Ok, IReadOnlyList<CalendarItem>? Items = null, CalendarItem? Item = null, string? Reason = null,
    string? Message = null, string? Undo = null, string? From = null, string? To = null, IReadOnlyList<string>? Calendars = null);

/// <summary>Answers the assistants' requests with the <see cref="CalendarService"/> (in K-Desktop's process).</summary>
public static class CalendarAssistant
{
    /// <summary>The longest list given to the model.</summary>
    public const int MaxItems = 40;

    private static readonly string[] DateFormats = ["yyyy-M-d", "yyyy-M-d H:mm", "yyyy-M-dTH:mm", "yyyy-M-d H:mm:ss", "yyyy-M-dTH:mm:ss", "d/M/yyyy", "d/M/yyyy H:mm"];

    public static async Task<CalendarAnswer> HandleAsync(CalendarService calendar, CalendarRequest request, DateTime now, CancellationToken cancel = default)
    {
        try
        {
            return request.Op switch
            {
                "list" => List(calendar, request, now),
                "add" => await AddAsync(calendar, request, now, cancel).ConfigureAwait(false),
                "change" => await ChangeAsync(calendar, request, now, cancel).ConfigureAwait(false),
                "delete" => await DeleteAsync(calendar, request, now, cancel).ConfigureAwait(false),
                "undo" => await UndoAsync(calendar, request, cancel).ConfigureAwait(false),
                "calendars" => new CalendarAnswer(true, Calendars: [.. calendar.Calendars.Select(Describe)]),
                _ => new CalendarAnswer(false, Reason: "op"),
            };
        }
        catch (CalendarConflictException ex)
        {
            return new CalendarAnswer(false, Reason: "conflict", Message: ex.Message);
        }
        catch (CalendarException ex)
        {
            return new CalendarAnswer(false, Reason: "readOnly", Message: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new CalendarAnswer(false, Reason: "offline", Message: ex.Message);
        }
    }

    private static string Describe(CalendarInfo c) => c.ReadOnly ? c.Name + " (read only)" : c.Name;

    // ── ranges ───────────────────────────────────────────────────────────

    /// <summary>
    /// The span of days a request means (local times, end exclusive): its range word, its dates, or —looking for a cita by
    /// its words— from a month ago to a year ahead; nothing at all: the next 7 days.
    /// </summary>
    public static (DateTime From, DateTime To)? Span(CalendarRequest r, DateTime now, bool searching)
    {
        var today = now.Date;
        if (Day(r.From) is { } from)
        {
            var to = Day(r.To) is { } t && t >= from ? t.AddDays(1) : from.AddDays(1);
            return (from, to);
        }
        var range = (r.Range ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
        if (range.Length == 0) return searching ? (today.AddDays(-31), today.AddDays(366)) : (now, today.AddDays(8));
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        switch (range)
        {
            case "today": return (today, today.AddDays(1));
            case "tomorrow": return (today.AddDays(1), today.AddDays(2));
            case "yesterday": return (today.AddDays(-1), today);
            case "this_week" or "week": return (today, monday.AddDays(7));
            case "next_week": return (monday.AddDays(7), monday.AddDays(14));
            case "weekend" or "this_weekend":
                var saturday = today.DayOfWeek == DayOfWeek.Sunday ? today.AddDays(-1) : monday.AddDays(5);
                return (saturday < today ? today : saturday, saturday.AddDays(2));
            case "this_month" or "month": return (today, new DateTime(today.Year, today.Month, 1).AddMonths(1));
            case "next_month":
                var first = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                return (first, first.AddMonths(1));
            case "next_7_days" or "upcoming" or "next": return (now, today.AddDays(8));
            case "next_30_days": return (now, today.AddDays(31));
            case "past" or "last_30_days": return (today.AddDays(-30), now);
        }
        if (Enum.TryParse<DayOfWeek>(range, ignoreCase: true, out var weekday) && !int.TryParse(range, out _))
        {
            var day = today.AddDays(((int)weekday - (int)today.DayOfWeek + 7) % 7);
            return (day, day.AddDays(1));
        }
        if (Day(range) is { } date) return (date, date.AddDays(1));
        return null;
    }

    private static DateTime? Day(string? text) =>
        DateTime.TryParseExact((text ?? string.Empty).Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ? d.Date : null;

    /// <summary>«2026-11-05 17:30» → that moment, with whether it had a time.</summary>
    public static (DateTime At, bool HasTime)? Moment(string? text, DateTime now)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        if (t.Equals("now", StringComparison.OrdinalIgnoreCase)) return (now, true);
        if (TimeOnly.TryParseExact(t, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            var at = now.Date + clock.ToTimeSpan();
            return (at < now ? at.AddDays(1) : at, true);
        }
        if (!DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var moment)) return null;
        return (moment, t.Contains(':', StringComparison.Ordinal));
    }

    // ── list ─────────────────────────────────────────────────────────────

    private static CalendarAnswer List(CalendarService calendar, CalendarRequest r, DateTime now)
    {
        if (Span(r, now, searching: !string.IsNullOrWhiteSpace(r.Text)) is not { } span) return new CalendarAnswer(false, Reason: "range");
        CalendarInfo? only = null;
        if (!string.IsNullOrWhiteSpace(r.Calendar) && (only = calendar.FindCalendar(r.Calendar)) is null)
            return new CalendarAnswer(false, Reason: "calendar", Calendars: [.. calendar.Calendars.Select(Describe)]);
        var found = calendar.Search(r.Text, Local(span.From), Local(span.To), only);
        return new CalendarAnswer(true, [.. found.Take(MaxItems).Select(o => Item(calendar, o))],
            From: span.From.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), To: span.To.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset Local(DateTime at) => CalendarTime.ToInstant(at, TimeZoneInfo.Local);

    public static CalendarItem Item(CalendarService calendar, CalendarOccurrence o)
    {
        string Time(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return new CalendarItem(o.Key, o.Event.Title,
            o.AllDay ? o.Event.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Time(o.Start),
            o.AllDay ? o.Event.End.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Time(o.End),
            o.AllDay, calendar.CalendarOf(o.Series)?.Name ?? string.Empty, o.Series.IsRecurring,
            o.Event.Location, o.Event.MeetingUrl, o.Event.Notes is { Length: > 300 } notes ? notes[..300] + "…" : o.Event.Notes);
    }

    // ── add ──────────────────────────────────────────────────────────────

    private static async Task<CalendarAnswer> AddAsync(CalendarService calendar, CalendarRequest r, DateTime now, CancellationToken cancel)
    {
        var cita = r.Cita ?? new CalendarCita();
        if (string.IsNullOrWhiteSpace(cita.Title)) return new CalendarAnswer(false, Reason: "title");
        var target = string.IsNullOrWhiteSpace(cita.Calendar ?? r.Calendar) ? calendar.DefaultCalendar : calendar.FindCalendar(cita.Calendar ?? r.Calendar, writable: true);
        if (target is null) return new CalendarAnswer(false, Reason: "calendar", Calendars: [.. calendar.Calendars.Where(c => !c.ReadOnly).Select(c => c.Name)]);
        if (Build(new CalendarEvent { SourceId = target.SourceId, CalendarId = target.Id }, cita, now, isNew: true) is not { } e)
            return new CalendarAnswer(false, Reason: "start");
        var change = await calendar.AddAsync(e, cancel).ConfigureAwait(false);
        var added = change.Result!;
        var first = Recurrence.Expand(added, CalendarTime.StartOf(added).AddMinutes(-1), DateTimeOffset.MaxValue.AddYears(-1)).FirstOrDefault();
        return new CalendarAnswer(true, Item: first is null ? null : Item(calendar, first), Undo: UndoOf(change));
    }

    /// <summary>
    /// <paramref name="e"/> with what <paramref name="cita"/> says (what it does not say stays as it was). Null when the
    /// start cannot be read. A new one without an end lasts an hour (all day: that day).
    /// </summary>
    public static CalendarEvent? Build(CalendarEvent e, CalendarCita cita, DateTime now, bool isNew)
    {
        if (!string.IsNullOrWhiteSpace(cita.Title)) e = e with { Title = cita.Title.Trim() };
        var length = isNew ? TimeSpan.FromHours(1) : e.Duration;
        if (cita.Start is { } startText)
        {
            if (Moment(startText, now) is not { } start) return null;
            var allDay = cita.AllDay ?? !start.HasTime;
            if (allDay)
            {
                var days = Math.Max(1, Moment(cita.End, now) is { } end ? (end.At.Date - start.At.Date).Days + 1 : e.AllDay && !isNew ? (int)Math.Round(e.Duration.TotalDays) : 1);
                e = e with { AllDay = true, Start = start.At.Date, End = start.At.Date.AddDays(days), TimeZone = null };
            }
            else
            {
                var zone = e.AllDay || isNew || e.TimeZone is null ? CalendarTime.Iana(TimeZoneInfo.Local) : e.TimeZone;
                // The model says local times: in the cita's own zone when it has another.
                var wall = zone == CalendarTime.Iana(TimeZoneInfo.Local) ? start.At : CalendarTime.ToWall(CalendarTime.ToInstant(start.At, TimeZoneInfo.Local), CalendarTime.Zone(zone));
                var span = cita.Minutes is > 0 and var m ? TimeSpan.FromMinutes(m) : length > TimeSpan.Zero && !e.AllDay ? length : TimeSpan.FromHours(1);
                var endWall = wall + span;
                if (Moment(cita.End, now) is { HasTime: true } end && end.At > start.At)
                    endWall = wall + (end.At - start.At);
                e = e with { AllDay = false, Start = wall, End = endWall, TimeZone = zone };
            }
        }
        else if (isNew) return null;
        else if (cita.Minutes is > 0 and var minutes && !e.AllDay) e = e with { End = e.Start.AddMinutes(minutes) };
        else if (Moment(cita.End, now) is { HasTime: true } end && !e.AllDay)
        {
            var endWall = e.TimeZone is { } z ? CalendarTime.ToWall(CalendarTime.ToInstant(end.At, TimeZoneInfo.Local), CalendarTime.Zone(z)) : end.At;
            if (endWall > e.Start) e = e with { End = endWall };
        }
        if (cita.Location is { } place) e = e with { Location = place.Trim().Length == 0 ? null : place.Trim() };
        if (cita.Notes is { } notes) e = e with { Notes = notes.Trim().Length == 0 ? null : notes.Trim() };
        if (cita.Reminder is { } reminder) e = e with { Reminders = reminder < 0 ? [] : [Math.Min(reminder, 40320)] };
        if (cita.Repeat is { } repeat) e = e with { Recurrence = Rule(repeat, e.Start) };
        if (cita.Location is not null || cita.Notes is not null || isNew) e = e with { MeetingUrl = MeetingLinks.Find(e.Location, e.Notes) ?? (isNew ? null : e.MeetingUrl) };
        return e;
    }

    /// <summary>«weekly», «weekdays», «none», an RRULE… → the RRULE (null: it does not repeat).</summary>
    public static string? Rule(string repeat, DateTime start)
    {
        var r = repeat.Trim();
        return r.ToLowerInvariant() switch
        {
            "" or "none" or "no" or "never" => null,
            "daily" or "every_day" => "FREQ=DAILY",
            "weekdays" or "workdays" => "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
            "weekly" or "every_week" => "FREQ=WEEKLY;BYDAY=" + RecurrenceRule.DayCode(start.DayOfWeek),
            "biweekly" or "every_two_weeks" => "FREQ=WEEKLY;INTERVAL=2;BYDAY=" + RecurrenceRule.DayCode(start.DayOfWeek),
            "monthly" or "every_month" => "FREQ=MONTHLY",
            "yearly" or "annually" or "every_year" => "FREQ=YEARLY",
            _ => RecurrenceRule.Parse(r)?.ToString(),
        };
    }

    // ── change and delete ────────────────────────────────────────────────

    /// <summary>
    /// The appearance a request points at: by its key, or by its words (and day, if said) among the coming ones. Several
    /// different citas: <c>ambiguous</c> with them (the same series counts once, its next appearance).
    /// </summary>
    private static (CalendarOccurrence? Found, CalendarAnswer? Problem) Find(CalendarService calendar, CalendarRequest r, DateTime now)
    {
        if (Span(r with { Range = r.From is null ? null : r.Range }, now, searching: true) is not { } span) return (null, new CalendarAnswer(false, Reason: "range"));
        var all = calendar.Occurrences(Local(span.From), Local(span.To));
        if (!string.IsNullOrWhiteSpace(r.Key))
        {
            var byKey = all.FirstOrDefault(o => o.Key == r.Key) ?? calendar.Occurrences(Local(now.AddYears(-2)), Local(now.AddYears(3))).FirstOrDefault(o => o.Key == r.Key);
            return byKey is null ? (null, new CalendarAnswer(false, Reason: "notFound")) : (byKey, null);
        }
        if (string.IsNullOrWhiteSpace(r.Text)) return (null, new CalendarAnswer(false, Reason: "notFound"));
        CalendarInfo? only = string.IsNullOrWhiteSpace(r.Calendar) ? null : calendar.FindCalendar(r.Calendar);
        var found = calendar.Search(r.Text, Local(span.From), Local(span.To), only);
        // The ones still to come first; a series once.
        var distinct = found.OrderBy(o => o.End < Local(now)).ThenBy(o => o.Start)
            .GroupBy(o => o.Series.SourceId + "/" + o.Series.Id).Select(g => g.First()).ToList();
        if (distinct.Count == 0) return (null, new CalendarAnswer(false, Reason: "notFound"));
        if (distinct.Count > 1)
        {
            var exact = distinct.Where(o => CalendarService.Fold(o.Event.Title) == CalendarService.Fold(r.Text)).ToList();
            if (exact.Count == 1) return (exact[0], null);
            return (null, new CalendarAnswer(false, [.. distinct.Take(10).Select(o => Item(calendar, o))], Reason: "ambiguous"));
        }
        return (distinct[0], null);
    }

    private static EditScope Scope(string? scope) => (scope ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "all" or "series" or "every" => EditScope.All,
        "following" or "this_and_following" or "future" => EditScope.ThisAndFollowing,
        _ => EditScope.This,
    };

    private static async Task<CalendarAnswer> ChangeAsync(CalendarService calendar, CalendarRequest r, DateTime now, CancellationToken cancel)
    {
        var (o, problem) = Find(calendar, r, now);
        if (o is null) return problem!;
        var cita = r.Cita ?? new CalendarCita();
        var changed = Build(o.Appearance, cita, now, isNew: false);
        if (changed is null) return new CalendarAnswer(false, Reason: "start");
        var scope = Scope(r.Scope);
        if (cita.Repeat is not null && scope == EditScope.This) scope = EditScope.All;
        if (!string.IsNullOrWhiteSpace(cita.Calendar))
        {
            if (calendar.FindCalendar(cita.Calendar, writable: true) is not { } target)
                return new CalendarAnswer(false, Reason: "calendar", Calendars: [.. calendar.Calendars.Where(c => !c.ReadOnly).Select(c => c.Name)]);
            changed = changed with { SourceId = target.SourceId, CalendarId = target.Id };
            if (o.Series.IsRecurring) scope = EditScope.All;
        }
        var change = await calendar.ChangeAsync(o, changed, scope, cancel).ConfigureAwait(false);
        var result = change.Result;
        CalendarItem? item = null;
        if (result is not null)
        {
            var near = calendar.Occurrences(Local(now.AddYears(-1)), Local(now.AddYears(2)))
                .Where(x => x.Series.Id == (result.RecurrenceId is null ? result.Id : o.Series.Id))
                .OrderBy(x => Math.Abs((x.Start - CalendarTime.ToInstant(changed.Start, CalendarTime.Zone(changed.TimeZone))).Ticks)).FirstOrDefault();
            if (near is not null) item = Item(calendar, near);
        }
        return new CalendarAnswer(true, Item: item, Undo: UndoOf(change));
    }

    private static async Task<CalendarAnswer> DeleteAsync(CalendarService calendar, CalendarRequest r, DateTime now, CancellationToken cancel)
    {
        var (o, problem) = Find(calendar, r, now);
        if (o is null) return problem!;
        var item = Item(calendar, o);
        var change = await calendar.DeleteAsync(o, Scope(r.Scope), cancel).ConfigureAwait(false);
        return new CalendarAnswer(true, Item: item, Undo: UndoOf(change));
    }

    private static async Task<CalendarAnswer> UndoAsync(CalendarService calendar, CalendarRequest r, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(r.Undo)) return new CalendarAnswer(false, Reason: "notFound");
        CalendarChange? change;
        try { change = JsonSerializer.Deserialize<CalendarChange>(r.Undo, CalendarJson.Options); }
        catch (JsonException) { return new CalendarAnswer(false, Reason: "notFound"); }
        if (change is null) return new CalendarAnswer(false, Reason: "notFound");
        await calendar.RevertAsync(change, cancel).ConfigureAwait(false);
        return new CalendarAnswer(true);
    }

    private static string UndoOf(CalendarChange change) => JsonSerializer.Serialize(change, CalendarJson.Options);
}
