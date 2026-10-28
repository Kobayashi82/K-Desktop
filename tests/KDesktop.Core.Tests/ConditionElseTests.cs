using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>5.0.339: «Si…» that runs another command instead, and the condition «Un texto o variable».</summary>
public class ConditionElseTests
{
    private static ClipboardAction Copy(string text) => new() { Mode = ClipboardMode.SetText, Text = text, Paste = false };

    private static AssistantSayAction Say(string text) => new() { Assistant = "kdesktop", Text = text };

    private static ConditionAction IfResult(VariableTest test, string wanted, ConditionElse otherwise = ConditionElse.Stop, string elseId = "") =>
        new() { Condition = ConditionKind.Variable, Value = "{resultado}", Test = test, Text = wanted, Else = otherwise, ElseCommandId = elseId };

    private static Command Add(ConfigStore store, string name, params CommandAction[] actions)
    {
        var command = new Command { Name = name };
        foreach (var a in actions) command.Actions.Add(a);
        store.Config.Commands.Add(command);
        return command;
    }

    [Fact]
    public async Task NotMetRunsTheOtherCommandWithTheSameVariablesAndStops()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var instead = Add(store, "plan b", Say("plan b con {resultado}"));
        var command = Add(store, "a", Copy("lluvia"), IfResult(VariableTest.Contains, "sol", ConditionElse.RunCommand, instead.Id), Say("nunca"));

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal("plan b con lluvia", Assert.Single(ui.Said).Text);
        var entry = engine.History.Entries.First(e => e.CommandId == command.Id);
        Assert.Equal(RunOutcome.Stopped, entry.Outcome);
    }

    [Fact]
    public async Task MetGoesOnWithoutTheOtherCommand()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var instead = Add(store, "plan b", Say("plan b"));
        var command = Add(store, "a", Copy("hace sol"), IfResult(VariableTest.Contains, "SOL", ConditionElse.RunCommand, instead.Id), Say("sigue"));

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal("sigue", Assert.Single(ui.Said).Text);
    }

    [Fact]
    public async Task AnOtherCommandThatIsGoneOnlyStops()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var command = Add(store, "a", Copy("x"), IfResult(VariableTest.Equals, "y", ConditionElse.RunCommand, "borrado"), Say("nunca"));

        await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Empty(ui.Said);
    }

    [Theory]
    [InlineData("", VariableTest.NotEmpty, "", false)]
    [InlineData("algo", VariableTest.NotEmpty, "", true)]
    [InlineData("Hola Mundo", VariableTest.Contains, "mundo", true)]
    [InlineData("Hola", VariableTest.Equals, "HOLA", true)]
    [InlineData("7", VariableTest.Less, "10", true)]
    [InlineData("2,5", VariableTest.Equals, "2.5", true)]
    [InlineData("85 %", VariableTest.Greater, "90", false)]
    [InlineData("85%", VariableTest.Greater, "80", true)]
    [InlineData("b", VariableTest.Greater, "a", true)]
    public void Comparisons(string value, VariableTest test, string wanted, bool expected) =>
        Assert.Equal(expected, Conditions.Compare(value, test, wanted));

    [Fact]
    public void TheRiskOfACommandIncludesTheOneItRunsInstead()
    {
        var store = new ConfigStore();
        var power = Add(store, "apagar", new PowerAction { Mode = PowerMode.Shutdown });
        var command = Add(store, "a", IfResult(VariableTest.NotEmpty, "", ConditionElse.RunCommand, power.Id), Say("hola"));

        Assert.Equal(ToolRisk.AskAlways, ActionRisk.OfCommand(command, store.Config.FindCommand));
        Assert.True(store.Config.IsReferenced(power.Id));
    }

    [Fact]
    public void ItSaysWhatItDoes()
    {
        var c = IfResult(VariableTest.Greater, "80", ConditionElse.RunCommand, "x");
        c.ElseCommandName = "Plan B";
        Assert.Contains("80", c.Describe(), StringComparison.Ordinal);
        Assert.Contains("Plan B", c.Summary, StringComparison.Ordinal);
        var json = Json.Serialize<CommandAction>(c);
        var back = Assert.IsType<ConditionAction>(Json.Deserialize<CommandAction>(json));
        Assert.Equal((ConditionKind.Variable, VariableTest.Greater, ConditionElse.RunCommand, "x"), (back.Condition, back.Test, back.Else, back.ElseCommandId));
        Assert.DoesNotContain("Plan B", json, StringComparison.Ordinal);
    }
}
