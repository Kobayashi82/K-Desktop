using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KDesktop.Core.Calendar;

/// <summary>Times as iCalendar writes them: «20261030T090000», «20261030T080000Z», «20261030».</summary>
public static class IcsTime
{
    /// <summary>Reads a DATE or DATE-TIME. <paramref name="utc"/>: it ended in Z; <paramref name="isDate"/>: no time.</summary>
    public static bool TryParse(string text, out DateTime value, out bool utc, out bool isDate)
    {
        text = text.Trim();
        utc = text.EndsWith('Z') || text.EndsWith('z');
        if (utc) text = text[..^1];
        isDate = text.Length == 8;
        var ok = isDate
            ? DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
            : DateTime.TryParseExact(text, ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return ok;
    }

    public static string Format(DateTime value, bool utc) =>
        value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture) + (utc ? "Z" : string.Empty);

    public static string FormatDate(DateTime value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>«-PT10M», «P1D», «PT1H30M» → time span (negative: before).</summary>
    public static TimeSpan? ParseDuration(string text)
    {
        var m = Regex.Match(text.Trim(), @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success) return null;
        int N(int g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(N(2) * 7 + N(3), N(4), N(5), N(6));
        return m.Groups[1].Value == "-" ? -span : span;
    }

    public static string FormatDuration(TimeSpan span)
    {
        var sb = new StringBuilder(span < TimeSpan.Zero ? "-P" : "P");
        span = span.Duration();
        if (span.Days > 0) sb.Append(span.Days.ToString(CultureInfo.InvariantCulture)).Append('D');
        if (span.Hours > 0 || span.Minutes > 0 || span.Seconds > 0 || span.Days == 0)
        {
            sb.Append('T');
            if (span.Hours > 0) sb.Append(span.Hours.ToString(CultureInfo.InvariantCulture)).Append('H');
            if (span.Minutes > 0 || (span.Hours == 0 && span.Seconds == 0)) sb.Append(span.Minutes.ToString(CultureInfo.InvariantCulture)).Append('M');
            if (span.Seconds > 0) sb.Append(span.Seconds.ToString(CultureInfo.InvariantCulture)).Append('S');
        }
        return sb.ToString();
    }
}

/// <summary>The link of an online meeting in a text (Teams, Meet, Zoom, Webex…).</summary>
public static partial class MeetingLinks
{
    [GeneratedRegex(@"https://(?:[\w-]+\.)*(?:teams\.microsoft\.com|teams\.live\.com|meet\.google\.com|zoom\.us|zoomgov\.com|webex\.com|whereby\.com|meet\.jit\.si|gotomeet\.me|chime\.aws)/[^\s<>""')\]]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    public static string? Find(params string?[] texts)
    {
        foreach (var text in texts)
            if (!string.IsNullOrEmpty(text) && Link().Match(text) is { Success: true } m) return m.Value.TrimEnd('.', ',', ';');
        return null;
    }
}

/// <summary>
/// Reads and writes iCalendar (.ics, RFC 5545): the citas of a file or a subscription, and the own calendars exported.
/// What K-Desktop does not use (attendees, categories…) travels in <see cref="CalendarEvent.Extra"/> and is written back.
/// </summary>
public static partial class IcsFormat
{
    /// <summary>What a .ics holds.</summary>
    public sealed record IcsCalendar(string? Name, string? Color, IReadOnlyList<CalendarEvent> Events);

    private sealed record Property(string Name, Dictionary<string, string> Parameters, string Value, string Raw)
    {
        public string? Param(string name) => Parameters.TryGetValue(name, out var v) ? v : null;
    }

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "UID", "SUMMARY", "DTSTART", "DTEND", "DURATION", "LOCATION", "DESCRIPTION", "RRULE", "EXDATE", "RECURRENCE-ID",
        "LAST-MODIFIED", "DTSTAMP", "URL", "X-GOOGLE-CONFERENCE", "X-MICROSOFT-SKYPETEAMSMEETINGURL", "X-MICROSOFT-ONLINEMEETINGCONFLINK",
    };

    /// <summary>Reads a .ics. The citas come with their overrides inside their series; never throws on a bad line (it is skipped).</summary>
    public static IcsCalendar Read(string text, string sourceId = "", string calendarId = "")
    {
        var lines = Unfold(text);
        string? name = null, color = null;
        var events = new List<CalendarEvent>();
        var zones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<Property>? current = null;
        string? zoneId = null;
        var depth = new Stack<string>();
        foreach (var line in lines)
        {
            if (ParseLine(line) is not { } p) continue;
            if (p.Name.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                var what = p.Value.Trim().ToUpperInvariant();
                depth.Push(what);
                if (what == "VEVENT" && depth.Count == 2) current = [];
                continue;
            }
            if (p.Name.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                var what = depth.Count > 0 ? depth.Pop() : string.Empty;
                if (what == "VEVENT" && current is not null && depth.Count == 1)
                {
                    try
                    {
                        if (ToEvent(current, zones, sourceId, calendarId) is { } e) events.Add(e);
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                    {
                        Log.Warn($"Calendario: cita del .ics no válida: {ex.Message}");
                    }
                    current = null;
                }
                if (what == "VTIMEZONE") zoneId = null;
                continue;
            }
            var top = depth.Count > 0 ? depth.Peek() : string.Empty;
            if (top == "VCALENDAR")
            {
                if (p.Name.Equals("X-WR-CALNAME", StringComparison.OrdinalIgnoreCase)) name = Unescape(p.Value);
                else if (p.Name.Equals("X-APPLE-CALENDAR-COLOR", StringComparison.OrdinalIgnoreCase)) color = p.Value.Trim();
            }
            else if (top == "VTIMEZONE")
            {
                if (p.Name.Equals("TZID", StringComparison.OrdinalIgnoreCase)) zoneId = p.Value.Trim();
                else if (zoneId is not null && p.Name.Equals("X-LIC-LOCATION", StringComparison.OrdinalIgnoreCase)) zones[zoneId] = p.Value.Trim();
            }
            else if (top == "VEVENT" && current is not null) current.Add(p);
            else if (top == "VALARM" && current is not null && p.Name.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase)) current.Add(p with { Name = "X-KD-ALARM" });
        }
        return new IcsCalendar(name, color, Group(events));
    }

    /// <summary>The overrides (same UID, with RECURRENCE-ID) go inside their series; one without its series stays alone.</summary>
    private static List<CalendarEvent> Group(List<CalendarEvent> events)
    {
        var masters = new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
        var result = new List<CalendarEvent>();
        foreach (var e in events.Where(e => e.RecurrenceId is null))
        {
            if (masters.ContainsKey(e.Uid)) continue; // the same UID twice: the first wins
            masters[e.Uid] = e;
            result.Add(e);
        }
        foreach (var o in events.Where(e => e.RecurrenceId is not null))
        {
            if (masters.TryGetValue(o.Uid, out var master))
            {
                var updated = master with { Overrides = [.. master.Overrides.Where(x => x.RecurrenceId != o.RecurrenceId), o] };
                masters[o.Uid] = updated;
                result[result.IndexOf(master)] = updated;
            }
            else if (!o.Cancelled) result.Add(o with { RecurrenceId = null, Id = o.Uid + "_" + IcsTime.Format(o.RecurrenceId!.Value, false) });
        }
        return result;
    }

    private static CalendarEvent? ToEvent(List<Property> props, Dictionary<string, string> zones, string sourceId, string calendarId)
    {
        Property? Get(string n) => props.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        if (Get("DTSTART") is not { } dtStart) return null;
        if (!ReadTime(dtStart, zones, out var start, out var zone, out var allDay)) return null;

        DateTime end;
        if (Get("DTEND") is { } dtEnd && ReadTime(dtEnd, zones, out var e, out var endZone, out _))
            end = endZone == zone || allDay ? e : CalendarTime.ToWall(CalendarTime.ToInstant(e, CalendarTime.Zone(endZone)), CalendarTime.Zone(zone));
        else if (Get("DURATION") is { } dur && IcsTime.ParseDuration(dur.Value) is { } span)
            end = start + span;
        else
            end = allDay ? start.AddDays(1) : start;

        var uid = Get("UID")?.Value.Trim();
        if (string.IsNullOrEmpty(uid)) uid = CalendarEvent.NewUid();

        DateTime? recurrenceId = null;
        if (Get("RECURRENCE-ID") is { } rid && ReadTime(rid, zones, out var r, out var rZone, out _))
            recurrenceId = rZone == zone || allDay ? r : CalendarTime.ToWall(CalendarTime.ToInstant(r, CalendarTime.Zone(rZone)), CalendarTime.Zone(zone));

        var exceptions = new List<DateTime>();
        foreach (var ex in props.Where(p => p.Name.Equals("EXDATE", StringComparison.OrdinalIgnoreCase)))
            foreach (var value in ex.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!ReadTime(ex with { Value = value }, zones, out var x, out var xZone, out _)) continue;
                exceptions.Add(xZone == zone || allDay ? x : CalendarTime.ToWall(CalendarTime.ToInstant(x, CalendarTime.Zone(xZone)), CalendarTime.Zone(zone)));
            }

        var reminders = new List<int>();
        foreach (var alarm in props.Where(p => p.Name == "X-KD-ALARM"))
        {
            if (alarm.Param("VALUE") is "DATE-TIME" || !string.Equals(alarm.Param("RELATED") ?? "START", "START", StringComparison.OrdinalIgnoreCase)) continue;
            if (IcsTime.ParseDuration(alarm.Value) is { } before && before <= TimeSpan.Zero) reminders.Add((int)Math.Round(-before.TotalMinutes));
        }

        var updatedProp = Get("LAST-MODIFIED") ?? Get("DTSTAMP");
        var updated = updatedProp is not null && IcsTime.TryParse(updatedProp.Value, out var u, out var uUtc, out _)
            ? new DateTimeOffset(DateTime.SpecifyKind(u, uUtc ? DateTimeKind.Utc : DateTimeKind.Unspecified), uUtc ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(u))
            : default;

        var title = Unescape(Get("SUMMARY")?.Value ?? string.Empty);
        var location = Get("LOCATION") is { } loc ? Unescape(loc.Value) : null;
        var notes = Get("DESCRIPTION") is { } desc ? Unescape(desc.Value) : null;
        var meeting = Get("X-GOOGLE-CONFERENCE")?.Value ?? Get("X-MICROSOFT-SKYPETEAMSMEETINGURL")?.Value
            ?? MeetingLinks.Find(Get("URL")?.Value, location, notes);
        var status = Get("STATUS")?.Value.Trim();
        var rrule = Get("RRULE")?.Value.Trim();

        return new CalendarEvent
        {
            Id = uid,
            Uid = uid,
            SourceId = sourceId,
            CalendarId = calendarId,
            Title = title,
            Start = start,
            End = end,
            AllDay = allDay,
            TimeZone = zone,
            Location = string.IsNullOrWhiteSpace(location) ? null : location,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            MeetingUrl = meeting,
            Recurrence = recurrenceId is null && RecurrenceRule.Parse(rrule) is not null ? rrule : null,
            Exceptions = exceptions,
            RecurrenceId = recurrenceId,
            Cancelled = string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase),
            Reminders = [.. reminders.Distinct().Order()],
            Updated = updated,
            Extra = [.. props.Where(p => !Known.Contains(p.Name) && p.Name != "X-KD-ALARM").Select(p => p.Raw)],
        };
    }

    /// <summary>A DTSTART-like property: its wall-clock time and zone (null: floating; «UTC» for Z).</summary>
    private static bool ReadTime(Property p, Dictionary<string, string> zones, out DateTime value, out string? zone, out bool isDate)
    {
        zone = null;
        if (!IcsTime.TryParse(p.Value, out value, out var utc, out isDate)) return false;
        if (string.Equals(p.Param("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase)) isDate = true;
        if (isDate)
        {
            value = value.Date;
            return true;
        }
        if (utc) zone = "UTC";
        else if (p.Param("TZID") is { } tzid) zone = ZoneId(tzid.Trim('"'), zones);
        return true;
    }

    [GeneratedRegex(@"[A-Za-z]+/[A-Za-z_\-]+(?:/[A-Za-z_\-]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex IanaInside();

    /// <summary>A TZID as K-Desktop knows it: as it is, the VTIMEZONE's X-LIC-LOCATION, or the IANA name inside it («/mozilla.org/…/Europe/Madrid»).</summary>
    private static string? ZoneId(string tzid, Dictionary<string, string> zones)
    {
        if (CalendarTime.Known(tzid)) return tzid;
        if (zones.TryGetValue(tzid, out var location) && CalendarTime.Known(location)) return location;
        foreach (Match m in IanaInside().Matches(tzid))
            if (CalendarTime.Known(m.Value)) return m.Value;
        Log.Warn($"Calendario: zona «{tzid}» del .ics desconocida; se usa la del PC");
        return null;
    }

    private static List<string> Unfold(string text)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                sb.Append(raw, 1, raw.Length - 1);
                continue;
            }
            if (sb.Length > 0) lines.Add(sb.ToString());
            sb.Clear().Append(raw);
        }
        if (sb.Length > 0) lines.Add(sb.ToString());
        return lines;
    }

    /// <summary>«DTSTART;TZID="Europe/Madrid":20261030T090000» → name, parameters and value.</summary>
    private static Property? ParseLine(string line)
    {
        var inQuotes = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == ':' && !inQuotes) { colon = i; break; }
        }
        if (colon <= 0) return null;
        var head = line[..colon];
        var parts = new List<string>();
        var sb = new StringBuilder();
        inQuotes = false;
        foreach (var c in head)
        {
            if (c == '"') inQuotes = !inQuotes;
            if (c == ';' && !inQuotes) { parts.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        parts.Add(sb.ToString());
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts.Skip(1))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0) parameters[part[..eq].Trim()] = part[(eq + 1)..].Trim().Trim('"');
        }
        return new Property(parts[0].Trim().ToUpperInvariant(), parameters, line[(colon + 1)..], line);
    }

    public static string Unescape(string value)
    {
        if (value.IndexOf('\\', StringComparison.Ordinal) < 0) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch { 'n' or 'N' => '\n', _ => next });
            }
            else sb.Append(value[i]);
        }
        return sb.ToString();
    }

    public static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal).Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    // ── writing ──────────────────────────────────────────────────────────

    /// <summary>A .ics with these citas (series with their overrides, each as its own VEVENT).</summary>
    public static string Write(IEnumerable<CalendarEvent> events, string? name = null, string? color = null)
    {
        var list = events.ToList();
        var w = new IcsWriter();
        w.Line("BEGIN:VCALENDAR");
        w.Line("VERSION:2.0");
        w.Line("PRODID:-//Kobayashi82//K-Desktop//ES");
        w.Line("CALSCALE:GREGORIAN");
        if (!string.IsNullOrWhiteSpace(name)) w.Line("X-WR-CALNAME:" + Escape(name));
        if (!string.IsNullOrWhiteSpace(color)) w.Line("X-APPLE-CALENDAR-COLOR:" + color);
        var zones = list.SelectMany(e => e.Overrides.Prepend(e)).Where(e => !e.AllDay && e.TimeZone is not null && e.TimeZone != "UTC")
            .Select(e => CalendarTime.Iana(CalendarTime.Zone(e.TimeZone))).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in zones) WriteZone(w, zone);
        var stamp = IcsTime.Format(DateTime.UtcNow, true);
        foreach (var e in list)
        {
            WriteEvent(w, e, null, stamp);
            foreach (var o in e.Overrides) WriteEvent(w, o, e, stamp);
        }
        w.Line("END:VCALENDAR");
        return w.ToString();
    }

    private static void WriteEvent(IcsWriter w, CalendarEvent e, CalendarEvent? series, string stamp)
    {
        var zoneOf = series ?? e;
        w.Line("BEGIN:VEVENT");
        w.Line("UID:" + (series?.Uid ?? e.Uid));
        w.Line("DTSTAMP:" + stamp);
        if (e.Updated != default) w.Line("LAST-MODIFIED:" + IcsTime.Format(e.Updated.UtcDateTime, true));
        w.Line(TimeLine("DTSTART", e.Start, e.AllDay, e.TimeZone));
        w.Line(TimeLine("DTEND", e.End > e.Start ? e.End : e.Start + e.Duration, e.AllDay, e.TimeZone));
        if (e.RecurrenceId is { } rid) w.Line(TimeLine("RECURRENCE-ID", rid, zoneOf.AllDay, zoneOf.TimeZone));
        if (series is null && e.IsRecurring) w.Line("RRULE:" + e.Recurrence);
        if (series is null && e.Exceptions.Count > 0)
            w.Line(TimeLine("EXDATE", e.Exceptions[0], e.AllDay, e.TimeZone, e.Exceptions.Skip(1)));
        w.Line("SUMMARY:" + Escape(e.Title));
        if (!string.IsNullOrEmpty(e.Location)) w.Line("LOCATION:" + Escape(e.Location));
        if (!string.IsNullOrEmpty(e.Notes)) w.Line("DESCRIPTION:" + Escape(e.Notes));
        if (!string.IsNullOrEmpty(e.MeetingUrl)) w.Line("URL:" + e.MeetingUrl);
        if (e.Cancelled) w.Line("STATUS:CANCELLED");
        foreach (var extra in e.Extra)
            if (!(e.Cancelled && extra.StartsWith("STATUS", StringComparison.OrdinalIgnoreCase))) w.Line(extra);
        foreach (var minutes in e.Reminders)
        {
            w.Line("BEGIN:VALARM");
            w.Line("ACTION:DISPLAY");
            w.Line("DESCRIPTION:" + Escape(e.Title));
            w.Line("TRIGGER:" + IcsTime.FormatDuration(TimeSpan.FromMinutes(-minutes)));
            w.Line("END:VALARM");
        }
        w.Line("END:VEVENT");
    }

    private static string TimeLine(string name, DateTime value, bool allDay, string? zone, IEnumerable<DateTime>? more = null)
    {
        var values = more is null ? [value] : more.Prepend(value).ToList();
        if (allDay) return name + ";VALUE=DATE:" + string.Join(',', values.Select(IcsTime.FormatDate));
        if (zone == "UTC") return name + ":" + string.Join(',', values.Select(v => IcsTime.Format(v, true)));
        if (zone is null) return name + ":" + string.Join(',', values.Select(v => IcsTime.Format(v, false)));
        return name + ";TZID=" + CalendarTime.Iana(CalendarTime.Zone(zone)) + ":" + string.Join(',', values.Select(v => IcsTime.Format(v, false)));
    }

    /// <summary>A VTIMEZONE with the zone's current rule (enough for the clients that do not know the IANA names).</summary>
    private static void WriteZone(IcsWriter w, string iana)
    {
        var zone = CalendarTime.Zone(iana);
        var year = DateTime.Now.Year;
        var rule = zone.GetAdjustmentRules().FirstOrDefault(r => r.DateStart.Year <= year && r.DateEnd.Year >= year);
        var standard = zone.BaseUtcOffset;
        w.Line("BEGIN:VTIMEZONE");
        w.Line("TZID:" + iana);
        w.Line("X-LIC-LOCATION:" + iana);
        if (rule is null || rule.DaylightDelta == TimeSpan.Zero)
        {
            w.Line("BEGIN:STANDARD");
            w.Line("DTSTART:19700101T000000");
            w.Line("TZOFFSETFROM:" + Offset(standard));
            w.Line("TZOFFSETTO:" + Offset(standard));
            w.Line("END:STANDARD");
        }
        else
        {
            var daylight = standard + rule.DaylightDelta;
            ZonePart(w, "DAYLIGHT", rule.DaylightTransitionStart, standard, daylight);
            ZonePart(w, "STANDARD", rule.DaylightTransitionEnd, daylight, standard);
        }
        w.Line("END:VTIMEZONE");
    }

    private static void ZonePart(IcsWriter w, string kind, TimeZoneInfo.TransitionTime t, TimeSpan from, TimeSpan to)
    {
        w.Line("BEGIN:" + kind);
        var time = t.TimeOfDay.TimeOfDay;
        if (t.IsFixedDateRule)
        {
            w.Line("DTSTART:" + IcsTime.Format(new DateTime(1970, t.Month, t.Day) + time, false));
            w.Line($"RRULE:FREQ=YEARLY;BYMONTH={t.Month};BYMONTHDAY={t.Day}");
        }
        else
        {
            var week = t.Week == 5 ? -1 : t.Week;
            var first = new DateTime(1970, t.Month, 1);
            var day = first.AddDays(((int)t.DayOfWeek - (int)first.DayOfWeek + 7) % 7);
            day = week > 0 ? day.AddDays(7 * (week - 1)) : Enumerable.Range(0, 5).Select(i => day.AddDays(7 * i)).Last(d => d.Month == t.Month);
            w.Line("DTSTART:" + IcsTime.Format(day + time, false));
            w.Line(string.Create(CultureInfo.InvariantCulture, $"RRULE:FREQ=YEARLY;BYMONTH={t.Month};BYDAY={week}{RecurrenceRule.DayCode(t.DayOfWeek)}"));
        }
        w.Line("TZOFFSETFROM:" + Offset(from));
        w.Line("TZOFFSETTO:" + Offset(to));
        w.Line("END:" + kind);
    }

    private static string Offset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString("hhmm", CultureInfo.InvariantCulture);

    /// <summary>Lines of at most 75 bytes (RFC 5545), the rest folded with a space, ended in CRLF.</summary>
    private sealed class IcsWriter
    {
        private readonly StringBuilder _sb = new();

        public void Line(string line)
        {
            var bytes = 0;
            var start = 0;
            for (var i = 0; i < line.Length; i++)
            {
                var size = char.IsHighSurrogate(line[i]) ? 4 : char.IsLowSurrogate(line[i]) ? 0 : Encoding.UTF8.GetByteCount(line[i].ToString());
                if (bytes + size > (start == 0 ? 75 : 74) && !char.IsLowSurrogate(line[i]))
                {
                    _sb.Append(line, start, i - start).Append("\r\n ");
                    start = i;
                    bytes = 0;
                }
                bytes += size;
            }
            _sb.Append(line, start, line.Length - start).Append("\r\n");
        }

        public override string ToString() => _sb.ToString();
    }
}
