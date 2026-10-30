namespace KDesktop.Core.Calendar;

/// <summary>
/// The calendar's options (Calendar\settings.json, in the backups): the default calendar, the .ics subscriptions, the
/// accounts whose calendars are shown and how each calendar of a server looks here (name, colour, shown or not).
/// </summary>
public sealed class CalendarSettings
{
    /// <summary>Where new citas go when nobody says («local/personal»).</summary>
    public string? DefaultCalendar { get; set; }

    /// <summary>Also ring the reminders of Google and Outlook here (they already ring on the phone: off by default, so nothing rings twice).</summary>
    public bool AccountReminders { get; set; }

    /// <summary>Reminders: how they show (an OSD, a Windows notification, or said by an assistant).</summary>
    public ReminderStyle ReminderStyle { get; set; } = ReminderStyle.Osd;

    /// <summary>The assistant that says the reminders (with <see cref="ReminderStyle.Assistant"/>); null: the first one.</summary>
    public string? ReminderAssistant { get; set; }

    public List<IcsSubscriptionSettings> Subscriptions { get; set; } = [];

    /// <summary>The accounts (Accounts\) used for the calendar, with the calendars chosen in each.</summary>
    public List<CalendarAccountSettings> Accounts { get; set; } = [];

    /// <summary>How a calendar of a server looks here, by «source/calendar»: K-Desktop keeps it, not the server.</summary>
    public Dictionary<string, CalendarLook> Looks { get; set; } = [];
}

public enum ReminderStyle { Osd, Notification, Assistant }

/// <summary>A name, colour, visibility or default reminder chosen in K-Desktop for a calendar it does not own.</summary>
public sealed class CalendarLook
{
    public string? Name { get; set; }
    public string? Color { get; set; }
    public bool Visible { get; set; } = true;
    public int? DefaultReminder { get; set; }
}

/// <summary>A .ics read every few minutes (read only).</summary>
public sealed class IcsSubscriptionSettings
{
    public string Id { get; set; } = "ics-" + Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The address, encrypted with DPAPI (<see cref="Platform.Secrets"/>): a secret address of Google or Outlook gives
    /// anybody who has it the right to read the calendar.
    /// </summary>
    public string UrlProtected { get; set; } = string.Empty;

    public string Color { get; set; } = CalendarColors.Palette[1];

    /// <summary>Minutes between reads (15 by default; never less than 5).</summary>
    public int RefreshMinutes { get; set; } = 15;
}

/// <summary>An account of Accounts\ used for the calendar.</summary>
public sealed class CalendarAccountSettings
{
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Ids of the account's calendars shown in K-Desktop (empty the first time: its main one).</summary>
    public List<string> Calendars { get; set; } = [];
}
