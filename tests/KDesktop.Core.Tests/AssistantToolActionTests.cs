using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>G1: «Usar una herramienta» runs one of the assistants' tools without the AI and leaves its answer in {resultado}.</summary>
public class AssistantToolActionTests
{
    private static AssistantToolAction Tool(string arguments, string tool = "mcp_telegram_send_message", string? saveAs = null) =>
        new() { Tool = tool, Arguments = arguments, SaveAs = saveAs };

    private static AssistantSayAction Say(string text) => new() { Assistant = "kdesktop", Text = text };

    private static (CommandEngine Engine, FakeUi Ui, Command Command) Make(params CommandAction[] actions)
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var command = new Command { Name = "Mandar a Telegram" };
        foreach (var a in actions) command.Actions.Add(a);
        store.Config.Commands.Add(command);
        return (new CommandEngine(store, ui), ui, command);
    }

    [Fact]
    public async Task TheAnswerIsLeftForTheNextActions()
    {
        var (engine, ui, command) = Make(Tool("""{"chat_id":"123","text":"hola"}""", saveAs: "enviado"), Say("{resultado} / {enviado}"));
        ui.ToolAnswer = (tool, _) => new ToolTestMessage(tool, true, "Message sent.", 40, "x");

        await engine.RunAsync(command, TriggerSource.Hotkey);

        var used = Assert.Single(ui.Tools);
        Assert.Equal("mcp_telegram_send_message", used.Tool);
        Assert.False(used.Unattended);
        Assert.Equal("Message sent. / Message sent.", Assert.Single(ui.Said).Text);
    }

    [Fact]
    public void VariablesGoInsideEveryText()
    {
        var arguments = CommandEngine.ExpandArguments("""{"text":"Dijo: {fecha}","list":["{fecha}",1],"flag":true,"inner":{"a":"{fecha}"}}""");
        using var parsed = JsonDocument.Parse(arguments);
        var today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("Dijo: " + today, parsed.RootElement.GetProperty("text").GetString());
        Assert.Equal(today, parsed.RootElement.GetProperty("list")[0].GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("list")[1].GetInt32());
        Assert.True(parsed.RootElement.GetProperty("flag").GetBoolean());
        Assert.Equal(today, parsed.RootElement.GetProperty("inner").GetProperty("a").GetString());
    }

    [Fact]
    public async Task AResultWithQuotesStaysGoodJson()
    {
        var (engine, ui, command) = Make(Tool("{}", tool: "first"), Tool("""{"text":"«{resultado}»"}"""));
        ui.ToolAnswer = (tool, _) => new ToolTestMessage(tool, true, tool == "first" ? "dijo \"hola\"\ny adiós" : "ok", 1, "x");

        await engine.RunAsync(command, TriggerSource.Hotkey);

        Assert.Equal(2, ui.Tools.Count);
        using var parsed = JsonDocument.Parse(ui.Tools[1].Arguments);
        Assert.Equal("«dijo \"hola\"\ny adiós»", parsed.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void BrokenJsonIsSentAsItIs() => Assert.Equal("{nope", CommandEngine.ExpandArguments("{nope"));

    [Theory]
    [InlineData(TriggerSource.Hotkey, false)]
    [InlineData(TriggerSource.Voice, false)]
    [InlineData(TriggerSource.QuickMenu, false)]
    [InlineData(TriggerSource.Event, true)]
    [InlineData(TriggerSource.Startup, true)]
    [InlineData(TriggerSource.Web, true)]
    public async Task RunByItselfItSaysSo(TriggerSource source, bool unattended)
    {
        var (engine, ui, command) = Make(Tool("{}"));
        ui.ToolAnswer = (tool, _) => new ToolTestMessage(tool, true, "ok", 1, "x");

        await engine.RunAsync(command, source);

        Assert.Equal(unattended, Assert.Single(ui.Tools).Unattended);
    }

    [Fact]
    public async Task NotDoneTheRestIsNotRun()
    {
        var (engine, ui, command) = Make(Tool("{}"), Say("después"));

        await engine.RunAsync(command, TriggerSource.Hotkey);

        Assert.Empty(ui.Said);
        Assert.Equal(RunOutcome.Cancelled, Assert.Single(engine.History.Entries).Outcome);
    }

    [Fact]
    public async Task AFailureIsShownAndTheRestIsNotRun()
    {
        var (engine, ui, command) = Make(Tool("{}"), Say("después"));
        ui.ToolAnswer = (tool, _) => new ToolTestMessage(tool, false, "No responde el servidor.", 1, "x");

        await engine.RunAsync(command, TriggerSource.Hotkey);

        Assert.Empty(ui.Said);
        Assert.Contains(ui.Osd, o => o.Text == "No responde el servidor.");
    }

    [Fact]
    public async Task WithoutAToolNothingIsAsked()
    {
        var (engine, ui, command) = Make(Tool("{}", tool: ""), Say("después"));

        await engine.RunAsync(command, TriggerSource.Hotkey);

        Assert.Empty(ui.Tools);
        Assert.Single(ui.Said);
    }

    [Fact]
    public void ItIsKeptAndReadBack()
    {
        var action = Tool("""{"text":"{seleccion}"}""");
        var back = Assert.IsType<AssistantToolAction>(Json.Clone<CommandAction>(action));
        Assert.Equal(action.Tool, back.Tool);
        Assert.Equal(action.Arguments, back.Arguments);
        Assert.True(back.GivesResult);
        Assert.True(TextVariables.UsesSelection(back));
        Assert.Equal(ToolRisk.AskOnce, ActionRisk.Of(back));
    }
}
