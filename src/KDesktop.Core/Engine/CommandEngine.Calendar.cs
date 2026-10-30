using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

public sealed partial class CommandEngine
{
    /// <summary>The calendar («Crear cita», «Abrir la reunión»); set by K-Desktop at start.</summary>
    public CalendarService? Calendar { get; set; }

    /// <summary>The cita «Abrir la reunión» opens, or null.</summary>
    public static CalendarOccurrence? MeetingToJoin(CalendarService calendar, CalendarJoinWhich which, string calendarKey, DateTimeOffset now)
    {
        var withLink = calendar.Occurrences(now - TimeSpan.FromDays(1), now + TimeSpan.FromHours(12))
            .Where(o => !string.IsNullOrWhiteSpace(o.Event.MeetingUrl) && !o.AllDay)
            .Where(o => calendarKey.Trim().Length == 0 || o.Series.SourceId + "/" + o.Series.CalendarId == calendarKey.Trim())
            .ToList();
        // «Now»: started (or about to, 10 minutes before) and not over; the one that started last.
        var current = withLink.Where(o => o.Start - TimeSpan.FromMinutes(10) <= now && (o.End > now || o.End <= o.Start && o.Start > now - TimeSpan.FromMinutes(30)))
            .OrderByDescending(o => o.Start).FirstOrDefault();
        return which == CalendarJoinWhich.Current ? current : withLink.FirstOrDefault(o => o.Start > now) ?? current;
    }

    private async Task CalendarAddAsync(CalendarAddAction a, ExecutionContext ctx)
    {
        if (Calendar is not { } calendar) throw new InvalidOperationException(Loc.T("calendar.notReady"));
        var title = TextVariables.Expand(a.Title).Trim();
        if (title.Length == 0) title = Loc.T("agenda.untitled");
        DateTime start;
        if (a.When == CalendarWhen.In) start = DateTime.Now.AddMinutes(Math.Max(0, a.InMinutes));
        else
        {
            var day = EventTime.ParseDate(TextVariables.Expand(a.Date)) ?? DateOnly.FromDateTime(DateTime.Now);
            start = day.ToDateTime(a.AllDay ? TimeOnly.MinValue : EventTime.Parse(a.Time) ?? new TimeOnly(9, 0));
        }
        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0);
        var allDay = a.When == CalendarWhen.At && a.AllDay;
        var e = new CalendarEvent
        {
            Title = title,
            Start = allDay ? start.Date : start,
            End = allDay ? start.Date.AddDays(1) : start.AddMinutes(Math.Max(0, a.DurationMinutes)),
            AllDay = allDay,
            TimeZone = allDay ? null : CalendarTime.Iana(TimeZoneInfo.Local),
            Location = TextVariables.Expand(a.Location).Trim() is { Length: > 0 } place ? place : null,
            Notes = TextVariables.Expand(a.Notes).Trim() is { Length: > 0 } notes ? notes : null,
            Reminders = a.Reminder >= 0 ? [a.Reminder] : [],
        };
        if (a.Calendar.Split('/', 2) is [var source, var id] && calendar.Calendar(source, id) is not null)
            e = e with { SourceId = source, CalendarId = id };
        e = e with { MeetingUrl = MeetingLinks.Find(e.Location, e.Notes) };
        ctx.Token.ThrowIfCancellationRequested();
        await calendar.AddAsync(e, ctx.Token).ConfigureAwait(false);
        Gave(a, title);
        _ui.ShowOsd(Loc.T("osd.calendarAdded", title, (allDay ? start.ToString("d", Loc.Instance.Culture) : start.ToString("g", Loc.Instance.Culture))),
            OsdKind.Success, null, OsdLook.Of("CalendarAdd24"));
    }

    private Task CalendarJoinAsync(CalendarJoinAction a)
    {
        if (Calendar is not { } calendar) throw new InvalidOperationException(Loc.T("calendar.notReady"));
        var o = MeetingToJoin(calendar, a.Which, a.Calendar, DateTimeOffset.Now);
        if (o is null)
        {
            _ui.ShowOsd(Loc.T(a.Which == CalendarJoinWhich.Current ? "osd.calendarNoCurrent" : "osd.calendarNoNext"), OsdKind.Warning, null, OsdLook.Of("CalendarCancel24"));
            Gave(a, string.Empty);
            return Task.CompletedTask;
        }
        Gave(a, o.Event.MeetingUrl!);
        Platform.ProcessLauncher.Launch(new Platform.LaunchSpec(o.Event.MeetingUrl!), asAdmin: false);
        _ui.ShowOsd(Loc.T("osd.calendarJoining", o.Event.Title), OsdKind.Info, null, OsdLook.Of("Video24"));
        return Task.CompletedTask;
    }
}
