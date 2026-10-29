using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>B8 (5.0.351): events of any kind made and saved at once by an assistant (<see cref="AssistantEvents"/>).</summary>
public class AssistantEventsTests
{
    /// <summary>Tuesday 27 October 2026, 15:00.</summary>
    private static readonly DateTime Now = new(2026, 10, 27, 15, 0, 0);

    private static AppConfig Config(params string[] commands)
    {
        var config = new AppConfig();
        foreach (var name in commands)
        {
            var command = new Command { Name = name };
            command.Actions.Add(new ShowMessageAction { Text = name });
            config.Commands.Add(command);
        }
        return config;
    }

    /// <summary>«Steam» is the process «steam», «Fall Guys» «fallguys_client»: like K-Desktop finds them, without the PC.</summary>
    private static string Process(string said) => said.ToLowerInvariant() switch
    {
        "fall guys" => "fallguys_client",
        var other => other.Replace(" ", string.Empty, StringComparison.Ordinal),
    };

    private static MadeEventMessage Make(AppConfig config, EventSpec spec) =>
        AssistantEvents.Handle(config, new MakeEventMessage("e-1", "add", "kobii", spec), Now, Process);

    [Fact]
    public void WhenAProgramOpensItRunsCommandsAndAnOrder()
    {
        var config = Config("Modo juego", "Modo normal");
        var made = Make(config, new EventSpec("program_opens", Programs: ["Fall Guys"], Commands: ["modo juego", "Teletransporte"], EndCommands: ["Modo normal"],
            Order: "dime la batería del mando", Tools: ["pc_status"], OnlyFrom: "18:00", OnlyTo: "23:30"));
        Assert.True(made.Ok);
        var rule = Assert.Single(config.Events);
        Assert.Equal(made.EventId, rule.Id);
        Assert.Equal("kobii", rule.CreatedBy);
        Assert.True(rule.Enabled);
        Assert.False(rule.Once);
        Assert.Equal(["fallguys_client"], Assert.IsType<ProcessStartTrigger>(rule.Trigger).Processes);
        Assert.Equal([config.Commands[0].Id], rule.Commands);
        Assert.Equal([config.Commands[1].Id], rule.EndCommands);
        Assert.Equal("dime la batería del mando", rule.Ask!.Text);
        Assert.Equal(["pc_status"], rule.Ask.Tools);
        Assert.True(rule.Ask.IsScheduled);
        var only = Assert.Single(rule.Conditions.Items);
        Assert.Equal((ConditionKind.Schedule, "18:00", "23:30"), (only.Condition, only.TimeFrom, only.TimeTo));
        // What it said that is no command is told, and left out.
        Assert.Equal(["Teletransporte"], made.Unknown);
        Assert.Equal(["fallguys_client"], made.Programs);
        Assert.Equal(["Modo juego", "«dime la batería del mando»"], made.Runs);
        Assert.Equal(rule.Summary, made.Summary);
        Assert.StartsWith(rule.Summary, rule.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyKinds()
    {
        var config = Config("Bloquear", "Brillo bajo");
        Assert.Equal(10, Assert.IsType<IdleTrigger>(Rule(new EventSpec("idle", Minutes: 10, Commands: ["Bloquear"]))).Minutes);
        Assert.Equal(SessionEvent.Resume, Assert.IsType<SessionTrigger>(Rule(new EventSpec("wake", Commands: ["Bloquear"]))).Event);
        var sun = Assert.IsType<SunTrigger>(Rule(new EventSpec("sunset", Offset: -30, Commands: ["Brillo bajo"])));
        Assert.Equal((SunEvent.Sunset, -30), (sun.Event, sun.OffsetMinutes));
        var battery = Assert.IsType<BatteryTrigger>(Rule(new EventSpec("battery", Percent: 20, Order: "avísame")));
        Assert.Equal((LoadComparison.Below, 20), (battery.Comparison, battery.Percent));
        var load = Assert.IsType<LoadTrigger>(Rule(new EventSpec("load", What: "gpu", Percent: 90, Above: true, Minutes: 2, Order: "avísame")));
        Assert.Equal((LoadResource.Gpu, 90, 120), (load.Resource, load.Percent, load.Seconds));
        var audio = Assert.IsType<AudioDeviceTrigger>(Rule(new EventSpec("audio_device", Match: "cascos", Order: "dime su batería")));
        Assert.Equal(("cascos", AudioDeviceEvent.Connected), (audio.Name, audio.Event));
        Assert.Equal(NetworkState.Offline, Assert.IsType<NetworkTrigger>(Rule(new EventSpec("offline", Order: "avísame"))).State);
        var timed = Assert.IsType<TimeTrigger>(Rule(new EventSpec("time", Time: "9:00", Days: "workdays", Commands: ["Bloquear"])));
        Assert.Equal(("09:00", WeekDays.Workdays, false), (timed.Time, timed.Days, timed.HasDate));

        EventTrigger Rule(EventSpec spec)
        {
            var made = Make(config, spec);
            Assert.True(made.Ok, made.Reason);
            return config.Events.Single(e => e.Id == made.EventId).Trigger;
        }
    }

    [Fact]
    public void AFileInAFolder()
    {
        var folder = Directory.CreateTempSubdirectory("kd-event-").FullName;
        try
        {
            var config = Config();
            var made = Make(config, new EventSpec("file", Folder: folder, Filter: "*.pdf", Order: "avísame"));
            var file = Assert.IsType<FileTrigger>(config.Events.Single(e => e.Id == made.EventId).Trigger);
            Assert.Equal((folder, "*.pdf", FileChange.Created), (file.Folder, file.Filter, file.Change));
            Assert.Equal("folder", Make(config, new EventSpec("file", Folder: Path.Combine(folder, "no-existe"), Order: "x")).Reason);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void WhatCannotBeMadeIsNotSaved()
    {
        var config = Config("Bloquear");
        Assert.Equal("when", Make(config, new EventSpec("tomorrow", Commands: ["Bloquear"])).Reason);
        Assert.Equal("time", Make(config, new EventSpec("time", Time: "pronto", Commands: ["Bloquear"])).Reason);
        Assert.Equal("past", Make(config, new EventSpec("time", Time: "10:00", Date: "2026-10-27", Commands: ["Bloquear"])).Reason);
        Assert.Equal("programs", Make(config, new EventSpec("program_opens", Commands: ["Bloquear"])).Reason);
        var nothing = Make(config, new EventSpec("lock", Commands: ["Teletransporte"]));
        Assert.Equal("nothing", nothing.Reason);
        Assert.Equal(["Teletransporte"], nothing.Unknown);
        Assert.Empty(config.Events);
    }

    [Fact]
    public void OnADateItIsOnlyOnce()
    {
        var config = Config("Bloquear");
        Make(config, new EventSpec("time", Time: "20:00", Date: "2026-10-30", Commands: ["Bloquear"]));
        var rule = Assert.Single(config.Events);
        Assert.True(rule.Once);
        Assert.Equal("2026-10-30", ((TimeTrigger)rule.Trigger).Date);
    }

    [Fact]
    public void OnlyWhatAnAssistantMadeIsRemoved()
    {
        var config = Config("Bloquear");
        var mine = new EventRule { Name = "Mío", Trigger = new IdleTrigger() };
        config.Events.Add(mine);
        var made = Make(config, new EventSpec("lock", Commands: ["Bloquear"]));
        Assert.False(AssistantEvents.Handle(config, new MakeEventMessage("e-2", "remove", Remove: mine.Id), Now).Ok);
        Assert.True(AssistantEvents.Handle(config, new MakeEventMessage("e-3", "remove", Remove: made.EventId), Now).Ok);
        Assert.Equal([mine], config.Events);
    }

    [Fact]
    public void NotEndlessly()
    {
        var config = Config("Bloquear");
        for (var i = 0; i < AssistantEvents.MaxMade; i++) config.Events.Add(new EventRule { CreatedBy = "lander" });
        Assert.Equal("tooMany", Make(config, new EventSpec("lock", Commands: ["Bloquear"])).Reason);
    }

    [Theory]
    [InlineData("", WeekDays.None)]
    [InlineData("daily", WeekDays.None)]
    [InlineData("workdays", WeekDays.Workdays)]
    [InlineData("weekend", WeekDays.Weekend)]
    [InlineData("monday, friday", WeekDays.Monday | WeekDays.Friday)]
    [InlineData("Saturdays", WeekDays.Saturday)]
    [InlineData("workdays, weekend", WeekDays.None)]
    public void Days(string said, WeekDays expected) => Assert.Equal(expected, AssistantEvents.Days(said));
}
