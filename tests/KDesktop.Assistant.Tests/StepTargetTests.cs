using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Storage;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 6.2: each window action comes with the window it acts on and where it ends up, as the engine then leaves it.
/// On a window of this test, given to the engine as the one in front (the user's windows are never touched).
/// </summary>
[Collection(OnTheDesktop.Name)]
public class StepTargetTests
{
    [Fact]
    public async Task WhereAWindowEndsUpIsWhereTheEngineLeavesIt()
    {
        await TestWindows.RunAsync("KDesktop destino", async handle =>
        {
            var hwnd = (IntPtr)handle;
            var front = ForegroundWindow.FromHandle(hwnd);
            var store = new ConfigStore();
            var engine = new CommandEngine(store, new NoUi());

            async Task<(StepTarget Target, ScreenRect Now)> Apply(WindowAction action)
            {
                var target = StepTargets.Of(action, front)!;
                var command = new Command { Name = "t" };
                command.Actions.Add(action);
                await engine.RunAsync(command, TriggerSource.Manual, front);
                await Task.Delay(250);
                return (target, WindowLayout.VisibleBounds(hwnd));
            }

            static void Same(StepRect expected, ScreenRect actual, int slack = 0)
            {
                Assert.InRange(actual.Left, expected.X - slack, expected.X + slack);
                Assert.InRange(actual.Top, expected.Y - slack, expected.Y + slack);
                Assert.InRange(actual.Width, expected.W - slack, expected.W + slack);
                Assert.InRange(actual.Height, expected.H - slack, expected.H + slack);
            }

            // «Colocar» to the pixel, and what the step says of the window before.
            var before = WindowLayout.VisibleBounds(hwnd);
            var (snap, now) = await Apply(new WindowAction { Operation = WindowOperation.Snap, Snap = WindowSnap.LeftHalf, ShowOsd = false });
            Assert.Equal(handle, snap.Hwnd);
            Assert.Equal("normal", snap.State);
            Assert.Equal(new StepRect(before.Left, before.Top, before.Width, before.Height), snap.Rect);
            Assert.Contains("KDesktop destino", snap.Title);
            Same(snap.Result!, now);

            (snap, now) = await Apply(new WindowAction { Operation = WindowOperation.Snap, Snap = WindowSnap.Custom, X = 10, Y = 20, Width = 30, Height = 40, ShowOsd = false });
            Same(snap.Result!, now);

            // Maximise: its monitor's work area.
            var (max, _) = await Apply(new WindowAction { Operation = WindowOperation.Maximize, ShowOsd = false });
            Assert.Equal(WindowLayout.WorkAreaOf(hwnd) is var area ? new StepRect(area.Left, area.Top, area.Width, area.Height) : null, max.Result);
            Assert.True(WindowLayout.IsMaximized(hwnd));

            // Centring a maximised window: restored and centred, to a pixel or two (from its placement).
            var (center, centered) = await Apply(new WindowAction { Operation = WindowOperation.Center, ShowOsd = false });
            Assert.Equal("maximized", center.State);
            Same(center.Result!, centered, slack: 3);

            // Another monitor, when there is one: to the pixel.
            if (WindowLayout.WorkAreas().Count > 1)
            {
                var (moved, there) = await Apply(new WindowAction { Operation = WindowOperation.MoveToMonitor, Monitor = MonitorChoice.Next, ShowOsd = false });
                Same(moved.Result!, there);
                await Apply(new WindowAction { Operation = WindowOperation.MoveToMonitor, Monitor = MonitorChoice.Previous, ShowOsd = false });
            }

            // Minimised: no rectangle, and it goes nowhere visible.
            var (min, _) = await Apply(new WindowAction { Operation = WindowOperation.Minimize, ShowOsd = false });
            Assert.Null(min.Result);
            var minimized = StepTargets.Of(new WindowAction { Operation = WindowOperation.Restore }, front)!;
            Assert.Equal("minimized", minimized.State);
            Assert.Null(minimized.Rect);
        });
    }

    [Fact]
    public void ActionsWithoutAPlaceHaveNoTarget()
    {
        Assert.Null(StepTargets.Of(new WaitAction(), ForegroundWindow.None));
        Assert.Null(StepTargets.Of(new WindowAction { Operation = WindowOperation.Snap }, ForegroundWindow.None));
        var app = StepTargets.Of(new RunAppAction { Path = @"C:\Programas\Notepad++\notepad++.exe" }, ForegroundWindow.None)!;
        Assert.Equal((0L, "notepad++"), (app.Hwnd, app.Process));
    }

    /// <summary>The engine's messages go nowhere.</summary>
    private sealed class NoUi : KDesktop.Core.Engine.IUiServices
    {
        public void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null) { }
        public IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null) => new Nothing();
        public IVoiceOsd ShowVoiceOsd(string text) => throw new NotSupportedException();
        public void ShowQuickMenu(string menuId, ForegroundWindow previousForeground) { }
        public void ShowMainWindow() { }
        public void RunOnUi(Action action) => action();
        public void RequestExit() { }
        public void RunAssistantExtra(string assistant, string extra) { }
        public void AskAssistant(string assistant, string text) { }
        public void AssistantSay(string assistant, string text) { }
        private sealed class Nothing : IDisposable { public void Dispose() { } }
    }
}
