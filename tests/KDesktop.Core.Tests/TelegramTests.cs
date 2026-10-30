using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Tests;

public sealed class TelegramTests : IDisposable
{
    private const string Token = "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-telegram-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (DirectoryNotFoundException) { }
    }

    /// <summary>A connection that gives what the test puts in and keeps what is sent.</summary>
    private sealed class FakeConnection : ITelegramConnection
    {
        public BlockingCollection<TelegramIncoming> Incoming { get; } = [];
        public ConcurrentQueue<(long Chat, string Text)> Sent { get; } = new();
        public ConcurrentQueue<Exception> SendFailures { get; } = new();
        public Exception? GetMeFailure { get; set; }
        public int GetMeCalls;

        public TelegramCapabilities Capabilities => TelegramCapabilities.Bot;

        public Task<TelegramUser> GetMeAsync(CancellationToken cancel)
        {
            Interlocked.Increment(ref GetMeCalls);
            if (GetMeFailure is { } f) throw f;
            return Task.FromResult(new TelegramUser(777, "K-Desktop de Ana", "kd_ana_bot"));
        }

        public Task<IReadOnlyList<TelegramIncoming>> ReceiveAsync(CancellationToken cancel) => Task.Run<IReadOnlyList<TelegramIncoming>>(() =>
        {
            var first = Incoming.Take(cancel);
            var list = new List<TelegramIncoming> { first };
            while (Incoming.TryTake(out var more)) list.Add(more);
            return list;
        }, cancel);

        public ConcurrentQueue<IReadOnlyList<IReadOnlyList<TelegramButton>>> Keyboards { get; } = new();
        public ConcurrentQueue<(int Message, string Text)> Edited { get; } = new();

        public Task<int> SendTextAsync(long chat, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken cancel)
        {
            if (SendFailures.TryDequeue(out var f)) throw f;
            if (buttons is not null) Keyboards.Enqueue(buttons);
            Sent.Enqueue((chat, text));
            return Task.FromResult(Sent.Count);
        }

        public Task<int> SendFileAsync(long chat, TelegramFileKind kind, string path, string? caption, CancellationToken cancel) => Task.FromResult(1);
        public Task SendChatActionAsync(long chat, string action, CancellationToken cancel) => Task.CompletedTask;
        public Task EditTextAsync(long chat, int message, string text, CancellationToken cancel)
        {
            Edited.Enqueue((message, text));
            return Task.CompletedTask;
        }

        public Task AnswerButtonAsync(string callbackId, string? text, CancellationToken cancel) => Task.CompletedTask;
        public Task DeleteAsync(long chat, int message, CancellationToken cancel) => Task.CompletedTask;
        public Task DownloadAsync(string fileId, string destination, CancellationToken cancel) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static string Protect(string s) => "p:" + s;
    private static string? Unprotect(string s) => s.StartsWith("p:", StringComparison.Ordinal) ? s[2..] : null;

    private (TelegramService Service, FakeConnection Connection) Make()
    {
        var connection = new FakeConnection();
        var service = new TelegramService(_folder, Protect, Unprotect, _ => connection) { RetryBase = TimeSpan.FromMilliseconds(20) };
        return (service, connection);
    }

    private static TelegramIncoming From(long user, string text, TelegramChatKind kind = TelegramChatKind.Private, long? chat = null) =>
        new(1, chat ?? user, kind, null, 1, new TelegramUser(user, "Ana", "ana"), DateTimeOffset.UtcNow, text);

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public void TokensThatLookRight()
    {
        Assert.True(TelegramBotConnection.LooksLikeToken(Token));
        Assert.True(TelegramBotConnection.LooksLikeToken("  " + Token + " "));
        Assert.False(TelegramBotConnection.LooksLikeToken("hola"));
        Assert.False(TelegramBotConnection.LooksLikeToken("abc:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw"));
        Assert.False(TelegramBotConnection.LooksLikeToken("123456789:AAH dqTcvCH1vGWJxfSeofSAs0K5PAL"));
    }

    [Fact]
    public void AnswersOfTheBotApi()
    {
        Assert.Equal(5, TelegramBotConnection.Parse("""{"ok":true,"result":{"message_id":5}}""", 200).GetProperty("message_id").GetInt32());
        var slow = Assert.Throws<TelegramException>(() => TelegramBotConnection.Parse("""{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 7","parameters":{"retry_after":7}}""", 429));
        Assert.Equal(TimeSpan.FromSeconds(7), slow.RetryAfter);
        Assert.True(Assert.Throws<TelegramException>(() => TelegramBotConnection.Parse("""{"ok":false,"error_code":401,"description":"Unauthorized"}""", 401)).BadToken);
        Assert.Equal(409, Assert.Throws<TelegramException>(() => TelegramBotConnection.Parse("""{"ok":false,"error_code":409,"description":"Conflict"}""", 409)).Code);
        Assert.Equal(502, Assert.Throws<TelegramException>(() => TelegramBotConnection.Parse("<html>Bad gateway</html>", 502)).Code);
    }

    [Fact]
    public void UpdatesAsMessages()
    {
        var text = JsonDocument.Parse("""
            {"update_id":10,"message":{"message_id":3,"date":1761800000,"from":{"id":42,"is_bot":false,"first_name":"Ana","last_name":"Gil","username":"anagil"},
             "chat":{"id":42,"type":"private"},"text":"/start 123456"}}
            """).RootElement;
        var m = TelegramBotConnection.ParseUpdate(text)!;
        Assert.Equal(42, m.From.Id);
        Assert.Equal("Ana Gil", m.From.Name);
        Assert.Equal("Ana Gil (@anagil)", m.From.Shown);
        Assert.Equal(TelegramChatKind.Private, m.ChatKind);
        Assert.Equal("/start 123456", m.Text);

        var photo = JsonDocument.Parse("""
            {"update_id":11,"message":{"message_id":4,"date":1761800000,"from":{"id":42,"is_bot":false,"first_name":"Ana"},
             "chat":{"id":-100,"type":"supergroup","title":"Casa"},"caption":"mira",
             "photo":[{"file_id":"small","file_size":10},{"file_id":"big","file_size":900}]}}
            """).RootElement;
        var p = TelegramBotConnection.ParseUpdate(photo)!;
        Assert.Equal(TelegramContent.Photo, p.Content);
        Assert.Equal("big", p.File!.Id);
        Assert.Equal(TelegramChatKind.Group, p.ChatKind);
        Assert.Equal("Casa", p.ChatTitle);
        Assert.Equal("mira", p.Text);

        var bot = JsonDocument.Parse("""{"update_id":12,"message":{"message_id":5,"from":{"id":9,"is_bot":true,"first_name":"B"},"chat":{"id":9,"type":"private"},"text":"x"}}""").RootElement;
        Assert.Null(TelegramBotConnection.ParseUpdate(bot));
        Assert.Null(TelegramBotConnection.ParseUpdate(JsonDocument.Parse("""{"update_id":13,"edited_message":{}}""").RootElement));
    }

    [Fact]
    public async Task TheBotApiOverHttp()
    {
        var asked = new List<string>();
        using var handler = new Handler(request =>
        {
            asked.Add(request.RequestUri!.AbsolutePath);
            var method = request.RequestUri.AbsolutePath.Split('/')[^1];
            return method switch
            {
                "getMe" => """{"ok":true,"result":{"id":777,"is_bot":true,"first_name":"KD","username":"kd_bot"}}""",
                "getUpdates" => """{"ok":true,"result":[{"update_id":50,"message":{"message_id":1,"date":1,"from":{"id":42,"is_bot":false,"first_name":"Ana"},"chat":{"id":42,"type":"private"},"text":"hola"}}]}""",
                _ => """{"ok":true,"result":{"message_id":9}}""",
            };
        });
        using var http = new HttpClient(handler);
        using var connection = new TelegramBotConnection(Token, http);
        Assert.Equal("kd_bot", (await connection.GetMeAsync(default)).Username);
        Assert.Equal("hola", Assert.Single(await connection.ReceiveAsync(default)).Text);
        await connection.ReceiveAsync(default);
        Assert.Contains("offset=51", handler.Bodies[^1], StringComparison.Ordinal);
        Assert.Equal(9, await connection.SendTextAsync(42, "¡hola!", [[new("Sí", "y")]], default));
        Assert.Contains("inline_keyboard", Uri.UnescapeDataString(handler.Bodies[^1]), StringComparison.Ordinal);
        Assert.All(asked, a => Assert.StartsWith("/bot" + Token + "/", a, StringComparison.Ordinal));
    }

    private sealed class Handler(Func<HttpRequestMessage, string> answer) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(request), Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task LinksTheOwnerWithTheCode()
    {
        var (service, connection) = Make();
        using var _ = service;
        Assert.Equal(TelegramState.Off, service.State);
        await service.SetBotAsync(Token);
        Assert.Equal("p:" + Token, service.Bot!.TokenProtected);
        Assert.Equal("kd_ana_bot", service.Bot.Username);
        await Until(() => service.State == TelegramState.Connected);
        Assert.False(service.SendToOwner("nadie"));

        // Before linking, nobody is listened to.
        connection.Incoming.Add(From(42, "/start 000000"));
        var code = service.StartLinking();
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal($"https://t.me/kd_ana_bot?start={code}", service.LinkUrl);

        TelegramOwner? linked = null;
        service.OwnerLinked += o => linked = o;
        connection.Incoming.Add(From(42, "/start " + code));
        await Until(() => linked is not null);
        Assert.Equal(42, service.Owner!.Id);
        Assert.Null(service.LinkCode);
        await Until(() => connection.Sent.Any(s => s.Chat == 42));

        // Kept, and the owner gets what is sent.
        using (var again = new TelegramService(_folder, Protect, Unprotect, _ => connection)) Assert.Equal(42, again.Owner!.Id);
        Assert.True(await service.SendToOwnerAsync("prueba"));
        Assert.Contains(connection.Sent, s => s is { Chat: 42, Text: "prueba" });
    }

    [Fact]
    public async Task WrongCodesAndStrangers()
    {
        var (service, connection) = Make();
        using var _ = service;
        await service.SetBotAsync(Token);
        await Until(() => service.State == TelegramState.Connected);
        var code = service.StartLinking();
        var wrong = code == "111111" ? "222222" : "111111";

        // In a group, the code does not link.
        connection.Incoming.Add(From(42, "/start " + code, TelegramChatKind.Group, -100));
        for (var i = 0; i < TelegramService.LinkTries; i++) connection.Incoming.Add(From(66, "/start " + wrong));
        await Until(() => service.LinkCode is null);
        Assert.Null(service.Owner);

        // The linking was given up: the right code no longer works.
        connection.Incoming.Add(From(42, "/start " + code));
        await Task.Delay(100);
        Assert.Null(service.Owner);
    }

    [Fact]
    public async Task TheCodeRunsOut()
    {
        var (service, connection) = Make();
        using var _ = service;
        var now = DateTimeOffset.UtcNow;
        service.Now = () => now;
        await service.SetBotAsync(Token);
        await Until(() => service.State == TelegramState.Connected);
        var code = service.StartLinking();
        now += TelegramService.LinkLifetime + TimeSpan.FromSeconds(1);
        Assert.Null(service.LinkCode);
        connection.Incoming.Add(From(42, code));
        await Task.Delay(100);
        Assert.Null(service.Owner);
    }

    [Fact]
    public async Task WaitsWhenTelegramSaysSoAndComesBack()
    {
        var (service, connection) = Make();
        using var _ = service;
        connection.GetMeFailure = new HttpRequestException("sin red");
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SetBotAsync(Token));
        Assert.Null(service.Bot);

        connection.GetMeFailure = null;
        await service.SetBotAsync(Token);
        await Until(() => service.State == TelegramState.Connected);
        var code = service.StartLinking();
        connection.Incoming.Add(From(42, code));
        await Until(() => service.Owner is not null);

        // Too fast: it waits and sends it anyway, in order.
        connection.SendFailures.Enqueue(new TelegramException("despacio", 429, TimeSpan.FromMilliseconds(50)));
        Assert.True(service.SendToOwner("uno"));
        Assert.True(await service.SendToOwnerAsync("dos"));
        var texts = connection.Sent.Where(s => s.Chat == 42).Select(s => s.Text).ToList();
        Assert.Equal(["uno", "dos"], texts.TakeLast(2));

        // Refused (the owner blocked the bot): given up, not retried for ever.
        connection.SendFailures.Enqueue(new TelegramException("bloqueado", 403));
        Assert.False(await service.SendToOwnerAsync("tres"));
    }

    [Fact]
    public async Task AWrongTokenStopsIt()
    {
        var (service, connection) = Make();
        using var _ = service;
        await service.SetBotAsync(Token);
        await Until(() => service.State == TelegramState.Connected);
        connection.GetMeFailure = new TelegramException("token", 401);
        service.SetEnabled(false);
        Assert.Equal(TelegramState.Off, service.State);
        service.SetEnabled(true);
        await Until(() => service.State == TelegramState.Problem);
        var calls = connection.GetMeCalls;
        await Task.Delay(150);
        Assert.Equal(calls, connection.GetMeCalls);
    }

    [Fact]
    public async Task AnotherBotForgetsTheOwner()
    {
        var (service, connection) = Make();
        using var _ = service;
        await service.SetBotAsync(Token);
        await Until(() => service.State == TelegramState.Connected);
        connection.Incoming.Add(From(42, service.StartLinking()));
        await Until(() => service.Owner is not null);
        await service.SetBotAsync(Token); // the same bot, a new token: the owner stays
        Assert.NotNull(service.Owner);
        service.RemoveBot();
        Assert.Null(service.Owner);
        Assert.Null(service.Bot);
        Assert.Equal(TelegramState.Off, service.State);
    }

    /// <summary>A service connected and with its owner (42).</summary>
    private async Task<(TelegramService Service, FakeConnection Connection)> Linked()
    {
        var (service, connection) = Make();
        await service.SetBotAsync(Token);
        await Until(() => service.State == TelegramState.Connected);
        connection.Incoming.Add(From(42, service.StartLinking()));
        await Until(() => service.Owner is not null && connection.Sent.Any(s => s.Chat == 42));
        return (service, connection);
    }

    private static TelegramIncoming Person(long user, string name, string text, string? username = null, TelegramChatKind kind = TelegramChatKind.Private, long? chat = null) =>
        new(1, chat ?? user, kind, kind == TelegramChatKind.Group ? "Casa" : null, 1, new TelegramUser(user, name, username), DateTimeOffset.UtcNow, text);

    private static TelegramIncoming Button(long user, string data) =>
        new(1, user, TelegramChatKind.Private, null, 99, new TelegramUser(user, "X", null), DateTimeOffset.UtcNow, string.Empty, TelegramContent.Other, CallbackId: "cb", CallbackData: data);

    [Fact]
    public async Task SomeoneNewIsToldOnceAndTheOwnerDecides()
    {
        var (service, connection) = await Linked();
        using var _ = service;
        TelegramPerson? told = null;
        var served = new ConcurrentQueue<(TelegramIncoming, TelegramVerdict)>();
        service.StrangerArrived += p => told = p;
        service.Received += (m, v) => served.Enqueue((m, v));

        connection.Incoming.Add(Person(66, "Ana", "hola, ¿me ayudas?", "ana"));
        await Until(() => told is not null && !connection.Keyboards.IsEmpty);
        Assert.Equal(66, Assert.Single(service.Settings.Pending).Id);
        var keyboard = Assert.Single(connection.Keyboards);
        Assert.Equal(["p:allow:66", "p:ignore:66", "p:block:66"], keyboard[0].Select(b => b.Data));
        Assert.Empty(served); // «todos los demás» is «Ignorar» by default

        // Again: not told twice.
        connection.Incoming.Add(Person(66, "Ana", "¿hola?", "ana"));
        await Task.Delay(100);
        Assert.Single(connection.Keyboards);

        // A button of someone else's does nothing.
        connection.Incoming.Add(Button(66, "p:allow:66"));
        await Task.Delay(100);
        Assert.Empty(service.Settings.Contacts);

        // The owner allows her: a contact, «Solo charla»; the message says what was done.
        connection.Incoming.Add(Button(42, "p:allow:66"));
        await Until(() => service.Settings.Contacts.Count == 1 && !connection.Edited.IsEmpty);
        Assert.Equal(TelegramLevel.ChatOnly, service.Settings.Contacts[0].Level);
        Assert.Empty(service.Settings.Pending);

        // Now she is served, and her new name is kept.
        connection.Incoming.Add(Person(66, "Ana María", "gracias", "anamaria"));
        await Until(() => !served.IsEmpty);
        Assert.Equal(TelegramRoute.Contact, served.Single().Item2.Route);
        Assert.Equal("anamaria", service.Settings.Contacts[0].Username);
        Assert.NotNull(service.Settings.Contacts[0].LastWrote);

        // Blocked: dropped without telling.
        service.Block(66);
        connection.Incoming.Add(Person(66, "Ana María", "¿hola?"));
        await Task.Delay(100);
        Assert.Single(served);
        Assert.Equal(66, Assert.Single(service.Settings.Blocked).Id);
        service.Forget(66);
        Assert.Empty(service.Settings.Blocked);
    }

    [Fact]
    public async Task LimitsAndGames()
    {
        var (service, connection) = await Linked();
        using var _ = service;
        var served = new ConcurrentQueue<long>();
        service.Received += (m, _) => served.Enqueue(m.From.Id);
        service.Update(s =>
        {
            s.Contacts.Add(new TelegramContact { Id = 2, Name = "Ana", Level = TelegramLevel.ChatOnly });
            s.Limits.PerMinute = 2;
        });
        var before = connection.Sent.Count;
        for (var i = 0; i < 4; i++) connection.Incoming.Add(Person(2, "Ana", "hola " + i));
        await Until(() => served.Count == 2 && connection.Sent.Count == before + 1);
        await Task.Delay(100);
        Assert.Equal(2, served.Count);
        Assert.Equal(before + 1, connection.Sent.Count); // «ahora no puedo», once

        // While gaming, only the owner.
        service.Busy = () => true;
        connection.Incoming.Add(Person(42, "Yo", "hola"));
        await Until(() => served.Count == 3);
        Assert.Equal(42, served.Last());
        service.Update(s => s.Limits.OnlyOwnerWhileGaming = false);
        service.Update(s => s.Limits.PerMinute = 10);
        connection.Incoming.Add(Person(2, "Ana", "ya"));
        await Until(() => served.Count == 4);
    }

    [Fact]
    public async Task GroupsAndWritingFirst()
    {
        var (service, connection) = await Linked();
        using var _ = service;
        var served = new ConcurrentQueue<TelegramIncoming>();
        service.Received += (m, _) => served.Enqueue(m);
        connection.Incoming.Add(Person(42, "Yo", "hola a todos", kind: TelegramChatKind.Group, chat: -10));
        connection.Incoming.Add(Person(42, "Yo", "@kd_ana_bot hola", kind: TelegramChatKind.Group, chat: -10));
        await Until(() => served.Count == 1);
        Assert.Equal("Casa", Assert.Single(service.Settings.Groups).Title);
        service.Update(s => s.Groups[0].Ignore = true);
        connection.Incoming.Add(Person(42, "Yo", "@kd_ana_bot otra", kind: TelegramChatKind.Group, chat: -10));
        await Task.Delay(100);
        Assert.Single(served);

        // Who K-Desktop may write to: the owner; a contact who wrote; one who never did, so many an hour.
        service.Update(s =>
        {
            s.Limits.NewPerHour = 1;
            s.Contacts.Add(new TelegramContact { Id = 2, Name = "Ana", LastWrote = DateTimeOffset.UtcNow });
            s.Contacts.Add(new TelegramContact { Id = 3, Name = "Pepe" });
            s.Contacts.Add(new TelegramContact { Id = 5, Name = "Luis", MayWrite = false, LastWrote = DateTimeOffset.UtcNow });
        });
        Assert.True(service.CanWriteTo(42));
        Assert.True(service.CanWriteTo(2));
        Assert.True(service.CanWriteTo(2));
        Assert.True(service.CanWriteTo(3));
        Assert.False(service.CanWriteTo(3));
        Assert.False(service.CanWriteTo(5));
        Assert.False(service.CanWriteTo(99));
    }

    [Fact]
    public void LongTextsInSeveralMessages()
    {
        var text = string.Join('\n', Enumerable.Range(0, 900).Select(i => "línea " + i));
        var parts = TelegramService.Split(text);
        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.InRange(p.Length, 1, TelegramService.MaxText));
        Assert.Equal(text.Replace("\n", "", StringComparison.Ordinal), string.Concat(parts).Replace("\n", "", StringComparison.Ordinal));
        Assert.Equal(["a"], TelegramService.Split("  a "));
        Assert.Equal(3, TelegramService.Split(new string('x', 10), 4).Count);
    }
}
