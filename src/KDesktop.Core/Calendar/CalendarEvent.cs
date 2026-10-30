using System.Text.Json.Serialization;

namespace KDesktop.Core.Calendar;

/// <summary>
/// A cita, the way iCalendar sees one (VEVENT), so a .ics goes in and out without losing anything and every source
/// (the own one, a .ics subscription, Google, Outlook) is translated to the same thing.
/// <para>
/// <see cref="Start"/> and <see cref="End"/> are wall-clock times in <see cref="TimeZone"/> (<c>TZID</c>): a weekly
/// meeting at 9:00 in Madrid stays at 9:00 when summer time comes. No zone: the PC's (a «floating» time). An all-day
/// one keeps only the dates (End is the day after the last one, as in iCalendar), so it never moves with the zone.
/// </para>
/// </summary>
public sealed record CalendarEvent
{
    /// <summary>Its id in its source (the own calendar: the same as <see cref="Uid"/>; Google, Graph: theirs).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>iCalendar <c>UID</c>: the same in every copy of the cita (a .ics exported and imported again).</summary>
    public string Uid { get; init; } = string.Empty;

    /// <summary>The source it comes from (<see cref="ICalendarSource.Id"/>).</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>The calendar inside its source (<see cref="CalendarInfo.Id"/>).</summary>
    public string CalendarId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>Wall-clock start in <see cref="TimeZone"/> (all day: the date at 0:00).</summary>
    public DateTime Start { get; init; }

    /// <summary>Wall-clock end (exclusive). All day: the day after the last one at 0:00.</summary>
    public DateTime End { get; init; }

    public bool AllDay { get; init; }

    /// <summary>IANA («Europe/Madrid») or Windows («Romance Standard Time») id; null: the PC's own zone.</summary>
    public string? TimeZone { get; init; }

    public string? Location { get; init; }

    public string? Notes { get; init; }

    /// <summary>The link of an online meeting (Teams, Meet, Zoom), when the source says it or it is in the text.</summary>
    public string? MeetingUrl { get; init; }

    /// <summary>iCalendar <c>RRULE</c> without the «RRULE:» («FREQ=WEEKLY;BYDAY=MO,WE»); null: it does not repeat.</summary>
    public string? Recurrence { get; init; }

    /// <summary><c>EXDATE</c>: wall-clock starts of the appearances deleted from the series.</summary>
    public IReadOnlyList<DateTime> Exceptions { get; init; } = [];

    /// <summary>
    /// Appearances of the series changed one by one (<c>RECURRENCE-ID</c>): each says which one it replaces in
    /// <see cref="RecurrenceId"/>; <see cref="Cancelled"/> when that one was deleted (Google says it that way).
    /// </summary>
    public IReadOnlyList<CalendarEvent> Overrides { get; init; } = [];

    /// <summary>In an override: the wall-clock start (in the series' zone) of the appearance it replaces.</summary>
    public DateTime? RecurrenceId { get; init; }

    /// <summary>An override that deletes its appearance.</summary>
    public bool Cancelled { get; init; }

    /// <summary>Reminders, in minutes before the start (10 = «10 min antes»).</summary>
    public IReadOnlyList<int> Reminders { get; init; } = [];

    /// <summary>The source's version of it (HTTP ETag) for <c>If-Match</c>; null in the own calendar.</summary>
    public string? Etag { get; init; }

    /// <summary>Last change (iCalendar <c>LAST-MODIFIED</c>).</summary>
    public DateTimeOffset Updated { get; init; }

    /// <summary>Whatever else came in a .ics that K-Desktop does not use (attendees, categories…), to write it back.</summary>
    public IReadOnlyList<string> Extra { get; init; } = [];

    [JsonIgnore] public bool IsRecurring => !string.IsNullOrWhiteSpace(Recurrence);

    [JsonIgnore] public TimeSpan Duration => End > Start ? End - Start : AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;

    /// <summary>A new UID, like the ones the own calendar makes.</summary>
    public static string NewUid() => Guid.NewGuid().ToString("N") + "@k-desktop";
}

/// <summary>
/// One appearance of a cita in time: a cita that does not repeat has one; a series, one per repetition in the range
/// asked. Start and End are real instants (already in the PC's zone when shown).
/// </summary>
/// <param name="Event">The cita (the series, or the override that replaces this appearance).</param>
/// <param name="Series">The series it belongs to (the same as Event when it does not repeat).</param>
/// <param name="RecurrenceId">In a series, which appearance it is (wall-clock start in the series' zone).</param>
public sealed record CalendarOccurrence(CalendarEvent Event, CalendarEvent Series, DateTimeOffset Start, DateTimeOffset End, DateTime? RecurrenceId)
{
    public bool AllDay => Event.AllDay;

    /// <summary>A stable key of this appearance: the cita's source, id and which repetition.</summary>
    public string Key => $"{Series.SourceId}/{Series.Id}" + (RecurrenceId is { } r ? "/" + r.ToString("yyyyMMddTHHmmss", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);

    /// <summary>
    /// The cita as this appearance, with its own start and end (in the series' zone): what is changed when only this
    /// one, or this one and the following, change.
    /// </summary>
    public CalendarEvent Appearance => RecurrenceId is { } r && ReferenceEquals(Event, Series) ? Event with { Start = r, End = r + Event.Duration } : Event;

    /// <summary>All day: the first date, as it is (no zone).</summary>
    public DateOnly Date => DateOnly.FromDateTime(AllDay ? Event.Start : Start.LocalDateTime);
}

/// <summary>A calendar inside a source: «Personal», «Trabajo», a .ics of the holidays…</summary>
public sealed record CalendarInfo
{
    public string Id { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    /// <summary>The name the user chose (the assistant understands it better than «Outlook»).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>«#3B82F6».</summary>
    public string Color { get; init; } = CalendarColors.Default;

    public bool ReadOnly { get; init; }

    /// <summary>Shown in the agenda. A hidden one still counts for the events and the assistant.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Reminder for new citas of this calendar, in minutes before (null: none).</summary>
    public int? DefaultReminder { get; init; }
}

/// <summary>What a source can do: what it cannot is explained instead of failing.</summary>
[Flags]
public enum CalendarCapabilities
{
    None = 0,
    /// <summary>Citas can be created, changed and deleted.</summary>
    Write = 1,
    /// <summary>Series (RRULE) can be created and changed.</summary>
    Recurrence = 2,
    /// <summary>Single appearances of a series can be changed or deleted.</summary>
    Overrides = 4,
    /// <summary>It keeps reminders of its own (Google and Outlook ring on the phone too).</summary>
    Reminders = 8,
    /// <summary>Each calendar has a colour of its own the source keeps.</summary>
    Colors = 16,
    /// <summary>Calendars can be added and removed.</summary>
    ManageCalendars = 32,
    All = Write | Recurrence | Overrides | Reminders | Colors | ManageCalendars,
}

public static class CalendarColors
{
    public const string Default = "#3B82F6";

    /// <summary>The colours offered for a new calendar, in order.</summary>
    public static IReadOnlyList<string> Palette { get; } =
        ["#3B82F6", "#22C55E", "#EF4444", "#F59E0B", "#8B5CF6", "#EC4899", "#14B8A6", "#64748B", "#F97316", "#0EA5E9"];
}
