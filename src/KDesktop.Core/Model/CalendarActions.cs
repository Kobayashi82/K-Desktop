using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>«Crear cita»: when it is, absolute (a day and a time) or from now («dentro de 1 h»).</summary>
public enum CalendarWhen { In, At }

/// <summary>«Abrir la reunión»: the cita going on now, or the next one with a link.</summary>
public enum CalendarJoinWhich { Current, Next }

/// <summary>
/// «Crear cita» (calendar, phase 3.2): adds a cita to a calendar (empty: the default one) with a title and a place that
/// may have variables, at a moment or some minutes from now, for a while. It gives the title in {resultado}.
/// </summary>
public partial class CalendarAddAction : CommandAction
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private CalendarWhen _when = CalendarWhen.In;

    /// <summary>With «Dentro de»: minutes from now.</summary>
    [ObservableProperty] private int _inMinutes = 60;

    /// <summary>With «El día»: «yyyy-MM-dd» (empty: today) and «HH:mm».</summary>
    [ObservableProperty] private string _date = string.Empty;
    [ObservableProperty] private string _time = "09:00";

    [ObservableProperty] private int _durationMinutes = 60;
    [ObservableProperty] private bool _allDay;

    /// <summary>«source/calendar»; empty: the default calendar.</summary>
    [ObservableProperty] private string _calendar = string.Empty;
    [ObservableProperty] private string _location = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    /// <summary>Reminder in minutes before; -1: none (the calendar's default, if it has one).</summary>
    [ObservableProperty] private int _reminder = -1;

    /// <summary>The date for the editor's picker.</summary>
    [JsonIgnore]
    public DateTime? DateValue
    {
        get => EventTime.ParseDate(Date)?.ToDateTime(TimeOnly.MinValue);
        set => Date = value is { } d ? EventTime.FormatDate(DateOnly.FromDateTime(d)) : string.Empty;
    }

    partial void OnDateChanged(string value) => OnPropertyChanged(nameof(DateValue));

    [JsonIgnore] public override bool GivesResult => true;

    public override bool IsTransient(string? property) => base.IsTransient(property) || property is nameof(DateValue);

    public override ActionKind Kind => ActionKind.CalendarAdd;

    public override string Summary
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "…" : Title.Trim();
            return When == CalendarWhen.In
                ? Loc.T("summary.calendarAddIn", title, InMinutes)
                : Loc.T("summary.calendarAddAt", title, EventTime.ParseDate(Date)?.ToString("d", Loc.Instance.Culture) ?? Loc.T("calendar.today"), AllDay ? Loc.T("calendar.allDay") : Time);
        }
    }
}

/// <summary>
/// «Abrir la reunión» (calendar, phase 3.3): opens the link (Teams, Meet, Zoom…) of the cita going on now, or of the
/// next one that has one (up to 12 hours ahead); of any calendar or one.
/// </summary>
public partial class CalendarJoinAction : CommandAction
{
    [ObservableProperty] private CalendarJoinWhich _which = CalendarJoinWhich.Current;

    /// <summary>«source/calendar»; empty: any.</summary>
    [ObservableProperty] private string _calendar = string.Empty;

    [JsonIgnore] public override bool GivesResult => true;

    public override ActionKind Kind => ActionKind.CalendarJoin;

    public override string Summary => Loc.T($"summary.calendarJoin.{Which}");
}

/// <summary>
/// «Enviar correo» (mail, phase 3.3): a mail from one of the accounts (empty: the first one), to, subject and text with
/// variables, and files attached (one per line; {resultado} of «Captura de pantalla» attaches the capture). For reports
/// and notices of the commands themselves; «solo enviar» accounts only reach the addresses allowed.
/// </summary>
public partial class MailSendAction : CommandAction
{
    [ObservableProperty] private string _account = string.Empty;
    [ObservableProperty] private string _to = string.Empty;
    [ObservableProperty] private string _subject = string.Empty;
    [ObservableProperty] private string _body = string.Empty;

    /// <summary>Files to attach, one per line (variables allowed).</summary>
    [ObservableProperty] private string _attachments = string.Empty;

    /// <summary>Leave it as a draft instead of sending it.</summary>
    [ObservableProperty] private bool _draftOnly;

    [JsonIgnore] public override bool GivesResult => true;

    public override ActionKind Kind => ActionKind.MailSend;

    public override string Summary => Loc.T(DraftOnly ? "summary.mailDraft" : "summary.mailSend", string.IsNullOrWhiteSpace(To) ? "…" : To.Trim(), string.IsNullOrWhiteSpace(Subject) ? "…" : Subject.Trim());
}
