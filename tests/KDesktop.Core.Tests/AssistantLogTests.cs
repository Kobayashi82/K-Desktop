using KDesktop.Core.Achievements;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Engine.Events;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>B3 (5.0.345): the «Registro», the statistics and the checks of a command or an event for the assistants.</summary>
public sealed class AssistantLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-log-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private static readonly DateTime Day = new(2026, 10, 28);

    private static List<HistoryEntry> History() =>
    [
        new(Day.AddDays(-1).AddHours(8), "c1", "Copia de fotos", TriggerSource.Event, "Evento «Copias»", RunOutcome.Error, "No se encuentra D:\\Fotos", 120),
        new(Day.AddHours(9), "c2", "Modo juego", TriggerSource.Hotkey, null, RunOutcome.Ok, null, 300),
        new(Day.AddHours(10), "c2", "Modo juego", TriggerSource.Voice, "pon el modo juego", RunOutcome.Ok, null, 280),
        new(Day.AddHours(11), null, "abre la nevera", TriggerSource.Voice, null, RunOutcome.NotUnderstood, null, 0),
    ];

    [Fact]
    public void TheLogIsFilteredByDayNameOriginAndProblems()
    {
        var history = History();
        var skip = new EventSkip(Day.AddHours(7).ToUniversalTime(), "e1", "Copias", SkipReason.Condition, "Horario: 9:00–14:00");

        var today = AssistantLog.Query(history, [skip], new LogQueryMessage("1", Since: "2026-10-28", Until: "2026-10-28"));
        Assert.Equal(3, today.Total);
        Assert.Equal("abre la nevera", today.Runs[0].Name); // newest first
        Assert.Equal("condition", Assert.Single(today.Skips).Reason);

        var problems = AssistantLog.Query(history, [], new LogQueryMessage("2", Problems: true));
        Assert.Equal(["abre la nevera", "Copia de fotos"], problems.Runs.Select(r => r.Name));
        Assert.Equal("error", problems.Runs[1].Outcome);

        var voice = AssistantLog.Query(history, [skip], new LogQueryMessage("3", Source: "voice"));
        Assert.Equal(2, voice.Total);
        Assert.Empty(voice.Skips); // the skips are events'

        // By the event's name too (the run's detail).
        var copias = AssistantLog.Query(history, [skip], new LogQueryMessage("4", Name: "copias"));
        Assert.Equal("Copia de fotos", Assert.Single(copias.Runs).Name);
        Assert.Single(copias.Skips);
        Assert.Equal("2026-10-27 08:00:00", copias.Oldest);
    }

    [Fact]
    public void TheSameSkipAgainIsCountedNotRepeated()
    {
        using var state = new EventState(Path.Combine(_folder, "events-state.json"));
        for (var i = 0; i < 5; i++) state.Skipped(new EventSkip(Day.AddMinutes(i), "e1", "Cada minuto", SkipReason.Condition, "Batería por debajo de 20 %"));
        state.Skipped(new EventSkip(Day.AddMinutes(9), "e2", "Otro", SkipReason.Paused));
        Assert.Equal(2, state.Skips.Count);
        Assert.Equal(5, state.Skips[0].Times);
        Assert.Equal(Day.AddMinutes(4), state.Skips[0].Time);
        state.Save();

        using var again = new EventState(Path.Combine(_folder, "events-state.json"));
        Assert.Equal(5, again.Skips[0].Times);
    }

    [Fact]
    public void UsageTellsTheMostRunAndTheRecent()
    {
        var config = new AppConfig();
        config.Commands.Add(new Command { Id = "c2", Name = "Modo juego" });
        var data = new StatsData { FirstUse = new DateTime(2026, 10, 1) };
        data.Counters[Stat.CommandsRun] = 40;
        data.CommandRuns["c2"] = 30;
        data.CommandRuns["gone"] = 99; // deleted: not told
        data.RunsBySource["hotkey"] = 25;
        var usage = AssistantLog.Usage(new StatsSnapshot(data, 0, 0), History(), config, new UsageMessage("1", 7), Day.AddHours(12));
        Assert.Equal("2026-10-01", usage.Since);
        Assert.Equal("Modo juego", Assert.Single(usage.Top).Name);
        Assert.Equal(2, usage.Recent.Single(r => r.Name == "Modo juego").Count);
        Assert.Equal(1, usage.Recent.Single(r => r.Name == "Copia de fotos").Failed);
        Assert.DoesNotContain(usage.Recent, r => r.Name == "abre la nevera");
    }

    [Fact]
    public async Task ACommandWithAMissingProgramAndTurnedOff()
    {
        var config = new AppConfig();
        var command = new Command { Id = "c1", Name = "Copia de fotos", Enabled = false };
        command.Actions.Add(new RunAppAction { Path = @"C:\No\Existe\copiar.exe" });
        config.Commands.Add(command);
        var rule = new EventRule { Id = "e1", Name = "Copias", Trigger = new TimeTrigger { Time = "08:00" } };
        rule.Commands.Add("c1");
        config.Events.Add(rule);

        var found = await AssistantLog.DiagnoseAsync(config, History(), [], new DiagnoseMessage("1", "copia de fotos"), paused: false);
        Assert.True(found.Found);
        Assert.Equal("command", found.Kind);
        Assert.Contains(found.Problems, p => p.Contains("turned off", StringComparison.Ordinal));
        Assert.Contains(found.Problems, p => p.Contains(@"C:\No\Existe\copiar.exe", StringComparison.Ordinal));
        Assert.Contains(found.Facts, f => f.Contains("Copias", StringComparison.Ordinal)); // the event that runs it
        Assert.Equal("error", Assert.Single(found.Runs).Outcome);

        var none = await AssistantLog.DiagnoseAsync(config, [], [], new DiagnoseMessage("2", "zzzz"), paused: false);
        Assert.False(none.Found);
    }

    [Fact]
    public async Task AnEventWithItsConditionNotMetAndASkip()
    {
        var config = new AppConfig();
        config.Commands.Add(new Command { Id = "c1", Name = "Copia de fotos", Enabled = false });
        var rule = new EventRule { Id = "e1", Name = "Copias de la mañana", Trigger = new TimeTrigger { Time = "08:00" } };
        rule.Commands.Add("c1");
        rule.Commands.Add("borrado");
        rule.Conditions.Items.Add(new ConditionAction { Condition = ConditionKind.Network, Text = "Casa" });
        config.Events.Add(rule);
        var skips = new[] { new EventSkip(Day.AddHours(8).ToUniversalTime(), "e1", rule.Name, SkipReason.Missed, "2026-10-28 08:00") };

        var found = await AssistantLog.DiagnoseAsync(config, History(), skips, new DiagnoseMessage("1", "copias de la mañana", "event"), paused: true,
            _ => Task.FromResult((false, "Hay una red conectada: Casa")));
        Assert.Equal("event", found.Kind);
        Assert.Contains(found.Problems, p => p.Contains("paused", StringComparison.Ordinal));
        Assert.Contains(found.Problems, p => p.Contains("Hay una red conectada: Casa", StringComparison.Ordinal));
        Assert.Contains(found.Problems, p => p.Contains("«Copia de fotos» is turned off", StringComparison.Ordinal));
        Assert.Contains(found.Problems, p => p.Contains("no longer exists", StringComparison.Ordinal));
        Assert.Equal("missed", Assert.Single(found.Skips).Reason);
    }
}
