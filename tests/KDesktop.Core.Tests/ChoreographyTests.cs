using System.Diagnostics;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>Stage 6.1: a run that asks the assistant before each action, which never holds a command back.</summary>
public class ChoreographyTests
{
    /// <summary>A choreography that answers each step with <see cref="Answer"/> and writes down what it was told.</summary>
    private sealed class Script(Func<ChoreographyStep, CancellationToken, Task<StepGo>> answer, TimeSpan timeout) : IChoreography
    {
        public TimeSpan StepTimeout => timeout;

        public List<(string Command, int Index, int Count, int Depth)> Steps { get; } = [];

        public List<(int Index, StepOutcome Outcome)> Outcomes { get; } = [];

        public Task<StepGo> BeforeAsync(ChoreographyStep step, CancellationToken cancellationToken)
        {
            lock (Steps) Steps.Add((step.Context.Command.Name, step.Index, step.Count, step.Context.Depth));
            return answer(step, cancellationToken);
        }

        public void After(ChoreographyStep step, StepOutcome outcome, string? message)
        {
            lock (Outcomes) Outcomes.Add((step.Index, outcome));
        }
    }

    private static (CommandEngine Engine, FakeUi Ui, Command Command) Setup(params CommandAction[] actions)
    {
        var store = new ConfigStore();
        var command = new Command { Name = "a" };
        foreach (var a in actions) command.Actions.Add(a);
        store.Config.Commands.Add(command);
        var ui = new FakeUi();
        return (new CommandEngine(store, ui), ui, command);
    }

    private static AssistantSayAction Say(string text) => new() { Text = text };

    [Fact]
    public async Task AnAssistantThatNeverAnswersOnlyDelaysTheCommand()
    {
        var (engine, ui, command) = Setup(Say("1"), Say("2"), Say("3"));
        var silent = new Script((_, token) => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => StepGo.Run, TaskScheduler.Default), TimeSpan.FromMilliseconds(80));
        var watch = Stopwatch.StartNew();
        await engine.RunChoreographedAsync(command, TriggerSource.Assistant, silent);
        Assert.Equal(["1", "2", "3"], ui.Said.Select(s => s.Text));
        Assert.Equal([(1, StepOutcome.Ok), (2, StepOutcome.Ok), (3, StepOutcome.Ok)], silent.Outcomes);
        Assert.InRange(watch.ElapsedMilliseconds, 200, 5000);
    }

    [Fact]
    public async Task GoRunsAtOnceAndSkipLeavesItOut()
    {
        var (engine, ui, command) = Setup(Say("1"), Say("2"), Say("3"));
        var script = new Script((step, _) => Task.FromResult(step.Index == 2 ? StepGo.Skip : StepGo.Run), TimeSpan.FromSeconds(30));
        var watch = Stopwatch.StartNew();
        await engine.RunChoreographedAsync(command, TriggerSource.Assistant, script);
        Assert.Equal(["1", "3"], ui.Said.Select(s => s.Text));
        Assert.Equal([(1, StepOutcome.Ok), (2, StepOutcome.Skipped), (3, StepOutcome.Ok)], script.Outcomes);
        Assert.True(watch.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public async Task ABrokenAssistantDoesNotStopIt()
    {
        var (engine, ui, command) = Setup(Say("1"), Say("2"));
        var broken = new Script((_, _) => throw new InvalidOperationException("roto"), TimeSpan.FromSeconds(30));
        await engine.RunChoreographedAsync(command, TriggerSource.Assistant, broken);
        Assert.Equal(["1", "2"], ui.Said.Select(s => s.Text));
    }

    [Fact]
    public async Task CancellingStopsTheRunWhileItWaits()
    {
        var (engine, ui, command) = Setup(Say("1"), Say("2"));
        using var cancel = new CancellationTokenSource();
        var waiting = new Script((_, token) => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => StepGo.Run, token), TimeSpan.FromSeconds(30));
        var run = engine.RunChoreographedAsync(command, TriggerSource.Assistant, waiting, cancel.Token);
        await Task.Delay(100);
        cancel.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(ui.Said);
        Assert.Equal(RunOutcome.Cancelled, engine.History.Entries[^1].Outcome);
    }

    [Fact]
    public async Task NestedCommandsBringTheirStepsWithTheirDepth()
    {
        var (engine, ui, outer) = Setup(Say("1"));
        var inner = new Command { Name = "b" };
        inner.Actions.Add(Say("2"));
        inner.Actions.Add(new WaitAction { Seconds = 0, Enabled = false });
        inner.Actions.Add(Say("3"));
        engine.Config.Commands.Add(inner);
        outer.Actions.Add(new CommandControlAction { CommandId = inner.Id });
        var script = new Script((_, _) => Task.FromResult(StepGo.Run), TimeSpan.FromSeconds(30));
        await engine.RunChoreographedAsync(outer, TriggerSource.Assistant, script);
        Assert.Equal(["1", "2", "3"], ui.Said.Select(s => s.Text));
        Assert.Equal([("a", 1, 2, 0), ("a", 2, 2, 0), ("b", 1, 2, 1), ("b", 2, 2, 1)], script.Steps);
    }

    [Fact]
    public async Task OtherRunsDoNotWaitForAnything()
    {
        var (engine, ui, command) = Setup(Say("1"));
        Engine.ExecutionContext? seen = null;
        engine.ActionStarting += (_, ctx) => seen = ctx;
        await engine.RunAsync(command, TriggerSource.Hotkey);
        Assert.Equal(["1"], ui.Said.Select(s => s.Text));
        Assert.Null(seen!.Choreography);
        Assert.Equal(CancellationToken.None, seen.ChoreographyToken);
    }
}
