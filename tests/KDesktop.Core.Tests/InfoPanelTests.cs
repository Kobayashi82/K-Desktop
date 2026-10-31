using System.Globalization;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>«Paneles» (5.0.452): the figures as a panel shows them, the config and moving a panel to another PC.</summary>
public class InfoPanelTests
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    [Theory]
    [InlineData(PanelItemKind.NetDown, 0, "0,0 kB/s")]
    [InlineData(PanelItemKind.NetDown, 2867, "2,8 kB/s")]
    [InlineData(PanelItemKind.NetUp, 19354, "18,9 kB/s")]
    [InlineData(PanelItemKind.NetUp, 512000, "500 kB/s")]
    [InlineData(PanelItemKind.NetDown, 1048576, "1,0 MB/s")]
    [InlineData(PanelItemKind.NetDown, 42.5 * 1024 * 1024, "42,5 MB/s")]
    [InlineData(PanelItemKind.NetDown, 3.0 * 1024 * 1024 * 1024, "3,0 GB/s")]
    [InlineData(PanelItemKind.Cpu, 7.4, "7%")]
    [InlineData(PanelItemKind.Vram, 81.2, "81%")]
    [InlineData(PanelItemKind.GpuTemp, 36, "36°")]
    public void FiguresAreShortAndInTheLanguageOfTheInterface(PanelItemKind kind, double value, string expected) =>
        Assert.Equal(expected, PanelSampler.Format(kind, value, Spanish));

    [Fact]
    public void AFigureNotReadYetIsADash() => Assert.Equal("—", PanelSampler.Format(PanelItemKind.Cpu, null, Spanish));

    [Fact]
    public void GraphsOfSharesGoToAHundredAndSpeedsToTheirTop()
    {
        Assert.Equal(100, PanelSampler.GraphTop(PanelItemKind.Ram, [3, 5]));
        Assert.Equal(1024, PanelSampler.GraphTop(PanelItemKind.NetUp, [10, 20]));
        Assert.Equal(50000, PanelSampler.GraphTop(PanelItemKind.NetDown, [10, 50000, 20]));
    }

    [Fact]
    public void ANewPanelShowsTheUsualFiguresInTwoRows()
    {
        var panel = InfoPanel.CreateDefault("Panel");
        Assert.Equal(2, panel.Rows);
        Assert.Equal([PanelItemKind.NetUp, PanelItemKind.NetDown, PanelItemKind.Cpu, PanelItemKind.Gpu, PanelItemKind.Ram, PanelItemKind.Vram, PanelItemKind.GpuTemp],
            panel.Items.Select(i => i.Kind));
        Assert.False(panel.IsPlaced);
        Assert.Equal("↑  ↓  CPU  GPU  RAM  VRAM  " + InfoPanelItem.DefaultLabel(PanelItemKind.GpuTemp), panel.Summary);
    }

    [Fact]
    public void ItsOwnNameReplacesTheUsualOneAndAnEmptyOneGivesItBack()
    {
        var item = new InfoPanelItem { Kind = PanelItemKind.Cpu, Label = "Proc" };
        Assert.Equal("Proc", item.ShownLabel);
        item.Label = " ";
        Assert.Equal("CPU", item.ShownLabel);
    }

    [Fact]
    public void ChangingAFigureSavesThePanel()
    {
        var panel = InfoPanel.CreateDefault("Panel");
        var modified = 0;
        panel.Modified += (_, _) => modified++;
        panel.Items[0].Look = PanelItemLook.Graph;
        panel.Items.RemoveAt(1);
        Assert.Equal(2, modified);
    }

    [Fact]
    public void AConfigEditedByHandIsMendedAndThePlaceSurvivesSaving()
    {
        var config = new AppConfig();
        var panel = InfoPanel.CreateDefault("Panel");
        panel.Rows = 0;
        panel.FontSize = 200;
        panel.Interval = 10;
        panel.Monitor = @"\\.\DISPLAY2";
        panel.X = 0.25;
        panel.Y = 0.5;
        config.Panels.Add(panel);

        var back = Json.Clone(config);
        Assert.True(back.Upgrade());
        var mended = back.Panels.Single();
        Assert.Equal(1, mended.Rows);
        Assert.Equal(48, mended.FontSize);
        Assert.Equal(250, mended.Interval);
        Assert.Equal((0.25, 0.5), (mended.X, mended.Y));
        Assert.Equal(PanelItemLook.Text, mended.Items[0].Look);
        Assert.Null(mended.Background);
    }

    [Fact]
    public async Task TheActionShowsHidesAndLocksOneOrAllOfThem()
    {
        var store = new ConfigStore();
        var ui = new FakeUi();
        var engine = new CommandEngine(store, ui);
        var desk = InfoPanel.CreateDefault("Escritorio");
        var games = InfoPanel.CreateDefault("Juegos");
        games.Enabled = false;
        store.Config.Panels.Add(desk);
        store.Config.Panels.Add(games);
        var command = new Command { Name = "Paneles" };
        store.Config.Commands.Add(command);

        // Switching all of them: one is shown, so all are hidden.
        await engine.TestActionAsync(new PanelAction(), command);
        Assert.False(desk.Enabled || games.Enabled);
        await engine.TestActionAsync(new PanelAction { PanelId = games.Id, Mode = PanelActionMode.Show }, command);
        Assert.True(games.Enabled);
        Assert.False(desk.Enabled);
        await engine.TestActionAsync(new PanelAction { Mode = PanelActionMode.Lock }, command);
        Assert.True(desk.Locked && games.Locked);
        await engine.TestActionAsync(new PanelAction { PanelId = "no-such-panel", Mode = PanelActionMode.Show }, command);
        Assert.Equal(OsdKind.Warning, ui.Osd[^1].Kind);
    }

    [Fact]
    public void ACommandThatShowsAPanelTakesItWhenExported()
    {
        var config = new AppConfig();
        var panel = InfoPanel.CreateDefault("Escritorio");
        config.Panels.Add(panel);
        var command = new Command { Name = "Ver panel" };
        command.Actions.Add(new PanelAction { PanelId = panel.Id, Mode = PanelActionMode.Show });
        config.Commands.Add(command);
        config.RefreshReferenceNames();
        Assert.Equal("Escritorio", ((PanelAction)command.Actions[0]).PanelName);

        var bundle = ItemTransfer.Collect(config, TransferKind.Command, command.Id);
        Assert.Equal(panel.Id, Assert.Single(bundle.Panels).Id);
    }

    [Fact]
    public void APanelTravelsInAKdxAndComesBackAsACopy()
    {
        var config = new AppConfig();
        var panel = InfoPanel.CreateDefault("Escritorio");
        panel.Items[0].Look = PanelItemLook.Graph;
        config.Panels.Add(panel);
        var file = Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N")[..8] + ItemTransfer.Extension);
        ItemTransfer.Export(config, TransferKind.Panel, panel.Id, file);

        var other = new AppConfig();
        var result = ItemTransfer.Apply(other, ItemTransfer.Read(file), ItemTransfer.ClashChoice.Copy);
        Assert.Equal(TransferKind.Panel, result.Kind);
        var copy = other.Panels.Single();
        Assert.Equal("Escritorio", copy.Name);
        Assert.Equal(PanelItemLook.Graph, copy.Items[0].Look);

        panel.Name = "Cambiado";
        var again = ItemTransfer.Read(file);
        Assert.Single(ItemTransfer.Clashes(config, again.Bundle));
    }
}
