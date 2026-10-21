using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 1.5 with the real assistant process and its probe: it sees the monitors and the windows, and gets the
/// events of a window this test opens (without activating it), moves and closes.
/// </summary>
public class WorldTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(WorldTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    [Fact]
    public async Task TheAssistantSeesAWindowOpenMoveAndClose()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "world-" + Guid.NewGuid().ToString("N")[..8]);
        var log = Path.Combine(data, "Logs", "assistant.log");
        using var process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--assistant-probe", "200,200", "--data", data, "--allow-elevated"])
        { UseShellExecute = false })!;
        try
        {
            await Until(() => Read(log).Contains("«probe» cargado"));
            // No windows at all is possible (everything minimized): the test opens its own.
            Assert.Matches(@"mundo con [1-9]\d* monitor\(es\) y \d+ ventanas", Read(log));

            var title = "KDesktop prueba " + Guid.NewGuid().ToString("N")[..6];
            var handle = await RunWindowAsync(title, async (window, events) =>
            {
                await Until(() => Read(log).Contains($"WindowOpened {{ Window = WindowInfo {{ Handle = {events}"));
                window.Left += 150;
                await Until(() => Read(log).Contains($"WindowMoved {{ Window = WindowInfo {{ Handle = {events}"));
            });
            await Until(() => Read(log).Contains($"WindowClosed {{ Handle = {handle} }}"));
            Assert.Contains(title, Read(log));
        }
        finally
        {
            using (var exit = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--data", data, "--exit"]) { UseShellExecute = false })!)
                await exit.WaitForExitAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException) { process.Kill(); }
        }
    }

    [Fact]
    public async Task WindowsGiveTheirPictureAndTheirIcon()
    {
        var services = new KDesktop.Assistant.Host.World.HostWindowServices(new KDesktop.Assistant.Host.World.WindowScanner(new HashSet<nint>()));
        await RunWindowAsync("KDesktop miniatura", async (window, handle) =>
        {
            await Task.Delay(200);
            var picture = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(services.Thumbnail(handle, 100));
            Assert.True(picture.IsFrozen);
            Assert.InRange(Math.Max(picture.PixelWidth, picture.PixelHeight), 90, 100);
            Assert.NotNull(services.AppIcon(handle));
            Assert.Same(services.AppIcon(handle), services.AppIcon(handle)); // cached per executable
        });
        Assert.Null(services.Thumbnail(0, 100));
    }

    [Fact]
    public void TheRecycleBinIconIsOnTheScreenOrNowhere()
    {
        // The desktop of whoever runs the tests: it may have no Recycle Bin icon, but asking must not fail.
        if (KDesktop.Assistant.Host.World.DesktopIcons.FindRecycleBin() is not { } icon) return;
        Assert.InRange(icon.X, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth * 4);
        Assert.InRange(icon.Y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight * 4);
    }

    /// <summary>A plain window of this test on an STA thread of its own, shown without taking the focus; closed at the end.</summary>
    private static Task<long> RunWindowAsync(string title, Func<Window, long, Task> steps)
    {
        var done = new TaskCompletionSource<long>();
        var thread = new Thread(() =>
        {
            var window = new Window
            {
                Title = title, Width = 320, Height = 200, Left = 300, Top = 300,
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            };
            window.Show();
            var handle = (long)new WindowInteropHelper(window).Handle;
            Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await steps(window, handle);
                    window.Close();
                    done.TrySetResult(handle);
                }
                catch (Exception ex)
                {
                    window.Close();
                    done.TrySetException(ex);
                }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static string Read(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new StreamReader(stream).ReadToEnd();
        }
        catch (IOException) { return string.Empty; }
    }

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(100);
        }
    }
}
