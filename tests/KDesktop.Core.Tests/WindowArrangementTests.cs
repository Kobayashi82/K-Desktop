using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>E1 (5.0.375) and E1b (zones): disposiciones of windows, without touching a real window.</summary>
public class WindowArrangementTests
{
    private static readonly ScreenMonitor Main = new(@"\\.\DISPLAY1", "DELL U2723QE", new ScreenRect(0, 0, 2560, 1400), true);
    private static readonly ScreenMonitor Side = new(@"\\.\DISPLAY2", "LG 27GL850", new ScreenRect(2560, 0, 1920, 1040), false);
    private static readonly ScreenMonitor Tv = new(@"\\.\DISPLAY3", "SAMSUNG", new ScreenRect(-3840, 0, 3840, 2100), false);

    private static ArrangedZone On(string name, string device) => new() { MonitorName = name, MonitorDevice = device };

    private static ArrangedZone Zone(double left, double top, double width, double height, params string[] programs)
    {
        var zone = new ArrangedZone { MonitorName = Main.Name, MonitorDevice = Main.Device };
        zone.SetRect(left, top, width, height);
        foreach (var p in programs) zone.Programs.Add(new ZoneProgram { Process = p });
        return zone;
    }

    [Fact]
    public void AZoneGoesToItsMonitorByNameAndElseToTheMainOne()
    {
        // Its monitor, even if Windows now numbers it otherwise.
        Assert.Same(Side, WindowArrangements.MonitorFor(On("LG 27GL850", @"\\.\DISPLAY5"), [Main, Side, Tv]));
        // Two the same: the GDI name tells them apart.
        var twin = Side with { Device = @"\\.\DISPLAY4", WorkArea = new ScreenRect(4480, 0, 1920, 1040) };
        Assert.Same(twin, WindowArrangements.MonitorFor(On("LG 27GL850", @"\\.\DISPLAY4"), [Main, Side, twin]));
        // The TV is not connected now: the main monitor (the user's choice), not the one with its old number.
        Assert.Same(Main, WindowArrangements.MonitorFor(On("SAMSUNG", @"\\.\DISPLAY9"), [Side, Main]));
        Assert.Null(WindowArrangements.MonitorFor(On("SAMSUNG", ""), []));
        Assert.Null(WindowArrangements.MonitorExactly("SAMSUNG", @"\\.\DISPLAY3", [Main, Side]));
    }

    [Fact]
    public void ThePlaceIsKeptInProportionAndInsideTheMonitor()
    {
        Assert.Equal(new ScreenRect(3520, 0, 960, 1040), WindowArrangements.RectIn(Zone(0.5, 0, 0.5, 1), Side.WorkArea));
        var (left, top, width, height) = WindowArrangements.FractionsOf(new ScreenRect(1280, 0, 1280, 1400), Main.WorkArea);
        Assert.Equal((0.5, 0d, 0.5, 1d), (left, top, width, height));
        // Saved half off its monitor: it comes back inside.
        Assert.Equal(new ScreenRect(1280, 0, 1280, 700), WindowArrangements.RectIn(0.8, -0.2, 0.5, 0.5, Main.WorkArea));
        Assert.True(Zone(0, 0, 1, 1).IsFull);
        Assert.False(Zone(0, 0, 0.5, 1).IsFull);
    }

    private sealed record Open(string Process, string Title);

    [Fact]
    public void WindowsOfTheSameProgramAreMatchedByTitleThenInOrder()
    {
        var entries = new[]
        {
            new ZoneProgram { Process = "chrome", Title = "Gmail" },
            new ZoneProgram { Process = "chrome", Title = "YouTube" },
            new ZoneProgram { Process = "code", Title = "K-Desktop" },
            new ZoneProgram { Process = "steam" },
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
        var zone = Zone(0.25, 0, 0.5, 1);
        zone.Programs.Add(new ZoneProgram { Process = "chrome", Name = "Google Chrome", Title = "Gmail", Path = @"C:\Chrome\chrome.exe" });
        arrangement.Zones.Add(zone);
        config.Arrangements.Add(arrangement);
        var command = new Command { Name = "Poner trabajo" };
        command.Actions.Add(new ArrangementAction { ArrangementId = arrangement.Id });
        config.Commands.Add(command);

        var json = Json.Serialize(config);
        Assert.DoesNotContain("\"windows\"", json, StringComparison.Ordinal);
        var back = Json.Deserialize<AppConfig>(json)!;
        var read = Assert.Single(back.Arrangements);
        Assert.Equal("Trabajo", read.Name);
        Assert.True(read.OpenMissing);
        var z = Assert.Single(read.Zones);
        Assert.Equal((0.25, 1d, @"\\.\DISPLAY1"), (z.Left, z.Height, z.MonitorDevice));
        Assert.Equal(("Gmail", @"C:\Chrome\chrome.exe"), (z.Programs[0].Title, z.Programs[0].Path));
        Assert.Same(read, back.FindArrangement(arrangement.Id));
        Assert.Same(read, back.FindArrangement("trabajo"));
        Assert.IsType<ArrangementAction>(Assert.Single(back.Commands[0].Actions));
    }

    [Fact]
    public void TheDisposicionesOf5_0_375BecomeZones()
    {
        // As 5.0.375 saved it: a list of windows.
        const string json = """
            {"arrangements":[{"id":"a1","name":"Viejo","openMissing":true,"windows":[
              {"process":"chrome","name":"Google Chrome","title":"Gmail","path":"C:\\c.exe","monitorName":"DELL U2723QE","monitorDevice":"\\\\.\\DISPLAY1","left":0,"top":0,"width":0.5,"height":1,"state":"Normal","enabled":true},
              {"process":"code","name":"Code","title":"K","path":"","monitorName":"DELL U2723QE","monitorDevice":"\\\\.\\DISPLAY1","left":0,"top":0,"width":0.5,"height":1,"state":"Normal","enabled":true},
              {"process":"vlc","name":"VLC","title":"","path":"","monitorName":"SAMSUNG","monitorDevice":"\\\\.\\DISPLAY3","left":0.1,"top":0.1,"width":0.3,"height":0.3,"state":"Maximized","enabled":true},
              {"process":"steam","name":"Steam","title":"","path":"","monitorName":"DELL U2723QE","monitorDevice":"\\\\.\\DISPLAY1","left":0.5,"top":0,"width":0.5,"height":1,"state":"Normal","enabled":false}
            ]}]}
            """;
        var config = Json.Deserialize<AppConfig>(json)!;
        Assert.True(config.Upgrade());
        var arrangement = Assert.Single(config.Arrangements);
        Assert.Null(arrangement.Windows);
        Assert.True(arrangement.OpenMissing);
        Assert.Equal(3, arrangement.Zones.Count);
        // Two in the same place share the zone; the maximised one is the whole TV; the one switched off leaves its zone empty.
        Assert.Equal(["chrome", "code"], arrangement.Zones[0].Programs.Select(p => p.Process));
        Assert.Equal("C:\\c.exe", arrangement.Zones[0].Programs[0].Path);
        Assert.True(arrangement.Zones[1].IsFull);
        Assert.Equal("SAMSUNG", arrangement.Zones[1].MonitorName);
        Assert.Empty(arrangement.Zones[2].Programs);
        Assert.Equal("3 zonas  ·  3 programas", arrangement.Summary);
        Assert.DoesNotContain("\"windows\"", Json.Serialize(config), StringComparison.Ordinal);
        Assert.False(arrangement.ConvertWindows());
    }

    [Fact]
    public void SavingAsTheyAreMakesAZoneOfEachWindow()
    {
        var zones = WindowArrangements.ZonesOf(
        [
            new("chrome", "Google Chrome", "Gmail", "", Main, new ScreenRect(0, 0, 1280, 1400), false),
            new("code", "Code", "K", "", Main, new ScreenRect(0, 0, 1280, 1400), false),
            new("vlc", "VLC", "", "", Side, new ScreenRect(2600, 40, 800, 600), true),
            new("notepad", "Bloc de notas", "", "", Main, new ScreenRect(2000, 1000, 1280, 1000), false),
        ]);
        Assert.Equal(3, zones.Count);
        Assert.Equal(2, zones[0].Programs.Count);
        Assert.True(zones[1].IsFull);
        Assert.Equal("LG 27GL850", zones[1].MonitorName);
        // Half out of its monitor: kept inside it.
        Assert.True(zones[2].Right <= 1.0001 && zones[2].Bottom <= 1.0001);
    }

    [Fact]
    public void ATemplateCutsTheMonitorAndKeepsThePrograms()
    {
        var all = new List<ArrangedZone> { Zone(0, 0, 0.5, 1, "chrome"), Zone(0.5, 0, 0.5, 1, "code"), On("LG 27GL850", @"\\.\DISPLAY2") };
        var mine = all.Take(2).ToList();
        var fresh = ZoneCuts.Apply(ZoneTemplate.Columns3, all, mine, Main.Name, Main.Device);
        Assert.Equal(3, fresh.Count);
        Assert.Equal(4, all.Count); // the other monitor's zone stays
        Assert.Equal(["chrome"], fresh[0].Programs.Select(p => p.Process));
        Assert.Empty(fresh[1].Programs);
        Assert.Equal(["code"], fresh[2].Programs.Select(p => p.Process)); // its middle (0.75) is in the last third
        Assert.Equal(1, fresh.Sum(z => z.Width), 3);
    }

    [Fact]
    public void AZoneIsCutDraggedAndJoined()
    {
        var all = new List<ArrangedZone> { Zone(0, 0, 1, 1, "chrome") };
        var right = ZoneCuts.Split(all, all[0], vertical: true, at: 0.6);
        Assert.NotNull(right);
        Assert.Equal((0.6, 0.4), (all[0].Width, right.Width));
        Assert.Equal(["chrome"], all[0].Programs.Select(p => p.Process));
        Assert.Empty(right.Programs);
        // Too close to an edge: not cut.
        Assert.Null(ZoneCuts.Split(all, right, vertical: true, at: 0.62));

        var line = Assert.Single(ZoneCuts.Lines(all));
        Assert.True(line.Vertical);
        Assert.True(line.CanJoin);
        var moved = ZoneCuts.Move(line, 0.25);
        Assert.Equal((0.25, 0.25, 0.75), (moved.At, all[0].Width, right.Width));
        // Not past the smallest a zone gets.
        ZoneCuts.Move(moved, 0.99);
        Assert.Equal(1 - ZoneCuts.Smallest, all[0].Width, 4);

        right.Programs.Add(new ZoneProgram { Process = "code" });
        var kept = ZoneCuts.Join(all, Assert.Single(ZoneCuts.Lines(all)));
        Assert.Same(all[0], kept);
        Assert.Single(all);
        Assert.Equal(1, all[0].Width, 4);
        Assert.Equal(["chrome", "code"], all[0].Programs.Select(p => p.Process));
    }

    [Fact]
    public void TheHalvesOfAGridAreSeparateLinesAndATJunctionMovesTogether()
    {
        var grid = ZoneCuts.Rects(ZoneTemplate.Grid).Select(r => Zone(r.Left, r.Top, r.Width, r.Height)).ToList();
        var lines = ZoneCuts.Lines(grid);
        Assert.Equal(4, lines.Count);
        Assert.All(lines, l => Assert.True(l.CanJoin));

        var big = ZoneCuts.Rects(ZoneTemplate.BigAndTwo).Select(r => Zone(r.Left, r.Top, r.Width, r.Height)).ToList();
        lines = ZoneCuts.Lines(big);
        var vertical = Assert.Single(lines, l => l.Vertical);
        // One big zone on the left, two on the right: one line, which cannot join (not the same span).
        Assert.Equal((1, 2), (vertical.Before.Count, vertical.After.Count));
        Assert.False(vertical.CanJoin);
        ZoneCuts.Move(vertical, 0.5);
        Assert.All(big.Skip(1), z => Assert.Equal(0.5, z.Left, 4));
    }

    [Fact]
    public void ItStaysTheActiveOneOfItsMonitorsAndTheMonitorsAreRemembered()
    {
        var active = new List<ActiveArrangement> { new() { MonitorName = Main.Name, MonitorDevice = Main.Device, ArrangementId = "old" } };
        var arrangement = new WindowArrangement();
        arrangement.Zones.Add(Zone(0, 0, 1, 1));
        arrangement.Zones.Add(On(Side.Name, Side.Device));
        Assert.True(WindowArrangements.SetActive(active, arrangement));
        Assert.Equal(2, active.Count);
        Assert.All(active, a => Assert.Equal(arrangement.Id, a.ArrangementId));
        Assert.False(WindowArrangements.SetActive(active, arrangement));

        var known = new List<KnownMonitor>();
        Assert.True(WindowArrangements.Remember(known, [Main, Side]));
        Assert.False(WindowArrangements.Remember(known, [Main]));
        Assert.Equal(2, known.Count);
        Assert.True(WindowArrangements.Remember(known, [Main with { WorkArea = new ScreenRect(0, 0, 3840, 2100) }]));
        Assert.Equal(3840, known.Single(k => k.Name == Main.Name).WorkWidth);
    }

    [Fact]
    public void ItsSummaryAndChangesFollowTheZones()
    {
        var arrangement = new WindowArrangement();
        var changes = 0;
        arrangement.Modified += (_, _) => changes++;
        Assert.Equal("Sin zonas", arrangement.Summary);
        var zone = Zone(0, 0, 0.5, 1);
        arrangement.Zones.Add(zone);
        zone.Programs.Add(new ZoneProgram { Process = "chrome", Name = "Google Chrome" });
        zone.Left = 0.1;
        Assert.Equal(3, changes);
        Assert.Equal("1 zona  ·  1 programa", arrangement.Summary);
        Assert.Equal("Google Chrome", zone.Label);
        zone.Programs.Add(new ZoneProgram { Process = "code" });
        zone.Programs.Add(new ZoneProgram { Process = "vlc" });
        Assert.Equal("Google Chrome, code +1", zone.Label);
        var copy = arrangement.Duplicate();
        Assert.NotSame(zone, copy.Zones[0]);
        Assert.Equal(3, copy.Zones[0].Programs.Count);
    }
}
