using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>B7 (5.0.350): <c>list_windows</c> (made-up windows) and <c>arrange_windows</c> (through a K-Desktop of the test).</summary>
public class WindowToolsTests
{
    private static readonly string[] ChromeAndCode = ["chrome", "vs code"];
    private static readonly string[] ChromeAlone = ["chrome"];
    private static readonly string[] ChromeAndPhotoshop = ["chrome", "photoshop"];

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class Desk : ICommandBridge
    {
        public List<(IReadOnlyList<string> Apps, string? Layout, int? Monitor)> Asked { get; } = [];

        public EndMessage End { get; set; } = new("r", "ok", [], "Google Chrome, Visual Studio Code");

        public Task<EndMessage?> ArrangeAsync(IReadOnlyList<string> apps, string? layout, int? monitor, string? assistant, CancellationToken cancellationToken)
        {
            Asked.Add((apps, layout, monitor));
            return Task.FromResult<EndMessage?>(End);
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }

    [Fact]
    public async Task TheWindowsAreListedFrontFirst()
    {
        IReadOnlyList<ListedWindow> windows =
        [
            new("Google Chrome", "chrome", "YouTube - Google Chrome", true, false, true, 1, "full", 100),
            new("Visual Studio Code", "Code", "Program.cs - Visual Studio Code", false, false, false, 2, "left", 50),
            new("Spotify", "Spotify", "Spotify", false, true, false, 1, "full", 0),
        ];
        var tool = new ListWindowsTool(() => (windows, 2));
        var result = await tool.RunAsync(Args(new { }), new ToolContext(null), default);
        Assert.Contains("3 windows open on 2 monitors, the one in front first:", result.Text, StringComparison.Ordinal);
        Assert.Contains("1. Google Chrome \"YouTube - Google Chrome\" (chrome), in front: maximized, monitor 1.", result.Text, StringComparison.Ordinal);
        Assert.Contains("2. Visual Studio Code \"Program.cs - Visual Studio Code\" (Code): on the left, 50% of the screen, monitor 2.", result.Text, StringComparison.Ordinal);
        Assert.Contains("3. Spotify (Spotify): minimized, monitor 1.", result.Text, StringComparison.Ordinal);
        Assert.True(tool.ReadsOnly);
        Assert.Equal("There are no windows open (only the desktop).", ListWindowsTool.Describe([], 1));
        // One monitor: monitors are not mentioned.
        Assert.DoesNotContain("monitor", ListWindowsTool.Describe([windows[1]], 1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArrangingGoesToKDesktop()
    {
        var desk = new Desk();
        var tool = new ArrangeWindowsTool(desk);
        var done = await tool.RunAsync(Args(new { apps = ChromeAndCode, layout = "side_by_side" }), new ToolContext("lander"), default);
        Assert.True(done.Acted);
        Assert.False(done.Failed);
        Assert.Equal(["chrome", "vs code"], desk.Asked[0].Apps);
        Assert.Equal("columns", desk.Asked[0].Layout);
        Assert.Null(desk.Asked[0].Monitor);

        // Given as one text, and on a monitor.
        await tool.RunAsync(Args(new { apps = "Chrome y Steam", monitor = 2, layout = "grid" }), new ToolContext("lander"), default);
        Assert.Equal(["Chrome", "Steam"], desk.Asked[1].Apps);
        Assert.Equal(("grid", 2), (desk.Asked[1].Layout, desk.Asked[1].Monitor));
    }

    [Fact]
    public async Task OneWindowOrNoneOpenIsNotDone()
    {
        var desk = new Desk();
        var tool = new ArrangeWindowsTool(desk);
        var one = await tool.RunAsync(Args(new { apps = ChromeAlone }), new ToolContext(null), default);
        Assert.True(one.Failed);
        Assert.Contains("control_app", one.Text, StringComparison.Ordinal);
        Assert.Empty(desk.Asked);

        desk.End = new EndMessage("r", "failed", [], "photoshop", "noWindow");
        var missing = await tool.RunAsync(Args(new { apps = ChromeAndPhotoshop }), new ToolContext(null), default);
        Assert.True(missing.Failed);
        Assert.Contains("no open window of \"photoshop\"", missing.Text, StringComparison.Ordinal);

        Assert.True((await new ArrangeWindowsTool(new Desk { End = null! }).RunAsync(Args(new { apps = ChromeAndCode }), new ToolContext(null), default)).Failed);
        Assert.Equal(ToolRisk.Safe, tool.Risk);
    }
}
