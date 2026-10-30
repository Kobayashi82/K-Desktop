using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The right-click menu with the real KDesktop.exe in assistant mode and Dot: a right click (sent as a message to its hit
/// window) asks K-Desktop for the menu, and until <c>menuClosed</c> its hit window stays where it is but below the menu (it
/// is «always on top» and would end up over it, and a click on it closes the menu), so a second right click on Dot asks for
/// the menu again. A dot shows above the taskbar for a few seconds.
/// </summary>
[Collection(OnTheDesktop.Name)]
public partial class MenuProcessTests
{
    private const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    private static string Exe { get; } = Path.GetFullPath(typeof(MenuProcessTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.Root, "tests", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila tests/DotAssistant primero");

    [Fact]
    public async Task ItsHitAreaStaysBelowTheMenuWhileItIsOpen()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "menu-" + Guid.NewGuid().ToString("N")[..8]);
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
        Process? process = null;
        var loaded = new TaskCompletionSource<LoadedMessage>();
        var menu = new TaskCompletionSource<MenuMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage l) loaded.TrySetResult(l);
            if (m is MenuMessage asked) menu.TrySetResult(asked);
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", DotPackage, Path.Combine(data, "Assistants", "Data", "dot"), "startup")));
            var result = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(result.Ok, result.Error);

            nint hit = 0;
            await Until(() => (hit = HitWindowOf(process.Id)) != 0 && IsWindowVisible(hit));

            PostMessage(hit, WM_RBUTTONDOWN, 2, 0);
            PostMessage(hit, WM_RBUTTONUP, 0, 0);
            var asked = await menu.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("dot", asked.Assistant);

            // The menu opens after the right click, «always on top» too: a window of ours stands in for it.
            var standIn = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "Static", "menu", WS_POPUP, -4000, -4000, 10, 10, 0, 0, 0, 0);
            Assert.NotEqual(0, standIn);
            try
            {
                SetWindowPos(standIn, -1, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                Assert.True(IsAbove(standIn, hit));
                // Dot keeps moving its hit area while the menu is open: it stays there, and below the menu.
                await Task.Delay(800);
                Assert.True(IsWindowVisible(hit));
                Assert.True(IsAbove(standIn, hit));

                // A second right click on it reaches it: the menu again.
                menu = new TaskCompletionSource<MenuMessage>();
                PostMessage(hit, WM_RBUTTONDOWN, 2, 0);
                PostMessage(hit, WM_RBUTTONUP, 0, 0);
                Assert.Equal("dot", (await menu.Task.WaitAsync(TimeSpan.FromSeconds(10))).Assistant);

                // Closed: it takes back the top.
                Assert.True(server.Send(new MenuClosedMessage("dot")));
                await Until(() => IsAbove(hit, standIn));
            }
            finally
            {
                DestroyWindow(standIn);
            }

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }
    }

    /// <summary>The hit window of that process (there is one: Dot has a single hit area).</summary>
    internal static nint HitWindowOf(int pid)
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid) return true;
            if (ClassOf(hwnd) != "KDesktopAssistantHit") return true;
            found = hwnd;
            return false;
        }, 0);
        return found;
    }

    private static unsafe string ClassOf(nint hwnd)
    {
        var name = stackalloc char[64];
        var length = GetClassName(hwnd, name, 64);
        return new string(name, 0, Math.Max(0, length));
    }

    internal static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(50);
        }
    }

    /// <summary>Whether <paramref name="upper"/> is over <paramref name="lower"/> in the z-order.</summary>
    private static bool IsAbove(nint upper, nint lower)
    {
        for (var w = GetWindow(lower, GW_HWNDPREV); w != 0; w = GetWindow(w, GW_HWNDPREV))
            if (w == upper) return true;
        return false;
    }

    private const uint GW_HWNDPREV = 3, WS_POPUP = 0x80000000, SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint command);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out int processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
