using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Accounts;

namespace KDesktop.Core.Calendar;

/// <summary>
/// The repetitions of Microsoft Graph (<c>patternedRecurrence</c>) as iCalendar's RRULE and back: daily, weekly with its
/// days, monthly by day N or «the second Tuesday», yearly; until a date, N times or never. What Outlook cannot keep
/// (BYSETPOS, several days of the month…) is said instead of changed.
/// </summary>
public static class GraphRecurrence
{
    private static readonly string[] Indexes = ["first", "second", "third", "fourth", "last"];

    public static string DayName(DayOfWeek day) => day.ToString().ToLowerInvariant();

    /// <summary>Graph → RRULE (null: it does not repeat, or it cannot be read).</summary>
    public static string? ToRule(JsonElement recurrence, DateTime start)
    {
        if (recurrence.ValueKind != JsonValueKind.Object || !recurrence.TryGetProperty("pattern", out var pattern) || pattern.ValueKind != JsonValueKind.Object) return null;
        string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
        var days = pattern.TryGetProperty("daysOfWeek", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().Select(x => Enum.TryParse<DayOfWeek>(x.GetString(), true, out var w) ? (DayOfWeek?)w : null).OfType<DayOfWeek>().ToList()
            : [];
        var index = Array.IndexOf(Indexes, Str(pattern, "index") ?? "first");
        var ordinal = index == 4 ? -1 : index + 1;
        var interval = Math.Max(1, Int(pattern, "interval"));
        RecurrenceRule rule = Str(pattern, "type") switch
        {
            "daily" => new() { Frequency = RecurrenceFrequency.Daily, Interval = interval },
            "weekly" => new() { Frequency = RecurrenceFrequency.Weekly, Interval = interval, ByDay = [.. days.Select(x => new WeekdayNum(0, x))],
                WeekStart = Enum.TryParse<DayOfWeek>(Str(pattern, "firstDayOfWeek"), true, out var w0) ? w0 : DayOfWeek.Sunday },
            "absoluteMonthly" => new() { Frequency = RecurrenceFrequency.Monthly, Interval = interval, ByMonthDay = [Int(pattern, "dayOfMonth") is > 0 and var dm ? dm : start.Day] },
            "relativeMonthly" => new() { Frequency = RecurrenceFrequency.Monthly, Interval = interval, ByDay = [.. days.Select(x => new WeekdayNum(ordinal, x))] },
            "absoluteYearly" => new() { Frequency = RecurrenceFrequency.Yearly, Interval = interval, ByMonth = [Int(pattern, "month") is > 0 and var m ? m : start.Month],
                ByMonthDay = [Int(pattern, "dayOfMonth") is > 0 and var dy ? dy : start.Day] },
            "relativeYearly" => new() { Frequency = RecurrenceFrequency.Yearly, Interval = interval, ByMonth = [Int(pattern, "month") is > 0 and var m2 ? m2 : start.Month],
                ByDay = [.. days.Select(x => new WeekdayNum(ordinal, x))] },
            _ => new() { Frequency = RecurrenceFrequency.Daily, Interval = -1 },
        };
        if (rule.Interval < 0) return null;
        if (recurrence.TryGetProperty("range", out var range) && range.ValueKind == JsonValueKind.Object)
        {
            switch (Str(range, "type"))
            {
                case "endDate" when DateTime.TryParseExact(Str(range, "endDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end):
                    // The whole last day counts, at the series' own time.
                    rule = rule with { Until = end.Date + start.TimeOfDay, UntilUtc = false };
                    break;
                case "numbered" when Int(range, "numberOfOccurrences") > 0:
                    rule = rule with { Count = Int(range, "numberOfOccurrences") };
                    break;
            }
        }
        return rule.ToString();
    }

    /// <summary>RRULE → Graph; throws <see cref="CalendarException"/> for what Outlook cannot keep.</summary>
    public static JsonObject ToGraph(string rrule, DateTime start, string zone, bool allDay)
    {
        var rule = RecurrenceRule.Parse(rrule) ?? throw new CalendarException(Localization.Loc.T("calendar.error.outlookRule"));
        if (rule.BySetPos.Count > 0 || rule.ByMonthDay.Count > 1 || rule.ByMonth.Count > 1 || rule.Rest.Count > 0
            || rule.ByDay.Select(d => d.Ordinal).Distinct().Count() > 1)
            throw new CalendarException(Localization.Loc.T("calendar.error.outlookRule"));
        var pattern = new JsonObject { ["interval"] = rule.Interval };
        JsonArray Days(IEnumerable<DayOfWeek> days) => new([.. days.Select(x => (JsonNode)DayName(x))]);
        string Index(int ordinal) => ordinal == -1 ? "last" : Indexes[Math.Clamp(ordinal, 1, 4) - 1];
        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Daily:
                if (rule.ByDay.Count > 0)
                {
                    pattern["type"] = "weekly";
                    pattern["daysOfWeek"] = Days(rule.ByDay.Select(d => d.Day));
                }
                else pattern["type"] = "daily";
                break;
            case RecurrenceFrequency.Weekly:
                pattern["type"] = "weekly";
                pattern["daysOfWeek"] = Days(rule.ByDay.Count > 0 ? rule.ByDay.Select(d => d.Day) : [start.DayOfWeek]);
                pattern["firstDayOfWeek"] = DayName(rule.WeekStart);
                break;
            case RecurrenceFrequency.Monthly when rule.ByDay.Count > 0:
                pattern["type"] = "relativeMonthly";
                pattern["daysOfWeek"] = Days(rule.ByDay.Select(d => d.Day));
                pattern["index"] = Index(rule.ByDay[0].Ordinal == 0 ? 1 : rule.ByDay[0].Ordinal);
                break;
            case RecurrenceFrequency.Monthly:
                pattern["type"] = "absoluteMonthly";
                pattern["dayOfMonth"] = rule.ByMonthDay.Count > 0 ? rule.ByMonthDay[0] is > 0 and var dm ? dm : throw new CalendarException(Localization.Loc.T("calendar.error.outlookRule")) : start.Day;
                break;
            case RecurrenceFrequency.Yearly when rule.ByDay.Count > 0:
                pattern["type"] = "relativeYearly";
                pattern["daysOfWeek"] = Days(rule.ByDay.Select(d => d.Day));
                pattern["index"] = Index(rule.ByDay[0].Ordinal == 0 ? 1 : rule.ByDay[0].Ordinal);
                pattern["month"] = rule.ByMonth.Count > 0 ? rule.ByMonth[0] : start.Month;
                break;
            default:
                pattern["type"] = "absoluteYearly";
                pattern["dayOfMonth"] = rule.ByMonthDay.Count > 0 ? rule.ByMonthDay[0] : start.Day;
                pattern["month"] = rule.ByMonth.Count > 0 ? rule.ByMonth[0] : start.Month;
                break;
        }
        var range = new JsonObject
        {
            ["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["recurrenceTimeZone"] = allDay ? CalendarTime.Windows(TimeZoneInfo.Local) : zone,
        };
        if (rule.Count is { } count)
        {
            range["type"] = "numbered";
            range["numberOfOccurrences"] = count;
        }
        else if (rule.Until is { } until)
        {
            var last = rule.UntilUtc ? CalendarTime.ToWall(new DateTimeOffset(until, TimeSpan.Zero), CalendarTime.Zone(zone)) : until;
            range["type"] = "endDate";
            range["endDate"] = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else range["type"] = "noEnd";
        return new JsonObject { ["pattern"] = pattern, ["range"] = range };
    }
}

/// <summary>
/// The calendars of a Microsoft account (Outlook.com, Microsoft 365; calendar, phase 8), with Microsoft Graph. Graph gives
/// the changes of a span of time already as appearances (<c>calendarView/delta</c>: what changed since the last
/// deltaLink, also the deleted ones); K-Desktop keeps their series (<c>seriesMaster</c>, read when one of its
/// appearances changes) with the repetition translated, the changed appearances as overrides and the missing ones as
/// deleted. The span goes from 90 days ago to a year ahead and moves once a day. Changes with <c>If-Match</c>.
/// </summary>
public sealed class OutlookCalendar(AccountService accounts, CalendarAccountSettings settings, string cacheFile, HttpClient http)
    : RemoteCalendar(accounts, settings, cacheFile, http)
{
    private const string Api = "https://graph.microsoft.com/v1.0";
    private static readonly TimeSpan Back = TimeSpan.FromDays(90), Ahead = TimeSpan.FromDays(366);
    private const string Select = "subject,body,bodyPreview,start,end,isAllDay,location,recurrence,reminderMinutesBeforeStart,isReminderOn,onlineMeeting,onlineMeetingUrl,originalStartTimeZone,originalStart,lastModifiedDateTime,iCalUId,type,seriesMasterId,isCancelled";

    public override CalendarSourceKind Kind => CalendarSourceKind.Outlook;

    protected override bool IsMain(CalendarInfo calendar) => calendar.Id == _main;

    private string? _main;

    private static string Escape(string id) => Uri.EscapeDataString(id);

    private static HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\", outlook.body-content-type=\"text\", odata.maxpagesize=200");
        return request;
    }

    protected override async Task<List<CalendarInfo>> FetchCalendarsAsync(CancellationToken cancel)
    {
        var list = new List<CalendarInfo>();
        string? url = $"{Api}/me/calendars?$top=100";
        while (url is not null)
        {
            var current = url;
            using var response = await SendAsync(() => Get(current), cancel).ConfigureAwait(false);
            using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
            foreach (var item in Values(doc.RootElement))
            {
                var id = Str(item, "id") ?? string.Empty;
                if (item.TryGetProperty("isDefaultCalendar", out var def) && def.ValueKind == JsonValueKind.True) _main = id;
                list.Add(new CalendarInfo
                {
                    Id = id,
                    SourceId = Id,
                    Name = Str(item, "name") ?? string.Empty,
                    Color = Str(item, "hexColor") is { Length: > 0 } hex ? hex : CalendarColors.Palette[list.Count % CalendarColors.Palette.Count],
                    ReadOnly = !(item.TryGetProperty("canEdit", out var edit) && edit.ValueKind == JsonValueKind.True),
                });
            }
            url = Str(doc.RootElement, "@odata.nextLink");
        }
        _main ??= list.FirstOrDefault()?.Id;
        return list;
    }

    // ── reading ──────────────────────────────────────────────────────────

    private static IEnumerable<JsonElement> Values(JsonElement root) =>
        root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The appearances seen in the span, per calendar: id → (series, original start in UTC, kind).</summary>
    private sealed record Instance(string Id, string? Series, DateTimeOffset Original, string Type, string CalendarId);

    private Dictionary<string, Instance> Instances()
    {
        lock (Gate)
            return Cache.Extra.TryGetValue("instances", out var json)
                ? JsonSerializer.Deserialize<Dictionary<string, Instance>>(json.GetRawText(), CalendarJson.Options) ?? []
                : [];
    }

    private void SetInstances(Dictionary<string, Instance> instances)
    {
        lock (Gate) Cache.Extra["instances"] = JsonSerializer.SerializeToElement(instances, CalendarJson.Options);
    }

    protected override async Task SyncCalendarAsync(CalendarInfo calendar, CancellationToken cancel)
    {
        string? delta;
        DateTimeOffset full;
        lock (Gate)
        {
            delta = Cache.SyncTokens.GetValueOrDefault(calendar.Id);
            full = Cache.FullSync.GetValueOrDefault(calendar.Id);
        }
        var now = DateTimeOffset.UtcNow;
        if (now - full > TimeSpan.FromDays(1)) delta = null;
        var fresh = delta is null;
        var url = delta ?? $"{Api}/me/calendars/{Escape(calendar.Id)}/calendarView/delta?startDateTime={Escape((now - Back).ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture))}&endDateTime={Escape((now + Ahead).ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture))}";
        var instances = Instances();
        if (fresh) foreach (var key in instances.Where(i => i.Value.CalendarId == calendar.Id).Select(i => i.Key).ToList()) instances.Remove(key);
        var singles = new List<CalendarEvent>();
        var removed = new HashSet<string>();
        var touched = new HashSet<string>();
        string? next = url;
        while (next is not null)
        {
            var current = next;
            using var response = await SendAsync(() => Get(current), cancel, allowNotFound: true).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                lock (Gate) Cache.SyncTokens.Remove(calendar.Id);
                return;
            }
            using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
            foreach (var item in Values(doc.RootElement))
            {
                var id = Str(item, "id") ?? string.Empty;
                if (item.TryGetProperty("@removed", out _))
                {
                    if (instances.Remove(id, out var gone) && gone.Series is { } s) touched.Add(s);
                    removed.Add(id);
                    continue;
                }
                var type = Str(item, "type") ?? "singleInstance";
                var series = Str(item, "seriesMasterId");
                if (type == "singleInstance" || series is null)
                {
                    if (ToEvent(item, Id, calendar.Id) is { } e) singles.Add(e);
                    continue;
                }
                var original = Str(item, "originalStart") is { } o && DateTimeOffset.TryParse(o, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at
                    : StartOf(item) ?? DateTimeOffset.MinValue;
                instances[id] = new Instance(id, series, original, type, calendar.Id);
                touched.Add(series);
                if (type == "exception" && ToEvent(item, Id, calendar.Id) is { } changed) singles.Add(changed with { SeriesId = series });
            }
            next = Str(doc.RootElement, "@odata.nextLink");
            if (next is null) delta = Str(doc.RootElement, "@odata.deltaLink");
        }

        // The series whose appearances changed: read again, with the missing appearances as deleted.
        var masters = new List<CalendarEvent>();
        foreach (var seriesId in touched)
        {
            using var response = await SendAsync(() => Get($"{Api}/me/events/{Escape(seriesId)}?$select={Select}"), cancel, allowNotFound: true).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                removed.Add(seriesId);
                continue;
            }
            using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
            if (ToEvent(doc.RootElement, Id, calendar.Id) is not { } master) continue;
            masters.Add(master with { Exceptions = Missing(master, instances.Values.Where(i => i.Series == seriesId), now) });
        }

        lock (Gate)
        {
            if (fresh) Cache.Events.RemoveAll(e => e.CalendarId == calendar.Id && !e.Id.StartsWith("pending-", StringComparison.Ordinal));
            var replaced = singles.Select(e => e.Id).Concat(masters.Select(m => m.Id)).Concat(removed).ToHashSet();
            Cache.Events.RemoveAll(e => e.CalendarId == calendar.Id && (replaced.Contains(e.Id) || (e.SeriesId is { } s && removed.Contains(s))));
            // An exception that went back to normal is an appearance again.
            Cache.Events.RemoveAll(e => e.SeriesId is not null && instances.TryGetValue(e.Id, out var i) && i.Type != "exception");
            Cache.Events.AddRange(masters);
            Cache.Events.AddRange(singles);
            if (delta is not null) Cache.SyncTokens[calendar.Id] = delta;
            if (fresh) Cache.FullSync[calendar.Id] = now;
        }
        SetInstances(instances);
    }

    /// <summary>The appearances of a series in the span that Graph did not give: deleted ones (EXDATE).</summary>
    private static List<DateTime> Missing(CalendarEvent master, IEnumerable<Instance> seen, DateTimeOffset now)
    {
        var there = seen.Select(i => i.Original.UtcDateTime).ToHashSet();
        var missing = new List<DateTime>();
        var plain = master with { Exceptions = [], Overrides = [] };
        foreach (var o in Recurrence.Expand(plain, now - Back, now + Ahead - TimeSpan.FromDays(1)))
        {
            var utc = master.AllDay ? DateTime.SpecifyKind(o.RecurrenceId!.Value, DateTimeKind.Utc) : o.Start.UtcDateTime;
            if (!there.Contains(utc) && !(master.AllDay && there.Any(t => t.Date == o.RecurrenceId!.Value.Date))) missing.Add(o.RecurrenceId!.Value);
        }
        return missing;
    }

    private static DateTimeOffset? StartOf(JsonElement item) =>
        item.TryGetProperty("start", out var s) && Str(s, "dateTime") is { } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;

    /// <summary>A Graph event (read with times in UTC) as K-Desktop sees it, in its own zone.</summary>
    public static CalendarEvent? ToEvent(JsonElement item, string sourceId, string calendarId)
    {
        var id = Str(item, "id");
        if (id is null || StartOf(item) is not { } start) return null;
        var end = item.TryGetProperty("end", out var e) && Str(e, "dateTime") is { } endText
                  && DateTime.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var endAt)
            ? new DateTimeOffset(DateTime.SpecifyKind(endAt, DateTimeKind.Utc)) : start.AddHours(1);
        var allDay = item.TryGetProperty("isAllDay", out var a) && a.ValueKind == JsonValueKind.True;
        var zoneName = Str(item, "originalStartTimeZone");
        var zone = zoneName is null || zoneName.StartsWith("tzone://", StringComparison.Ordinal) || !CalendarTime.Known(zoneName)
            ? (zoneName == "tzone://Microsoft/Utc" ? "UTC" : CalendarTime.Iana(TimeZoneInfo.Local)) : zoneName;
        DateTime startWall, endWall;
        if (allDay)
        {
            // All day: Graph gives 0:00 of its dates.
            startWall = start.UtcDateTime.Date;
            endWall = end.UtcDateTime.Date;
            if (endWall <= startWall) endWall = startWall.AddDays(1);
        }
        else
        {
            startWall = CalendarTime.ToWall(start, CalendarTime.Zone(zone));
            endWall = CalendarTime.ToWall(end, CalendarTime.Zone(zone));
        }
        var location = item.TryGetProperty("location", out var l) ? Str(l, "displayName") : null;
        var notes = item.TryGetProperty("body", out var b) ? Str(b, "content")?.Trim() : null;
        var meeting = (item.TryGetProperty("onlineMeeting", out var om) ? Str(om, "joinUrl") : null) ?? Str(item, "onlineMeetingUrl") ?? MeetingLinks.Find(location, notes);
        var reminders = item.TryGetProperty("isReminderOn", out var on) && on.ValueKind == JsonValueKind.True
                        && item.TryGetProperty("reminderMinutesBeforeStart", out var rm) && rm.TryGetInt32(out var minutes) ? new List<int> { minutes } : [];
        var type = Str(item, "type");
        var rule = type == "seriesMaster" && item.TryGetProperty("recurrence", out var rec) ? GraphRecurrence.ToRule(rec, startWall) : null;
        DateTime? recurrenceId = null;
        if (type is "exception" or "occurrence" && Str(item, "originalStart") is { } os
            && DateTimeOffset.TryParse(os, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var original))
            recurrenceId = allDay ? original.UtcDateTime.Date : CalendarTime.ToWall(original, CalendarTime.Zone(zone));
        return new CalendarEvent
        {
            Id = id,
            Uid = Str(item, "iCalUId") ?? id,
            SourceId = sourceId,
            CalendarId = calendarId,
            Title = Str(item, "subject") ?? string.Empty,
            Start = startWall,
            End = endWall,
            AllDay = allDay,
            TimeZone = allDay ? null : zone,
            Location = string.IsNullOrWhiteSpace(location) ? null : location,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            MeetingUrl = meeting,
            Recurrence = rule,
            RecurrenceId = recurrenceId,
            Cancelled = item.TryGetProperty("isCancelled", out var c) && c.ValueKind == JsonValueKind.True,
            Reminders = reminders,
            Etag = Str(item, "@odata.etag"),
            Updated = Str(item, "lastModifiedDateTime") is { } u && DateTimeOffset.TryParse(u, CultureInfo.InvariantCulture, DateTimeStyles.None, out var up) ? up : default,
        };
    }

    // ── writing ──────────────────────────────────────────────────────────

    /// <summary>The body of a Graph event (times in the cita's own zone, by its Windows name).</summary>
    public static JsonObject ToGraph(CalendarEvent e, bool withRecurrence)
    {
        var zone = e.AllDay ? CalendarTime.Windows(TimeZoneInfo.Local) : CalendarTime.Windows(CalendarTime.Zone(e.TimeZone));
        JsonObject Time(DateTime wall) => new()
        {
            ["dateTime"] = wall.ToString(e.AllDay ? "yyyy-MM-dd'T'00:00:00" : "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["timeZone"] = zone,
        };
        var body = new JsonObject
        {
            ["subject"] = e.Title,
            ["start"] = Time(e.Start),
            ["end"] = Time(e.End > e.Start ? e.End : e.Start + e.Duration),
            ["isAllDay"] = e.AllDay,
            ["location"] = new JsonObject { ["displayName"] = e.Location ?? string.Empty },
            ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = e.Notes ?? string.Empty },
            ["isReminderOn"] = e.Reminders.Count > 0,
        };
        if (e.Reminders.Count > 0) body["reminderMinutesBeforeStart"] = e.Reminders[0];
        if (withRecurrence) body["recurrence"] = e.IsRecurring ? GraphRecurrence.ToGraph(e.Recurrence!, e.Start, zone, e.AllDay) : null;
        return body;
    }

    private static HttpRequestMessage Write(HttpMethod method, string url, JsonObject? body, string? etag)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\", outlook.body-content-type=\"text\"");
        if (body is not null) request.Content = JsonBody(body.ToJsonString());
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }

    private async Task<CalendarEvent> ReadBackAsync(HttpResponseMessage response, string calendarId, CancellationToken cancel)
    {
        using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
        return ToEvent(doc.RootElement, Id, calendarId) ?? throw new CalendarException(Localization.Loc.T("calendar.error.notFound"));
    }

    protected override async Task<CalendarEvent> CreateAsync(CalendarEvent e, CancellationToken cancel)
    {
        var body = ToGraph(e, withRecurrence: true);
        if (body["recurrence"] is null) body.Remove("recurrence");
        using var response = await SendAsync(() => Write(HttpMethod.Post, $"{Api}/me/calendars/{Escape(e.CalendarId)}/events", body, null), cancel).ConfigureAwait(false);
        var created = await ReadBackAsync(response, e.CalendarId, cancel).ConfigureAwait(false);
        // Deleted appearances of a series made without connection: deleted one by one.
        foreach (var rid in e.Exceptions) await CancelOccurrenceAsync(created, rid, cancel).ConfigureAwait(false);
        return created with { Exceptions = e.Exceptions };
    }

    protected override async Task<CalendarEvent> PatchAsync(CalendarEvent e, CancellationToken cancel)
    {
        CalendarEvent? old;
        lock (Gate) old = Cache.Events.FirstOrDefault(x => x.Id == e.Id);
        if (old is not null && old.CalendarId != e.CalendarId)
        {
            // Graph cannot move between calendars: a copy there, the old one goes.
            var copy = await CreateAsync(e with { Id = string.Empty, Etag = null }, cancel).ConfigureAwait(false);
            await RemoveAsync(old, cancel).ConfigureAwait(false);
            Drop(old.Id);
            return copy;
        }
        var body = ToGraph(e, withRecurrence: old?.Recurrence != e.Recurrence || old?.Start != e.Start);
        using var response = await SendAsync(() => Write(HttpMethod.Patch, $"{Api}/me/events/{Escape(e.Id)}", body, e.Etag), cancel).ConfigureAwait(false);
        var saved = await ReadBackAsync(response, e.CalendarId, cancel).ConfigureAwait(false);
        return saved with { Exceptions = saved.IsRecurring ? e.Exceptions : [] };
    }

    protected override async Task RemoveAsync(CalendarEvent e, CancellationToken cancel)
    {
        using var response = await SendAsync(() => Write(HttpMethod.Delete, $"{Api}/me/events/{Escape(e.Id)}", null, null), cancel, allowNotFound: true).ConfigureAwait(false);
    }

    /// <summary>The id of one appearance: from the ones seen, or asked to Graph around its time.</summary>
    private async Task<string?> OccurrenceIdAsync(CalendarEvent series, DateTime recurrenceId, CancellationToken cancel)
    {
        var at = series.AllDay ? new DateTimeOffset(DateTime.SpecifyKind(recurrenceId.Date, DateTimeKind.Utc)) : CalendarTime.ToInstant(recurrenceId, CalendarTime.Zone(series.TimeZone));
        var known = Instances().Values.FirstOrDefault(i => i.Series == series.Id && (series.AllDay ? i.Original.UtcDateTime.Date == at.UtcDateTime.Date : i.Original == at));
        if (known is not null) return known.Id;
        var from = (at - TimeSpan.FromDays(1)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var to = (at + TimeSpan.FromDays(1)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        using var response = await SendAsync(() => Get($"{Api}/me/events/{Escape(series.Id)}/instances?startDateTime={Escape(from)}&endDateTime={Escape(to)}&$select=originalStart,start"), cancel).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
        foreach (var item in Values(doc.RootElement))
            if (Str(item, "originalStart") is { } os && DateTimeOffset.TryParse(os, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var original)
                && (series.AllDay ? original.UtcDateTime.Date == at.UtcDateTime.Date : original == at))
                return Str(item, "id");
        return null;
    }

    protected override async Task<CalendarEvent> PatchOccurrenceAsync(CalendarEvent series, CalendarEvent changed, CancellationToken cancel)
    {
        var id = await OccurrenceIdAsync(series, changed.RecurrenceId!.Value, cancel).ConfigureAwait(false)
                 ?? throw new CalendarException(Localization.Loc.T("calendar.error.notFound"));
        var body = ToGraph(changed with { Recurrence = null, TimeZone = changed.TimeZone ?? series.TimeZone }, withRecurrence: false);
        using var response = await SendAsync(() => Write(HttpMethod.Patch, $"{Api}/me/events/{Escape(id)}", body, null), cancel).ConfigureAwait(false);
        var saved = await ReadBackAsync(response, series.CalendarId, cancel).ConfigureAwait(false);
        return saved with { SeriesId = series.Id, RecurrenceId = changed.RecurrenceId };
    }

    protected override async Task CancelOccurrenceAsync(CalendarEvent series, DateTime recurrenceId, CancellationToken cancel)
    {
        if (await OccurrenceIdAsync(series, recurrenceId, cancel).ConfigureAwait(false) is not { } id) return;
        using var response = await SendAsync(() => Write(HttpMethod.Delete, $"{Api}/me/events/{Escape(id)}", null, null), cancel, allowNotFound: true).ConfigureAwait(false);
        lock (Gate) Cache.Events.RemoveAll(x => x.Id == id);
    }
}
