using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>The orders the assistants program for later (5.0.288): events of K-Desktop with what was approved.</summary>
public class AssistantScheduleTests
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

    private static ScheduleMessage Add(string order, string time, string? date = null, string? repeat = null, string[]? tools = null, string[]? commands = null) =>
        new("s-1", "add", "lander", order, time, date, repeat, tools, commands);

    [Theory]
    [InlineData("today", "2026-10-27")]
    [InlineData("tomorrow", "2026-10-28")]
    [InlineData("friday", "2026-10-30")]
    [InlineData("next friday", "2026-10-30")]
    [InlineData("Tuesday", "2026-10-27")]
    [InlineData("2026-11-05", "2026-11-05")]
    public void DaysAsTheModelWritesThem(string said, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), AssistantSchedule.Day(said, DateOnly.FromDateTime(Now)));

    [Theory]
    [InlineData("")]
    [InlineData("someday")]
    [InlineData("13")]
    public void WhatIsNoDayIsNull(string said) => Assert.Null(AssistantSchedule.Day(said, DateOnly.FromDateTime(Now)));

    [Fact]
    public void AnOrderForLaterIsAOnceEventThatAsksTheAssistantWithWhatWasApproved()
    {
        var config = Config("Vaciar papelera", "Apagar equipo");
        var done = AssistantSchedule.Handle(config, Add("mira el tiempo y vacía la papelera", "19:00", "2026-10-30", tools: ["weather", "send_telegram"],
            commands: ["vaciar papelera", "Lavar el coche"]), Now);

        Assert.True(done.Ok);
        Assert.Equal(["Lavar el coche"], done.Unknown);
        var rule = Assert.Single(config.Events);
        Assert.True(rule.Once);
        Assert.Equal("lander", rule.CreatedBy);
        var trigger = Assert.IsType<TimeTrigger>(rule.Trigger);
        Assert.Equal("19:00", trigger.Time);
        Assert.Equal("2026-10-30", trigger.Date);
        Assert.NotNull(rule.Ask);
        Assert.Equal("mira el tiempo y vacía la papelera", rule.Ask.Text);
        Assert.Equal(["weather", "send_telegram"], rule.Ask.Tools);
        Assert.Equal([config.Commands[0].Id], rule.Ask.Commands);
        Assert.True(rule.Ask.IsScheduled);
        Assert.Equal("2026-10-30 19:00", Assert.Single(done.Items!).When);
    }

    [Fact]
    public void WithoutADayItIsTheNextTimeItIsThatHour()
    {
        var config = Config();
        AssistantSchedule.Handle(config, Add("apaga el ordenador", "07:00"), Now);
        AssistantSchedule.Handle(config, Add("baja el volumen", "23:00"), Now);
        Assert.Equal("2026-10-28", ((TimeTrigger)config.Events[0].Trigger).Date);
        Assert.Equal("2026-10-27", ((TimeTrigger)config.Events[1].Trigger).Date);
    }

    [Fact]
    public void ATimeThatPassedIsRefused()
    {
        var config = Config();
        var done = AssistantSchedule.Handle(config, Add("apaga el ordenador", "09:00", "2026-10-27"), Now);
        Assert.False(done.Ok);
        Assert.Equal("past", done.Reason);
        Assert.Empty(config.Events);
    }

    [Fact]
    public void ARepeatedOrderIsNotOnce()
    {
        var config = Config();
        AssistantSchedule.Handle(config, Add("dime las noticias", "08:00", repeat: "workdays"), Now);
        AssistantSchedule.Handle(config, Add("riega las plantas", "10:00", "2026-10-30", repeat: "weekly"), Now);
        var workdays = config.Events[0];
        Assert.False(workdays.Once);
        Assert.Equal(WeekDays.Workdays, ((TimeTrigger)workdays.Trigger).Days);
        Assert.False(((TimeTrigger)workdays.Trigger).HasDate);
        Assert.Equal(WeekDays.Friday, ((TimeTrigger)config.Events[1].Trigger).Days);
        var list = AssistantSchedule.Handle(config, new ScheduleMessage("s-2", "list"), Now);
        Assert.Equal(["workdays", "weekly"], list.Items!.Select(i => i.Repeat));
    }

    [Fact]
    public void CancelFindsItByItsWordsOrTakesTheNextOne()
    {
        var config = Config();
        AssistantSchedule.Handle(config, Add("apaga el ordenador", "23:00"), Now);
        AssistantSchedule.Handle(config, Add("mira el tiempo", "18:00"), Now);
        AssistantSchedule.Handle(config, Add("vacía la papelera", "20:00"), Now);
        // A user's own event is never theirs to remove.
        config.Events.Add(new EventRule { Name = "Mío", Trigger = new TimeTrigger { Time = "17:00" } });

        var byWords = AssistantSchedule.Handle(config, new ScheduleMessage("s-3", "cancel", Event: "ordenador"), Now);
        Assert.Equal("apaga el ordenador", Assert.Single(byWords.Items!).Order);
        var next = AssistantSchedule.Handle(config, new ScheduleMessage("s-4", "cancel"), Now);
        Assert.Equal("mira el tiempo", Assert.Single(next.Items!).Order);
        Assert.False(AssistantSchedule.Handle(config, new ScheduleMessage("s-5", "cancel", Event: "lavadora"), Now).Ok);
        Assert.Equal(["vacía la papelera", "Mío"], config.Events.Select(e => e.Name));
    }

    [Fact]
    public void APermitAllowsOnlyWhatWasApproved()
    {
        var config = Config("Vaciar papelera", "Apagar equipo");
        var ask = new EventAsk { Assistant = "lander", Text = "x", Tools = ["control_app"], Commands = [config.Commands[0].Id] };
        var permit = ScheduledPermit.Of(ask, DateTime.UtcNow);
        Assert.True(permit.Allows(config.Commands[0]));
        Assert.False(permit.Allows(config.Commands[1]));
        Assert.True(permit.AllowsActs);
        Assert.True(permit.IsValid(DateTime.UtcNow));
        Assert.False(permit.IsValid(DateTime.UtcNow + ScheduledPermit.Lasts + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TheOrderGoesWithTheFlowAndLeavesWithIt()
    {
        Assert.Null(ScheduledOrder.Current);
        using (ScheduledOrder.Enter("k", ["weather"]))
        {
            Assert.Equal("k", ScheduledOrder.Current?.Key);
            Assert.Contains("weather", ScheduledOrder.Current!.Allowed);
        }
        Assert.Null(ScheduledOrder.Current);
    }
}
