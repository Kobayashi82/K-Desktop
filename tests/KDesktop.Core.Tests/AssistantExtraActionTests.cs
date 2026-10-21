using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>Stage 2.6 of the assistant plan: «Asistente: acción», the extras of the assistants as K-Desktop actions.</summary>
public class AssistantExtraActionTests
{
    [Fact]
    public void SavesBothIdsAndNotItsName()
    {
        var command = new Command { Name = "Manual" };
        command.Actions.Add(new AssistantExtraAction { Assistant = "lander", Extra = "lander.manual", ExtraName = "Lander: Modo manual" });
        var json = Json.Serialize(command);
        Assert.Contains("\"assistantExtra\"", json);
        Assert.DoesNotContain("Modo manual", json);
        Assert.DoesNotContain("target", json, StringComparison.OrdinalIgnoreCase);

        var back = Assert.IsType<AssistantExtraAction>(Assert.Single(Json.Deserialize<Command>(json)!.Actions));
        Assert.Equal("lander", back.Assistant);
        Assert.Equal("lander.manual", back.Extra);
        Assert.Equal("lander/lander.manual", back.Target);
    }

    [Fact]
    public void TargetJoinsAndSplitsTheIds()
    {
        var action = new AssistantExtraAction { Target = "dot/dot.jump" };
        Assert.Equal(("dot", "dot.jump"), (action.Assistant, action.Extra));
        var changed = new List<string?>();
        action.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        action.Extra = "dot.sit";
        Assert.Contains(nameof(AssistantExtraAction.Target), changed);
        Assert.True(action.IsTransient(nameof(AssistantExtraAction.Target)));
        Assert.False(action.IsTransient(nameof(AssistantExtraAction.Extra)));

        action.Target = "";
        Assert.Equal(string.Empty, action.Target);
    }

    [Fact]
    public async Task TheEngineHandsItToTheUi()
    {
        var ui = new FakeUi();
        var store = new ConfigStore();
        var command = new Command { Name = "Manual" };
        command.Actions.Add(new AssistantExtraAction { Assistant = "lander", Extra = "lander.manual" });
        store.Config.Commands.Add(command);
        await new CommandEngine(store, ui).RunAsync(command, TriggerSource.Hotkey);
        Assert.Equal([("lander", "lander.manual")], ui.Extras);
    }

    [Fact]
    public async Task AnOrderForAnAssistantIsSavedAndHandedToTheUi()
    {
        var command = new Command { Name = "Notas" };
        command.Actions.Add(new AssistantAskAction { Assistant = "lander", Text = "¿qué tengo apuntado?" });
        command.Actions.Add(new AssistantAskAction());
        var json = Json.Serialize(command);
        Assert.Contains("\"assistantAsk\"", json);
        var back = Json.Deserialize<Command>(json)!;
        var first = Assert.IsType<AssistantAskAction>(back.Actions[0]);
        Assert.Equal(("lander", "¿qué tengo apuntado?"), (first.Assistant, first.Text));
        Assert.Equal("«¿qué tengo apuntado?»", first.Summary);
        Assert.NotEqual(string.Empty, Assert.IsType<AssistantAskAction>(back.Actions[1]).Summary);

        var ui = new FakeUi();
        var store = new ConfigStore();
        store.Config.Commands.Add(back);
        await new CommandEngine(store, ui).RunAsync(back, TriggerSource.Hotkey);
        Assert.Equal([("lander", "¿qué tengo apuntado?"), ("", "")], ui.Asked);
    }

    [Fact]
    public async Task TheBrainActionIsSavedAndHandedToTheUi()
    {
        var command = new Command { Name = "Antes de jugar" };
        command.Actions.Add(new AssistantBrainAction { Op = AssistantBrainOp.Unload });
        Assert.Contains("\"assistantBrain\"", Json.Serialize(command));
        var back = Json.Deserialize<Command>(Json.Serialize(command))!;
        Assert.Equal(AssistantBrainOp.Unload, Assert.IsType<AssistantBrainAction>(back.Actions[0]).Op);
        var ui = new FakeUi();
        var store = new ConfigStore();
        store.Config.Commands.Add(back);
        await new CommandEngine(store, ui).RunAsync(back, TriggerSource.Hotkey);
        Assert.Equal([AssistantBrainOp.Unload], ui.Brain);
    }

    [Fact]
    public async Task SayingSomethingIsSavedAndHandedToTheUi()
    {
        var command = new Command { Name = "Copia terminada" };
        command.Actions.Add(new AssistantSayAction { Assistant = "lander", Text = "La copia ha terminado." });
        var back = Json.Deserialize<Command>(Json.Serialize(command))!;
        Assert.Contains("\"assistantSay\"", Json.Serialize(command));
        var ui = new FakeUi();
        var store = new ConfigStore();
        store.Config.Commands.Add(back);
        await new CommandEngine(store, ui).RunAsync(back, TriggerSource.Event);
        Assert.Equal([("lander", "La copia ha terminado.")], ui.Said);
    }

    [Fact]
    public void HotkeyUsersAreTheCommandsForEveryProgramAndKDesktop()
    {
        var config = new AppConfig();
        var mine = new Command { Name = "Mío" };
        mine.Hotkeys.Add("CTRL + ALT + L");
        var other = new Command { Name = "Otro" };
        other.Hotkeys.Add("ALT + CTRL + L");
        var onlyNotepad = new Command { Name = "Bloc" };
        onlyNotepad.Hotkeys.Add("CTRL + ALT + L");
        onlyNotepad.Processes.Add("notepad");
        var off = new Command { Name = "Apagado", Enabled = false };
        off.Hotkeys.Add("CTRL + ALT + L");
        foreach (var c in new[] { mine, other, onlyNotepad, off }) config.Commands.Add(c);

        Assert.Equal(["Otro"], config.HotkeyUsers("ctrl + alt + l", except: mine));
        Assert.Equal(["Mío", "Otro"], config.HotkeyUsers("CTRL + ALT + L"));
        Assert.Empty(config.HotkeyUsers(""));

        config.Settings.LauncherHotkey = "CTRL + ALT + L";
        Assert.Equal(["Otro", "K-Desktop"], config.HotkeyUsers("CTRL + ALT + L", except: mine));
    }

    [Fact]
    public void TheAssistantActionsAreOutsideTheAchievements()
    {
        Assert.True(ActionCatalog.Get(ActionKind.AssistantExtra).NeedsAssistant);
        Assert.DoesNotContain(ActionCatalog.Everyday, a => a.Kind == ActionKind.AssistantExtra);
        Assert.Null(AchievementCatalog.Get($"kind-{ActionKind.AssistantExtra}"));
        var completist = AchievementCatalog.Get("completist")!;
        Assert.Equal(ActionCatalog.Everyday.Count, completist.Target);

        // Using it does not count towards «every kind».
        var stats = new StatsData();
        stats.ActionKinds.Add(nameof(ActionKind.AssistantExtra));
        stats.ActionKinds.Add(nameof(ActionKind.Wait));
        Assert.Equal(1, AchievementService.Progress(stats, completist));
    }
}
