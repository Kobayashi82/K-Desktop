using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>D1 (5.0.370): «Asistente: preguntar» waits for the answer and does something with it.</summary>
public class AssistantAnswerTests
{
    private static AssistantAskAction Ask(AssistantAnswer answer, string text = "resume esto", string? saveAs = null) =>
        new() { Assistant = "lander", Text = text, Answer = answer, SaveAs = saveAs };

    private static AssistantSayAction Say(string text) => new() { Assistant = "kdesktop", Text = text };

    private static (CommandEngine Engine, FakeUi Ui, Command Command) Make(params CommandAction[] actions)
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var command = new Command { Name = "a" };
        foreach (var a in actions) command.Actions.Add(a);
        store.Config.Commands.Add(command);
        return (new CommandEngine(store, ui), ui, command);
    }

    [Fact]
    public async Task TheAnswerIsLeftForTheNextActions()
    {
        var (engine, ui, command) = Make(Ask(AssistantAnswer.Keep, "traduce: hola", "traduccion"), Say("{resultado} / {traduccion}"));
        ui.Reply = (who, text) => new RepliedMessage("r", true, text == "traduce: hola" ? "hello" : "?", Assistant: who);

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal([("lander", "traduce: hola")], ui.Asked);
        Assert.Equal("hello / hello", Assert.Single(ui.Said).Text);
        Assert.Empty(ui.Copied);
        Assert.Empty(ui.Sheets);
    }

    [Fact]
    public async Task ALongAnswerGivesItsWholeText()
    {
        var (engine, ui, command) = Make(Ask(AssistantAnswer.Sheet), Say("{resultado}"));
        ui.Reply = (who, _) => new RepliedMessage("r", true, "El resumen.", "# Todo\n\nEl texto entero.", who);

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal(("lander", "# Todo\n\nEl texto entero."), Assert.Single(ui.Sheets));
        Assert.Equal("# Todo\n\nEl texto entero.", Assert.Single(ui.Said).Text);
    }

    [Fact]
    public async Task CopyLeavesItCopied()
    {
        var (engine, ui, command) = Make(Ask(AssistantAnswer.Copy));
        ui.Reply = (who, _) => new RepliedMessage("r", true, "corto", Assistant: who);

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal(["corto"], ui.Copied);
    }

    [Fact]
    public async Task WithoutAnAnswerTheRestIsNotRun()
    {
        var (engine, ui, command) = Make(Ask(AssistantAnswer.Keep), Say("después"));

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Empty(ui.Said);
        Assert.Equal(RunOutcome.Cancelled, Assert.Single(engine.History.Entries).Outcome);
    }

    [Fact]
    public async Task AFailedAnswerIsShownAndNotUsed()
    {
        var (engine, ui, command) = Make(Ask(AssistantAnswer.Copy), Say("después"));
        ui.Reply = (who, _) => new RepliedMessage("r", false, "No tengo cerebro ahora.", Assistant: who);

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Empty(ui.Copied);
        Assert.Empty(ui.Said);
        Assert.Contains(ui.Osd, o => o.Text == "No tengo cerebro ahora.");
    }

    [Fact]
    public async Task SayingItDoesNotWait()
    {
        var (engine, ui, command) = Make(Ask(AssistantAnswer.Say, "hola"), Say("[{resultado}]"));
        ui.Reply = (_, _) => throw new InvalidOperationException("no debería esperar la respuesta");

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal([("lander", "hola")], ui.Asked);
        Assert.Equal("[]", Assert.Single(ui.Said).Text);
    }

    [Fact]
    public void TheAnswerAndBringingOutAreSaved()
    {
        var ask = Ask(AssistantAnswer.Paste);
        ask.BringOut = false;
        var back = Assert.IsType<AssistantAskAction>(Json.Deserialize<CommandAction>(Json.Serialize<CommandAction>(ask)));
        Assert.Equal((AssistantAnswer.Paste, false), (back.Answer, back.BringOut));
        Assert.True(back.GivesResult);
        Assert.Contains("«resume esto»", back.Summary, StringComparison.Ordinal);

        // From before 5.0.370: said, and it comes out (like «Asistente: acción»).
        var old = Assert.IsType<AssistantAskAction>(Json.Deserialize<CommandAction>("""{"type":"assistantAsk","assistant":"lander","text":"hola"}"""));
        Assert.Equal((AssistantAnswer.Say, true), (old.Answer, old.BringOut));
        Assert.False(old.GivesResult);
    }
}
