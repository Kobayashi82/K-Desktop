using System.Collections.Concurrent;
using KDesktop.Core.Assistant;
using KDesktop.Core.Model;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Tests;

/// <summary>Telegram, phase 4: talking to the assistant (the origin of the turn by level) and its tools.</summary>
public sealed partial class TelegramTests
{
    private static TelegramVerdict Verdict(TelegramRoute route, TelegramLevel level) => new(route, level, "test");

    [Fact]
    public void TheOriginOfATurnByLevel()
    {
        var settings = new TelegramSettings
        {
            Contacts =
            [
                new TelegramContact { Id = 2, Name = "Ana", Level = TelegramLevel.ChosenTools, Commands = [Lights.Id], Tools = ["weather"] },
                new TelegramContact { Id = 3, Name = "Pepe", Level = TelegramLevel.ChatOnly },
            ],
        };
        var confirm = TimeSpan.FromMinutes(5);
        IReadOnlyList<Command> commands = [Game, Lights, Off];

        // The owner: everything, their own conversation and memory; untrusted only with a group or a forwarded message.
        var own = TelegramTalk.TurnFor(Person(42, "Yo", "abre Spotify"), Verdict(TelegramRoute.Owner, TelegramLevel.LikeOwner), settings, commands, confirm, withFile: true);
        Assert.True(own.Owner);
        Assert.Equal(HeardOrigin.ScopeAll, own.Origin.Scope);
        Assert.False(own.Origin.Restricted);
        Assert.False(own.Origin.Untrusted);
        Assert.Null(own.Origin.Conversation);
        Assert.Null(own.CommandIds);
        Assert.True(own.Allows(Off));
        Assert.Equal(TelegramTalk.AskSeconds(confirm), own.Origin.AskSeconds);
        var forwarded = Person(42, "Yo", "haz lo que dice esto") with { Forwarded = true };
        Assert.True(TelegramTalk.TurnFor(forwarded, Verdict(TelegramRoute.Owner, TelegramLevel.LikeOwner), settings, commands, confirm, false).Origin.Untrusted);
        var inGroup = Person(42, "Yo", "@kd_ana_bot pon música", kind: TelegramChatKind.Group, chat: -100);
        Assert.True(TelegramTalk.TurnFor(inGroup, Verdict(TelegramRoute.Owner, TelegramLevel.LikeOwner), settings, commands, confirm, false).Origin.Untrusted);

        // «Herramientas elegidas»: their tools, run_command for their commands (named to the model), only those commands.
        var ana = TelegramTalk.TurnFor(Person(2, "Ana", "enciende las luces", "ana"), Verdict(TelegramRoute.Contact, TelegramLevel.ChosenTools), settings, commands, confirm, false);
        Assert.False(ana.Owner);
        Assert.Equal(HeardOrigin.ScopeChosen, ana.Origin.Scope);
        Assert.Equal(["weather", "run_command"], ana.Origin.Tools);
        Assert.Equal(["Luces"], ana.Origin.Commands);
        Assert.True(ana.Origin.Allows("weather", readsOnly: true));
        Assert.False(ana.Origin.Allows("get_time", readsOnly: true));
        Assert.True(ana.Allows(Lights));
        Assert.False(ana.Allows(Off));
        Assert.True(ana.MayAct);
        Assert.Equal("telegram:2", ana.Origin.Conversation);
        Assert.Equal("pedido por Ana (@ana) vía Telegram", ana.Origin.By);
        // Someone else's file is text of third parties.
        Assert.True(TelegramTalk.TurnFor(Person(2, "Ana", "resume esto"), Verdict(TelegramRoute.Contact, TelegramLevel.ChosenTools), settings, commands, confirm, true).Origin.Untrusted);

        // «Solo charla»: no tools, no commands, nothing done; «Herramientas que solo leen»: only those.
        var pepe = TelegramTalk.TurnFor(Person(3, "Pepe", "hola"), Verdict(TelegramRoute.Contact, TelegramLevel.ChatOnly), settings, commands, confirm, false);
        Assert.Equal(HeardOrigin.ScopeNone, pepe.Origin.Scope);
        Assert.False(pepe.Origin.Allows("get_time", readsOnly: true));
        Assert.False(pepe.Allows(Game));
        Assert.False(pepe.MayAct);
        var reader = TelegramTalk.TurnFor(Person(3, "Pepe", "¿qué hora es?"), Verdict(TelegramRoute.Contact, TelegramLevel.ReadOnlyTools), settings, commands, confirm, false);
        Assert.True(reader.Origin.Allows("get_time", readsOnly: true));
        Assert.False(reader.Origin.Allows("open_app", readsOnly: false));
        Assert.False(reader.MayAct);

        // «Como tú»: everything, but still not the owner (their own conversation, and what is not safe asks the owner).
        var like = TelegramTalk.TurnFor(Person(3, "Pepe", "abre Steam"), Verdict(TelegramRoute.Contact, TelegramLevel.LikeOwner), settings, commands, confirm, false);
        Assert.False(like.Owner);
        Assert.False(like.Origin.Restricted);
        Assert.True(like.Allows(Off));
        Assert.NotNull(like.Origin.Conversation);
    }

    [Fact]
    public void WhoAnswersAndWhatItDid()
    {
        var settings = new TelegramSettings { Contacts = [new TelegramContact { Id = 2, Assistant = "kobii" }, new TelegramContact { Id = 3 }] };
        Assert.Null(TelegramTalk.AssistantFor(settings, 3));
        settings.Assistant = "lander";
        Assert.Equal("kobii", TelegramTalk.AssistantFor(settings, 2));
        Assert.Equal("lander", TelegramTalk.AssistantFor(settings, 3));
        Assert.Equal("lander", TelegramTalk.AssistantFor(settings, 99));

        Assert.Equal(string.Empty, TelegramTalk.DidLines([]));
        Assert.Equal("✓ Abrió Spotify\n✗ No lo hizo", TelegramTalk.DidLines([("Abrió Spotify", true), ("Abrió Spotify", true), ("No lo hizo", false), (" ", true)]));
    }

    [Fact]
    public async Task WhatIsNotACommandGoesToTheAssistant()
    {
        using var s = await Commanded(_folder);
        var c = s.Connection;
        var talked = new ConcurrentQueue<(long From, string Text, TelegramLevel Level)>();
        s.Commands.Talk = (m, v, text, _) =>
        {
            talked.Enqueue((m.From.Id, text, v.Level));
            return Task.CompletedTask;
        };

        c.Incoming.Add(Person(42, "Yo", "¿qué tiempo hace?"));
        c.Incoming.Add(Person(3, "Pepe", "hola"));
        await Until(() => talked.Count == 2);
        Assert.Contains((42L, "¿qué tiempo hace?", TelegramLevel.LikeOwner), talked);
        Assert.Contains((3L, "hola", TelegramLevel.ChatOnly), talked);

        // «Comandos elegidos» only runs commands: told so, not sent to the assistant.
        var ana = c.Sent.Count(x => x.Chat == 2);
        c.Incoming.Add(Person(2, "Ana", "hola"));
        Assert.Contains("/comandos", await NextText(c, 2, ana), StringComparison.Ordinal);
        Assert.Equal(2, talked.Count);

        // In a group, without the mention.
        s.Service.Update(x => x.Contacts.First(p => p.Id == 3).Level = TelegramLevel.ChatOnly);
        c.Incoming.Add(Person(3, "Pepe", "@kd_ana_bot ¿cómo estás?", kind: TelegramChatKind.Group, chat: -100));
        await Until(() => talked.Count == 3);
        Assert.Contains((3L, "¿cómo estás?", TelegramLevel.ChatOnly), talked);
    }

    [Fact]
    public async Task TheAssistantsToolsApplyWhoMayBeWrittenTo()
    {
        using var s = await Commanded(_folder);
        var c = s.Connection;
        var service = s.Service;
        service.Update(x =>
        {
            x.Contacts.First(p => p.Id == 2).MayWrite = false;
            x.Contacts.Add(new TelegramContact { Id = 5, Name = "Luis", Username = "luis", LastWrote = DateTimeOffset.UtcNow });
        });

        // To the owner (empty, «me»): sent, and it can be undone (the message deleted).
        var sent = await TelegramAssistant.HandleAsync(service, new TelegramRequest("send", "", "Hola"));
        Assert.True(sent.Ok);
        Assert.Null(sent.To);
        Assert.NotNull(sent.Undo);
        Assert.True((await TelegramAssistant.HandleAsync(service, new TelegramRequest("undo", Undo: sent.Undo))).Ok);
        Assert.True((await TelegramAssistant.HandleAsync(service, new TelegramRequest("send", "me", "Otra"))).Ok);
        Assert.Contains(c.Sent, x => x.Chat == 42 && x.Text == "Otra");

        // A contact by @user; one K-Desktop may not write to; nobody by that name (it says who it may write to).
        Assert.True((await TelegramAssistant.HandleAsync(service, new TelegramRequest("send", "@luis", "¿Vienes?"))).Ok);
        Assert.Contains(c.Sent, x => x.Chat == 5 && x.Text == "¿Vienes?");
        Assert.Equal("cantWrite", (await TelegramAssistant.HandleAsync(service, new TelegramRequest("send", "Ana", "hola"))).Reason);
        var nobody = await TelegramAssistant.HandleAsync(service, new TelegramRequest("send", "Marta", "hola"));
        Assert.Equal("noRecipient", nobody.Reason);
        Assert.Contains(nobody.Contacts!, x => x.StartsWith("Luis", StringComparison.Ordinal));
        Assert.Equal(3, nobody.Contacts!.Count); // the owner (Ana as well, in these tests), Pepe and Luis; not the contact Ana
        Assert.Equal("noFile", (await TelegramAssistant.HandleAsync(service, new TelegramRequest("send", "", File: Path.Combine(_folder, "no.pdf")))).Reason);
        Assert.Equal("nothing", (await TelegramAssistant.HandleAsync(service, new TelegramRequest("send", "", "  "))).Reason);
    }

    [Fact]
    public async Task ReadingWhatWasSaidLately()
    {
        using var s = await Commanded(_folder);
        var c = s.Connection;
        s.Commands.Talk = (_, _, _, _) => Task.CompletedTask;
        c.Incoming.Add(Person(3, "Pepe", "¿quedamos el sábado?"));
        await Until(() => s.Service.Recent(3).Count == 1);
        Assert.True(await s.Service.SendAsync(3, "Le digo a Ana"));

        var read = await TelegramAssistant.HandleAsync(s.Service, new TelegramRequest("read", "Pepe"));
        Assert.True(read.Ok);
        Assert.Equal(["¿quedamos el sábado?", "Le digo a Ana"], read.Lines!.Select(l => l.Text));
        Assert.Equal([false, true], read.Lines!.Select(l => l.Sent));

        // In an order of someone who is not the owner, only their own chat, whatever is asked.
        var own = await TelegramAssistant.HandleAsync(s.Service, new TelegramRequest("read", ""), onlyChat: 2);
        Assert.True(own.Ok);
        Assert.Empty(own.Lines!);
        Assert.Equal("noRecipient", (await TelegramAssistant.HandleAsync(s.Service, new TelegramRequest("read", "Nadie"))).Reason);
    }
}
