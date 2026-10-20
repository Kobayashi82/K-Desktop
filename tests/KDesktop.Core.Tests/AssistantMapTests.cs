using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>Stage 0.4 of the assistant plan: every action has a gesture and a risk.</summary>
public class AssistantMapTests
{
    public static TheoryData<ActionKind> AllKinds => [.. Enum.GetValues<ActionKind>()];

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void EveryActionKindHasGestureAndRisk(ActionKind kind)
    {
        var gestures = ActionGestures.ForKind(kind);
        if (kind != ActionKind.Condition) Assert.NotEmpty(gestures);
        Assert.True(gestures.Count(g => g.Commits) <= 1, "Solo un gesto hace la acción");
        _ = ActionRisk.OfKind(kind);
    }

    [Fact]
    public void EveryActionClassHasAKind()
    {
        // The JSON type list is the real list of actions: each one must be reachable through ForKind / OfKind.
        var classes = typeof(CommandAction).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(CommandAction)) && !t.IsAbstract).ToList();
        var kinds = classes.Select(t => ((CommandAction)Activator.CreateInstance(t)!).Kind).ToHashSet();
        Assert.Equal(Enum.GetValues<ActionKind>().Length, kinds.Count);
    }

    [Theory]
    [InlineData(WindowOperation.Minimize, false, CaptionButton.Minimize)]
    [InlineData(WindowOperation.ToggleMaximize, true, CaptionButton.Restore)]
    [InlineData(WindowOperation.ToggleMaximize, false, CaptionButton.Maximize)]
    public void WindowButtonsArePressed(WindowOperation op, bool maximized, CaptionButton button)
    {
        var steps = ActionGestures.For(new WindowAction { Operation = op }, maximized);
        var step = Assert.Single(steps);
        Assert.Equal(GestureKind.PressButton, step.Kind);
        Assert.Equal(button, step.Button);
    }

    [Fact]
    public void SnappingAMaximizedWindowRestoresItFirst()
    {
        var steps = ActionGestures.For(new WindowAction { Operation = WindowOperation.Snap }, maximized: true);
        Assert.Equal([GestureKind.PressButton, GestureKind.MoveWindow], steps.Select(s => s.Kind));
        Assert.False(steps[0].Commits);
        Assert.True(steps[1].Commits);
    }

    [Fact]
    public void TrayActionsGoToTheTrayThenSpeak()
    {
        var steps = ActionGestures.For(new VolumeAction());
        Assert.Equal([GestureKind.GoTo, GestureKind.Communicate], steps.Select(s => s.Kind));
        Assert.Equal(GesturePlace.Tray, steps[0].Place);
    }

    [Theory]
    [InlineData(typeof(PowerAction))]
    [InlineData(typeof(RunScriptAction))]
    [InlineData(typeof(SendInputAction))]
    [InlineData(typeof(PlayMacroAction))]
    [InlineData(typeof(HttpRequestAction))]
    public void DangerousActionsAlwaysAsk(Type type) =>
        Assert.Equal(ToolRisk.AskAlways, ActionRisk.Of((CommandAction)Activator.CreateInstance(type)!));

    [Fact]
    public void ClosingAnAppAsksOnce()
    {
        Assert.Equal(ToolRisk.AskOnce, ActionRisk.Of(new AppControlAction { Mode = AppControlMode.Close }));
        Assert.Equal(ToolRisk.Safe, ActionRisk.Of(new AppControlAction { Mode = AppControlMode.Activate }));
    }

    [Fact]
    public void HarmlessCommandIsConfirmedTheFirstTime()
    {
        var command = new Command { Name = "Mensaje" };
        command.Actions.Add(new ShowMessageAction());
        Assert.Equal(ToolRisk.AskOnce, ActionRisk.OfCommand(command, _ => null));
        Assert.Equal(ToolRisk.Safe, ActionRisk.OfCommand(command, _ => null, trusted: true));
    }

    [Fact]
    public void CommandThatCallsAPowerCommandAlwaysAsks()
    {
        var power = new Command { Name = "Apagar" };
        power.Actions.Add(new PowerAction());
        var caller = new Command { Name = "Buenas noches" };
        caller.Actions.Add(new ShowMessageAction());
        caller.Actions.Add(new CommandControlAction { Mode = CommandControlMode.Run, CommandId = power.Id });

        Assert.Equal(ToolRisk.AskAlways, ActionRisk.OfCommand(caller, id => id == power.Id ? power : null));
    }

    [Fact]
    public void ConditionBranchesCountWhicheverWayTheyGo()
    {
        var command = new Command { Name = "Si…" };
        command.Actions.Add(new ConditionAction());
        command.Actions.Add(new RunScriptAction());
        Assert.Equal(ToolRisk.AskAlways, ActionRisk.OfCommand(command, _ => null));
    }

    [Fact]
    public void DisabledActionsDoNotCount()
    {
        var command = new Command { Name = "X" };
        command.Actions.Add(new PowerAction { Enabled = false });
        Assert.Equal(ToolRisk.AskOnce, ActionRisk.OfCommand(command, _ => null));
    }

    [Fact]
    public void CommandLoopsAreCut()
    {
        var a = new Command { Name = "A" };
        var b = new Command { Name = "B" };
        a.Actions.Add(new CommandControlAction { Mode = CommandControlMode.Run, CommandId = b.Id });
        b.Actions.Add(new CommandControlAction { Mode = CommandControlMode.Run, CommandId = a.Id });
        Assert.Equal(ToolRisk.AskOnce, ActionRisk.OfCommand(a, id => id == a.Id ? a : id == b.Id ? b : null));
    }
}
