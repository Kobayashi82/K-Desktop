using System.Collections.Concurrent;
using KDesktop.Core.Assistant;
using KDesktop.Core.Model;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Tests;

/// <summary>Telegram, phase 3: commands without AI, confirmations, files that arrive and the event.</summary>
public sealed partial class TelegramTests
{
    private sealed class Setup : IDisposable
    {
        public required TelegramService Service { get; init; }
        public required FakeConnection Connection { get; init; }
        public required TelegramCommands Commands { get; init; }
        public ConcurrentQueue<string> Ran { get; } = new();
        public ConcurrentQueue<(TelegramIncoming M, string? Path)> Arrived { get; } = new();
        public void Dispose() => Service.Dispose();
    }

    private static readonly Command Game = new() { Name = "Modo juego" };
    private static readonly Command Lights = new() { Name = "Luces" };
    private static readonly Command Off = new() { Name = "Apagar el PC" };
    private static readonly Command Secret = new() { Name = "Secreto", Hidden = true };

    private async Task<Setup> Commanded(string downloads)
    {
        var (service, connection) = await Linked();
        Setup? setup = null;
        var commands = new TelegramCommands(service, () => [Game, Lights, Off, Secret], c =>
        {
            setup!.Ran.Enqueue(c.Name);
            return Task.CompletedTask;
        }, c => c == Off ? ToolRisk.AskAlways : ToolRisk.Safe)
        {
            DownloadFolder = () => downloads,
            ConfirmFor = TimeSpan.FromSeconds(5),
        };
        setup = new Setup { Service = service, Connection = connection, Commands = commands };
        commands.Arrived += (m, _, path) => setup.Arrived.Enqueue((m, path));
        service.Update(s =>
        {
            s.Contacts.Add(new TelegramContact { Id = 2, Name = "Ana", Level = TelegramLevel.ChosenCommands, Commands = [Lights.Id, Off.Id] });
            s.Contacts.Add(new TelegramContact { Id = 3, Name = "Pepe", Level = TelegramLevel.ChatOnly });
        });
        return setup;
    }

    private static IReadOnlyList<TelegramButton> LastKeyboard(FakeConnection c) => [.. c.Keyboards.Last().SelectMany(r => r)];

    private static async Task<string> NextText(FakeConnection c, long chat, int before)
    {
        await Until(() => c.Sent.Count(s => s.Chat == chat) > before);
        return c.Sent.Where(s => s.Chat == chat).Last().Text;
    }

    [Fact]
    public async Task CommandsListedAndRun()
    {
        using var s = await Commanded(_folder);
        var c = s.Connection;

        // The owner sees every command but the hidden ones, by name; Ana only hers; Pepe none.
        var keyboards = c.Keyboards.Count;
        c.Incoming.Add(Person(42, "Yo", "/comandos"));
        await Until(() => c.Keyboards.Count > keyboards);
        Assert.Equal(["Apagar el PC", "Luces", "Modo juego"], LastKeyboard(c).Select(b => b.Text));
        c.Incoming.Add(Person(2, "Ana", "/commands"));
        await Until(() => c.Keyboards.Count > keyboards + 1);
        Assert.Equal(["Apagar el PC", "Luces"], LastKeyboard(c).Select(b => b.Text));
        var pepe = c.Sent.Count(x => x.Chat == 3);
        c.Incoming.Add(Person(3, "Pepe", "/comandos"));
        Assert.Contains("No tienes comandos", await NextText(c, 3, pepe), StringComparison.Ordinal);

        // By name (case, accents, spaces or «_» do not matter), the hidden ones too; it says how it went.
        var mine = c.Sent.Count(x => x.Chat == 42);
        c.Incoming.Add(Person(42, "Yo", "/modo_JUEGO"));
        Assert.Equal("✓ Modo juego", await NextText(c, 42, mine));
        c.Incoming.Add(Person(42, "Yo", "/secreto"));
        Assert.Equal("✓ Secreto", await NextText(c, 42, mine + 1));
        c.Incoming.Add(Person(42, "Yo", "/nada"));
        Assert.Contains("No conozco ese comando", await NextText(c, 42, mine + 2), StringComparison.Ordinal);

        // A button: hers runs; one of a command she may not run does not.
        c.Incoming.Add(Button(2, "c:" + Lights.Id));
        await Until(() => s.Ran.Contains("Luces"));
        c.Incoming.Add(Button(2, "c:" + Game.Id));
        await Task.Delay(150);
        Assert.Equal(["Modo juego", "Secreto", "Luces"], s.Ran);

        // A text that is not a command does nothing yet (the assistant, phase 4), but the events get it.
        c.Incoming.Add(Person(42, "Yo", "hola"));
        await Until(() => s.Arrived.Any(a => a.M.Text == "hola"));
    }

    [Fact]
    public async Task ConfirmationsGoToTheOwner()
    {
        using var s = await Commanded(_folder);
        var c = s.Connection;

        // Ana asks for something risky: she is told to wait; the owner gets «Sí» / «No».
        var keyboards = c.Keyboards.Count;
        c.Incoming.Add(Person(2, "Ana", "/apagar el pc"));
        await Until(() => c.Keyboards.Count > keyboards);
        var yes = LastKeyboard(c).Single(b => b.Data.StartsWith("y:", StringComparison.Ordinal)).Data;
        Assert.Contains(c.Sent, x => x.Chat == 2 && x.Text.Contains("necesita el permiso", StringComparison.Ordinal));
        Assert.Empty(s.Ran);

        // Only the owner's answer counts.
        c.Incoming.Add(Button(2, yes));
        await Task.Delay(100);
        Assert.Empty(s.Ran);
        c.Incoming.Add(Button(42, yes));
        await Until(() => s.Ran.Contains("Apagar el PC"));
        Assert.Contains(c.Edited, e => e.Text.StartsWith('✓'));

        // «No»: not done, and she is told.
        keyboards = c.Keyboards.Count;
        c.Incoming.Add(Person(2, "Ana", "/apagar_el_pc"));
        await Until(() => c.Keyboards.Count > keyboards);
        c.Incoming.Add(Button(42, LastKeyboard(c).Single(b => b.Data.StartsWith("n:", StringComparison.Ordinal)).Data));
        await Until(() => c.Sent.Any(x => x.Chat == 2 && x.Text.EndsWith("no se ha lanzado.", StringComparison.Ordinal)));
        Assert.Single(s.Ran);

        // Nobody answers: it expires and is not done.
        s.Commands.ConfirmFor = TimeSpan.FromMilliseconds(150);
        c.Incoming.Add(Person(2, "Ana", "/apagar el pc"));
        await Until(() => c.Sent.Any(x => x.Chat == 2 && x.Text.Contains("nadie lo ha confirmado", StringComparison.Ordinal)));
        Assert.Single(s.Ran);

        // At the PC, it is asked there.
        string? askedHere = null;
        s.Commands.OwnerPresent = () => true;
        s.Commands.AskOnPc = q =>
        {
            askedHere = q;
            return Task.FromResult<bool?>(true);
        };
        c.Incoming.Add(Person(2, "Ana", "/apagar el pc"));
        await Until(() => s.Ran.Count == 2);
        Assert.Contains("Apagar el PC", askedHere, StringComparison.Ordinal);

        // The owner: as «Antes de hacer algo» says («Nunca»: not asked).
        s.Commands.OwnerPresent = () => false;
        s.Commands.Policy = () => AskPolicy.Never;
        c.Incoming.Add(Person(42, "Yo", "/apagar el pc"));
        await Until(() => s.Ran.Count == 3);
    }

    [Fact]
    public async Task FilesAreKeptAndTheEventFires()
    {
        var downloads = Path.Combine(_folder, "Descargas");
        using var s = await Commanded(downloads);
        var c = s.Connection;
        TelegramIncoming Doc(long from, string name) =>
            new(1, from, TelegramChatKind.Private, null, 1, new TelegramUser(from, "X", null), DateTimeOffset.UtcNow, "mira", TelegramContent.Document, new TelegramFile("f-" + name, name, null, 10));
        c.Incoming.Add(Doc(42, "factura.pdf"));
        c.Incoming.Add(Doc(42, "factura.pdf"));
        await Until(() => s.Arrived.Count(a => a.Path is not null) == 2);
        Assert.True(File.Exists(Path.Combine(downloads, "factura.pdf")));
        Assert.True(File.Exists(Path.Combine(downloads, "factura (2).pdf")));
        Assert.Contains(c.Sent, x => x.Chat == 42 && x.Text.Contains("Guardado en Descargas", StringComparison.Ordinal));

        // Someone new's file is not kept (and «todos los demás» ignores them: no event either).
        c.Incoming.Add(Doc(66, "virus.exe"));
        await Task.Delay(150);
        Assert.False(File.Exists(Path.Combine(downloads, "virus.exe")));

        // The event: from whom, words, kind; and its variables.
        var m = new TelegramIncoming(1, 2, TelegramChatKind.Private, null, 1, new TelegramUser(2, "Ana", "ana"), DateTimeOffset.UtcNow, "Apunta: comprar pan");
        Assert.False(new TelegramTrigger().Matches(m, fromOwner: false));
        Assert.True(new TelegramTrigger().Matches(m, fromOwner: true));
        Assert.True(new TelegramTrigger { OnlyOwner = false, From = "@ana", Contains = "apunta pan" }.Matches(m, false));
        Assert.False(new TelegramTrigger { OnlyOwner = false, From = "pepe" }.Matches(m, false));
        Assert.False(new TelegramTrigger { MessageKind = TelegramMessageKind.Voice }.Matches(m, true));
        var vars = Engine.Events.EventService.Variables(m, @"C:\x\foto.jpg");
        Assert.Equal("Ana (@ana)", vars.Named["telegram_de"]);
        Assert.Equal(@"C:\x\foto.jpg", vars.Named["telegram_file"]);
        Assert.Equal("2", vars.Named["telegram_id"]);
        Assert.Equal("Apunta: comprar pan", vars.Result);
    }

    [Fact]
    public async Task RecipientsAndQuestions()
    {
        var (service, connection) = await Linked();
        using var _ = service;
        service.Update(s => s.Contacts.Add(new TelegramContact { Id = 2, Name = "Ana", Username = "anita" }));
        Assert.Equal(42, service.Recipient(""));
        Assert.Equal(2, service.Recipient("2"));
        Assert.Equal(2, service.Recipient("@Anita"));
        Assert.Equal(2, service.Recipient("ana"));
        Assert.Null(service.Recipient("99"));
        Assert.Null(service.Recipient("Luis"));

        // A file with a caption longer than Telegram allows: the file, then the text.
        var file = Path.Combine(_folder, "a.txt");
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(file, "x");
        var before = connection.Sent.Count;
        Assert.True(await service.SendAsync(42, new string('a', 2000), file));
        Assert.Equal(before + 1, connection.Sent.Count); // the file is not a text; the text is

        // A question nobody answers.
        Assert.Null(await service.AskOwnerAsync("¿Seguro?", TimeSpan.FromMilliseconds(100)));
        await Until(() => connection.Edited.Any(e => e.Text.Contains("Sin respuesta", StringComparison.Ordinal)));
    }
}
