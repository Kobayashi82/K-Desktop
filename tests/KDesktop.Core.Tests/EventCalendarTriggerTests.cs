using KDesktop.Core.Calendar;
using KDesktop.Core.Engine;
using KDesktop.Core.Engine.Events;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

public class EventCalendarTriggerTests
{
    private static CalendarOccurrence Occurrence(string title, string? place = null, string? link = null, bool allDay = false)
    {
        var start = new DateTime(2026, 11, 5, 17, 0, 0);
        var e = new CalendarEvent
        {
            Id = "1", Uid = "1", SourceId = "local", CalendarId = "work", Title = title, Location = place, MeetingUrl = link,
            Start = allDay ? start.Date : start, End = allDay ? start.Date.AddDays(1) : start.AddHours(1), AllDay = allDay, TimeZone = allDay ? null : "Europe/Madrid",
        };
        return Recurrence.Expand(e, DateTimeOffset.MinValue.AddYears(2000), DateTimeOffset.MaxValue.AddYears(-1)).Single();
    }

    [Fact]
    public void FiltersByTitleCalendarPlaceAndLink()
    {
        var meeting = Occurrence("Reunión de equipo", link: "https://teams.microsoft.com/l/x");
        Assert.True(new CalendarTrigger().Matches(meeting));
        Assert.True(new CalendarTrigger { TitleContains = "REUNION" }.Matches(meeting));
        Assert.False(new CalendarTrigger { TitleContains = "dentista" }.Matches(meeting));
        Assert.True(new CalendarTrigger { Calendar = "local/work" }.Matches(meeting));
        Assert.False(new CalendarTrigger { Calendar = "local/personal" }.Matches(meeting));
        Assert.True(new CalendarTrigger { OnlyOnline = true }.Matches(meeting));
        Assert.False(new CalendarTrigger { OnlyWithPlace = true }.Matches(meeting));
        Assert.False(new CalendarTrigger().Matches(Occurrence("Fiesta", allDay: true)));
        Assert.True(new CalendarTrigger { IncludeAllDay = true }.Matches(Occurrence("Fiesta", allDay: true)));
    }

    [Fact]
    public void MomentsAndEnd()
    {
        var o = Occurrence("Dentista", place: "Consulta 3");
        Assert.Equal(o.Start.AddMinutes(-10), new CalendarTrigger { Minutes = 10 }.MomentOf(o));
        Assert.Equal(o.Start, new CalendarTrigger { Moment = CalendarMoment.Start }.MomentOf(o));
        Assert.Equal(o.End, new CalendarTrigger { Moment = CalendarMoment.End }.MomentOf(o));
        Assert.True(new CalendarTrigger { Moment = CalendarMoment.Start }.HasEnd);
        Assert.False(new CalendarTrigger().HasEnd);
    }

    [Fact]
    public void VariablesOfTheCita()
    {
        var v = EventService.Variables(Occurrence("Dentista", place: "Consulta 3"), null);
        Assert.Equal("Dentista", v.Result);
        Assert.True(v.TryGet("cita_lugar", out var place));
        Assert.Equal("Consulta 3", place);
        Assert.True(v.TryGet("appointment", out var title));
        Assert.Equal("Dentista", title);
    }

    [Fact]
    public void SavedAndReadBack()
    {
        var rule = new EventRule { Name = "Antes de reunión", Trigger = new CalendarTrigger { Minutes = 15, TitleContains = "reunión", OnlyOnline = true } };
        var copy = Json.Deserialize<EventRule>(Json.Serialize(rule))!;
        var t = Assert.IsType<CalendarTrigger>(copy.Trigger);
        Assert.Equal(15, t.Minutes);
        Assert.True(t.OnlyOnline);
    }
}
