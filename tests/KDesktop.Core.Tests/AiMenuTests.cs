using KDesktop.Core.Engine;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>D2 (5.0.371): «Crear menú de IA» makes the menu, its commands and the one that opens it.</summary>
public class AiMenuTests
{
    [Fact]
    public void TheMenuHasItsSixCommandsWithTheSelection()
    {
        var config = new AppConfig();
        var (menu, opener) = AiMenu.AddTo(config, "kdesktop");

        Assert.Same(menu, Assert.Single(config.QuickMenus));
        Assert.Equal(6, menu.Items.Count);
        var commands = menu.Items.Select(i => config.Commands.Single(c => c.Id == i.CommandId)).ToList();
        var asks = commands.SkipLast(1).Select(c => Assert.IsType<AssistantAskAction>(Assert.Single(c.Actions))).ToList();
        // «Leer en voz alta» (D4): said, not asked.
        var read = Assert.IsType<AssistantSayAction>(Assert.Single(commands[^1].Actions));
        Assert.True(TextVariables.UsesSelection(read.Text), read.Text);
        Assert.Equal("kdesktop", read.Assistant);
        Assert.All(asks, a => Assert.True(TextVariables.UsesSelection(a.Text), a.Text));
        Assert.All(asks, a => Assert.Equal("kdesktop", a.Assistant));
        Assert.Equal([AssistantAnswer.Paste, AssistantAnswer.Paste, AssistantAnswer.Sheet, AssistantAnswer.Sheet, AssistantAnswer.Sheet], asks.Select(a => a.Answer));
        Assert.Equal(menu.Id, Assert.IsType<OpenQuickMenuAction>(Assert.Single(opener.Actions)).MenuId);
        Assert.Equal(7, config.Commands.Count);
    }

    [Fact]
    public void MadeAgainItTakesFreeNames()
    {
        var config = new AppConfig();
        var (first, _) = AiMenu.AddTo(config, "kdesktop");
        var (second, opener) = AiMenu.AddTo(config, "kdesktop");

        Assert.NotEqual(first.Name, second.Name);
        Assert.Equal(14, config.Commands.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(second.Id, Assert.IsType<OpenQuickMenuAction>(Assert.Single(opener.Actions)).MenuId);
    }
}
