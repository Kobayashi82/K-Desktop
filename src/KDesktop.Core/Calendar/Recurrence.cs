using System.Globalization;
using System.Text;

namespace KDesktop.Core.Calendar;

public enum RecurrenceFrequency { Daily, Weekly, Monthly, Yearly }

/// <summary>A day of the week in BYDAY, with its place in the month or year (2TU: the second Tuesday; -1FR: the last Friday; 0: every one).</summary>
public readonly record struct WeekdayNum(int Ordinal, DayOfWeek Day)
{
    public override string ToString() => (Ordinal == 0 ? string.Empty : Ordinal.ToString(CultureInfo.InvariantCulture)) + RecurrenceRule.DayCode(Day);
}

/// <summary>
/// The part of iCalendar's RRULE K-Desktop works out by itself (the three sources use it): every N days, weeks (some
/// days), months (day N, «the second Tuesday», the last day) or years, until a date or a number of times. BYSETPOS
/// too («the last working day»). BYHOUR, BYMINUTE… are kept in <see cref="Rest"/> and written back, but not used.
/// </summary>
public sealed record RecurrenceRule
{
    public RecurrenceFrequency Frequency { get; init; } = RecurrenceFrequency.Weekly;

    public int Interval { get; init; } = 1;

    /// <summary>How many times in all (the first one included); null: no number.</summary>
    public int? Count { get; init; }

    /// <summary>The last moment it may happen: wall-clock in the cita's zone, or UTC when <see cref="UntilUtc"/>.</summary>
    public DateTime? Until { get; init; }

    public bool UntilUtc { get; init; }

    /// <summary>UNTIL was a date (all-day citas): the whole day counts.</summary>
    public bool UntilIsDate { get; init; }

    public IReadOnlyList<WeekdayNum> ByDay { get; init; } = [];

    /// <summary>1…31, or -1 for the last day, -2 for the one before…</summary>
    public IReadOnlyList<int> ByMonthDay { get; init; } = [];

    public IReadOnlyList<int> ByMonth { get; init; } = [];

    public IReadOnlyList<int> BySetPos { get; init; } = [];

    public DayOfWeek WeekStart { get; init; } = DayOfWeek.Monday;

    /// <summary>Parts not understood («BYHOUR=9»), kept as they came.</summary>
    public IReadOnlyList<string> Rest { get; init; } = [];

    private static readonly string[] Codes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    public static string DayCode(DayOfWeek day) => Codes[(int)day];

    /// <summary>«FREQ=WEEKLY;BYDAY=MO,WE» (with or without «RRULE:»); null when it is not a rule.</summary>
    public static RecurrenceRule? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) text = text[6..];
        RecurrenceFrequency? frequency = null;
        var rule = new RecurrenceRule();
        var rest = new List<string>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            var key = part[..eq].ToUpperInvariant();
            var value = part[(eq + 1)..];
            switch (key)
            {
                case "FREQ":
                    frequency = value.ToUpperInvariant() switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        "YEARLY" => RecurrenceFrequency.Yearly,
                        _ => null,
                    };
                    if (frequency is null) return null; // HOURLY, MINUTELY…: not for citas
                    break;
                case "INTERVAL" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) && interval > 0:
                    rule = rule with { Interval = interval };
                    break;
                case "COUNT" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0:
                    rule = rule with { Count = count };
                    break;
                case "UNTIL" when IcsTime.TryParse(value, out var until, out var utc, out var isDate):
                    rule = rule with { Until = until, UntilUtc = utc, UntilIsDate = isDate };
                    break;
                case "BYDAY":
                    var days = new List<WeekdayNum>();
                    foreach (var d in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (ParseDay(d) is { } day) days.Add(day);
                    rule = rule with { ByDay = days };
                    break;
                case "BYMONTHDAY":
                    rule = rule with { ByMonthDay = Numbers(value, -31, 31) };
                    break;
                case "BYMONTH":
                    rule = rule with { ByMonth = Numbers(value, 1, 12) };
                    break;
                case "BYSETPOS":
                    rule = rule with { BySetPos = Numbers(value, -366, 366) };
                    break;
                case "WKST" when Array.IndexOf(Codes, value.ToUpperInvariant()) is var w and >= 0:
                    rule = rule with { WeekStart = (DayOfWeek)w };
                    break;
                default:
                    rest.Add(part);
                    break;
            }
        }
        return frequency is { } f ? rule with { Frequency = f, Rest = rest } : null;
    }

    private static WeekdayNum? ParseDay(string text)
    {
        if (text.Length < 2) return null;
        var code = Array.IndexOf(Codes, text[^2..].ToUpperInvariant());
        if (code < 0) return null;
        var number = text[..^2];
        if (number.Length == 0) return new WeekdayNum(0, (DayOfWeek)code);
        return int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n != 0 && Math.Abs(n) <= 53
            ? new WeekdayNum(n, (DayOfWeek)code) : null;
    }

    private static List<int> Numbers(string text, int min, int max)
    {
        var list = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n != 0 && n >= min && n <= max) list.Add(n);
        return list;
    }

    /// <summary>The RRULE text, without «RRULE:».</summary>
    public override string ToString()
    {
        var sb = new StringBuilder("FREQ=").Append(Frequency.ToString().ToUpperInvariant());
        if (Interval > 1) sb.Append(";INTERVAL=").Append(Interval.ToString(CultureInfo.InvariantCulture));
        if (Count is { } count) sb.Append(";COUNT=").Append(count.ToString(CultureInfo.InvariantCulture));
        if (Until is { } until) sb.Append(";UNTIL=").Append(UntilIsDate ? IcsTime.FormatDate(until) : IcsTime.Format(until, UntilUtc));
        if (ByDay.Count > 0) sb.Append(";BYDAY=").Append(string.Join(',', ByDay));
        if (ByMonthDay.Count > 0) sb.Append(";BYMONTHDAY=").Append(string.Join(',', ByMonthDay.Select(n => n.ToString(CultureInfo.InvariantCulture))));
        if (ByMonth.Count > 0) sb.Append(";BYMONTH=").Append(string.Join(',', ByMonth.Select(n => n.ToString(CultureInfo.InvariantCulture))));
        if (BySetPos.Count > 0) sb.Append(";BYSETPOS=").Append(string.Join(',', BySetPos.Select(n => n.ToString(CultureInfo.InvariantCulture))));
        if (WeekStart != DayOfWeek.Monday) sb.Append(";WKST=").Append(DayCode(WeekStart));
        foreach (var part in Rest) sb.Append(';').Append(part);
        return sb.ToString();
    }

    /// <summary>
    /// The wall-clock starts of the series, in order, from <paramref name="start"/> (always the first one) on, as many
    /// as <see cref="Count"/> or up to <see cref="Until"/>. <paramref name="toUtc"/> turns a wall-clock time into UTC
    /// (for an UNTIL in UTC). Endless series end when the caller stops reading.
    /// </summary>
    public IEnumerable<DateTime> Starts(DateTime start, Func<DateTime, DateTime> toUtc)
    {
        var produced = 0;
        var time = start.TimeOfDay;
        var emptyPeriods = 0;
        var first = true;
        for (var period = 0; period < 100_000; period += Interval)
        {
            var candidates = Period(start, period);
            if (candidates.Count == 0)
            {
                // «Every 30 February»: never; give up after a long while without any.
                if (++emptyPeriods > 2000) yield break;
                continue;
            }
            emptyPeriods = 0;
            foreach (var day in candidates)
            {
                var at = day.Date + time;
                if (at < start) continue;
                if (first)
                {
                    first = false;
                    // DTSTART is always the first appearance, even when it is not one of the rule's days.
                    if (at != start)
                    {
                        if (Past(start, toUtc)) yield break;
                        yield return start;
                        if (Count is { } c0 && ++produced >= c0) yield break;
                    }
                }
                if (Past(at, toUtc)) yield break;
                yield return at;
                if (Count is { } c && ++produced >= c) yield break;
            }
        }
    }

    private bool Past(DateTime at, Func<DateTime, DateTime> toUtc)
    {
        if (Until is not { } until) return false;
        if (UntilIsDate) return at.Date > until.Date;
        return UntilUtc ? toUtc(at) > until : at > until;
    }

    /// <summary>The days of the period <paramref name="index"/> (days, weeks, months or years after the start's), in order.</summary>
    private List<DateTime> Period(DateTime start, int index)
    {
        var days = new List<DateTime>();
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                var day = start.Date.AddDays(index);
                if (Matches(day, monthScope: true)) days.Add(day);
                break;
            case RecurrenceFrequency.Weekly:
                var offset = ((int)start.DayOfWeek - (int)WeekStart + 7) % 7;
                var weekStart = start.Date.AddDays(-offset + 7L * index);
                var weekly = ByDay.Count > 0 ? ByDay.Select(d => d.Day).ToHashSet() : [start.DayOfWeek];
                for (var i = 0; i < 7; i++)
                {
                    var d = weekStart.AddDays(i);
                    if (weekly.Contains(d.DayOfWeek) && (ByMonth.Count == 0 || ByMonth.Contains(d.Month))) days.Add(d);
                }
                break;
            case RecurrenceFrequency.Monthly:
                var month = new DateTime(start.Year, start.Month, 1).AddMonths(index);
                if (ByMonth.Count == 0 || ByMonth.Contains(month.Month)) days.AddRange(InMonth(month, start));
                break;
            case RecurrenceFrequency.Yearly:
                var year = start.Year + index;
                if (year > 9998) break;
                if (ByMonth.Count > 0 || ByMonthDay.Count > 0 || ByDay.Count == 0)
                {
                    IEnumerable<int> months = ByMonth.Count > 0 ? ByMonth.Order() : ByMonthDay.Count > 0 ? Enumerable.Range(1, 12) : [start.Month];
                    foreach (var m in months) days.AddRange(InMonth(new DateTime(year, m, 1), start));
                }
                else
                {
                    // «The 20th Monday of the year», «every Friday of the year»: BYDAY over the whole year.
                    days.AddRange(WeekdaysIn(new DateTime(year, 1, 1), new DateTime(year, 12, 31)));
                }
                break;
        }
        return BySetPos.Count == 0 ? days : SetPositions(days);
    }

    private List<DateTime> InMonth(DateTime month, DateTime start)
    {
        var last = DateTime.DaysInMonth(month.Year, month.Month);
        var list = new List<DateTime>();
        if (ByMonthDay.Count == 0 && ByDay.Count == 0)
        {
            // Day N of each month; a month without it (31 in April, 29 February) is skipped, as in iCalendar.
            if (start.Day <= last) list.Add(new DateTime(month.Year, month.Month, start.Day));
            return list;
        }
        IEnumerable<DateTime> days = ByDay.Count > 0
            ? WeekdaysIn(month, new DateTime(month.Year, month.Month, last))
            : Enumerable.Range(1, last).Select(d => new DateTime(month.Year, month.Month, d));
        if (ByMonthDay.Count > 0)
        {
            var wanted = ByMonthDay.Select(n => n > 0 ? n : last + n + 1).Where(n => n >= 1 && n <= last).ToHashSet();
            days = days.Where(d => wanted.Contains(d.Day));
        }
        list.AddRange(days.Distinct().Order());
        return list;
    }

    /// <summary>The days between <paramref name="from"/> and <paramref name="to"/> that BYDAY asks for (with their place inside that span).</summary>
    private IEnumerable<DateTime> WeekdaysIn(DateTime from, DateTime to)
    {
        var result = new SortedSet<DateTime>();
        foreach (var group in ByDay.GroupBy(d => d.Day))
        {
            var all = new List<DateTime>();
            var first = from.AddDays(((int)group.Key - (int)from.DayOfWeek + 7) % 7);
            for (var d = first; d <= to; d = d.AddDays(7)) all.Add(d);
            foreach (var wanted in group)
            {
                if (wanted.Ordinal == 0) result.UnionWith(all);
                else if (wanted.Ordinal > 0 && wanted.Ordinal <= all.Count) result.Add(all[wanted.Ordinal - 1]);
                else if (wanted.Ordinal < 0 && -wanted.Ordinal <= all.Count) result.Add(all[all.Count + wanted.Ordinal]);
            }
        }
        return result;
    }

    /// <summary>Whether a day passes the limits of a daily rule (BYMONTH, BYMONTHDAY, BYDAY without places).</summary>
    private bool Matches(DateTime day, bool monthScope)
    {
        if (ByMonth.Count > 0 && !ByMonth.Contains(day.Month)) return false;
        if (ByDay.Count > 0 && !ByDay.Any(d => d.Day == day.DayOfWeek)) return false;
        if (ByMonthDay.Count > 0 && monthScope)
        {
            var last = DateTime.DaysInMonth(day.Year, day.Month);
            if (!ByMonthDay.Any(n => (n > 0 ? n : last + n + 1) == day.Day)) return false;
        }
        return true;
    }

    private List<DateTime> SetPositions(List<DateTime> days)
    {
        var picked = new SortedSet<DateTime>();
        foreach (var pos in BySetPos)
        {
            var index = pos > 0 ? pos - 1 : days.Count + pos;
            if (index >= 0 && index < days.Count) picked.Add(days[index]);
        }
        return [.. picked];
    }
}

/// <summary>Turns wall-clock times of a zone into instants, the way iCalendar wants it around the change of time.</summary>
public static class CalendarTime
{
    private static readonly Dictionary<string, TimeZoneInfo?> Zones = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The zone of an id (IANA or Windows); unknown or null: the PC's.</summary>
    public static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        lock (Zones)
        {
            if (!Zones.TryGetValue(id, out var zone))
            {
                zone = Find(id);
                Zones[id] = zone;
                if (zone is null) Log.Warn($"Calendario: zona horaria desconocida «{id}»; se usa la del PC");
            }
            return zone ?? TimeZoneInfo.Local;
        }
    }

    /// <summary>Whether K-Desktop knows that zone.</summary>
    public static bool Known(string? id) => !string.IsNullOrWhiteSpace(id) && Find(id) is not null;

    private static TimeZoneInfo? Find(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) || TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out windows))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(windows); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return null;
    }

    /// <summary>The IANA name of a zone («Europe/Madrid»), for a .ics or Google; the id itself when there is none.</summary>
    public static string Iana(TimeZoneInfo zone)
    {
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
    }

    /// <summary>The Windows name of a zone («Romance Standard Time»), for Microsoft Graph.</summary>
    public static string Windows(TimeZoneInfo zone)
    {
        if (!zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out var windows) ? windows : zone.Id;
    }

    /// <summary>
    /// The instant of a wall-clock time in a zone. A time that does not exist (2:30 when the clock jumps from 2:00 to
    /// 3:00) is read with the offset from before the jump, so it lands at 3:30; one that happens twice (2:30 when it
    /// goes back) is the first.
    /// </summary>
    public static DateTimeOffset ToInstant(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(wall)) offset = zone.GetUtcOffset(wall.AddHours(-6));
        else if (zone.IsAmbiguousTime(wall)) offset = zone.GetAmbiguousTimeOffsets(wall).Max();
        else offset = zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    /// <summary>The start of a cita as an instant (all day: 0:00 of its date where the PC is).</summary>
    public static DateTimeOffset StartOf(CalendarEvent e) => ToInstant(e.Start, e.AllDay ? TimeZoneInfo.Local : Zone(e.TimeZone));

    public static DateTimeOffset EndOf(CalendarEvent e) => ToInstant(e.End > e.Start ? e.End : e.Start + e.Duration, e.AllDay ? TimeZoneInfo.Local : Zone(e.TimeZone));

    /// <summary>A real instant as wall-clock time in a zone.</summary>
    public static DateTime ToWall(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, zone).DateTime, DateTimeKind.Unspecified);
}

/// <summary>Works out the appearances of citas in a span of time: the repetitions, without the deleted ones, with the changed ones.</summary>
public static class Recurrence
{
    /// <summary>The appearances of <paramref name="e"/> that touch [<paramref name="from"/>, <paramref name="to"/>), in order.</summary>
    public static IEnumerable<CalendarOccurrence> Expand(CalendarEvent e, DateTimeOffset from, DateTimeOffset to)
    {
        if (e.Cancelled) yield break;
        var rule = RecurrenceRule.Parse(e.Recurrence);
        if (rule is null)
        {
            var start = CalendarTime.StartOf(e);
            var end = CalendarTime.EndOf(e);
            if (Touches(start, end, from, to)) yield return new CalendarOccurrence(e, e, start, end, null);
            yield break;
        }

        var zone = e.AllDay ? TimeZoneInfo.Local : CalendarTime.Zone(e.TimeZone);
        var length = e.Duration;
        var removed = new HashSet<DateTime>(e.Exceptions.Select(x => e.AllDay ? x.Date : x));
        var replaced = new HashSet<DateTime>(e.Overrides.Where(o => o.RecurrenceId is not null).Select(o => e.AllDay ? o.RecurrenceId!.Value.Date : o.RecurrenceId!.Value));
        var list = new List<CalendarOccurrence>();
        foreach (var wall in rule.Starts(e.Start, w => CalendarTime.ToInstant(w, zone).UtcDateTime))
        {
            var start = CalendarTime.ToInstant(wall, zone);
            if (start >= to) break;
            var end = CalendarTime.ToInstant(wall + length, zone);
            if (!Touches(start, end, from, to)) continue;
            var key = e.AllDay ? wall.Date : wall;
            if (removed.Contains(key) || replaced.Contains(key)) continue;
            list.Add(new CalendarOccurrence(e, e, start, end, wall));
        }
        foreach (var o in e.Overrides)
        {
            if (o.Cancelled || o.RecurrenceId is null) continue;
            var start = CalendarTime.StartOf(o);
            var end = CalendarTime.EndOf(o);
            if (Touches(start, end, from, to)) list.Add(new CalendarOccurrence(o, e, start, end, o.RecurrenceId));
        }
        foreach (var o in list.OrderBy(o => o.Start)) yield return o;
    }

    /// <summary>A cita without length (a moment) touches the span when it is inside it.</summary>
    private static bool Touches(DateTimeOffset start, DateTimeOffset end, DateTimeOffset from, DateTimeOffset to) =>
        start < to && (end > from || (end <= start && start >= from));

    /// <summary>How many appearances a series has before <paramref name="recurrenceId"/> (for «this one and the following» with COUNT).</summary>
    public static int CountBefore(CalendarEvent series, DateTime recurrenceId)
    {
        if (RecurrenceRule.Parse(series.Recurrence) is not { } rule) return 0;
        var zone = series.AllDay ? TimeZoneInfo.Local : CalendarTime.Zone(series.TimeZone);
        return rule.Starts(series.Start, w => CalendarTime.ToInstant(w, zone).UtcDateTime).TakeWhile(w => w < recurrenceId).Count();
    }
}
