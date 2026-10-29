using KDesktop.Core.Engine;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>D2 (5.0.371): «Crear menú de IA» makes the menu, its commands and the one that opens it.</summary>
public class AiMenuTests
{
    [Fact]
    public void TheMenuHasItsFiveCommandsWithTheSelection()
    {
        var config = new AppConfig();
        var (menu, opener) = AiMenu.AddTo(config, "kdesktop");

        Assert.Same(menu, Assert.Single(config.QuickMenus));
        Assert.Equal(5, menu.Items.Count);
        var commands = menu.Items.Select(i => config.Commands.Single(c => c.Id == i.CommandId)).ToList();
        var asks = commands.Select(c => Assert.IsType<AssistantAskAction>(Assert.Single(c.Actions))).ToList();
        Assert.All(asks, a => Assert.True(TextVariables.UsesSelection(a.Text), a.Text));
        Assert.All(asks, a => Assert.Equal("kdesktop", a.Assistant));
        Assert.Equal([AssistantAnswer.Paste, AssistantAnswer.Paste, AssistantAnswer.Sheet, AssistantAnswer.Sheet, AssistantAnswer.Sheet], asks.Select(a => a.Answer));
        Assert.Equal(menu.Id, Assert.IsType<OpenQuickMenuAction>(Assert.Single(opener.Actions)).MenuId);
        Assert.Equal(6, config.Commands.Count);
    }

    [Fact]
    public void MadeAgainItTakesFreeNames()
    {
        var config = new AppConfig();
        var (first, _) = AiMenu.AddTo(config, "kdesktop");
        var (second, opener) = AiMenu.AddTo(config, "kdesktop");

        Assert.NotEqual(first.Name, second.Name);
        Assert.Equal(12, config.Commands.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(second.Id, Assert.IsType<OpenQuickMenuAction>(Assert.Single(opener.Actions)).MenuId);
    }
}
