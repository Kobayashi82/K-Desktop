using KDesktop.Core.Calendar;

namespace KDesktop.Core.Tests;

public sealed class CalendarAssistantTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-calassist-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly CalendarService _calendar;

    /// <summary>Thursday 5 November 2026, 10:00.</summary>
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0);

    public CalendarAssistantTests() => _calendar = new CalendarService(_folder, s => s, s => s);

    public void Dispose()
    {
        _calendar.Dispose();
        try { Directory.Delete(_folder, true); }
        catch (DirectoryNotFoundException) { }
    }

    private Task<CalendarAnswer> Ask(CalendarRequest r) => CalendarAssistant.HandleAsync(_calendar, r, Now);

    [Fact]
    public void Ranges()
    {
        Assert.Equal((new DateTime(2026, 11, 6), new DateTime(2026, 11, 7)), CalendarAssistant.Span(new("list", Range: "tomorrow"), Now, false));
        Assert.Equal((new DateTime(2026, 11, 5), new DateTime(2026, 11, 9)), CalendarAssistant.Span(new("list", Range: "this_week"), Now, false));
        Assert.Equal((new DateTime(2026, 11, 9), new DateTime(2026, 11, 16)), CalendarAssistant.Span(new("list", Range: "next_week"), Now, false));
        Assert.Equal((new DateTime(2026, 11, 7), new DateTime(2026, 11, 9)), CalendarAssistant.Span(new("list", Range: "weekend"), Now, false));
        Assert.Equal((new DateTime(2026, 11, 10), new DateTime(2026, 11, 11)), CalendarAssistant.Span(new("list", Range: "Tuesday"), Now, false));
        Assert.Equal((new DateTime(2026, 12, 1), new DateTime(2026, 12, 3)), CalendarAssistant.Span(new("list", From: "2026-12-01", To: "2026-12-02"), Now, false));
        Assert.Null(CalendarAssistant.Span(new("list", Range: "algún día"), Now, false));
    }

    [Fact]
    public async Task AddListChangeDeleteAndUndo()
    {
        var added = await Ask(new("add", Cita: new CalendarCita("Dentista", "2026-11-06 17:00", Minutes: 30, Location: "Consulta 3", Reminder: 10)));
        Assert.True(added.Ok);
        Assert.Equal("2026-11-06 17:00", added.Item!.Start);
        Assert.Equal("2026-11-06 17:30", added.Item.End);

        var tomorrow = await Ask(new("list", Range: "tomorrow"));
        var item = Assert.Single(tomorrow.Items!);
        Assert.Equal("Dentista", item.Title);
        Assert.Equal("Consulta 3", item.Location);

        var found = await Ask(new("list", Text: "dentista"));
        Assert.Single(found.Items!);

        var moved = await Ask(new("change", Text: "dentista", Cita: new CalendarCita(Start: "2026-11-06 18:00")));
        Assert.True(moved.Ok);
        Assert.Equal("2026-11-06 18:00", moved.Item!.Start);
        Assert.Equal("2026-11-06 18:30", moved.Item.End);
        await Ask(new("undo", Undo: moved.Undo));
        Assert.Equal("2026-11-06 17:00", (await Ask(new("list", Range: "tomorrow"))).Items![0].Start);

        var deleted = await Ask(new("delete", Key: item.Key));
        Assert.True(deleted.Ok);
        Assert.Empty((await Ask(new("list", Range: "tomorrow"))).Items!);
        await Ask(new("undo", Undo: deleted.Undo));
        Assert.Single((await Ask(new("list", Range: "tomorrow"))).Items!);

        await Ask(new("undo", Undo: added.Undo));
        Assert.Empty((await Ask(new("list", Range: "tomorrow"))).Items!);
    }

    [Fact]
    public async Task AmbiguousAndCalendarsByName()
    {
        var work = _calendar.Local.AddCalendar("Trabajo");
        await Ask(new("add", Cita: new CalendarCita("Reunión de equipo", "2026-11-06 09:00", Calendar: "trabajo")));
        await Ask(new("add", Cita: new CalendarCita("Reunión con Ana", "2026-11-07 12:00")));
        var both = await Ask(new("delete", Text: "reunión"));
        Assert.False(both.Ok);
        Assert.Equal("ambiguous", both.Reason);
        Assert.Equal(2, both.Items!.Count);
        var only = await Ask(new("list", Range: "this_week", Calendar: "el de trabajo"));
        Assert.Equal("Reunión de equipo", Assert.Single(only.Items!).Title);
        Assert.Equal(work.Name, only.Items![0].Calendar);
        Assert.Equal("calendar", (await Ask(new("add", Cita: new CalendarCita("X", "2026-11-06 09:00", Calendar: "vacaciones")))).Reason);
    }

    [Fact]
    public async Task SeriesAllDayAndScopes()
    {
        var gym = await Ask(new("add", Cita: new CalendarCita("Gimnasio", "2026-11-09 19:00", Repeat: "weekly")));
        Assert.True(gym.Ok);
        Assert.True(gym.Item!.Repeats);
        var holiday = await Ask(new("add", Cita: new CalendarCita("Puente", "2026-12-07", End: "2026-12-08")));
        Assert.True(holiday.Item!.AllDay);
        Assert.Equal("2026-12-08", holiday.Item.End);

        var next = (await Ask(new("list", Range: "next_week"))).Items!.Single(i => i.Title == "Gimnasio");
        await Ask(new("change", Key: next.Key, Cita: new CalendarCita(Title: "Gimnasio tarde"), Scope: "this"));
        var month = (await Ask(new("list", From: "2026-11-09", To: "2026-11-30"))).Items!.Where(i => i.Title.StartsWith("Gimnasio", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Gimnasio tarde", "Gimnasio", "Gimnasio", "Gimnasio"], month.Select(i => i.Title));
    }
}
