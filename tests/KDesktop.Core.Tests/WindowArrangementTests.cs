using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>E1 (5.0.375): disposiciones of windows, without touching a real window.</summary>
public class WindowArrangementTests
{
    private static readonly ScreenMonitor Main = new(@"\\.\DISPLAY1", "DELL U2723QE", new ScreenRect(0, 0, 2560, 1400), true);
    private static readonly ScreenMonitor Side = new(@"\\.\DISPLAY2", "LG 27GL850", new ScreenRect(2560, 0, 1920, 1040), false);
    private static readonly ScreenMonitor Tv = new(@"\\.\DISPLAY3", "SAMSUNG", new ScreenRect(-3840, 0, 3840, 2100), false);

    private static ArrangedWindow On(string name, string device) => new() { Process = "chrome", MonitorName = name, MonitorDevice = device };

    [Fact]
    public void AWindowGoesToItsMonitorByNameAndElseToTheMainOne()
    {
        // Its monitor, even if Windows now numbers it otherwise.
        Assert.Same(Side, WindowArrangements.MonitorFor(On("LG 27GL850", @"\\.\DISPLAY5"), [Main, Side, Tv]));
        // Two the same: the GDI name tells them apart.
        var twin = Side with { Device = @"\\.\DISPLAY4", WorkArea = new ScreenRect(4480, 0, 1920, 1040) };
        Assert.Same(twin, WindowArrangements.MonitorFor(On("LG 27GL850", @"\\.\DISPLAY4"), [Main, Side, twin]));
        // The TV is not connected now: the main monitor (the user's choice), not the one with its old number.
        Assert.Same(Main, WindowArrangements.MonitorFor(On("SAMSUNG", @"\\.\DISPLAY9"), [Side, Main]));
        Assert.Null(WindowArrangements.MonitorFor(On("SAMSUNG", ""), []));
    }

    [Fact]
    public void ThePlaceIsKeptInProportionAndInsideTheMonitor()
    {
        var window = new ArrangedWindow { Left = 0.5, Top = 0, Width = 0.5, Height = 1 };
        Assert.Equal(new ScreenRect(3520, 0, 960, 1040), WindowArrangements.RectIn(window, Side.WorkArea));
        var (left, top, width, height) = WindowArrangements.FractionsOf(new ScreenRect(1280, 0, 1280, 1400), Main.WorkArea);
        Assert.Equal((0.5, 0d, 0.5, 1d), (left, top, width, height));
        // Saved half off its monitor: it comes back inside.
        var off = new ArrangedWindow { Left = 0.8, Top = -0.2, Width = 0.5, Height = 0.5 };
        Assert.Equal(new ScreenRect(1280, 0, 1280, 700), WindowArrangements.RectIn(off, Main.WorkArea));
    }

    private sealed record Open(string Process, string Title);

    [Fact]
    public void WindowsOfTheSameProgramAreMatchedByTitleThenInOrder()
    {
        var entries = new[]
        {
            new ArrangedWindow { Process = "chrome", Title = "Gmail" },
            new ArrangedWindow { Process = "chrome", Title = "YouTube" },
            new ArrangedWindow { Process = "code", Title = "K-Desktop" },
            new ArrangedWindow { Process = "steam" },
        };
        var youTube = new Open("chrome", "YouTube");
        var other = new Open("chrome", "Noticias");
        var code = new Open("Code", "otro proyecto");
        var matched = WindowArrangements.Match(entries, [other, youTube, code], w => w.Process, w => w.Title);

        // YouTube keeps its own window; Gmail is not open, so it takes the other one of Chrome; Steam has none.
        Assert.Same(other, matched[0]);
        Assert.Same(youTube, matched[1]);
        Assert.Same(code, matched[2]);
        Assert.Null(matched[3]);
    }

    [Fact]
    public void ItIsSavedAndReadBackAndActionsFindItByItsId()
    {
        var config = new AppConfig();
        var arrangement = new WindowArrangement { Name = "Trabajo", OpenMissing = true };
        arrangement.Windows.Add(new ArrangedWindow
        {
            Process = "chrome", Name = "Google Chrome", Title = "Gmail", Path = @"C:\Chrome\chrome.exe", MonitorName = "DELL U2723QE",
            MonitorDevice = @"\\.\DISPLAY1", Left = 0.25, Top = 0, Width = 0.5, Height = 1, State = ArrangedState.Maximized,
        });
        config.Arrangements.Add(arrangement);
        var command = new Command { Name = "Poner trabajo" };
        command.Actions.Add(new ArrangementAction { ArrangementId = arrangement.Id });
        config.Commands.Add(command);

        var back = Json.Deserialize<AppConfig>(Json.Serialize(config))!;
        var read = Assert.Single(back.Arrangements);
        Assert.Equal("Trabajo", read.Name);
        Assert.True(read.OpenMissing);
        var window = Assert.Single(read.Windows);
        Assert.Equal((0.25, 1d, ArrangedState.Maximized, @"\\.\DISPLAY1"), (window.Left, window.Height, window.State, window.MonitorDevice));
        Assert.Same(read, back.FindArrangement(arrangement.Id));
        Assert.Same(read, back.FindArrangement("trabajo"));
        Assert.IsType<ArrangementAction>(Assert.Single(back.Commands[0].Actions));
    }

    [Fact]
    public void ItsRowSaysWhereEachWindowIs()
    {
        Assert.Equal("left", ArrangedWindow.Place(0, 0, 0.5, 1));
        Assert.Equal("topRight", ArrangedWindow.Place(0.5, 0, 0.5, 0.5));
        Assert.Equal("full", ArrangedWindow.Place(0, 0, 1, 1));
        var row = new ArrangedWindow { MonitorName = "LG 27GL850", Left = 0.5, Width = 0.5, Height = 1 };
        Assert.Equal("LG 27GL850  ·  derecha", row.Where);
        row.State = ArrangedState.Maximized;
        Assert.Equal("LG 27GL850  ·  maximizada", row.Where);

        var arrangement = new WindowArrangement();
        var changes = 0;
        arrangement.Modified += (_, _) => changes++;
        arrangement.Windows.Add(row);
        row.Left = 0;
        Assert.Equal(2, changes);
        Assert.Equal("1 ventana", arrangement.Summary);
        Assert.Equal(row.Left, arrangement.Duplicate().Windows[0].Left);
        Assert.NotSame(row, arrangement.Duplicate().Windows[0]);
    }
}
