using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

public class ClipboardTextTests
{
    [Fact]
    public void CleanUrlTakesOutOnlyTrackers()
    {
        Assert.Equal("https://example.com/a?id=5#top",
            ClipboardText.CleanUrl("https://example.com/a?utm_source=x&id=5&fbclid=abc#top"));
        Assert.Equal("https://youtu.be/xyz", ClipboardText.CleanUrl("https://youtu.be/xyz?si=AbC123"));
        Assert.Equal("https://example.com/", ClipboardText.CleanUrl("https://example.com/"));
    }

    [Fact]
    public void CleanUrlsKeepsTheRestOfTheText()
    {
        Assert.Equal("Mira esto: https://a.com/p?q=1. Y esto http://b.org.",
            ClipboardText.CleanUrls("Mira esto: https://a.com/p?q=1&utm_medium=social. Y esto http://b.org?gclid=1."));
    }

    [Fact]
    public void Transforms()
    {
        var es = new System.Globalization.CultureInfo("es-ES");
        Assert.Equal("ÁRBOL", ClipboardText.Transform(ClipboardMode.Upper, "árbol", es));
        Assert.Equal("hola mundo", ClipboardText.Transform(ClipboardMode.Lower, "HOLA Mundo", es));
        Assert.Equal("Hola Mundo", ClipboardText.Transform(ClipboardMode.Capitalize, "hOLA mUNDO", es));
        Assert.Equal("a b" + Environment.NewLine + "c", ClipboardText.Transform(ClipboardMode.Trim, "\n  a    b  \r\n c \n\n", es));
        Assert.Null(ClipboardText.Transform(ClipboardMode.Clear, "x", es));
    }
}

public class WakeOnLanTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff")]
    [InlineData("AABBCCDDEEFF")]
    [InlineData("aabb.ccdd.eeff")]
    public void ParsesMacFormats(string text) =>
        Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF], WakeOnLan.ParseMac(text));

    [Theory]
    [InlineData("")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("GG:BB:CC:DD:EE:FF")]
    public void RejectsBadMacs(string text) => Assert.Null(WakeOnLan.ParseMac(text));

    [Fact]
    public void MagicPacketIsSixFfAndSixteenMacs()
    {
        var mac = new byte[] { 1, 2, 3, 4, 5, 6 };
        var packet = WakeOnLan.MagicPacket(mac);
        Assert.Equal(102, packet.Length);
        Assert.All(packet.Take(6), b => Assert.Equal(0xFF, b));
        for (var i = 0; i < 16; i++) Assert.Equal(mac, packet.Skip(6 + i * 6).Take(6));
    }
}

public class WindowLayoutTests
{
    private static readonly ScreenRect Area = new(0, 40, 1920, 1000);

    [Fact]
    public void HalvesAndThirdsCoverTheArea()
    {
        var left = WindowLayout.SnapRect(Area, WindowSnap.LeftHalf);
        var right = WindowLayout.SnapRect(Area, WindowSnap.RightHalf);
        Assert.Equal(new ScreenRect(0, 40, 960, 1000), left);
        Assert.Equal(left.Right, right.Left);
        Assert.Equal(Area.Right, right.Right);

        var a = WindowLayout.SnapRect(Area, WindowSnap.LeftThird);
        var b = WindowLayout.SnapRect(Area, WindowSnap.CenterThird);
        var c = WindowLayout.SnapRect(Area, WindowSnap.RightThird);
        Assert.Equal(a.Right, b.Left);
        Assert.Equal(b.Right, c.Left);
        Assert.Equal(Area.Right, c.Right);
    }

    [Fact]
    public void CustomIsKeptInsideTheArea()
    {
        var r = WindowLayout.SnapRect(Area, WindowSnap.Custom, 80, 0, 50, 100);
        Assert.Equal(1536, r.Left);
        Assert.Equal(Area.Right, r.Right);
    }

    [Fact]
    public void MapKeepsProportions()
    {
        var from = new ScreenRect(0, 0, 1920, 1040);
        var to = new ScreenRect(1920, 0, 2560, 1400);
        var moved = WindowLayout.MapRect(new ScreenRect(480, 260, 960, 520), from, to);
        Assert.Equal(new ScreenRect(2560, 350, 1280, 700), moved);
    }

    [Fact]
    public void CenteredFitsInTheArea()
    {
        var r = WindowLayout.Centered(new ScreenRect(5, 5, 800, 600), Area);
        Assert.Equal(new ScreenRect(560, 240, 800, 600), r);
    }
}

public class ConditionTests
{
    private static ConditionAction FileCondition(string path, ConditionElse otherwise, int skip = 1, bool negate = false) =>
        new() { Condition = ConditionKind.FileExists, Text = path, Else = otherwise, SkipCount = skip, Negate = negate };

    [Fact]
    public void IndentFollowsTheBlocks()
    {
        var c = new Command();
        var outer = new ConditionAction { Else = ConditionElse.Skip, SkipCount = 3 };
        var inner = new ConditionAction { Else = ConditionElse.Skip, SkipCount = 1 };
        var a = new WaitAction();
        var b = new WaitAction();
        var d = new WaitAction();
        foreach (var x in new CommandAction[] { outer, a, inner, b, d }) c.Actions.Add(x);
        Assert.Equal([0, 1, 1, 2, 0], c.Actions.Select(x => x.Indent));

        outer.Else = ConditionElse.Stop;
        Assert.Equal([0, 0, 0, 1, 0], c.Actions.Select(x => x.Indent));
    }

    private static async Task<List<CommandAction>> RunAsync(params CommandAction[] actions)
    {
        var store = new ConfigStore();
        var command = new Command { Name = "t" };
        foreach (var a in actions) command.Actions.Add(a);
        store.Config.Commands.Add(command);
        var engine = new CommandEngine(store, new FakeUi());
        var ran = new List<CommandAction>();
        engine.ActionStarting += (a, _) => { lock (ran) ran.Add(a); };
        await engine.RunAsync(command, TriggerSource.Manual);
        return ran.Where(a => a is not ConditionAction).ToList();
    }

    [Fact]
    public async Task NotMetSkipsTheNextActions()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var skipped1 = new WaitAction { Seconds = 0 };
        var skipped2 = new WaitAction { Seconds = 0, Enabled = false };
        var after = new WaitAction { Seconds = 0 };
        var ran = await RunAsync(FileCondition(missing, ConditionElse.Skip, 2), skipped1, skipped2, after);
        Assert.Equal([after], ran);
    }

    [Fact]
    public async Task NotMetStops()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var ran = await RunAsync(FileCondition(missing, ConditionElse.Stop), new WaitAction { Seconds = 0 });
        Assert.Empty(ran);
    }

    [Fact]
    public async Task MetGoesOnAndNegateTurnsItAround()
    {
        var file = Path.GetTempFileName();
        try
        {
            var a = new WaitAction { Seconds = 0 };
            Assert.Equal([a], await RunAsync(FileCondition(file, ConditionElse.Stop), a));
            Assert.Empty(await RunAsync(FileCondition(file, ConditionElse.Stop, negate: true), new WaitAction { Seconds = 0 }));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ScheduleCrossesMidnight()
    {
        var c = new ConditionAction { Condition = ConditionKind.Schedule, TimeFrom = "22:00", TimeTo = "06:00" };
        Assert.True(Conditions.InSchedule(c, new DateTime(2026, 10, 16, 23, 30, 0)));
        Assert.True(Conditions.InSchedule(c, new DateTime(2026, 10, 17, 5, 0, 0)));
        Assert.False(Conditions.InSchedule(c, new DateTime(2026, 10, 17, 12, 0, 0)));
        c.Days = WeekDays.Weekend;
        Assert.False(Conditions.InSchedule(c, new DateTime(2026, 10, 16, 23, 30, 0))); // Friday
    }

    [Fact]
    public void DescriptionsHaveText()
    {
        foreach (var kind in Enum.GetValues<ConditionKind>())
            foreach (var negate in new[] { false, true })
            {
                var c = new ConditionAction { Condition = kind, Negate = negate, Text = "x" };
                Assert.DoesNotContain("if.", c.Describe());
                c.Text = "";
                Assert.DoesNotContain("if.", c.Describe());
            }
    }
}

public class HistoryTests
{
    [Fact]
    public async Task RunsAreRecordedWithTheirOutcome()
    {
        var store = new ConfigStore();
        var ok = new Command { Name = "ok" };
        ok.Actions.Add(new WaitAction { Seconds = 0 });
        var stopped = new Command { Name = "stopped" };
        stopped.Actions.Add(new ConditionAction { Condition = ConditionKind.FileExists, Text = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) });
        store.Config.Commands.Add(ok);
        store.Config.Commands.Add(stopped);
        var engine = new CommandEngine(store, new FakeUi());

        using (ExecutionHistory.Describe("«prueba»")) await engine.RunAsync(ok, TriggerSource.Voice);
        await engine.RunAsync(stopped, TriggerSource.Hotkey);

        var entries = engine.History.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(("ok", RunOutcome.Ok, "«prueba»"), (entries[0].Name, entries[0].Outcome, entries[0].Detail));
        Assert.Equal(("stopped", RunOutcome.Stopped, (string?)null), (entries[1].Name, entries[1].Outcome, entries[1].Detail));
    }

    [Fact]
    public void KeepsOnlyTheLastOnes()
    {
        var history = new ExecutionHistory(null);
        for (var i = 0; i < ExecutionHistory.Capacity + 10; i++)
            history.Add(new HistoryEntry(DateTime.Now, null, i.ToString(), TriggerSource.Manual, null, RunOutcome.Ok, null, 0));
        Assert.Equal(ExecutionHistory.Capacity, history.Entries.Count);
        Assert.Equal("10", history.Entries[0].Name);
    }
}
