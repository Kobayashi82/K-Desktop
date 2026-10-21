using System.Collections.Concurrent;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>Stage 6.3, K-Desktop's side: a run with choreography over the pipe, as an assistant's process sees it.</summary>
public class AssistantChoreographyTests
{
    private sealed class Setup
    {
        public Setup()
        {
            Command.Actions.Add(new AssistantSayAction { Text = "1" });
            Command.Actions.Add(new CommandControlAction { CommandId = Inner.Id });
            Command.Actions.Add(new AssistantSayAction { Text = "3" });
            Inner.Actions.Add(new AssistantSayAction { Text = "2" });
            Store.Config.Commands.Add(Command);
            Store.Config.Commands.Add(Inner);
            Store.Config.Settings.AssistantTrustedCommands = [Command.Id];
            Engine = new CommandEngine(Store, Ui);
        }

        public ConfigStore Store { get; } = new();
        public FakeUi Ui { get; } = new();
        public Command Command { get; } = new() { Name = "a" };
        public Command Inner { get; } = new() { Name = "b" };
        public CommandEngine Engine { get; }
        public ConcurrentQueue<PipeMessage> Sent { get; } = new();

        /// <summary>Runs the command as the assistant asked for it, with that choreography.</summary>
        public Task<EndMessage> RunAsync(AssistantChoreography choreography) =>
            AssistantCommands.RunAsync(Engine, new RunMessage("dot", choreography.Run, Command.Id, true), "Dot", (_, _) => Task.FromResult("yes"), choreography);

        /// <summary>A choreography that answers each step with <paramref name="answer"/> (null: no answer).</summary>
        public AssistantChoreography Make(Func<StepMessage, PipeMessage?> answer, TimeSpan? timeout = null, string? speed = null, int animateMax = 0)
        {
            AssistantChoreography? made = null;
            made = new AssistantChoreography("r-1", m =>
            {
                Sent.Enqueue(m);
                if (m is StepMessage step && answer(step) is { } reply) Task.Run(() => made!.OnAnswer(reply));
            }, timeout ?? TimeSpan.FromSeconds(10)) { Speed = speed, AnimateMax = animateMax };
            return made;
        }
    }

    [Fact]
    public async Task EveryStepIsSentAnsweredAndReported()
    {
        var s = new Setup();
        using var choreography = s.Make(step => new GoMessage(step.Run, step.Index));
        var end = await s.RunAsync(choreography);
        Assert.Equal("ok", end.Status);
        Assert.Equal(["1", "2", "3"], s.Ui.Said.Select(x => x.Text));
        // Calling the other command is not a step of its own; its action is, one level down, and the numbering goes on.
        var steps = s.Sent.OfType<StepMessage>().ToList();
        Assert.Equal([(1, 3, 0), (2, 1, 1), (3, 3, 0)], steps.Select(x => (x.Index, x.Count, x.Depth)));
        Assert.All(steps, x => Assert.Equal(("AssistantSay", "r-1", 10000), (x.Kind, x.Run, x.TimeoutMs)));
        Assert.Equal([1, 2, 3], s.Sent.OfType<StepResultMessage>().Where(r => r.Status == "ok").Select(r => r.Index));
    }

    [Fact]
    public async Task EachStepCarriesTheSpeedOfTheManoeuvres()
    {
        var s = new Setup();
        using var choreography = s.Make(step => new GoMessage(step.Run, step.Index), speed: "fast");
        await s.RunAsync(choreography);
        Assert.All(s.Sent.OfType<StepMessage>(), x => Assert.Equal("fast", x.Speed));
    }

    [Fact]
    public async Task ALongCommandShowsOnlyItsWindowActions()
    {
        // «a» has 3 actions: over the limit of 2 nothing of it is a step (none is on a window); «b», with 1, still is.
        var s = new Setup();
        using var choreography = s.Make(step => new GoMessage(step.Run, step.Index), animateMax: 2);
        var end = await s.RunAsync(choreography);
        Assert.Equal("ok", end.Status);
        Assert.Equal(["1", "2", "3"], s.Ui.Said.Select(x => x.Text));
        Assert.Equal([1], s.Sent.OfType<StepMessage>().Select(x => x.Count));
    }

    [Fact]
    public void WindowActionsAreTheOnesOnWindows()
    {
        Assert.True(ActionGestures.IsWindowAction(new WindowAction()));
        Assert.True(ActionGestures.IsWindowAction(new AppControlAction { Mode = AppControlMode.Close }));
        Assert.False(ActionGestures.IsWindowAction(new AppControlAction { Mode = AppControlMode.WaitFor }));
        Assert.False(ActionGestures.IsWindowAction(new VolumeAction()));
    }

    [Fact]
    public async Task DoneRunsItAnywayAndSkipLeavesItOut()
    {
        var s = new Setup();
        using var choreography = s.Make(step => step.Index == 1 ? new DoneMessage(step.Run, 1) : step.Index == 2 ? new SkipMessage(step.Run, 2) : new GoMessage(step.Run, step.Index));
        var end = await s.RunAsync(choreography);
        Assert.Equal("ok", end.Status);
        Assert.Equal(["1", "3"], s.Ui.Said.Select(x => x.Text));
        Assert.Contains(s.Sent.OfType<StepResultMessage>(), r => r is { Index: 2, Status: "skipped" });
    }

    [Fact]
    public async Task SkippingAllEndsItCancelled()
    {
        var s = new Setup();
        using var choreography = s.Make(step => step.Index == 2 ? new SkipMessage(step.Run, 0, All: true) : new GoMessage(step.Run, step.Index));
        var end = await s.RunAsync(choreography);
        Assert.Equal("cancelled", end.Status);
        Assert.Equal(["1"], s.Ui.Said.Select(x => x.Text));
    }

    [Fact]
    public async Task AnAssistantThatLeftDoesNotHoldItBack()
    {
        var s = new Setup();
        using var choreography = s.Make(_ => null, TimeSpan.FromSeconds(30));
        var run = s.RunAsync(choreography);
        await Task.Delay(200);
        choreography.Abandon();
        var end = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("ok", end.Status);
        Assert.Equal(["1", "2", "3"], s.Ui.Said.Select(x => x.Text));
        Assert.Single(s.Sent.OfType<StepMessage>());
    }

    [Fact]
    public void AnswersOfOtherRunsAreNotTaken()
    {
        using var choreography = new AssistantChoreography("r-1", _ => { });
        Assert.False(choreography.OnAnswer(new GoMessage("r-2", 1)));
        Assert.True(choreography.OnAnswer(new GoMessage("r-1", 7)));
    }
}
