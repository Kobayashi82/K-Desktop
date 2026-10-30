using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>What the editor of a cita gives back: the cita as it was left, or that it is to be deleted.</summary>
public sealed record CalendarEventResult(CalendarEvent Event, bool Delete);

/// <summary>
/// A cita, new or to change: title, calendar, when (all day or with hours), how it repeats, reminder, place, meeting
/// link and notes. The times are those of the cita's own zone (said below them when it is not the PC's). A cita of a
/// read-only calendar is only shown.
/// </summary>
public partial class CalendarEventDialog : FluentWindow
{
    public sealed record CalendarOption(CalendarInfo Info)
    {
        public string Name => Info.Name;
        public Brush Brush => Pages.AgendaPage.Brush(Info.Color);
    }

    private sealed record Option(string Key, string Name, int Value = 0);

    private CalendarEvent _original = new();
    private RecurrenceRule? _customRule;
    private bool _readOnly;

    /// <summary>The series' rule as it came, and what the form makes of it untouched: not touched, it is kept as it was.</summary>
    private string? _seriesRule, _loadedRule;

    public CalendarEventResult? Result { get; private set; }

    public CalendarEventDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>Opens the editor; <paramref name="occurrence"/> null: a new cita.</summary>
    public static CalendarEventResult? Edit(Window? owner, CalendarEvent e, CalendarOccurrence? occurrence)
    {
        var dlg = new CalendarEventDialog { Owner = owner };
        dlg.Load(e, occurrence);
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    private void Load(CalendarEvent e, CalendarOccurrence? occurrence)
    {
        _original = e;
        var calendar = App.Current.Calendar;
        var source = occurrence is null ? null : calendar.Source(occurrence.Series.SourceId);
        var info = occurrence is null ? null : calendar.CalendarOf(occurrence.Series);
        _readOnly = occurrence is not null && (info?.ReadOnly == true || source?.Capabilities.HasFlag(CalendarCapabilities.Write) != true);

        TitleBar.Title = Loc.T(occurrence is null ? "agenda.dialogNew" : _readOnly ? "agenda.dialogSee" : "agenda.dialogEdit");
        TitleBox.Text = e.Title;
        LocationBox.Text = e.Location ?? string.Empty;
        MeetingBox.Text = e.MeetingUrl ?? string.Empty;
        NotesBox.Text = e.Notes ?? string.Empty;

        // Only calendars of the same source can be chosen for a series (moving a series to another server makes a copy).
        var writable = calendar.Calendars
            .Where(c => !c.ReadOnly && calendar.Source(c.SourceId)?.Capabilities.HasFlag(CalendarCapabilities.Write) == true)
            .ToList();
        var options = (_readOnly && info is not null ? [info] : writable).Select(c => new CalendarOption(c)).ToList();
        CalendarBox.ItemsSource = options;
        var current = info ?? calendar.DefaultCalendar;
        CalendarBox.SelectedItem = options.FirstOrDefault(o => o.Info.SourceId == current.SourceId && o.Info.Id == current.Id) ?? options.FirstOrDefault();

        AllDayCheck.IsChecked = e.AllDay;
        StartDate.Date = e.Start.Date;
        StartTime.Time = e.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        var end = e.End > e.Start ? e.End : e.Start.AddHours(1);
        // All day: the last day is shown (iCalendar keeps the day after).
        EndDate.Date = e.AllDay ? end.Date.AddDays(-1) < e.Start.Date ? e.Start.Date : end.Date.AddDays(-1) : end.Date;
        EndTime.Time = end.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (!e.AllDay && e.TimeZone is { } zone && CalendarTime.Zone(zone).Id != TimeZoneInfo.Local.Id)
        {
            ZoneText.Text = Loc.T("agenda.field.zone", CalendarTime.Zone(zone).DisplayName);
            ZoneText.Visibility = Visibility.Visible;
        }

        LoadRepeat(e, occurrence);
        LoadReminder(e);
        OnAllDayChanged(this, new RoutedEventArgs());
        _seriesRule = occurrence?.Series.Recurrence ?? e.Recurrence;
        _loadedRule = BuildRule(e.Start, e.TimeZone, e.AllDay);

        if (occurrence is not null && !_readOnly) DeleteButton.Visibility = Visibility.Visible;
        if (_readOnly)
        {
            ReadOnlyText.Text = Loc.T("agenda.readOnly", info?.Name ?? source?.Name ?? string.Empty);
            ReadOnlyBox.Visibility = Visibility.Visible;
            foreach (var child in Form.Children.OfType<FrameworkElement>().Where(c => c != ReadOnlyBox)) child.IsEnabled = false;
            SaveButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = Loc.T("common.close");
        }
    }

    // ── repeat ───────────────────────────────────────────────────────────

    private void LoadRepeat(CalendarEvent e, CalendarOccurrence? occurrence)
    {
        var start = e.Start;
        var week = (start.Day - 1) / 7 + 1;
        var lastWeek = start.Day + 7 > DateTime.DaysInMonth(start.Year, start.Month);
        var dayName = Loc.Instance.Culture.DateTimeFormat.GetDayName(start.DayOfWeek);
        var options = new List<Option>
        {
            new("none", Loc.T("calendar.repeat.none")),
            new("daily", Loc.T("calendar.repeat.daily")),
            new("workdays", Loc.T("agenda.repeat.workdays")),
            new("weekly", Loc.T("calendar.repeat.weekly")),
            new("monthly", Loc.T("agenda.repeat.monthlyDay", start.Day)),
            new("monthlyNth", Loc.T("agenda.repeat.monthlyNth", Loc.T("calendar.ordinal." + Math.Min(week, 4)), dayName)),
        };
        if (lastWeek) options.Add(new("monthlyLast", Loc.T("agenda.repeat.monthlyNth", Loc.T("calendar.ordinal.last"), dayName)));
        options.Add(new("yearly", Loc.T("agenda.repeat.yearly", start.ToString(Loc.Instance.Culture.DateTimeFormat.MonthDayPattern, Loc.Instance.Culture))));

        var rule = RecurrenceRule.Parse(occurrence?.Series.Recurrence ?? e.Recurrence);
        var key = rule is null ? "none" : Recognise(rule, start);
        if (key == "custom")
        {
            _customRule = rule;
            options.Add(new("custom", Loc.T("agenda.repeat.custom")));
            CustomText.Text = CalendarText.Repeat(rule!.ToString());
        }
        RepeatBox.ItemsSource = options;
        RepeatBox.SelectedItem = options.First(o => o.Key == key);

        EndsBox.ItemsSource = new List<Option> { new("never", Loc.T("agenda.ends.never")), new("until", Loc.T("agenda.ends.until")), new("count", Loc.T("agenda.ends.count")) };
        if (rule?.Count is { } count)
        {
            CountBox.Value = count;
            EndsBox.SelectedIndex = 2;
        }
        else if (rule?.Until is { } until)
        {
            UntilDate.Date = rule.UntilUtc ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(until, DateTimeKind.Utc), TimeZoneInfo.Local).Date : until.Date;
            EndsBox.SelectedIndex = 1;
        }
        else EndsBox.SelectedIndex = 0;
        if (rule is not null)
        {
            IntervalBox.Value = rule.Interval;
            if (rule.Frequency == RecurrenceFrequency.Weekly)
                DaysPicker.Days = rule.ByDay.Count == 0 ? WeekDaysExtensions.Of(start.DayOfWeek) : rule.ByDay.Aggregate(WeekDays.None, (all, d) => all | WeekDaysExtensions.Of(d.Day));
        }
        if (DaysPicker.Days == WeekDays.None) DaysPicker.Days = WeekDaysExtensions.Of(start.DayOfWeek);
    }

    /// <summary>Which simple choice a rule is, or «custom» (kept as it is unless another one is chosen).</summary>
    private static string Recognise(RecurrenceRule r, DateTime start)
    {
        if (r.ByMonth.Count > 0 || r.BySetPos.Count > 0 || r.Rest.Count > 0) return "custom";
        switch (r.Frequency)
        {
            case RecurrenceFrequency.Daily when r.ByDay.Count == 0 && r.ByMonthDay.Count == 0:
                return "daily";
            case RecurrenceFrequency.Weekly when r.ByMonthDay.Count == 0 && r.ByDay.All(d => d.Ordinal == 0):
                var days = r.ByDay.Select(d => d.Day).ToHashSet();
                return r.Interval == 1 && days.SetEquals([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]) ? "workdays" : "weekly";
            case RecurrenceFrequency.Monthly when r.ByDay.Count == 0 && (r.ByMonthDay.Count == 0 || r.ByMonthDay is [var d] && d == start.Day):
                return "monthly";
            case RecurrenceFrequency.Monthly when r.ByMonthDay.Count == 0 && r.ByDay is [var nth] && nth.Day == start.DayOfWeek:
                return nth.Ordinal == -1 ? "monthlyLast" : nth.Ordinal == Math.Min((start.Day - 1) / 7 + 1, 4) ? "monthlyNth" : "custom";
            case RecurrenceFrequency.Yearly when r.ByDay.Count == 0 && r.ByMonthDay.Count == 0:
                return "yearly";
            default:
                return "custom";
        }
    }

    private void OnRepeatChanged(object sender, SelectionChangedEventArgs e)
    {
        var key = (RepeatBox.SelectedItem as Option)?.Key ?? "none";
        RepeatPanel.Visibility = key == "none" ? Visibility.Collapsed : Visibility.Visible;
        DaysPicker.Visibility = key == "weekly" ? Visibility.Visible : Visibility.Collapsed;
        IntervalRow.Visibility = key is "workdays" or "custom" ? Visibility.Collapsed : Visibility.Visible;
        CustomText.Visibility = key == "custom" ? Visibility.Visible : Visibility.Collapsed;
        IntervalUnit.Text = key switch
        {
            "daily" => Loc.T("agenda.unit.days"),
            "weekly" => Loc.T("agenda.unit.weeks"),
            "yearly" => Loc.T("agenda.unit.years"),
            _ => Loc.T("agenda.unit.months"),
        };
    }

    private void OnEndsChanged(object sender, SelectionChangedEventArgs e)
    {
        UntilDate.Visibility = EndsBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        CountBox.Visibility = EndsBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (EndsBox.SelectedIndex == 1 && UntilDate.Date is null) UntilDate.Date = (StartDate.Date ?? DateTime.Today).AddMonths(3);
    }

    /// <summary>The RRULE chosen (null: it does not repeat).</summary>
    private string? BuildRule(DateTime start, string? zone, bool allDay)
    {
        var key = (RepeatBox.SelectedItem as Option)?.Key ?? "none";
        if (key == "none") return null;
        var interval = Math.Max(1, IntervalBox.Value);
        RecurrenceRule rule = key switch
        {
            "daily" => new() { Frequency = RecurrenceFrequency.Daily, Interval = interval },
            "workdays" => new()
            {
                Frequency = RecurrenceFrequency.Weekly,
                ByDay = [new(0, DayOfWeek.Monday), new(0, DayOfWeek.Tuesday), new(0, DayOfWeek.Wednesday), new(0, DayOfWeek.Thursday), new(0, DayOfWeek.Friday)],
            },
            "weekly" => new()
            {
                Frequency = RecurrenceFrequency.Weekly, Interval = interval,
                ByDay = [.. Enum.GetValues<DayOfWeek>().Where(d => DaysPicker.Days != WeekDays.None && DaysPicker.Days.Includes(d)).Select(d => new WeekdayNum(0, d))],
            },
            "monthly" => new() { Frequency = RecurrenceFrequency.Monthly, Interval = interval },
            "monthlyNth" => new() { Frequency = RecurrenceFrequency.Monthly, Interval = interval, ByDay = [new(Math.Min((start.Day - 1) / 7 + 1, 4), start.DayOfWeek)] },
            "monthlyLast" => new() { Frequency = RecurrenceFrequency.Monthly, Interval = interval, ByDay = [new(-1, start.DayOfWeek)] },
            "yearly" => new() { Frequency = RecurrenceFrequency.Yearly, Interval = interval },
            _ => (_customRule ?? new RecurrenceRule()) with { Count = null, Until = null, UntilUtc = false, UntilIsDate = false },
        };
        if (EndsBox.SelectedIndex == 2) rule = rule with { Count = Math.Max(1, CountBox.Value) };
        else if (EndsBox.SelectedIndex == 1 && UntilDate.Date is { } until)
        {
            var lastMoment = until.Date.AddDays(1).AddSeconds(-1);
            if (allDay) rule = rule with { Until = until.Date, UntilIsDate = true };
            else if (zone is null) rule = rule with { Until = lastMoment };
            else
            {
                var utc = CalendarTime.ToInstant(lastMoment, CalendarTime.Zone(zone)).UtcDateTime;
                rule = rule with { Until = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), UntilUtc = true };
            }
        }
        return rule.ToString();
    }

    // ── reminder ─────────────────────────────────────────────────────────

    private static readonly int[] Reminders = [0, 5, 10, 15, 30, 60, 120, 1440, 2880, 10080];

    private void LoadReminder(CalendarEvent e)
    {
        var options = new List<Option> { new("none", Loc.T("agenda.reminder.none"), -1) };
        var values = Reminders.ToList();
        if (e.Reminders.Count > 0 && !values.Contains(e.Reminders[0])) values.Add(e.Reminders[0]);
        foreach (var m in values.Order())
            options.Add(new("m", m == 0 ? Loc.T("agenda.reminder.atStart") : Loc.T("agenda.reminder.before", CalendarText.Minutes(m)), m));
        ReminderBox.ItemsSource = options;
        ReminderBox.SelectedItem = e.Reminders.Count == 0 ? options[0] : options.First(o => o.Value == e.Reminders[0] && o.Key == "m");
    }

    // ── result ───────────────────────────────────────────────────────────

    private void OnAllDayChanged(object sender, RoutedEventArgs e)
    {
        var allDay = AllDayCheck.IsChecked == true;
        StartTime.Visibility = EndTime.Visibility = allDay ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var allDay = AllDayCheck.IsChecked == true;
        if (StartDate.Date is not { } startDay)
        {
            StartDate.Focus();
            return;
        }
        var endDay = EndDate.Date ?? startDay;
        DateTime start, end;
        if (allDay)
        {
            start = startDay.Date;
            end = (endDay.Date < start ? start : endDay.Date).AddDays(1);
        }
        else
        {
            var startTime = EventTime.Parse(StartTime.Time) ?? new TimeOnly(9, 0);
            var endTime = EventTime.Parse(EndTime.Time) ?? startTime.AddHours(1);
            start = startDay.Date + startTime.ToTimeSpan();
            end = endDay.Date + endTime.ToTimeSpan();
            if (end < start) end = start.AddHours(1);
        }
        var zone = allDay ? null : _original.TimeZone ?? (string.IsNullOrEmpty(_original.Id) ? CalendarTime.Iana(TimeZoneInfo.Local) : null);
        var calendar = (CalendarBox.SelectedItem as CalendarOption)?.Info;
        var reminder = (ReminderBox.SelectedItem as Option)?.Value ?? -1;
        var meeting = MeetingBox.Text.Trim();
        var rule = BuildRule(start, zone, allDay);
        if (rule == _loadedRule && allDay == _original.AllDay) rule = _seriesRule;
        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            TitleBox.Focus();
            return;
        }
        Result = new CalendarEventResult(_original with
        {
            Title = title,
            SourceId = calendar?.SourceId ?? _original.SourceId,
            CalendarId = calendar?.Id ?? _original.CalendarId,
            AllDay = allDay,
            Start = start,
            End = end,
            TimeZone = zone,
            Location = string.IsNullOrWhiteSpace(LocationBox.Text) ? null : LocationBox.Text.Trim(),
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            MeetingUrl = meeting.Length == 0 ? MeetingLinks.Find(LocationBox.Text, NotesBox.Text) : meeting,
            Recurrence = rule,
            Reminders = reminder < 0 ? [] : [reminder],
        }, false);
        DialogResult = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        Result = new CalendarEventResult(_original, true);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
