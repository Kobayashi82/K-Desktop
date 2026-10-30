using System.Net;
using System.Text.Json;
using KDesktop.Core.Accounts;
using KDesktop.Core.Calendar;

namespace KDesktop.Core.Tests;

/// <summary>Google translated both ways with answers as the servers give them, and the queue without connection.</summary>
public sealed class CalendarRemoteTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-remote-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (DirectoryNotFoundException) { }
    }

    private const string GoogleSeries = """
        {"kind":"calendar#event","etag":"\"3181\"","id":"abc123","status":"confirmed","summary":"Daily","description":"https://meet.google.com/aaa-bbbb-ccc",
         "location":"Sala 1","iCalUID":"abc123@google.com","updated":"2026-01-02T10:00:00.000Z",
         "start":{"dateTime":"2026-01-05T09:00:00+01:00","timeZone":"Europe/Madrid"},"end":{"dateTime":"2026-01-05T09:15:00+01:00","timeZone":"Europe/Madrid"},
         "recurrence":["RRULE:FREQ=WEEKLY;BYDAY=MO,WE","EXDATE;TZID=Europe/Madrid:20260107T090000"],
         "reminders":{"useDefault":false,"overrides":[{"method":"popup","minutes":5}]},
         "hangoutLink":"https://meet.google.com/aaa-bbbb-ccc"}
        """;

    private const string GoogleException = """
        {"id":"abc123_20260112T080000Z","status":"confirmed","summary":"Daily (tarde)","recurringEventId":"abc123",
         "originalStartTime":{"dateTime":"2026-01-12T09:00:00+01:00","timeZone":"Europe/Madrid"},
         "start":{"dateTime":"2026-01-12T17:00:00+01:00","timeZone":"Europe/Madrid"},"end":{"dateTime":"2026-01-12T17:15:00+01:00","timeZone":"Europe/Madrid"}}
        """;

    private const string GoogleCancelled = """
        {"id":"abc123_20260114T080000Z","status":"cancelled","recurringEventId":"abc123","originalStartTime":{"dateTime":"2026-01-14T09:00:00+01:00","timeZone":"Europe/Madrid"}}
        """;

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GoogleToKDesktop()
    {
        var series = GoogleCalendar.ToEvent(Json(GoogleSeries), "acc-x", "primary", "Europe/Madrid", 10)!;
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), series.Start);
        Assert.Equal("Europe/Madrid", series.TimeZone);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO,WE", series.Recurrence);
        Assert.Equal([new DateTime(2026, 1, 7, 9, 0, 0)], series.Exceptions);
        Assert.Equal([5], series.Reminders);
        Assert.Equal("https://meet.google.com/aaa-bbbb-ccc", series.MeetingUrl);
        Assert.Equal("\"3181\"", series.Etag);

        var moved = GoogleCalendar.ToEvent(Json(GoogleException), "acc-x", "primary", "Europe/Madrid", null)!;
        Assert.Equal("abc123", moved.SeriesId);
        Assert.Equal(new DateTime(2026, 1, 12, 9, 0, 0), moved.RecurrenceId);
        var gone = GoogleCalendar.ToEvent(Json(GoogleCancelled), "acc-x", "primary", "Europe/Madrid", null)!;
        Assert.True(gone.Cancelled);

        var merged = Assert.Single(RemoteCalendar.Merge([series, moved, gone]));
        var titles = Recurrence.Expand(merged, From, new DateTimeOffset(2026, 1, 16, 0, 0, 0, TimeSpan.Zero)).Select(o => o.Event.Title).ToList();
        // 5 (Mon), 7 deleted, 12 moved, 14 cancelled.
        Assert.Equal(["Daily", "Daily (tarde)"], titles);
    }

    [Fact]
    public void KDesktopToGoogle()
    {
        var series = GoogleCalendar.ToEvent(Json(GoogleSeries), "acc-x", "primary", "Europe/Madrid", null)!;
        var body = GoogleCalendar.ToGoogle(series);
        Assert.Equal("2026-01-05T09:00:00+01:00", body["start"]!["dateTime"]!.GetValue<string>());
        Assert.Equal("Europe/Madrid", body["start"]!["timeZone"]!.GetValue<string>());
        Assert.Equal("RRULE:FREQ=WEEKLY;BYDAY=MO,WE", body["recurrence"]![0]!.GetValue<string>());
        Assert.Equal("EXDATE;TZID=Europe/Madrid:20260107T090000", body["recurrence"]![1]!.GetValue<string>());
        Assert.Equal(5, body["reminders"]!["overrides"]![0]!["minutes"]!.GetValue<int>());
        Assert.Equal("abc123_20260112T080000Z", GoogleCalendar.InstanceId(series, new DateTime(2026, 1, 12, 9, 0, 0)));

        var allDay = new CalendarEvent { Id = "d", Title = "Fiesta", Start = new DateTime(2026, 5, 1), End = new DateTime(2026, 5, 2), AllDay = true };
        Assert.Equal("2026-05-01", GoogleCalendar.ToGoogle(allDay)["start"]!["date"]!.GetValue<string>());
        Assert.Equal("d_20260501", GoogleCalendar.InstanceId(allDay with { Recurrence = "FREQ=YEARLY" }, new DateTime(2026, 5, 1)));
    }

    [Fact]
    public async Task GoogleSyncAndQueueWithoutConnection()
    {
        var server = new FakeGoogle();
        Directory.CreateDirectory(Path.Combine(_folder, "Accounts"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "Accounts", "accounts.json"),
            """{"accounts":[{"id":"g1","provider":"google","email":"ana@gmail.com","uses":"calendarRead, calendarWrite","refreshTokenProtected":"r","clientId":"app"}],"apps":{"googleClientId":"app"}}""");
        var http = new HttpClient(server);
        var accounts = new AccountService(Path.Combine(_folder, "Accounts"), s => s, s => s, http);
        using var calendar = new CalendarService(Path.Combine(_folder, "Calendar"), s => s, s => s, http)
        {
            AccountSources = a => new GoogleCalendar(accounts, a, Path.Combine(_folder, "Calendar", "Cache", "g.json"), http),
        };
        calendar.Start();
        calendar.UseAccount("g1", []);
        var source = (RemoteCalendar)calendar.Source(CalendarService.AccountSourceId("g1"))!;
        await source.SyncAsync();
        Assert.Equal("Ana", Assert.Single(calendar.Calendars, c => c.SourceId == source.Id).Name);
        var o = Assert.Single(calendar.Occurrences(From, From.AddDays(30)));
        Assert.Equal("Cena", o.Event.Title);

        // Without connection: kept in the queue, shown already, sent when it comes back.
        server.Offline = true;
        await calendar.AddAsync(new CalendarEvent { Title = "Sin red", Start = new DateTime(2026, 1, 20, 10, 0, 0), End = new DateTime(2026, 1, 20, 11, 0, 0), TimeZone = "Europe/Madrid",
            SourceId = source.Id, CalendarId = "ana@gmail.com" });
        Assert.Equal(2, calendar.Occurrences(From, From.AddDays(30)).Count);
        Assert.Equal(0, server.Created);
        server.Offline = false;
        await source.SyncAsync();
        Assert.Equal(1, server.Created);
        Assert.DoesNotContain(source.Events, e => e.Id.StartsWith("pending-", StringComparison.Ordinal));

        // A conflict online: said, nothing overwritten.
        server.Conflict = true;
        var dinner = calendar.Occurrences(From, From.AddDays(30)).First(x => x.Event.Title == "Cena");
        await Assert.ThrowsAsync<CalendarConflictException>(() => calendar.ChangeAsync(dinner, dinner.Appearance with { Title = "Cena tarde" }, EditScope.All));
    }

    private sealed class FakeGoogle : HttpMessageHandler
    {
        public bool Offline { get; set; }
        public bool Conflict { get; set; }
        public int Created { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException("sin red");
            var url = request.RequestUri!.ToString();
            string body;
            var status = HttpStatusCode.OK;
            if (url.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal)) body = """{"access_token":"t","expires_in":3600}""";
            else if (url.Contains("/calendarList", StringComparison.Ordinal)) body = """{"items":[{"id":"ana@gmail.com","summary":"Ana","backgroundColor":"#9fe1e7","accessRole":"owner","primary":true}]}""";
            else if (request.Method == HttpMethod.Get && url.Contains("/events?", StringComparison.Ordinal))
                body = """{"timeZone":"Europe/Madrid","nextSyncToken":"s1","items":[{"id":"e1","etag":"\"1\"","status":"confirmed","summary":"Cena","start":{"dateTime":"2026-01-10T21:00:00+01:00"},"end":{"dateTime":"2026-01-10T23:00:00+01:00"}}]}""";
            else if (request.Method == HttpMethod.Post && url.EndsWith("/events", StringComparison.Ordinal))
            {
                Created++;
                body = """{"id":"new1","etag":"\"2\"","summary":"Sin red","start":{"dateTime":"2026-01-20T10:00:00+01:00","timeZone":"Europe/Madrid"},"end":{"dateTime":"2026-01-20T11:00:00+01:00","timeZone":"Europe/Madrid"}}""";
            }
            else if (request.Method == HttpMethod.Put && Conflict)
            {
                status = HttpStatusCode.PreconditionFailed;
                body = "{}";
            }
            else body = "{}";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
