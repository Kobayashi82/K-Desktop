using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>B7 (5.0.350): several windows placed at once (<c>arrange</c>), and where a window is on its monitor.</summary>
public class ArrangeWindowsTests
{
    private static readonly ScreenRect Area = new(0, 0, 1920, 1040);
    private static readonly ScreenRect Second = new(1920, 0, 2560, 1400);

    private static readonly OpenWindow Chrome = new(1, 10, "chrome", "YouTube - Google Chrome", "Google Chrome");
    private static readonly OpenWindow ChromeMail = new(2, 10, "chrome", "Correo - Google Chrome", "Google Chrome");
    private static readonly OpenWindow Code = new(3, 30, "Code", "Program.cs - K-Desktop - Visual Studio Code", "Visual Studio Code");
    private static readonly OpenWindow Steam = new(4, 20, "steamwebhelper", "Steam", "Steam Client WebHelper");
    private static readonly OpenWindow[] Windows = [Chrome, ChromeMail, Code, Steam];

    private static AssistantActs.Plan Arrange(string[] apps, string? layout = null, int? monitor = null) =>
        AssistantActs.BuildArrange(new ActMessage("lander", "a-1", AssistantActs.Arrange, Value: monitor, Apps: apps, Layout: layout), Windows,
            new ForegroundWindow(Steam.Hwnd, Steam.Pid, Steam.Process), [Area, Second], hwnd => hwnd == Code.Hwnd ? Second : Area);

    private static List<WindowAction> Actions(AssistantActs.Plan plan) => plan.Command!.Actions.Cast<WindowAction>().ToList();

    [Fact]
    public void TwoSideBySide()
    {
        var plan = Arrange(["chrome", "vs code"]);
        Assert.Null(plan.Reason);
        Assert.Equal("Google Chrome, Visual Studio Code", plan.Name);
        var actions = Actions(plan);
        Assert.Equal([Chrome.Hwnd, Code.Hwnd], actions.Select(a => a.Handle));
        Assert.All(actions, a => Assert.Equal(WindowOperation.Snap, a.Operation));
        // On the first window's monitor.
        Assert.Equal([new ScreenRect(0, 0, 960, 1040), new ScreenRect(960, 0, 960, 1040)], actions.Select(a => a.Place!.Value));
    }

    [Fact]
    public void OnTheMonitorAskedForAndEachWindowOnce()
    {
        // «las dos de Chrome»: two different windows of it; the window in front for «esta».
        var actions = Actions(Arrange(["chrome", "chrome", "esta"], "rows", monitor: 2));
        Assert.Equal([Chrome.Hwnd, ChromeMail.Hwnd, Steam.Hwnd], actions.Select(a => a.Handle));
        Assert.Equal(new ScreenRect(1920, 0, 2560, 467), actions[0].Place);
        Assert.Equal(new ScreenRect(1920, 933, 2560, 467), actions[2].Place);
        // The first window's monitor (Code is on the second one).
        Assert.Equal(1920, Actions(Arrange(["vs code", "steam"]))[0].Place!.Value.Left);
    }

    [Fact]
    public void WhatIsNotOpenIsSaid()
    {
        var plan = Arrange(["chrome", "photoshop"]);
        Assert.Null(plan.Command);
        Assert.Equal("noWindow", plan.Reason);
        Assert.Equal("photoshop", plan.Name);
        // A third Chrome window there is not.
        Assert.Equal("noWindow", Arrange(["chrome", "chrome", "chrome"]).Reason);
    }

    [Fact]
    public void Layouts()
    {
        Assert.Equal([new(0, 0, 640, 1040), new(640, 0, 640, 1040), new(1280, 0, 640, 1040)], AssistantActs.ArrangeRects(Area, 3, null));
        Assert.Equal([new(0, 0, 960, 1040), new(960, 0, 960, 520), new(960, 520, 960, 520)], AssistantActs.ArrangeRects(Area, 3, "grid"));
        Assert.Equal([new(0, 0, 960, 520), new(960, 0, 960, 520), new(0, 520, 960, 520), new(960, 520, 960, 520)], AssistantActs.ArrangeRects(Area, 4, null));
        Assert.Equal([new(0, 0, 1152, 1040), new(1152, 0, 768, 520), new(1152, 520, 768, 520)], AssistantActs.ArrangeRects(Area, 3, "main"));
        var five = AssistantActs.ArrangeRects(Area, 5, "grid");
        Assert.Equal(3, five.Count(r => r.Top == 0));
        Assert.Equal(2, five.Count(r => r.Top == 520));
        Assert.Equal([new(0, 0, 960, 1040), new(960, 0, 960, 1040)], AssistantActs.ArrangeRects(Area, 2, "grid"));
        // Nothing left uncovered: the parts touch.
        var six = AssistantActs.ArrangeRects(Area, 6, "columns");
        Assert.Equal(1920, six.Sum(r => r.Width));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040, "full")]
    [InlineData(0, 0, 960, 1040, "left")]
    [InlineData(960, 0, 960, 1040, "right")]
    [InlineData(960, 520, 960, 520, "bottomRight")]
    [InlineData(0, 0, 1920, 520, "top")]
    [InlineData(560, 270, 800, 500, "center")]
    public void WhereAWindowIs(int left, int top, int width, int height, string place) =>
        Assert.Equal(place, WindowList.PlaceIn(new ScreenRect(left, top, width, height), Area).Place);

    [Fact]
    public void HowMuchItCovers() => Assert.Equal(50, WindowList.PlaceIn(new ScreenRect(0, 0, 960, 1040), Area).Share);
}
