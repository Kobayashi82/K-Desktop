using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Host.Stage;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The showcase's stage (phase 16) without a process: its pretend desktop (one monitor far from any real one, a taskbar
/// and its windows, which an assistant moves and presses like real ones), the mouse over its picture turned into the events of
/// the hit areas without windows, and the clips written into a manifest without touching the rest of it.
/// </summary>
public class StageTests
{
    private double _now = 10;

    private (StageScene Scene, HostWorld World) Scene()
    {
        var world = new HostWorld(() => _now);
        var scene = new StageScene(world, () => _now, dark: false);
        scene.Update(_now);
        return (scene, world);
    }

    [Fact]
    public void TheStageIsADesktopOfItsOwnFarFromTheScreens()
    {
        var (scene, world) = Scene();
        var monitor = Assert.Single(world.Current.Monitors);
        Assert.Equal(scene.Bounds, monitor.Bounds);
        Assert.Equal(StageScene.Scale, monitor.DpiScale);
        // Nowhere near a real monitor (they are never 30 000 pixels away).
        Assert.True(monitor.Bounds.Left >= StageScene.OriginX && monitor.Bounds.Top >= StageScene.OriginY);
        Assert.False(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
            .IntersectsWith(new Rect(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, monitor.Bounds.Height)));
        // Two windows open (the calculator opens when asked), with their buttons, the main one in front.
        Assert.Equal([scene.Main.Handle, scene.Second.Handle], world.Current.Windows.Select(w => w.Handle));
        Assert.All(world.Current.Windows, w => Assert.NotNull(w.Buttons));
        Assert.Equal(scene.Main.Handle, scene.Front?.Handle);
        Assert.True(scene.WorkArea.Bottom <= scene.Taskbar.Top);
        Assert.True(scene.Taskbar.Contains(scene.Clock.Center) && scene.Taskbar.Contains(scene.Tray.Center));
    }

    [Fact]
    public void ItsWindowsAreMovedAndPressedLikeRealOnes()
    {
        var (scene, world) = Scene();
        var main = scene.Main;

        Assert.True(scene.TryCommand(main.Handle, WindowCommand.Minimize));
        scene.Update(_now);
        Assert.Equal(StageWindowState.Minimized, main.State);
        Assert.DoesNotContain(world.Current.Windows, w => w.Handle == main.Handle);
        Assert.Equal(scene.Second.Handle, scene.Front?.Handle);

        Assert.True(scene.TryCommand(main.Handle, WindowCommand.Activate));
        Assert.Equal(StageWindowState.Normal, main.State);
        Assert.Equal(main.Handle, scene.Front?.Handle);

        var to = scene.MoveDestination(main);
        Assert.NotEqual(main.Normal, to);
        Assert.True(scene.TryMove(main.Handle, to));
        scene.Update(_now);
        Assert.Equal(to, world.Current.Windows.Single(w => w.Handle == main.Handle).Bounds);

        scene.Maximize(main);
        Assert.Equal(scene.WorkArea, main.Bounds(scene.WorkArea));
        // A maximized window is not moved by dragging its edges (as Windows does), only restored first.
        Assert.False(scene.TryMove(main.Handle, to));

        scene.Close(main);
        Assert.False(scene.TryCommand(main.Handle, WindowCommand.Restore));
        Assert.Null(scene.InfoOf(main));

        scene.Reset();
        scene.Update(_now);
        Assert.Equal(main.Home, main.Normal);
        Assert.Equal(StageWindowState.Normal, main.State);
        Assert.Equal(StageWindowState.Closed, scene.Opened.State);
    }

    [Fact]
    public void OpeningAWindowThatIsOpenOnlyBringsItToTheFront()
    {
        var (scene, _) = Scene();
        var opened = scene.OpenNew();
        var animation = opened.Animation;
        Assert.NotNull(animation);
        _now += 0.05;
        scene.BringToFront(scene.Main);
        // Opened again a moment later (the gesture ended and its window came too): the same animation goes on.
        Assert.Same(opened, scene.OpenNew());
        Assert.Same(animation, opened.Animation);
        Assert.Equal(opened.Handle, scene.Front?.Handle);
    }

    /// <summary>The mouse over the stage's picture, as K-Desktop sends it.</summary>
    private static StageMessage At(string op, int x, int y) => new(op, x, y);

    [Fact]
    public void TheMouseOverThePictureMakesTheEventsOfTheAreas()
    {
        var (scene, _) = Scene();
        var events = new List<PointerEventKind>();
        var pointer = new HostPointer(e => events.Add(e.Kind), (_, _) => { }, () => { }) { Virtual = true };
        var body = pointer.CreateHitArea();
        // A round body of 60 pixels at (100, 100) of the stage.
        body.Place(new HitShape.Ellipse(PixelRect.FromSize(scene.Bounds.Left + 100, scene.Bounds.Top + 100, 60, 60)));
        var input = new StageInput(scene, () => pointer.VirtualAreas);

        input.OnStage(At(StageMessage.Cursor, 102, 102));
        Assert.Empty(events);
        Assert.Equal(new Point(scene.Bounds.Left + 102, scene.Bounds.Top + 102), scene.Cursor);

        input.OnStage(At(StageMessage.Cursor, 130, 130));
        input.OnStage(At(StageMessage.Down, 130, 130));
        input.OnStage(At(StageMessage.Up, 130, 130));
        input.OnStage(At(StageMessage.Down, 130, 130));
        input.OnStage(At(StageMessage.Up, 130, 130));
        Assert.Equal([PointerEventKind.Enter, PointerEventKind.Down, PointerEventKind.Up, PointerEventKind.Click,
            PointerEventKind.Down, PointerEventKind.Up, PointerEventKind.DoubleClick], events);

        events.Clear();
        input.OnStage(At(StageMessage.Down, 130, 130));
        input.OnStage(At(StageMessage.Cursor, 150, 130));
        input.OnStage(At(StageMessage.Cursor, 200, 160));
        input.OnStage(At(StageMessage.Up, 200, 160));
        // Let go away from its body: it leaves it.
        Assert.Equal([PointerEventKind.Down, PointerEventKind.DragStart, PointerEventKind.DragMove, PointerEventKind.Up, PointerEventKind.DragEnd,
            PointerEventKind.Leave], events);

        events.Clear();
        input.OnStage(At(StageMessage.Cursor, 130, 130));
        input.OnStage(At(StageMessage.Leave, 0, 0));
        Assert.Equal([PointerEventKind.Enter, PointerEventKind.Leave], events);
        Assert.Null(scene.Cursor);

        // Hidden, nothing reaches it.
        events.Clear();
        body.Hide();
        input.OnStage(At(StageMessage.Cursor, 130, 130));
        input.OnStage(At(StageMessage.Down, 130, 130));
        Assert.Empty(events);
        Assert.Empty(pointer.Handles);
    }

    [Fact]
    public void TheClipsGoIntoTheManifestAndTheRestStaysAsItWas()
    {
        var folder = Directory.CreateTempSubdirectory("kd-stage-").FullName;
        try
        {
            var clips = new JsonArray(new JsonObject { ["item"] = "react.success", ["clip"] = "showcase/react.success.webp" });

            // Without a showcase: added at the end, the comment and the CRLF kept.
            var without = Path.Combine(folder, "a.json");
            const string before = "{\r\n  // its id\r\n  \"id\": \"pip\",\r\n  \"name\":   \"Pip\"\r\n}\r\n";
            File.WriteAllText(without, before);
            StageRecorder.WriteManifest(without, clips);
            var text = File.ReadAllText(without);
            Assert.StartsWith("{\r\n  // its id\r\n  \"id\": \"pip\",\r\n  \"name\":   \"Pip\",\r\n  \"showcase\": [", text);
            Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
            Assert.Equal("react.success", Parse(text)["showcase"]![0]!["item"]!.GetValue<string>());

            // With one: only it changes.
            var with = Path.Combine(folder, "b.json");
            File.WriteAllText(with, "{\n  \"id\": \"pip\",\n  \"showcase\": [ { \"item\": \"old\", \"clip\": \"x.webp\" } ],\n  \"extras\": []\n}\n");
            StageRecorder.WriteManifest(with, clips);
            text = File.ReadAllText(with);
            Assert.StartsWith("{\n  \"id\": \"pip\",\n  \"showcase\": [", text);
            Assert.EndsWith("],\n  \"extras\": []\n}\n", text);
            Assert.DoesNotContain("old", text);
            Assert.Single(Parse(text)["showcase"]!.AsArray());
        }
        finally { Directory.Delete(folder, recursive: true); }

        static JsonNode Parse(string json) => JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
    }

    [Theory]
    [InlineData("react.success", "react.success.webp")]
    [InlineData("kobii.ball", "kobii.ball.webp")]
    [InlineData("a/b:c", "a_b_c.webp")]
    public void AClipIsNamedAfterItsItem(string item, string file) => Assert.Equal(file, StageRecorder.FileName(item));
}
