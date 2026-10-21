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
}
