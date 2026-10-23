using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 1.4 on the real screen, without touching the mouse or the keyboard: the probe of the assistant process is
/// drawn (the exact pixel), its hit area takes the mouse and the rest of the circle lets it through to the window
/// below, the focus never moves, a click (sent as a message to its own hit window) is received, and closing leaves
/// nothing behind. A small circle shows at (400, 400) for a few seconds.
/// </summary>
[Collection(OnTheDesktop.Name)]
public partial class OverlayTests
{
    private const int X = 400, Y = 400;
    private const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;

    private static string Exe { get; } = Path.GetFullPath(typeof(OverlayTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    [Fact]
    public async Task TheProbeIsDrawnClickableAndLetsTheRestThrough()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "overlay-" + Guid.NewGuid().ToString("N")[..8]);
        var foreground = GetForegroundWindow();
        using var process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--assistant-probe", $"{X},{Y}", "--data", data, "--allow-elevated"])
        { UseShellExecute = false })!;
        try
        {
            await Until(() => Pixel(X, Y) == (255, 127, 0));
            Assert.Equal((255, 127, 0), Pixel(X + 24, Y));          // the ring is drawn too…
            Assert.Equal("KDesktopAssistantHit", ClassAt(X, Y));    // …but only the middle takes the mouse
            Assert.NotEqual("KDesktopAssistantHit", ClassAt(X + 24, Y));
            Assert.NotEqual("KDesktopAssistantOverlay", ClassAt(X + 24, Y));
            Assert.Equal(foreground, GetForegroundWindow());

            var hit = WindowFromPoint(new POINT(X, Y));
            PostMessage(hit, WM_LBUTTONDOWN, 1, 0);
            PostMessage(hit, WM_LBUTTONUP, 0, 0);
            await Until(() => Pixel(X, Y) == (0, 120, 215));        // the click turned it blue
            Assert.Equal(foreground, GetForegroundWindow());
        }
        finally
        {
            using (var exit = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--data", data, "--exit"]) { UseShellExecute = false })!)
                await exit.WaitForExitAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(limit.Token); }
            catch (OperationCanceledException) { process.Kill(); }
        }
        Assert.NotEqual((0, 120, 215), Pixel(X, Y));
        var log = await File.ReadAllTextAsync(Path.Combine(data, "Logs", "assistant.log"));
        Assert.Contains("puntero: Click en body", log);
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

    private static (int, int, int) Pixel(int x, int y)
    {
        var dc = GetDC(0);
        try
        {
            var c = GetPixel(dc, x, y);
            return ((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
        }
        finally { ReleaseDC(0, dc); }
    }

    private static unsafe string ClassAt(int x, int y)
    {
        var name = stackalloc char[256];
        var length = GetClassName(WindowFromPoint(new POINT(x, y)), name, 256);
        return new string(name, 0, Math.Max(0, length));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct POINT(int X, int Y);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial uint GetPixel(nint dc, int x, int y);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
