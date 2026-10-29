using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>5.0.338: what one action gives reaches the next ones ({resultado}, «Guardar en variable»).</summary>
public class CommandVariablesTests
{
    private static ClipboardAction Copy(string text, string? saveAs = null) =>
        new() { Mode = ClipboardMode.SetText, Text = text, Paste = false, SaveAs = saveAs };

    private static AssistantSayAction Say(string text) => new() { Assistant = "kdesktop", Text = text };

    private static Command Add(ConfigStore store, string name, params CommandAction[] actions)
    {
        var command = new Command { Name = name };
        foreach (var a in actions) command.Actions.Add(a);
        store.Config.Commands.Add(command);
        return command;
    }

    [Fact]
    public async Task TheResultAndNamedVariablesReachTheNextActions()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var command = Add(store, "a", Copy("Madrid", "ciudad"), Copy("{resultado}!"), Say("{ciudad} · {resultado} · {nada}"));

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal(("kdesktop", "Madrid · Madrid! · {nada}"), Assert.Single(ui.Said));
    }

    [Fact]
    public async Task EachRunStartsEmpty()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var first = Add(store, "a", Copy("x", "v"));
        var second = Add(store, "b", Say("[{resultado}][{v}]"));

        await engine.RunAsync(first, TriggerSource.Manual);
        await engine.RunAsync(second, TriggerSource.Manual);

        Assert.Equal("[][{v}]", Assert.Single(ui.Said).Text);
    }

    [Fact]
    public async Task ACommandRunByAnotherSharesTheVariables()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var inner = Add(store, "inner", Say("dentro: {v}"), Copy("de vuelta", "w"));
        var outer = Add(store, "outer", Copy("hola", "v"),
            new CommandControlAction { Mode = CommandControlMode.Run, CommandId = inner.Id }, Say("fuera: {w}"));

        await engine.RunAsync(outer, TriggerSource.Manual);

        Assert.Equal(["dentro: hola", "fuera: de vuelta"], ui.Said.Select(s => s.Text));
    }

    [Fact]
    public async Task TheCommandsOfAnEventShareTheVariables()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var first = Add(store, "a", Copy("42"));
        var second = Add(store, "b", Say("{resultado}"));
        var rule = new EventRule { Name = "e" };
        rule.Commands.Add(first.Id);
        rule.Commands.Add(second.Id);

        await engine.RunEventAsync(rule, false, TriggerSource.Manual);

        Assert.Equal("42", Assert.Single(ui.Said).Text);
    }

    [Fact]
    public async Task SayingTheSelectionWithNothingSelectedSaysWhyInTheOsd()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var command = Add(store, "a");

        // Without a run, {seleccion} is empty (nothing is copied from the PC).
        await engine.TestActionAsync(Say("{seleccion}"), command);

        Assert.Empty(ui.Said);
        Assert.Equal(OsdKind.Warning, Assert.Single(ui.Osd).Kind);
    }

    [Fact]
    public void SayBringsItsAssistantOutAlsoInOldCommands()
    {
        Assert.True(new AssistantSayAction().BringOut);
        var old = Assert.IsType<AssistantSayAction>(Json.Deserialize<CommandAction>("""{"type":"assistantSay","assistant":"lander","text":"hola"}"""));
        Assert.True(old.BringOut);
    }

    [Fact]
    public void ExpandLeavesUnknownBracesAndEscapesOnlyPartsOfAnAddress()
    {
        var run = new CommandVariables();
        run.SetResult("a b", "q");
        CommandVariables.Use(run);
        try
        {
            Assert.Equal("{\"keep\":1} {other} a b", TextVariables.Expand("{\"keep\":1} {other} {Q}"));
            Assert.Equal("https://x.com/?s=a%20b", TextVariables.ExpandUrl("https://x.com/?s={resultado}"));
            run.SetResult("https://x.com/a b");
            Assert.Equal("https://x.com/a b", TextVariables.ExpandUrl(" {resultado} "));
        }
        finally { CommandVariables.Use(null); }
    }

    [Theory]
    [InlineData("ciudad", "ciudad")]
    [InlineData(" {año} ", "año")]
    [InlineData("mi_var2", "mi_var2")]
    [InlineData("2x", null)]
    [InlineData("a b", null)]
    [InlineData("", null)]
    public void VariableNames(string given, string? expected) => Assert.Equal(expected, CommandVariables.NameOf(given));

    [Fact]
    public void OnlyTheActionsThatGiveSomethingSaveIt()
    {
        Assert.True(new ScreenTextAction().GivesResult);
        Assert.True(new HttpRequestAction().GivesResult);
        Assert.False(new ClipboardAction { Mode = ClipboardMode.Clear }.GivesResult);
        Assert.False(new RunScriptAction { WaitForExit = false }.GivesResult);
        Assert.True(new RunScriptAction { WaitForExit = true, WindowMode = WindowMode.Hidden }.GivesResult);
        Assert.False(new RunScriptAction { WaitForExit = true, WindowMode = WindowMode.Hidden, Shell = ScriptShell.VBScript }.GivesResult);
        Assert.False(new VolumeAction().GivesResult);
        Assert.True(TextVariables.UsesSelection(Say("traduce: {selección}")));
        Assert.True(TextVariables.UsesSelection(Say("{SELECTION}")));
        Assert.False(TextVariables.UsesSelection(Say("traduce: {resultado}")));
    }
}
