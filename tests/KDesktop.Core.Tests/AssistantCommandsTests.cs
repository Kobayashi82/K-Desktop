using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>Phase 4 of the assistant plan: the assistants find K-Desktop's commands (4.1) and run them (4.2).</summary>
public class AssistantCommandsTests
{
    private static Command Make(string name, string? message = null)
    {
        var command = new Command { Name = name };
        command.Actions.Add(new ShowMessageAction { Text = message ?? name });
        return command;
    }

    [Fact]
    public void AnOrderOfTwoGivesBothPartsWithTheirScore()
    {
        var bin = Make("Vaciar papelera");
        var commands = new[] { bin, Make("Abrir Chrome"), Make("Apagar equipo") };
        var matched = AssistantCommands.Match(new MatchMessage("m-1", "vacía la papelera y baja el volumen"), commands, media: true, semantic: null);
        Assert.Equal("m-1", matched.Id);
        Assert.Equal(2, matched.Parts.Count);
        Assert.All(matched.Parts, p => Assert.True(p.Sure));
        Assert.Equal(bin.Id, matched.Parts[0].Candidates[0].Command);
        Assert.Equal("volumeDown", matched.Parts[1].Candidates[0].Media);
        Assert.Null(matched.Parts[1].Candidates[0].Command);
        Assert.All(matched.Parts, p => Assert.InRange(p.Candidates[0].Score, 0.8, 1.0));
        Assert.Empty(matched.Unmatched);
        Assert.True(matched.Score > 0.8);
    }

    [Theory]
    [InlineData("vacía la papelera, luego la abres")]
    [InlineData("vacía la papelera y ábrela")]
    [InlineData("vacía la papelera y luego habrela")]
    public void ItOrThemIsTheThingOfTheOrderBefore(string said)
    {
        var empty = Make("Vaciar Papelera");
        var open = Make("Abrir Papelera");
        var matched = AssistantCommands.Match(new MatchMessage("m", said), [empty, open, Make("Anterior")], media: false, semantic: null);
        Assert.Equal([empty.Id, open.Id], matched.Parts.Select(p => p.Candidates[0].Command));
        Assert.All(matched.Parts, p => Assert.True(p.Sure));
        Assert.Empty(matched.Unmatched);
    }

    [Fact]
    public void AnArticleIsNotAPronoun()
    {
        var matched = AssistantCommands.Match(new MatchMessage("m", "vacía la papelera y la vuelta"), [Make("Vaciar Papelera"), Make("Abrir Papelera")], media: false, semantic: null);
        Assert.Equal(["la vuelta"], matched.Unmatched);
    }

    [Fact]
    public void WhatItSaysNoToIsNotACandidate()
    {
        var previous = Make("Anterior");
        var commands = new[] { previous, Make("No molestar") };
        var refused = Assert.Single(AssistantCommands.Match(new MatchMessage("m", "no anterior"), commands, media: false, semantic: null).Parts);
        Assert.False(refused.Sure);
        Assert.DoesNotContain(refused.Candidates, c => c.Command == previous.Id);
        // A command whose name says no is still what it names.
        var named = Assert.Single(AssistantCommands.Match(new MatchMessage("m", "no molestar"), commands, media: false, semantic: null).Parts);
        Assert.True(named.Sure);
        Assert.Equal("No molestar", named.Candidates[0].Name);
    }

    [Fact]
    public void ALoneShortWordIsNotTheStartOfALongOne()
    {
        // «coño» ran the built-in «Reanudar» (its phrase «continúa»): three shared letters and a long ending.
        Assert.True(Voice.TextSimilarity.WordSimilarity("cono", "continua") < 0.6);
        Assert.True(Voice.TextSimilarity.WordSimilarity("abre", "abrir") >= 0.85);
        var matched = AssistantCommands.Match(new MatchMessage("m", "coño"), [Make("Vaciar Papelera")], media: true, semantic: null);
        Assert.DoesNotContain(matched.Parts, p => p.Sure);
    }

    [Fact]
    public void EachCandidateComesOnceBestFirst()
    {
        var youtube = Make("YouTube");
        youtube.VoicePhrases.Add("pon youtube");
        var matched = AssistantCommands.Match(new MatchMessage("m", "abre la tele"), [youtube, Make("Abrir Telegram"), Make("Teletexto")], media: false, semantic: null);
        var part = Assert.Single(matched.Parts);
        Assert.Equal(part.Candidates.Count, part.Candidates.Select(c => c.Command).Distinct().Count());
        Assert.Equal(part.Candidates.OrderByDescending(c => c.Score).Select(c => c.Score), part.Candidates.Select(c => c.Score));
    }

    [Fact]
    public void CommandsThatAskAnAssistantAreNotItsToRun()
    {
        var config = new AppConfig();
        var ask = new Command { Name = "Preguntar notas" };
        ask.Actions.Add(new AssistantAskAction { Text = "¿qué notas tengo?" });
        var greet = Make("Saludo");
        config.Commands.Add(ask);
        config.Commands.Add(greet);
        Assert.Equal([greet], AssistantCommands.Available(config));
    }

    [Fact]
    public void NothingClearGivesTheWholeTextNotSure()
    {
        var matched = AssistantCommands.Match(new MatchMessage("m-2", "cuéntame un chiste"), [Make("Vaciar papelera")], media: false, semantic: null);
        var part = Assert.Single(matched.Parts);
        Assert.False(part.Sure);
        Assert.Equal("cuéntame un chiste", part.Text);
    }

    [Fact]
    public void BuiltInMediaOrdersAreNamed()
    {
        Assert.Equal(new Voice.MediaOrder(Voice.MediaOrderKind.VolumeSet, 30), AssistantCommands.MediaOrderOf(new MediaMessage("volumeSet", 30)));
        Assert.Null(AssistantCommands.MediaOrderOf(new MediaMessage("dance")));
    }

    [Fact]
    public async Task ItRunsACommandRecordedAsTheAssistants()
    {
        var store = new ConfigStore();
        var command = Make("Saludo", "¡Hola!");
        store.Config.Commands.Add(command);
        var engine = new CommandEngine(store, new FakeUi());
        var end = await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-1", command.Id), "Lander", Yes);
        Assert.Equal(("r-1", "ok"), (end.Run, end.Status));
        var entry = Assert.Single(engine.History.Entries);
        Assert.Equal(TriggerSource.Assistant, entry.Source);
        Assert.Equal("Lander", entry.Detail);
        Assert.Equal(command.Id, entry.CommandId);

        // Gone, or turned off: failed at once, with why.
        Assert.Equal("failed", (await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-2", "nope"), "Lander", Yes)).Status);
        command.Enabled = false;
        var off = await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-3", command.Id), "Lander", Yes);
        Assert.Equal("failed", off.Status);
        Assert.NotEmpty(off.Messages);
    }

    [Fact]
    public async Task PausedItRunsNothing()
    {
        var store = new ConfigStore();
        var command = Make("Saludo");
        store.Config.Commands.Add(command);
        store.Config.Settings.Paused = true;
        var engine = new CommandEngine(store, new FakeUi());
        var end = await AssistantCommands.RunAsync(engine, new RunMessage("", "r-1", command.Id), "K-Desktop", Yes);
        Assert.Equal("failed", end.Status);
        Assert.Empty(engine.History.Entries);
    }

    private static Task<string> Yes(Command command, ToolRisk risk) => Task.FromResult("yes");

    [Fact]
    public async Task ItAsksFirstUnlessTrustedAndNotRisky()
    {
        var store = new ConfigStore();
        var greet = Make("Saludo");
        var power = new Command { Name = "Apagar" };
        power.Actions.Add(new PowerAction());
        var night = Make("Buenas noches");
        night.Actions.Add(new CommandControlAction { Mode = CommandControlMode.Run, CommandId = power.Id });
        foreach (var c in new[] { greet, power, night }) store.Config.Commands.Add(c);
        store.Config.Settings.AssistantAskBefore = AskPolicy.First;
        var engine = new CommandEngine(store, new FakeUi());
        var asked = new List<(string Name, ToolRisk Risk)>();
        Func<Command, ToolRisk, Task<string>> no = (c, r) => { asked.Add((c.Name, r)); return Task.FromResult("no"); };

        // The first time it asks, and without a «yes» nothing runs.
        var end = await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-1", greet.Id), "Lander", no);
        Assert.Equal("declined", end.Status);
        Assert.Equal(("Saludo", ToolRisk.AskOnce), asked[^1]);
        Assert.Empty(engine.History.Entries);

        // Trusted: it runs without asking.
        store.Config.Settings.AssistantTrustedCommands = [greet.Id];
        Assert.Equal("ok", (await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-2", greet.Id), "Lander", no)).Status);
        Assert.Single(asked);

        // One that calls a power command always asks, trusted or not.
        store.Config.Settings.AssistantTrustedCommands = [greet.Id, night.Id];
        Assert.Equal("declined", (await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-3", night.Id), "Lander", no)).Status);
        Assert.Equal(("Buenas noches", ToolRisk.AskAlways), asked[^1]);
    }

    [Fact]
    public async Task ByDefaultOnlyWhatIsRiskyAsksAndNeverAsksNothing()
    {
        var store = new ConfigStore();
        var greet = Make("Saludo");
        var power = new Command { Name = "Apagar" };
        power.Actions.Add(new PowerAction { Mode = PowerMode.Lock });
        foreach (var c in new[] { greet, power }) store.Config.Commands.Add(c);
        var engine = new CommandEngine(store, new FakeUi());
        var asked = new List<string>();
        Func<Command, ToolRisk, Task<string>> no = (c, _) => { asked.Add(c.Name); return Task.FromResult("no"); };

        Assert.Equal(AskPolicy.Risky, store.Config.Settings.AssistantAskBefore);
        Assert.Equal("ok", (await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-1", greet.Id), "Lander", no)).Status);
        Assert.Equal("declined", (await AssistantCommands.RunAsync(engine, new RunMessage("lander", "r-2", power.Id), "Lander", no)).Status);
        Assert.Equal(["Apagar"], asked);

        // «Never»: not even power asks (it is not run here: only described).
        Assert.Equal("always", AssistantCommands.Describe(store.Config, new DescribeMessage("d-1", power.Id)).Asks);
        store.Config.Settings.AssistantAskBefore = AskPolicy.Never;
        Assert.Equal("no", AssistantCommands.Describe(store.Config, new DescribeMessage("d-2", power.Id)).Asks);
    }

    [Theory]
    [InlineData(ToolRisk.Safe, AskPolicy.First, false, false)]
    [InlineData(ToolRisk.AskOnce, AskPolicy.First, false, true)]
    [InlineData(ToolRisk.AskOnce, AskPolicy.First, true, false)]
    [InlineData(ToolRisk.AskAlways, AskPolicy.First, true, true)]
    [InlineData(ToolRisk.AskOnce, AskPolicy.Risky, false, false)]
    [InlineData(ToolRisk.AskAlways, AskPolicy.Risky, false, true)]
    [InlineData(ToolRisk.AskAlways, AskPolicy.Never, false, false)]
    [InlineData(ToolRisk.AskAlways, "", false, true)]
    public void ThePolicyDecidesWhatIsAsked(ToolRisk risk, string policy, bool trusted, bool asks) =>
        Assert.Equal(asks, AskPolicy.Asks(risk, policy, trusted));

    [Theory]
    [InlineData("Sierra Steam", true)]
    [InlineData("cierra Steam", true)]
    [InlineData("close Steam", true)]
    [InlineData("sal de Steam", true)]
    [InlineData("abre Steam", false)]
    [InlineData("Steam", false)]
    [InlineData("abre Steam y cierra el navegador", false)]
    public void AnOrderToCloseNeverRunsACommandThatOpens(string said, bool contradicts)
    {
        var steam = new Command { Name = "Steam" };
        steam.Actions.Add(new RunAppAction { Path = "steam.exe" });
        Assert.Equal(contradicts, AssistantCommands.Contradicts(said, steam));
    }

    [Fact]
    public void AnOrderToOpenNeverRunsACommandThatCloses()
    {
        var close = new Command { Name = "Cerrar Steam" };
        close.Actions.Add(new AppControlAction { Mode = AppControlMode.Close, ProcessName = "steam" });
        Assert.True(AssistantCommands.Contradicts("abre Steam", close));
        Assert.False(AssistantCommands.Contradicts("cierra Steam", close));
        // A command that does something else (power, volume…) is never held back by the verbs.
        Assert.False(AssistantCommands.Contradicts("apaga el ordenador", new Command { Name = "Apagar", Actions = { new PowerAction() } }));
    }
}
