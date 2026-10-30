using System.Globalization;
using System.Net;
using System.Text.Json;
using KDesktop.Core.Calendar;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// The agenda on the phone (calendar, phase 9): api/calendar gives the calendars and the citas of some days; POST
/// api/calendar adds, changes or deletes one (with the same rules as the agenda of the PC); «calendar» in the event
/// stream when something changed.
/// </summary>
public sealed partial class WebServer
{
    /// <summary>The calendar (set by K-Desktop); null: the phone has no agenda.</summary>
    public CalendarService? Calendar { get; set; }

    private int _calendarPending;

    /// <summary>
    /// Tells the phones the calendar changed: a burst of changes (a sync, an import) is said once, a second after the
    /// first, so the last change is never left untold.
    /// </summary>
    public void NotifyCalendarChanged()
    {
        if (Interlocked.Exchange(ref _calendarPending, 1) == 1) return;
        _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ =>
        {
            Volatile.Write(ref _calendarPending, 0);
            Broadcast("calendar");
        }, TaskScheduler.Default);
    }

    private async Task<bool> CalendarAsync(WebRequest request, HttpListenerContext context)
    {
        var path = request.Path!;
        if (path != "/api/calendar") return false;
        var response = context.Response;
        if (Calendar is not { } calendar)
        {
            await WriteJsonAsync(response, new { error = "noCalendar" }, 404).ConfigureAwait(false);
            return true;
        }
        if (request.Method == "GET")
        {
            var query = context.Request.QueryString;
            var from = DateTime.TryParseExact(query["from"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : DateTime.Today;
            var days = int.TryParse(query["days"], out var d) ? Math.Clamp(d, 1, 120) : 14;
            var occurrences = calendar.Occurrences(CalendarTime.ToInstant(from, TimeZoneInfo.Local), CalendarTime.ToInstant(from.AddDays(days), TimeZoneInfo.Local), visibleOnly: true);
            var calendars = calendar.Calendars.Select(c => new
            {
                key = c.SourceId + "/" + c.Id,
                c.Name,
                c.Color,
                writable = !c.ReadOnly && calendar.Source(c.SourceId)?.Capabilities.HasFlag(CalendarCapabilities.Write) == true,
                c.Visible,
            }).ToList();
            var defaultCalendar = calendar.DefaultCalendar;
            await WriteJsonAsync(response, new
            {
                calendars,
                defaultCalendar = defaultCalendar.SourceId + "/" + defaultCalendar.Id,
                items = occurrences.Select(o =>
                {
                    var item = CalendarAssistant.Item(calendar, o);
                    return new
                    {
                        item.Key, item.Title, item.Start, item.End, item.AllDay, item.Location, item.Meeting, item.Repeats,
                        notes = o.Event.Notes,
                        calendar = o.Series.SourceId + "/" + o.Series.CalendarId,
                        reminder = o.Event.Reminders.Count > 0 ? o.Event.Reminders[0] : (int?)null,
                    };
                }),
            }).ConfigureAwait(false);
            return true;
        }
        if (request.Method != "POST") return false;
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        if (body is not { ValueKind: JsonValueKind.Object } json)
        {
            await WriteJsonAsync(response, new { error = "body" }, 400).ConfigureAwait(false);
            return true;
        }
        string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var op = Str(json, "op") ?? string.Empty;
        CalendarCita? cita = null;
        if (json.TryGetProperty("cita", out var c) && c.ValueKind == JsonValueKind.Object)
            cita = new CalendarCita(Str(c, "title"), Str(c, "start"), Str(c, "end"),
                c.TryGetProperty("allDay", out var allDay) && allDay.ValueKind is JsonValueKind.True or JsonValueKind.False ? allDay.GetBoolean() : null,
                null, Str(c, "location"), Str(c, "notes"), Str(c, "repeat"),
                c.TryGetProperty("reminder", out var r) && r.TryGetInt32(out var minutes) ? minutes : null);
        CalendarAnswer answer;
        try
        {
            if (op == "add")
            {
                // The phone says the calendar by its key, not by its name.
                var key = cita is null ? null : Str(c, "calendar");
                var target = key?.Split('/', 2) is [var source, var id] ? calendar.Calendar(source, id) : calendar.DefaultCalendar;
                if (target is null || cita is null || CalendarAssistant.Build(new CalendarEvent { SourceId = target.SourceId, CalendarId = target.Id }, cita, DateTime.Now, isNew: true) is not { } e)
                    answer = new CalendarAnswer(false, Reason: "start");
                else
                {
                    var change = await calendar.AddAsync(e).ConfigureAwait(false);
                    answer = new CalendarAnswer(true, Undo: change.Result?.Id);
                }
            }
            else if (op is "change" or "delete")
                answer = await CalendarAssistant.HandleAsync(calendar, new CalendarRequest(op, Key: Str(json, "key"), Scope: Str(json, "scope"), Cita: cita), DateTime.Now).ConfigureAwait(false);
            else
                answer = new CalendarAnswer(false, Reason: "op");
        }
        catch (Exception ex) when (ex is CalendarException or CalendarConflictException or HttpRequestException)
        {
            answer = new CalendarAnswer(false, Reason: "error", Message: ex.Message);
        }
        await WriteJsonAsync(response, new { ok = answer.Ok, reason = answer.Reason, message = answer.Message }, answer.Ok ? 200 : 400).ConfigureAwait(false);
        return true;
    }
}
