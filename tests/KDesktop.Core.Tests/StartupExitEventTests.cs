using System.Diagnostics;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

public class StartupExitEventTests
{
    private static Command Waiting(string name, double seconds)
    {
        var c = new Command { Name = name };
        c.Actions.Add(new WaitAction { Seconds = seconds });
        return c;
    }

    private static (CommandEngine Engine, AppConfig Config) Create(params (EventTrigger Trigger, Command[] Commands)[] events)
    {
        var store = new ConfigStore();
        foreach (var (trigger, commands) in events)
        {
            var rule = new EventRule { Name = trigger.Kind.ToString(), Trigger = trigger };
            foreach (var c in commands)
            {
                if (!store.Config.Commands.Contains(c)) store.Config.Commands.Add(c);
                rule.Commands.Add(c.Id);
            }
            store.Config.Events.Add(rule);
        }
        return (new CommandEngine(store, new FakeUi()), store.Config);
    }

    [Fact]
    public async Task StartupRunsOneAfterAnotherInListOrder()
    {
        var a = Waiting("a", 0.3);
        var b = Waiting("b", 0);
        var c = Waiting("c", 0);
        var (engine, _) = Create((new AppStartTrigger(), [a, b]), (new AppExitTrigger(), [Waiting("x", 0)]), (new AppStartTrigger(), [c]));
        var order = new List<(string, long)>();
        var clock = Stopwatch.StartNew();
        engine.ActionStarting += (_, ctx) => { lock (order) order.Add((ctx.Command.Name, clock.ElapsedMilliseconds)); };

        await engine.RunStartupCommandsAsync();

        Assert.Equal(["a", "b", "c"], order.Select(o => o.Item1));
        Assert.True(order[1].Item2 - order[0].Item2 >= 280, "b waited for a");
    }

    [Fact]
    public async Task PauseBlocksStartupAndExit()
    {
        var (engine, config) = Create((new AppStartTrigger(), [Waiting("a", 0)]), (new AppExitTrigger(), [Waiting("b", 0)]));
        config.Settings.Paused = true;
        var ran = 0;
        engine.Executed += (_, _) => Interlocked.Increment(ref ran);
        await engine.RunStartupCommandsAsync();
        await engine.RunExitCommandsAsync();
        Assert.Equal(0, ran);
    }

    [Fact]
    public async Task ConditionsAndDisabledEventsAreRespected()
    {
        var (engine, config) = Create((new AppStartTrigger(), [Waiting("a", 0)]), (new AppStartTrigger(), [Waiting("b", 0)]), (new AppStartTrigger(), [Waiting("c", 0)]));
        config.Events[0].Conditions.Items.Add(new ConditionAction { Condition = ConditionKind.ProcessRunning, Text = "a-program-that-is-not-running-xyz" });
        config.Events[1].Enabled = false;
        var ran = new List<string>();
        engine.Executed += (c, _) => { lock (ran) ran.Add(c.Name); };
        await engine.RunStartupCommandsAsync();
        Assert.Equal(["c"], ran);
    }

    [Fact]
    public async Task DisabledCommandStaysQuiet()
    {
        var off = Waiting("off", 0);
        off.Enabled = false;
        var (engine, _) = Create((new AppStartTrigger(), [off, Waiting("on", 0)]));
        var ran = new List<string>();
        engine.Executed += (c, _) => { lock (ran) ran.Add(c.Name); };
        await engine.RunStartupCommandsAsync();
        Assert.Equal(["on"], ran);
    }

    [Fact]
    public async Task ExitRunsTogetherAndNeverHangs()
    {
        var (engine, _) = Create(
            (new AppExitTrigger(), [Waiting("one", 1), Waiting("two", 1)]),
            (new AppExitTrigger(), [Waiting("hangs", 60)]));
        var started = new List<string>();
        engine.ActionStarting += (_, ctx) => { lock (started) started.Add(ctx.Command.Name); };
        var clock = Stopwatch.StartNew();

        await engine.RunExitCommandsAsync();

        Assert.Equal(3, started.Count); // all of them started at once
        Assert.InRange(clock.ElapsedMilliseconds, 4500, 6500);
        engine.CancelAll();
    }

    [Fact]
    public async Task ExitWithQuickCommandsDoesNotWait()
    {
        var (engine, _) = Create((new AppExitTrigger(), [Waiting("one", 0.5), Waiting("two", 0.5)]));
        var clock = Stopwatch.StartNew();
        await engine.RunExitCommandsAsync();
        Assert.InRange(clock.ElapsedMilliseconds, 400, 1500); // together, not one after another
    }

    [Fact]
    public async Task EventRunSkipsDeletedCommands()
    {
        var keep = Waiting("keep", 0);
        var (engine, config) = Create((new IdleTrigger(), [keep]));
        config.Events[0].Commands.Insert(0, "gone");
        var ran = new List<string>();
        engine.Executed += (c, s) => { lock (ran) ran.Add($"{c.Name}:{s}"); };
        await engine.RunEventAsync(config.Events[0], false);
        Assert.Equal(["keep:Event"], ran);
    }

    [Fact]
    public async Task CommandsActionRunsAndTogglesAnEvent()
    {
        var target = Waiting("inside", 0);
        var (engine, config) = Create((new IdleTrigger(), [target]));
        var rule = config.Events[0];
        var runner = new Command { Name = "runner" };
        runner.Actions.Add(new CommandControlAction { Target = CommandControlTarget.Event, EventId = rule.Id, Mode = CommandControlMode.Run });
        runner.Actions.Add(new CommandControlAction { Target = CommandControlTarget.Event, EventId = rule.Id, Mode = CommandControlMode.Toggle });
        config.Commands.Add(runner);
        var ran = new List<string>();
        engine.Executed += (c, s) => { lock (ran) ran.Add($"{c.Name}:{s}"); };

        await engine.RunAsync(runner, TriggerSource.Hotkey);

        Assert.Contains("inside:Command", ran);
        Assert.False(rule.Enabled);
    }
}
