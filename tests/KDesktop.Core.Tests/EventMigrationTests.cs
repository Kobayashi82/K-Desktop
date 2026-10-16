using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

public class EventMigrationTests
{
    private const string OldConfig = """
        {
          "schemaVersion": 2,
          "settings": { "language": "es" },
          "commands": [
            { "id": "a", "name": "Correo", "runOnStartup": true },
            { "id": "b", "name": "Nada" },
            { "id": "c", "name": "Música", "runOnStartup": true, "runOnExit": true, "enabled": false },
            { "id": "d", "name": "Altavoces", "runOnExit": true }
          ]
        }
        """;

    [Fact]
    public void StartupAndExitBecomeTwoEventsInListOrder()
    {
        var config = Json.Deserialize<AppConfig>(OldConfig)!;
        Assert.True(config.Upgrade());

        Assert.Equal(AppConfig.CurrentSchema, config.SchemaVersion);
        Assert.Equal(2, config.Events.Count);
        var start = config.Events[0];
        var exit = config.Events[1];
        Assert.IsType<AppStartTrigger>(start.Trigger);
        Assert.Equal(["a", "c"], start.Commands); // the disabled one too
        Assert.Equal("Al iniciar K-Desktop", start.Name);
        Assert.IsType<AppExitTrigger>(exit.Trigger);
        Assert.Equal(["c", "d"], exit.Commands);
        Assert.True(start.Enabled && exit.Enabled);

        var saved = Json.Serialize(config);
        Assert.DoesNotContain("runOnStartup", saved);
        Assert.DoesNotContain("runOnExit", saved);
        Assert.Contains("\"type\": \"appStart\"", saved);
        Assert.DoesNotContain("\"summary\"", saved);

        // Loading the migrated file again changes nothing
        var again = Json.Deserialize<AppConfig>(saved)!;
        Assert.False(again.Upgrade());
        Assert.Equal(2, again.Events.Count);
        Assert.Equal(["a", "c"], again.Events[0].Commands);
    }

    [Fact]
    public void NothingToMigrateCreatesNothing()
    {
        var config = Json.Deserialize<AppConfig>("""{ "schemaVersion": 2, "commands": [ { "id": "a", "name": "x" } ] }""")!;
        config.Upgrade();
        Assert.Empty(config.Events);
        Assert.Equal(AppConfig.CurrentSchema, config.SchemaVersion);
    }

    [Fact]
    public void LegacyImportCreatesTheEvents()
    {
        var config = new AppConfig();
        config.Commands.Add(new Command { Id = "1", Name = "A" });
        config.Commands.Add(new Command { Id = "2", Name = "B" });
        config.Commands.Add(new Command { Id = "3", Name = "C" });
        LegacyImporter.AddStartupEvents(config, new HashSet<string> { "3", "1" }, new HashSet<string>());
        var rule = Assert.Single(config.Events);
        Assert.IsType<AppStartTrigger>(rule.Trigger);
        Assert.Equal(["1", "3"], rule.Commands);
        Assert.False(config.Upgrade() && config.Events.Count != 1);
    }

    [Fact]
    public void EventsAreReferences()
    {
        var config = new AppConfig();
        var rule = new EventRule { Trigger = new IdleTrigger() };
        rule.EndCommands.Add("x");
        config.Events.Add(rule);
        Assert.True(config.IsReferenced("x"));
        Assert.Single(config.EventsUsing("x"));
        Assert.False(config.IsReferenced("y"));
    }

    [Fact]
    public void EveryTriggerSurvivesSaving()
    {
        var config = new AppConfig();
        var process = new ProcessStartTrigger { IncludeRunningAtStart = true };
        process.Processes.Add("steam");
        config.Events.Add(new EventRule { Name = "p", Trigger = process, CooldownSeconds = 3 });
        config.Events.Add(new EventRule { Name = "t", Trigger = new TimeTrigger { Time = "21:30", Days = WeekDays.Workdays, RunIfMissed = true } });
        config.Events.Add(new EventRule { Name = "s", Trigger = new SessionTrigger { Event = SessionEvent.Unlock } });
        config.Events[1].Conditions.TimeFrom = "22:00";

        var back = Json.Deserialize<AppConfig>(Json.Serialize(config))!;
        var p = Assert.IsType<ProcessStartTrigger>(back.Events[0].Trigger);
        Assert.Equal(["steam"], p.Processes);
        Assert.True(p.IncludeRunningAtStart);
        Assert.Equal(3, back.Events[0].CooldownSeconds);
        var t = Assert.IsType<TimeTrigger>(back.Events[1].Trigger);
        Assert.Equal(WeekDays.Workdays, t.Days);
        Assert.True(t.RunIfMissed);
        Assert.Equal("22:00", back.Events[1].Conditions.TimeFrom);
        Assert.Equal(SessionEvent.Unlock, Assert.IsType<SessionTrigger>(back.Events[2].Trigger).Event);
    }

    [Fact]
    public void EveryKindOfTheCatalogRoundTrips()
    {
        var config = new AppConfig();
        foreach (var info in EventCatalog.All) config.Events.Add(new EventRule { Name = info.Kind.ToString(), Trigger = info.Create() });
        var json = Json.Serialize(config);
        Assert.DoesNotContain("\"hasEnd\"", json);
        var back = Json.Deserialize<AppConfig>(json)!;
        Assert.Equal(EventCatalog.All.Select(k => k.Kind), back.Events.Select(e => e.Trigger.Kind));
        foreach (var rule in back.Events) Assert.False(string.IsNullOrWhiteSpace(rule.Summary));
    }
}
