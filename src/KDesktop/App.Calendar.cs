using System.Globalization;
using KDesktop.Core;
using KDesktop.Core.Calendar;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop;

/// <summary>
/// The calendar in K-Desktop's process (the agenda, the reminders, the events, the assistant and the phone ask it): it
/// works with the assistant off. Its reminders are an OSD, a notification card or said by an assistant.
/// </summary>
public partial class App
{
    public CalendarService Calendar { get; private set; } = null!;

    private void CreateCalendar()
    {
        Calendar = new CalendarService(CalendarService.DefaultFolder, Secrets.Protect, Secrets.Unprotect);
        Calendar.ReminderDue += r => Dispatcher.BeginInvoke(() => ShowCalendarReminder(r));
        CreateAccounts();
        CreateMail();
    }

    /// <summary>The Google accounts (Opciones › Cuentas): the calendar and the mail use them.</summary>
    public Core.Accounts.AccountService Accounts { get; private set; } = null!;

    private void CreateAccounts()
    {
        Accounts = new Core.Accounts.AccountService(Core.Accounts.AccountService.DefaultFolder, Secrets.Protect, Secrets.Unprotect)
        {
            // Signing in in the user's own browser, not one elevated like K-Desktop.
            OpenBrowser = url => ProcessLauncher.Launch(new LaunchSpec(url), asAdmin: false),
        };
        var http = CalendarService.CreateHttp();
        Calendar.AccountSources = settings => Accounts.Get(settings.AccountId) is null ? null
            : new GoogleCalendar(Accounts, settings, Calendar.CacheFile(CalendarService.AccountSourceId(settings.AccountId)), http);
        // An account disconnected or connected again: its calendars say it.
        Accounts.Changed += () => Calendar.NotifyChanged();
    }

    private void StartCalendar()
    {
        Web.Calendar = Calendar;
        Calendar.Changed += () => Web?.NotifyCalendarChanged();
        StartMail();
        try
        {
            Calendar.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Calendario: no se ha podido iniciar", ex);
        }
    }

    /// <summary>«Dentista · dentro de 10 min (17:00, Consulta 3)».</summary>
    public static string ReminderText(CalendarReminder r)
    {
        var o = r.Occurrence;
        var when = o.AllDay ? Loc.T("calendar.allDay") : o.Start.ToLocalTime().ToString("t", Loc.Instance.Culture);
        var lead = r.Minutes == 0 ? Loc.T("calendar.reminder.now") : Loc.T("calendar.reminder.in", CalendarText.Minutes(r.Minutes));
        var place = string.IsNullOrWhiteSpace(o.Event.Location) ? string.Empty : ", " + o.Event.Location;
        return $"{o.Event.Title} · {lead} ({when}{place})";
    }

    /// <summary>«Probar»: a reminder shown as the options say.</summary>
    public void ShowCalendarReminderPreview(CalendarReminder r) => ShowCalendarReminder(r);

    private void ShowCalendarReminder(CalendarReminder r)
    {
        var style = Calendar.Settings.ReminderStyle;
        var text = ReminderText(r);
        if (style == ReminderStyle.Assistant)
        {
            var id = Calendar.Settings.ReminderAssistant ?? Core.Assistant.AssistantTargeting.KDesktop;
            if (_assistant is not null && IsAssistant(id) && IsOnDesktop(id))
            {
                SayToAssistant(id, Loc.T("calendar.reminder.say", text), true);
                return;
            }
            style = ReminderStyle.Osd;
        }
        if (style == ReminderStyle.Notification)
        {
            var meeting = r.Occurrence.Event.MeetingUrl;
            _ = Ui.ShowNotificationAsync(new Notification(r.Occurrence.Event.Title, text, "kd:CalendarClock24:blue", NotifyColor.Blue,
                ShowNotificationAction.GlobalSound, TimeSpan.FromSeconds(20), null,
                () =>
                {
                    if (meeting is not null) OpenMeeting(meeting);
                    else ShowMainWindow(w => w.Navigate("agenda"));
                }));
            return;
        }
        Ui.ShowOsd(text, OsdKind.Info, TimeSpan.FromSeconds(10), OsdLook.Of("CalendarClock24"));
    }

    /// <summary>An assistant's request to the calendar (calendar_*), answered off the UI thread: a server may be slow.</summary>
    private void OnCalendar(Core.Assistant.AssistantSupervisor supervisor, Core.Assistant.Pipe.CalendarMessage request) =>
        _ = Task.Run(async () =>
        {
            CalendarAnswer answer;
            try
            {
                answer = await CalendarAssistant.HandleAsync(Calendar, request.Request, DateTime.Now).ConfigureAwait(false);
                if (answer.Ok && request.Request.Op is "add" or "change" or "delete")
                    Log.Info($"Asistente: calendario «{request.Request.Op}» ({request.Assistant ?? "módulo"})");
            }
            catch (Exception ex)
            {
                Log.Error("Asistente: error en el calendario", ex);
                answer = new CalendarAnswer(false, Reason: "error", Message: ex.Message);
            }
            supervisor.Send(new Core.Assistant.Pipe.CalendarDoneMessage(request.Id, answer));
        });

    /// <summary>Opens the link of an online meeting (Teams, Meet, Zoom) in its program or the browser.</summary>
    public static void OpenMeeting(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        if (!ProcessLauncher.Launch(new LaunchSpec(uri.AbsoluteUri), asAdmin: false)) Log.Warn($"Calendario: no se ha podido abrir {url}");
    }
}

/// <summary>The calendar's texts that are worked out (durations, repetitions, days).</summary>
public static class CalendarText
{
    /// <summary>«10 min», «1 h», «1 h 30 min», «2 días».</summary>
    public static string Minutes(int minutes)
    {
        if (minutes % 1440 == 0 && minutes >= 1440) return Loc.T(minutes == 1440 ? "calendar.days.one" : "calendar.days", minutes / 1440);
        if (minutes >= 60)
        {
            var h = minutes / 60;
            var m = minutes % 60;
            return m == 0 ? Loc.T("calendar.hours", h) : Loc.T("calendar.hoursMinutes", h, m);
        }
        return Loc.T("calendar.minutes", minutes);
    }

    /// <summary>«Hoy», «Mañana», «Ayer» or «jueves, 5 de noviembre».</summary>
    public static string Day(DateOnly day, DateOnly today)
    {
        if (day == today) return Loc.T("calendar.today");
        if (day == today.AddDays(1)) return Loc.T("calendar.tomorrow");
        if (day == today.AddDays(-1)) return Loc.T("calendar.yesterday");
        var culture = Loc.Instance.Culture;
        var text = day.ToString(day.Year == today.Year ? culture.DateTimeFormat.MonthDayPattern : culture.DateTimeFormat.LongDatePattern, culture);
        var weekday = culture.DateTimeFormat.GetDayName(day.DayOfWeek);
        return text.Contains(weekday, StringComparison.OrdinalIgnoreCase) ? text : weekday + ", " + text;
    }

    /// <summary>«9:00 – 10:30», «Todo el día», «Desde las 22:00» (one that goes on into the next day).</summary>
    public static string Hours(CalendarOccurrence o, DateOnly day)
    {
        if (o.AllDay) return Loc.T("calendar.allDay");
        var culture = Loc.Instance.Culture;
        var start = o.Start.ToLocalTime();
        var end = o.End.ToLocalTime();
        var startsToday = DateOnly.FromDateTime(start.DateTime) == day;
        var endsToday = DateOnly.FromDateTime(end.DateTime) == day || (end.TimeOfDay == TimeSpan.Zero && DateOnly.FromDateTime(end.DateTime) == day.AddDays(1));
        if (startsToday && endsToday) return end > start ? $"{start.ToString("t", culture)} – {end.ToString("t", culture)}" : start.ToString("t", culture);
        if (startsToday) return Loc.T("calendar.from", start.ToString("t", culture));
        if (endsToday) return Loc.T("calendar.until", end.ToString("t", culture));
        return Loc.T("calendar.allDay");
    }

    /// <summary>«Cada semana: lunes y miércoles · 10 veces», «Cada 2 meses», «Cada año hasta el 1/1/2030».</summary>
    public static string Repeat(string? rrule)
    {
        if (RecurrenceRule.Parse(rrule) is not { } rule) return Loc.T("calendar.repeat.none");
        var culture = Loc.Instance.Culture;
        var unit = rule.Frequency.ToString().ToLowerInvariant();
        var text = rule.Interval == 1 ? Loc.T($"calendar.repeat.{unit}") : Loc.T($"calendar.repeat.{unit}N", rule.Interval);
        if (rule.ByDay.Count > 0)
        {
            var days = rule.ByDay.Select(d => (d.Ordinal switch
            {
                0 => string.Empty,
                -1 => Loc.T("calendar.ordinal.last") + " ",
                _ => Loc.T("calendar.ordinal." + Math.Clamp(d.Ordinal, 1, 5)) + " ",
            }) + culture.DateTimeFormat.GetDayName(d.Day));
            text += ": " + string.Join(", ", days);
        }
        else if (rule.ByMonthDay.Count > 0)
            text += ": " + string.Join(", ", rule.ByMonthDay.Select(n => n == -1 ? Loc.T("calendar.lastDay") : n.ToString(CultureInfo.InvariantCulture)));
        if (rule.Count is { } count) text += " · " + Loc.T("calendar.repeat.times", count);
        else if (rule.Until is { } until) text += " · " + Loc.T("calendar.repeat.until", until.ToString("d", culture));
        return text;
    }
}
