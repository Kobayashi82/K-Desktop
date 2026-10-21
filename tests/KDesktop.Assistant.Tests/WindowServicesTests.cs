using System.Collections.Concurrent;
using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 6.5: the assistant process moves, minimizes and restores a normal window itself; one of an administrator's
/// process, and bringing any to the front, go to K-Desktop as <c>window</c>, which K-Desktop then applies.
/// </summary>
[Collection(OnTheDesktop.Name)]
public class WindowServicesTests
{
    [Fact]
    public async Task ANormalWindowIsChangedFromHere()
    {
        var sent = new ConcurrentQueue<PipeMessage>();
        var services = new HostWindowServices(new WindowScanner(new HashSet<nint>()), m => { sent.Enqueue(m); return true; });
        await TestWindows.RunAsync("KDesktop servicios", async handle =>
        {
            var hwnd = (IntPtr)handle;
            Assert.False(WindowTools.IsElevated(hwnd));
            Assert.True(services.TryMove(handle, new PixelRect(100, 120, 600, 520)));
            await Task.Delay(150);
            Assert.Equal(new ScreenRect(100, 120, 500, 400), WindowLayout.VisibleBounds(hwnd));

            Assert.True(services.TryCommand(handle, WindowCommand.Minimize));
            await Task.Delay(300);
            Assert.True(IsIconic(hwnd));
            Assert.True(services.TryCommand(handle, WindowCommand.Restore));
            await Task.Delay(300);
            Assert.False(IsIconic(hwnd));
            Assert.Empty(sent);

            // The caption buttons, from DWM: at the top right, in order.
            var buttons = Assert.IsType<CaptionButtons>(services.GetCaptionButtons(handle));
            Assert.True(buttons.Minimize.Right <= buttons.Maximize.Left + 1 && buttons.Maximize.Right <= buttons.Close.Left + 1);
            Assert.InRange(buttons.Close.Right, 590, 610);

            // To the front: only K-Desktop may.
            Assert.True(services.TryCommand(handle, WindowCommand.Activate));
            Assert.Equal(("activate", handle), sent.OfType<WindowMessage>().Select(w => (w.Op, w.Hwnd)).Single());

            // What K-Desktop does with a «window».
            Assert.True(AssistantWindows.Apply(new WindowMessage("w-1", handle, "move", new StepRect(150, 160, 520, 380))));
            await Task.Delay(150);
            Assert.Equal(new ScreenRect(150, 160, 520, 380), WindowLayout.VisibleBounds(hwnd));
            Assert.True(AssistantWindows.Apply(new WindowMessage("w-2", handle, "minimize")));
            await Task.Delay(300);
            Assert.True(IsIconic(hwnd));
            Assert.True(AssistantWindows.Apply(new WindowMessage("w-3", handle, "restore")));
            Assert.False(AssistantWindows.Apply(new WindowMessage("w-4", handle, "close")));
        });
        Assert.False(services.TryCommand(0x7FFF_FFFE, WindowCommand.Minimize));
        Assert.False(AssistantWindows.Apply(new WindowMessage("w-5", 0, "minimize")));
    }

    [Fact]
    public async Task AnAdministratorsWindowGoesThroughKDesktop()
    {
        var sent = new ConcurrentQueue<PipeMessage>();
        var services = new HostWindowServices(new WindowScanner(new HashSet<nint>()), m => { sent.Enqueue(m); return true; });
        await TestWindows.RunAsync("KDesktop servicios admin", async handle =>
        {
            // Elevation refused (Windows asked and nobody said yes): nothing to see here.
            if (handle == 0) return;
            Assert.True(WindowTools.IsElevated((IntPtr)handle));
            var before = WindowLayout.VisibleBounds((IntPtr)handle);

            // A drag: one move per frame; K-Desktop gets the latest ones, not every one.
            for (var i = 0; i < 20; i++)
            {
                Assert.True(services.TryMove(handle, new PixelRect(100 + i, 120, 600 + i, 520)));
                await Task.Delay(5);
            }
            await Task.Delay(HostWindowServices.MoveEvery * 3);
            var moves = sent.OfType<WindowMessage>().Where(w => w.Op == "move").ToList();
            Assert.InRange(moves.Count, 1, 10);
            Assert.Equal(new StepRect(119, 120, 500, 400), moves[^1].Rect);
            Assert.All(moves, m => Assert.Equal(handle, m.Hwnd));
            // This process could not have moved it.
            Assert.Equal(before, WindowLayout.VisibleBounds((IntPtr)handle));

            Assert.True(services.TryCommand(handle, WindowCommand.Minimize));
            Assert.Contains(sent.OfType<WindowMessage>(), w => w.Op == "minimize" && w.Hwnd == handle);
        }, elevated: true, lifetimeSeconds: 15);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);
}
