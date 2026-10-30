using System.Globalization;
using KDesktop.Core.Calendar;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine.Events;

/// <summary>
/// «Cita del calendario» (calendar, phase 3): looks at the coming citas every little while and fires the events whose
/// moment (some minutes before, the start, the end) came since the last look. A moment that went by with the PC asleep
/// or K-Desktop closed is said once in «Lo que no se ejecutó» and not run late, as with «A una hora».
/// </summary>
public sealed partial class EventService
{
    private static readonly TimeSpan CalendarPoll = TimeSpan.FromSeconds(20);

    /// <summary>The calendar the events of citas look at; set by K-Desktop at start.</summary>
    public CalendarService? Calendar
    {
        get;
        set
        {
            if (field is not null) field.Changed -= OnCalendarChanged;
            field = value;
            if (field is not null) field.Changed += OnCalendarChanged;
        }
    }

    private Timer? _calendarTimer;
    private DateTimeOffset _calendarLast;

    /// <summary>The appearances already fired by each event («key|moment»), so none fires twice.</summary>
    private readonly Dictionary<string, HashSet<string>> _calendarFired = new(StringComparer.Ordinal);

    /// <summary>«Empieza una cita» running: when its «Al terminar» is due, and the variables of that cita.</summary>
    private readonly Dictionary<string, (DateTimeOffset End, CommandVariables Variables)> _calendarStarted = new(StringComparer.Ordinal);

    private void ConfigureCalendar()
    {
        var any = Calendar is not null && Rules<CalendarTrigger>().Any();
        foreach (var id in _calendarFired.Keys.Where(id => !_rules.ContainsKey(id)).ToList()) _calendarFired.Remove(id);
        if (!any)
        {
            _calendarTimer?.Dispose();
            _calendarTimer = null;
            return;
        }
        if (_calendarTimer is null)
        {
            _calendarLast = DateTimeOffset.UtcNow;
            _calendarTimer = new Timer(_ => OnCalendarTick(), null, CalendarPoll, CalendarPoll);
        }
    }

    private void OnCalendarChanged()
    {
        lock (_lock)
        {
            if (_disposed || _calendarTimer is null) return;
        }
        // A cita added for in a minute: looked at soon, not in 20 s.
        _calendarTimer?.Change(TimeSpan.FromSeconds(1), CalendarPoll);
    }

    private void OnCalendarTick()
    {
        if (Calendar is not { } calendar) return;
        var fire = new List<(EventRule Rule, bool End, CommandVariables Variables)>();
        var upcoming = calendar.Upcoming();
        lock (_lock)
        {
            if (_disposed || _calendarTimer is null) return;
            var now = DateTimeOffset.UtcNow;
            var since = _calendarLast;
            _calendarLast = now;
            if (now < since - TimeSpan.FromMinutes(1)) return; // the clock went back: start looking from now

            foreach (var (id, started) in _calendarStarted.ToList())
            {
                if (started.End > now || !_rules.TryGetValue(id, out var entry)) continue;
                _calendarStarted.Remove(id);
                fire.Add((entry.Rule, true, started.Variables));
            }

            foreach (var (rule, trigger) in Rules<CalendarTrigger>())
            {
                if (!_calendarFired.TryGetValue(rule.Id, out var done)) _calendarFired[rule.Id] = done = [];
                foreach (var o in upcoming)
                {
                    if (!trigger.Matches(o)) continue;
                    var at = trigger.MomentOf(o);
                    if (at <= since || at > now) continue;
                    var key = o.Key + "|" + trigger.Moment + trigger.Minutes.ToString(CultureInfo.InvariantCulture);
                    if (!done.Add(key)) continue;
                    if (now - at > LateLimit)
                    {
                        Log.Info($"Evento «{rule.Name}»: se pasó su momento ({at.ToLocalTime():HH:mm}, «{o.Event.Title}») con el PC dormido o el reloj cambiado");
                        Skip(rule, SkipReason.Missed, at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " · " + o.Event.Title);
                        continue;
                    }
                    var variables = Variables(o, calendar);
                    Log.Info($"Eventos: cita «{o.Event.Title}» ({trigger.Moment}) → «{rule.Name}»");
                    if (trigger.Moment == CalendarMoment.Start) _calendarStarted[rule.Id] = (o.End > o.Start ? o.End : o.Start, variables);
                    fire.Add((rule, false, variables));
                }
                // What is long past is forgotten.
                if (done.Count > 500) done.Clear();
            }
        }
        foreach (var (rule, end, variables) in fire) Fire(rule, end, variables);
    }

    /// <summary>{cita}, {cita_hora}, {cita_lugar}, {cita_notas}, {cita_enlace}, {cita_calendario} (and {appointment}…); {resultado}: the title.</summary>
    public static CommandVariables Variables(CalendarOccurrence o, CalendarService? calendar)
    {
        var v = new CommandVariables();
        var time = o.AllDay ? string.Empty : o.Start.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        var name = calendar?.CalendarOf(o.Series)?.Name ?? string.Empty;
        void Set(string es, string en, string? value)
        {
            v.SetResult(value ?? string.Empty, es);
            v.SetResult(value ?? string.Empty, en);
        }
        Set("cita_hora", "appointment_time", time);
        Set("cita_lugar", "appointment_place", o.Event.Location);
        Set("cita_notas", "appointment_notes", o.Event.Notes);
        Set("cita_enlace", "appointment_link", o.Event.MeetingUrl);
        Set("cita_calendario", "appointment_calendar", name);
        Set("cita", "appointment", o.Event.Title); // last: {resultado} is the title
        return v;
    }
}
