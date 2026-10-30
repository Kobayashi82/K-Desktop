using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Accounts;

namespace KDesktop.Core.Calendar;

/// <summary>
/// The calendars of a Google account (calendar, phase 7), with the REST API of Calendar v3: the list of calendars
/// (calendarList), the citas with their series whole (<c>singleEvents=false</c>, K-Desktop works the repetitions out) and
/// only what changed since the last time (<c>syncToken</c>; when it expires, 410, everything again). Changes with
/// <c>If-Match</c> and the etag. Meet links, reminders and time zones are translated.
/// </summary>
public sealed class GoogleCalendar(AccountService accounts, CalendarAccountSettings settings, string cacheFile, HttpClient http)
    : RemoteCalendar(accounts, settings, cacheFile, http)
{
    private const string Api = "https://www.googleapis.com/calendar/v3";

    /// <summary>How far back the first read goes.</summary>
    private static readonly TimeSpan Back = TimeSpan.FromDays(180);

    public override CalendarSourceKind Kind => CalendarSourceKind.Google;

    protected override bool IsMain(CalendarInfo calendar) => calendar.Id == "primary" || calendar.Id.Equals(Account?.Email, StringComparison.OrdinalIgnoreCase);

    private static string Escape(string id) => Uri.EscapeDataString(id);

    protected override async Task<List<CalendarInfo>> FetchCalendarsAsync(CancellationToken cancel)
    {
        var list = new List<CalendarInfo>();
        string? page = null;
        do
        {
            var url = $"{Api}/users/me/calendarList?maxResults=250" + (page is null ? string.Empty : "&pageToken=" + Escape(page));
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancel).ConfigureAwait(false);
            using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
            foreach (var item in Items(doc.RootElement)) list.Add(CalendarOf(item, Id));
            page = Str(doc.RootElement, "nextPageToken");
        }
        while (page is not null);
        return list;
    }

    public static CalendarInfo CalendarOf(JsonElement item, string sourceId)
    {
        var role = Str(item, "accessRole");
        int? reminder = null;
        if (item.TryGetProperty("defaultReminders", out var defaults) && defaults.ValueKind == JsonValueKind.Array)
            foreach (var r in defaults.EnumerateArray())
                if (r.TryGetProperty("minutes", out var m) && m.TryGetInt32(out var minutes)) { reminder = minutes; break; }
        return new CalendarInfo
        {
            Id = Str(item, "id") ?? string.Empty,
            SourceId = sourceId,
            Name = Str(item, "summaryOverride") ?? Str(item, "summary") ?? string.Empty,
            Color = Str(item, "backgroundColor") ?? CalendarColors.Default,
            ReadOnly = role is not ("owner" or "writer"),
            DefaultReminder = reminder,
        };
    }

    protected override async Task SyncCalendarAsync(CalendarInfo calendar, CancellationToken cancel)
    {
        string? token;
        lock (Gate) token = Cache.SyncTokens.GetValueOrDefault(calendar.Id);
        var changed = new List<CalendarEvent>();
        var deleted = new List<string>();
        string? page = null, next = null;
        var full = token is null;
        while (true)
        {
            var url = $"{Api}/calendars/{Escape(calendar.Id)}/events?singleEvents=false&showDeleted=true&maxResults=2500";
            url += page is not null ? "&pageToken=" + Escape(page)
                : token is not null ? "&syncToken=" + Escape(token)
                : "&timeMin=" + Escape((DateTimeOffset.UtcNow - Back).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancel, allowNotFound: true).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Gone)
            {
                // The syncToken expired: everything again.
                lock (Gate) Cache.SyncTokens.Remove(calendar.Id);
                token = null;
                page = null;
                full = true;
                changed.Clear();
                deleted.Clear();
                continue;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return;
            using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
            var zone = Str(doc.RootElement, "timeZone");
            foreach (var item in Items(doc.RootElement))
            {
                if (Str(item, "status") == "cancelled" && Str(item, "recurringEventId") is null) deleted.Add(Str(item, "id") ?? string.Empty);
                else if (ToEvent(item, Id, calendar.Id, zone, calendar.DefaultReminder) is { } e) changed.Add(e);
            }
            page = Str(doc.RootElement, "nextPageToken");
            next = Str(doc.RootElement, "nextSyncToken") ?? next;
            if (page is null) break;
        }
        lock (Gate)
        {
            if (full) Cache.Events.RemoveAll(e => e.CalendarId == calendar.Id && !e.Id.StartsWith("pending-", StringComparison.Ordinal));
            var ids = changed.Select(e => e.Id).Concat(deleted).ToHashSet();
            Cache.Events.RemoveAll(e => e.CalendarId == calendar.Id && ids.Contains(e.Id));
            Cache.Events.RemoveAll(e => e.CalendarId == calendar.Id && e.SeriesId is { } s && deleted.Contains(s));
            Cache.Events.AddRange(changed);
            if (next is not null) Cache.SyncTokens[calendar.Id] = next;
        }
    }

    // ── Google → K-Desktop ───────────────────────────────────────────────

    private static IEnumerable<JsonElement> Items(JsonElement root) =>
        root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : [];

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A start or end of Google: <c>{"date"}</c> (all day) or <c>{"dateTime", "timeZone"}</c> → wall-clock time, zone and whether it is a date.</summary>
    private static (DateTime Wall, string? Zone, bool IsDate)? Time(JsonElement parent, string name, string? fallbackZone)
    {
        if (!parent.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.Object) return null;
        if (Str(t, "date") is { } date && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return (d, null, true);
        if (Str(t, "dateTime") is not { } text || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return null;
        var zone = Str(t, "timeZone") ?? fallbackZone;
        return (CalendarTime.ToWall(at, CalendarTime.Zone(zone)), zone, false);
    }

    /// <summary>A Google event as K-Desktop sees it; a changed or deleted appearance of a series comes with its SeriesId and RecurrenceId.</summary>
    public static CalendarEvent? ToEvent(JsonElement item, string sourceId, string calendarId, string? calendarZone, int? defaultReminder)
    {
        var id = Str(item, "id");
        if (id is null) return null;
        var series = Str(item, "recurringEventId");
        var cancelled = Str(item, "status") == "cancelled";
        var start = Time(item, "start", calendarZone);
        var end = Time(item, "end", calendarZone);
        DateTime? recurrenceId = null;
        if (series is not null && Time(item, "originalStartTime", calendarZone) is { } original) recurrenceId = original.Wall;
        if (start is null)
        {
            // A deleted appearance comes with its original time only.
            if (!cancelled || recurrenceId is null) return null;
            start = (recurrenceId.Value, calendarZone, false);
        }
        var s = start.Value;
        var e = end ?? (s.IsDate ? (s.Wall.AddDays(1), s.Zone, true) : (s.Wall.AddHours(1), s.Zone, false));
        // The end in the start's zone.
        var endWall = e.IsDate || e.Zone == s.Zone ? e.Wall : CalendarTime.ToWall(CalendarTime.ToInstant(e.Wall, CalendarTime.Zone(e.Zone)), CalendarTime.Zone(s.Zone));

        string? rule = null;
        var exceptions = new List<DateTime>();
        if (item.TryGetProperty("recurrence", out var recurrence) && recurrence.ValueKind == JsonValueKind.Array)
            foreach (var line in recurrence.EnumerateArray().Select(l => l.GetString() ?? string.Empty))
            {
                if (line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) rule = line[6..];
                else if (line.StartsWith("EXDATE", StringComparison.OrdinalIgnoreCase)) exceptions.AddRange(ExDates(line, s.Zone, s.IsDate));
            }

        var reminders = new List<int>();
        if (item.TryGetProperty("reminders", out var r))
        {
            if (r.TryGetProperty("useDefault", out var useDefault) && useDefault.ValueKind == JsonValueKind.True && defaultReminder is { } d) reminders.Add(d);
            if (r.TryGetProperty("overrides", out var overrides) && overrides.ValueKind == JsonValueKind.Array)
                foreach (var o in overrides.EnumerateArray())
                    if (o.TryGetProperty("minutes", out var m) && m.TryGetInt32(out var minutes)) reminders.Add(minutes);
        }

        var location = Str(item, "location");
        var notes = Str(item, "description");
        var meeting = Str(item, "hangoutLink");
        if (meeting is null && item.TryGetProperty("conferenceData", out var conference) && conference.TryGetProperty("entryPoints", out var points) && points.ValueKind == JsonValueKind.Array)
            meeting = points.EnumerateArray().Where(p => Str(p, "entryPointType") == "video").Select(p => Str(p, "uri")).FirstOrDefault(u => u is not null);
        meeting ??= MeetingLinks.Find(location, notes);
        var updated = Str(item, "updated") is { } u && DateTimeOffset.TryParse(u, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : default;

        return new CalendarEvent
        {
            Id = id,
            Uid = Str(item, "iCalUID") ?? id,
            SourceId = sourceId,
            CalendarId = calendarId,
            Title = Str(item, "summary") ?? string.Empty,
            Start = s.Wall,
            End = endWall,
            AllDay = s.IsDate,
            TimeZone = s.IsDate ? null : s.Zone,
            Location = location,
            Notes = notes,
            MeetingUrl = meeting,
            Recurrence = series is null && RecurrenceRule.Parse(rule) is not null ? rule : null,
            Exceptions = exceptions,
            SeriesId = series,
            RecurrenceId = recurrenceId,
            Cancelled = cancelled,
            Reminders = [.. reminders.Distinct().Order()],
            Etag = Str(item, "etag"),
            Updated = updated,
        };
    }

    /// <summary>«EXDATE;TZID=Europe/Madrid:20260112T090000,20260119T090000» → wall-clock times in the series' zone.</summary>
    public static IEnumerable<DateTime> ExDates(string line, string? zone, bool allDay)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) yield break;
        var head = line[..colon];
        var tzid = head.Split(';').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0].Equals("TZID", StringComparison.OrdinalIgnoreCase))?[1];
        foreach (var value in line[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IcsTime.TryParse(value, out var at, out var utc, out var isDate)) continue;
            if (allDay || isDate) yield return at.Date;
            else if (utc) yield return CalendarTime.ToWall(new DateTimeOffset(at, TimeSpan.Zero), CalendarTime.Zone(zone));
            else if (tzid is not null && tzid != zone) yield return CalendarTime.ToWall(CalendarTime.ToInstant(at, CalendarTime.Zone(tzid)), CalendarTime.Zone(zone));
            else yield return at;
        }
    }

    // ── K-Desktop → Google ───────────────────────────────────────────────

    /// <summary>The body of a Google event (series with its RRULE and EXDATE; an appearance with its own times).</summary>
    public static JsonObject ToGoogle(CalendarEvent e)
    {
        JsonObject Time(DateTime wall) => e.AllDay
            ? new JsonObject { ["date"] = wall.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            : new JsonObject
            {
                ["dateTime"] = CalendarTime.ToInstant(wall, CalendarTime.Zone(e.TimeZone)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                ["timeZone"] = CalendarTime.Iana(CalendarTime.Zone(e.TimeZone)),
            };
        var body = new JsonObject
        {
            ["summary"] = e.Title,
            ["start"] = Time(e.Start),
            ["end"] = Time(e.End > e.Start ? e.End : e.Start + e.Duration),
            ["location"] = e.Location ?? string.Empty,
            ["description"] = e.Notes ?? string.Empty,
            ["reminders"] = e.Reminders.Count == 0
                ? new JsonObject { ["useDefault"] = false, ["overrides"] = new JsonArray() }
                : new JsonObject { ["useDefault"] = false, ["overrides"] = new JsonArray([.. e.Reminders.Take(5).Select(m => (JsonNode)new JsonObject { ["method"] = "popup", ["minutes"] = m })]) },
        };
        if (e.IsRecurring && e.RecurrenceId is null)
        {
            var lines = new JsonArray { (JsonNode)("RRULE:" + e.Recurrence) };
            if (e.Exceptions.Count > 0)
            {
                var values = string.Join(',', e.Exceptions.Select(x => e.AllDay ? IcsTime.FormatDate(x) : IcsTime.Format(x, false)));
                lines.Add(e.AllDay ? "EXDATE;VALUE=DATE:" + values : $"EXDATE;TZID={CalendarTime.Iana(CalendarTime.Zone(e.TimeZone))}:{values}");
            }
            body["recurrence"] = lines;
        }
        else if (e.RecurrenceId is null) body["recurrence"] = new JsonArray();
        if (!string.IsNullOrEmpty(e.Uid) && e.Uid.Contains('@', StringComparison.Ordinal) && e.Id.Length == 0) body["iCalUID"] = e.Uid;
        return body;
    }

    /// <summary>The id of one appearance: «{series}_{20260105T080000Z}» (all day: «_20260105»).</summary>
    public static string InstanceId(CalendarEvent series, DateTime recurrenceId) =>
        series.Id + "_" + (series.AllDay ? IcsTime.FormatDate(recurrenceId)
            : IcsTime.Format(CalendarTime.ToInstant(recurrenceId, CalendarTime.Zone(series.TimeZone)).UtcDateTime, true));

    private CalendarEvent Read(JsonDocument doc, string calendarId)
    {
        var reminder = Calendars.FirstOrDefault(c => c.Id == calendarId)?.DefaultReminder;
        return ToEvent(doc.RootElement, Id, calendarId, null, reminder) ?? throw new CalendarException(Localization.Loc.T("calendar.error.notFound"));
    }

    protected override async Task<CalendarEvent> CreateAsync(CalendarEvent e, CancellationToken cancel)
    {
        var body = ToGoogle(e);
        body.Remove("iCalUID");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/calendars/{Escape(e.CalendarId)}/events") { Content = JsonBody(body.ToJsonString()) }, cancel).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
        return Read(doc, e.CalendarId);
    }

    protected override async Task<CalendarEvent> PatchAsync(CalendarEvent e, CancellationToken cancel)
    {
        CalendarEvent? old;
        lock (Gate) old = Cache.Events.FirstOrDefault(x => x.Id == e.Id);
        if (old is not null && old.CalendarId != e.CalendarId)
        {
            // Another calendar of the same account: «move», then the rest.
            using var moved = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post,
                $"{Api}/calendars/{Escape(old.CalendarId)}/events/{Escape(e.Id)}/move?destination={Escape(e.CalendarId)}"), cancel).ConfigureAwait(false);
            using var movedDoc = await ReadJsonAsync(moved, cancel).ConfigureAwait(false);
            e = e with { Etag = Read(movedDoc, e.CalendarId).Etag };
            Drop(e.Id);
        }
        var etag = e.Etag;
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"{Api}/calendars/{Escape(e.CalendarId)}/events/{Escape(e.Id)}") { Content = JsonBody(ToGoogle(e).ToJsonString()) };
            if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
            return request;
        }, cancel).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
        return Read(doc, e.CalendarId);
    }

    protected override async Task RemoveAsync(CalendarEvent e, CancellationToken cancel)
    {
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{Api}/calendars/{Escape(e.CalendarId)}/events/{Escape(e.Id)}");
            if (e.Etag is not null) request.Headers.TryAddWithoutValidation("If-Match", e.Etag);
            return request;
        }, cancel, allowNotFound: true).ConfigureAwait(false);
    }

    protected override async Task<CalendarEvent> PatchOccurrenceAsync(CalendarEvent series, CalendarEvent changed, CancellationToken cancel)
    {
        var id = InstanceId(series, changed.RecurrenceId!.Value);
        var body = ToGoogle(changed with { Recurrence = null });
        body.Remove("recurrence");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"{Api}/calendars/{Escape(series.CalendarId)}/events/{Escape(id)}") { Content = JsonBody(body.ToJsonString()) }, cancel).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancel).ConfigureAwait(false);
        return Read(doc, series.CalendarId) with { SeriesId = series.Id, RecurrenceId = changed.RecurrenceId };
    }

    protected override async Task CancelOccurrenceAsync(CalendarEvent series, DateTime recurrenceId, CancellationToken cancel)
    {
        var id = InstanceId(series, recurrenceId);
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{Api}/calendars/{Escape(series.CalendarId)}/events/{Escape(id)}"), cancel, allowNotFound: true).ConfigureAwait(false);
        lock (Gate) Cache.Events.RemoveAll(x => x.Id == id);
    }
}
