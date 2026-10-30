using System.Net;
using KDesktop.Core.Calendar;

namespace KDesktop.Core.Tests;

public sealed class CalendarServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-calendar-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (DirectoryNotFoundException) { }
    }

    private CalendarService Service(HttpClient? http = null, TimeProvider? time = null) =>
        new(_folder, s => "x:" + s, s => s.StartsWith("x:", StringComparison.Ordinal) ? s[2..] : null, http, time);

    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Cita(string title, DateTime start, string? rule = null) => new()
    {
        Title = title, Start = start, End = start.AddHours(1), TimeZone = "Europe/Madrid", Recurrence = rule,
    };

    [Fact]
    public async Task AddsToTheOwnCalendarAndKeepsItInAFile()
    {
        using (var service = Service())
        {
            var change = await service.AddAsync(Cita("Dentista", new DateTime(2026, 3, 4, 17, 0, 0)));
            Assert.Equal(LocalCalendar.SourceId, change.Result!.SourceId);
            Assert.Equal("personal", change.Result.CalendarId);
            Assert.Equal(change.Result.Uid, change.Result.Id);
        }
        using var again = Service();
        var o = Assert.Single(again.Occurrences(From, From.AddYears(1)));
        Assert.Equal("Dentista", o.Event.Title);
        Assert.Single(again.Search("DENTÍSTA", From, From.AddYears(1)));
        Assert.Empty(again.Search("médico", From, From.AddYears(1)));
    }

    [Fact]
    public async Task DefaultReminderOfTheCalendar()
    {
        using var service = Service();
        var personal = service.DefaultCalendar;
        service.SetLook(personal with { DefaultReminder = 15 });
        var change = await service.AddAsync(Cita("Algo", new DateTime(2026, 3, 4, 17, 0, 0)));
        Assert.Equal([15], change.Result!.Reminders);
    }

    [Fact]
    public async Task ChangesOneAppearanceAndUndoes()
    {
        using var service = Service();
        await service.AddAsync(Cita("Gimnasio", new DateTime(2026, 2, 2, 19, 0, 0), "FREQ=DAILY;COUNT=3"));
        var second = service.Occurrences(From, From.AddYears(1))[1];
        var change = await service.ChangeAsync(second, second.Appearance with { Title = "Gimnasio tarde", Start = new DateTime(2026, 2, 3, 20, 0, 0), End = new DateTime(2026, 2, 3, 21, 0, 0) }, EditScope.This);
        Assert.Equal(["Gimnasio", "Gimnasio tarde", "Gimnasio"], service.Occurrences(From, From.AddYears(1)).Select(o => o.Event.Title));
        await service.RevertAsync(change);
        Assert.All(service.Occurrences(From, From.AddYears(1)), o => Assert.Equal("Gimnasio", o.Event.Title));
    }

    [Fact]
    public async Task ThisAndFollowingMakesTwoSeriesAndUndoes()
    {
        using var service = Service();
        await service.AddAsync(Cita("Clase", new DateTime(2026, 2, 2, 18, 0, 0), "FREQ=WEEKLY;COUNT=6"));
        var third = service.Occurrences(From, From.AddYears(1))[2];
        var change = await service.ChangeAsync(third, third.Appearance with { Title = "Clase nueva" }, EditScope.ThisAndFollowing);
        Assert.Equal(2, service.Local.Events.Count);
        Assert.Equal(["Clase", "Clase", "Clase nueva", "Clase nueva", "Clase nueva", "Clase nueva"], service.Occurrences(From, From.AddYears(1)).Select(o => o.Event.Title));
        await service.RevertAsync(change);
        Assert.Single(service.Local.Events);
        Assert.Equal(6, service.Occurrences(From, From.AddYears(1)).Count);
    }

    [Fact]
    public async Task DeleteScopes()
    {
        using var service = Service();
        await service.AddAsync(Cita("Turno", new DateTime(2026, 2, 2, 8, 0, 0), "FREQ=DAILY;COUNT=5"));
        var all = service.Occurrences(From, From.AddYears(1));
        await service.DeleteAsync(all[1], EditScope.This);
        Assert.Equal(4, service.Occurrences(From, From.AddYears(1)).Count);
        await service.DeleteAsync(service.Occurrences(From, From.AddYears(1))[2], EditScope.ThisAndFollowing);
        Assert.Equal(2, service.Occurrences(From, From.AddYears(1)).Count);
        var removed = await service.DeleteAsync(service.Occurrences(From, From.AddYears(1))[0], EditScope.All);
        Assert.Empty(service.Occurrences(From, From.AddYears(1)));
        await service.RevertAsync(removed);
        Assert.Equal(2, service.Occurrences(From, From.AddYears(1)).Count);
    }

    [Fact]
    public async Task FindsCalendarsByName()
    {
        using var service = Service();
        var work = service.Local.AddCalendar("Trabajo");
        Assert.Equal(work.Id, service.FindCalendar("el de trabajo")?.Id);
        Assert.Equal("personal", service.FindCalendar("PERSONAL")?.Id);
        Assert.Null(service.FindCalendar("vacaciones"));
        var change = await service.AddAsync(Cita("Informe", new DateTime(2026, 3, 1, 10, 0, 0)) with { SourceId = "local", CalendarId = work.Id });
        Assert.Equal(work.Id, change.Result!.CalendarId);
    }

    [Fact]
    public async Task ImportAndExportIcs()
    {
        using var service = Service();
        await service.AddAsync(Cita("Uno", new DateTime(2026, 3, 1, 10, 0, 0), "FREQ=WEEKLY;COUNT=2"));
        var text = service.ExportIcs("local", "personal");
        var other = service.Local.AddCalendar("Copia");
        // The same UIDs: importing again replaces them (no duplicates).
        Assert.Equal(1, service.ImportIcs(text, other.Id));
        Assert.Single(service.Local.Events);
        Assert.Equal(other.Id, service.Local.Events[0].CalendarId);
    }

    [Fact]
    public async Task ReadOnlySourcesSayIt()
    {
        var handler = new IcsHandler("BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:f@x\nDTSTART;VALUE=DATE:20260501\nSUMMARY:Fiesta\nEND:VEVENT\nEND:VCALENDAR");
        using var service = Service(new HttpClient(handler));
        var sub = service.AddSubscription("Festivos", "webcal://example.com/fiestas.ics");
        await sub.SyncAsync();
        Assert.Equal("https://example.com/fiestas.ics", handler.LastUrl);
        var o = Assert.Single(service.Occurrences(From, From.AddYears(1)));
        Assert.Equal("Fiesta", o.Event.Title);
        await Assert.ThrowsAsync<CalendarException>(() => service.DeleteAsync(o, EditScope.All));
        Assert.True(service.Calendar(sub.Id, IcsSubscription.MainCalendar)!.ReadOnly);

        // The second time it asks with the ETag and the server says nothing changed.
        await sub.SyncAsync();
        Assert.Equal("\"v1\"", handler.LastIfNoneMatch);
        Assert.Single(service.Occurrences(From, From.AddYears(1)));

        // Kept on disk: there without network.
        using var again = Service(new HttpClient(new IcsHandler(null)));
        Assert.Single(again.Occurrences(From, From.AddYears(1)));
        await ((IcsSubscription)again.Source(sub.Id)!).SyncAsync();
        Assert.NotNull(again.Source(sub.Id)!.Problem);
        Assert.Single(again.Occurrences(From, From.AddYears(1)));
    }

    [Fact]
    public async Task RemindersRingOnceWhenTheirTimeComes()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 3, 4, 15, 0, 0, TimeSpan.Zero));
        using var service = Service(time: time);
        var start = new DateTime(2026, 3, 4, 17, 0, 0); // 16:00 UTC
        await service.AddAsync(Cita("Médico", start) with { Reminders = [10] });
        var rung = new List<CalendarReminder>();
        service.ReminderDue += rung.Add;
        service.Start();
        time.Now = new DateTimeOffset(2026, 3, 4, 15, 49, 0, TimeSpan.Zero);
        service.CheckReminders();
        Assert.Empty(rung);
        time.Now = new DateTimeOffset(2026, 3, 4, 15, 50, 10, TimeSpan.Zero);
        service.CheckReminders();
        Assert.Equal(10, Assert.Single(rung).Minutes);
        time.Now += TimeSpan.FromMinutes(1);
        service.CheckReminders();
        Assert.Single(rung);
    }

    [Fact]
    public async Task ReminderOfACitaAlreadyOverDoesNotRingAfterSleep()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 3, 4, 15, 0, 0, TimeSpan.Zero));
        using var service = Service(time: time);
        await service.AddAsync(Cita("Llamada", new DateTime(2026, 3, 4, 17, 0, 0)) with { Reminders = [5] });
        var rung = new List<CalendarReminder>();
        service.ReminderDue += rung.Add;
        service.Start();
        time.Now = new DateTimeOffset(2026, 3, 4, 18, 0, 0, TimeSpan.Zero); // the PC slept until after the cita
        service.CheckReminders();
        Assert.Empty(rung);
    }

    private sealed class IcsHandler(string? body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        public string? LastIfNoneMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (body is null) throw new HttpRequestException("sin red");
            LastUrl = request.RequestUri!.ToString();
            LastIfNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
            if (LastIfNoneMatch == "\"v1\"") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
