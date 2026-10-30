using KDesktop.Core.Calendar;

namespace KDesktop.Core.Tests;

public class CalendarIcsTests
{
    private const string Sample = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Google Inc//Google Calendar 70.9054//EN
        X-WR-CALNAME:Trabajo
        BEGIN:VTIMEZONE
        TZID:/mozilla.org/20050126_1/Europe/Madrid
        X-LIC-LOCATION:Europe/Madrid
        END:VTIMEZONE
        BEGIN:VEVENT
        DTSTART;TZID=Europe/Madrid:20260105T090000
        DTEND;TZID=Europe/Madrid:20260105T093000
        RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=4
        EXDATE;TZID=Europe/Madrid:20260112T090000
        UID:daily-1@google.com
        SUMMARY:Reunión diaria\, equipo
        DESCRIPTION:Enlace: https://meet.google.com/abc-defg-hij\nOrden del día
        LOCATION:Sala 2
        ATTENDEE;CN=Ana:mailto:ana@example.com
        LAST-MODIFIED:20251220T101010Z
        BEGIN:VALARM
        ACTION:DISPLAY
        TRIGGER:-PT10M
        END:VALARM
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;TZID=/mozilla.org/20050126_1/Europe/Madrid:20260119T110000
        DTEND;TZID=/mozilla.org/20050126_1/Europe/Madrid:20260119T113000
        RECURRENCE-ID;TZID=Europe/Madrid:20260119T090000
        UID:daily-1@google.com
        SUMMARY:Reunión movida
        END:VEVENT
        BEGIN:VEVENT
        DTSTART;VALUE=DATE:20260106
        DTEND;VALUE=DATE:20260107
        UID:reyes@holidays
        SUMMARY:Reyes
        END:VEVENT
        BEGIN:VEVENT
        DTSTART:20260210T150000Z
        DURATION:PT45M
        UID:utc@x
        SUMMARY:En UTC con una línea muy larga que hay que plegar porque pasa de setenta y cinco bytes de largo ñññ
        END:VEVENT
        END:VCALENDAR
        """;

    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReadsGoogleExport()
    {
        var ics = IcsFormat.Read(Sample, "ics-1", "main");
        Assert.Equal("Trabajo", ics.Name);
        Assert.Equal(3, ics.Events.Count);

        var series = ics.Events[0];
        Assert.Equal("Reunión diaria, equipo", series.Title);
        Assert.Equal("Europe/Madrid", series.TimeZone);
        Assert.Equal("Sala 2", series.Location);
        Assert.Equal("https://meet.google.com/abc-defg-hij", series.MeetingUrl);
        Assert.Contains("\nOrden del día", series.Notes);
        Assert.Equal([10], series.Reminders);
        Assert.Equal([new DateTime(2026, 1, 12, 9, 0, 0)], series.Exceptions);
        Assert.Single(series.Overrides);
        Assert.Contains(series.Extra, x => x.StartsWith("ATTENDEE", StringComparison.Ordinal));

        var titles = Recurrence.Expand(series, From, From.AddYears(1)).Select(o => o.Event.Title).ToList();
        Assert.Equal(["Reunión diaria, equipo", "Reunión movida", "Reunión diaria, equipo"], titles);

        var holiday = ics.Events[1];
        Assert.True(holiday.AllDay);
        Assert.Equal(new DateTime(2026, 1, 6), holiday.Start);

        var utc = ics.Events[2];
        Assert.Equal("UTC", utc.TimeZone);
        Assert.Equal(TimeSpan.FromMinutes(45), utc.Duration);
    }

    [Fact]
    public void RoundTripKeepsEverything()
    {
        var first = IcsFormat.Read(Sample);
        var text = IcsFormat.Write(first.Events, "Trabajo");
        Assert.All(text.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.Contains("BEGIN:VTIMEZONE", text);
        var second = IcsFormat.Read(text);
        Assert.Equal("Trabajo", second.Name);
        Assert.Equal(first.Events.Count, second.Events.Count);
        for (var i = 0; i < first.Events.Count; i++)
        {
            var a = first.Events[i];
            var b = second.Events[i];
            Assert.Equal(a.Title, b.Title);
            Assert.Equal(a.Start, b.Start);
            Assert.Equal(a.End, b.End);
            Assert.Equal(a.AllDay, b.AllDay);
            Assert.Equal(a.Recurrence, b.Recurrence);
            Assert.Equal(a.Exceptions, b.Exceptions);
            Assert.Equal(a.Reminders, b.Reminders);
            Assert.Equal(a.Overrides.Count, b.Overrides.Count);
            Assert.Equal(a.Notes, b.Notes);
            Assert.Equal(a.Extra, b.Extra);
            Assert.Equal(CalendarTime.StartOf(a), CalendarTime.StartOf(b));
        }
    }

    [Fact]
    public void DurationsAndTimes()
    {
        Assert.Equal(TimeSpan.FromMinutes(-10), IcsTime.ParseDuration("-PT10M"));
        Assert.Equal(TimeSpan.FromDays(8) + TimeSpan.FromHours(2), IcsTime.ParseDuration("P1W1DT2H"));
        Assert.Null(IcsTime.ParseDuration("10 minutos"));
        Assert.Equal("-PT10M", IcsTime.FormatDuration(TimeSpan.FromMinutes(-10)));
        Assert.Equal("P1D", IcsTime.FormatDuration(TimeSpan.FromDays(1)));
        Assert.True(IcsTime.TryParse("20261030T090000Z", out var t, out var utc, out var date));
        Assert.True(utc);
        Assert.False(date);
        Assert.Equal(new DateTime(2026, 10, 30, 9, 0, 0), t);
    }

    [Fact]
    public void BrokenLinesAreSkipped()
    {
        var ics = IcsFormat.Read("BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Sin fecha\nEND:VEVENT\nBEGIN:VEVENT\nDTSTART:2026\nEND:VEVENT\nBEGIN:VEVENT\nDTSTART:20260101\nSUMMARY:Bien\nEND:VEVENT\nEND:VCALENDAR");
        Assert.Equal("Bien", Assert.Single(ics.Events).Title);
    }

    [Fact]
    public void MeetingLinks()
    {
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/19%3a", Calendar.MeetingLinks.Find(null, "Unirse: https://teams.microsoft.com/l/meetup-join/19%3a."));
        Assert.Equal("https://us02web.zoom.us/j/123456", Calendar.MeetingLinks.Find("<https://us02web.zoom.us/j/123456>"));
        Assert.Null(Calendar.MeetingLinks.Find("https://example.com/teams"));
    }
}
