using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The right-click menu with the real KDesktop.exe in assistant mode and Dot: a right click (sent as a message to its hit
/// window) asks K-Desktop for the menu, and its hit window steps aside until <c>menuClosed</c> (it is «always on top» and
/// would end up over the menu, which a click on it closes). A dot shows above the taskbar for a few seconds.
/// </summary>
[Collection(OnTheDesktop.Name)]
public partial class MenuProcessTests
{
    private const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    private static string Exe { get; } = Path.GetFullPath(typeof(MenuProcessTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila samples/DotAssistant primero");

    [Fact]
    public async Task ItsHitAreaStepsAsideWhileTheMenuIsOpen()
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
            await Until(() => !IsWindowVisible(hit));
            // Dot keeps moving its hit area while the menu is open: it stays away all the same.
            await Task.Delay(600);
            Assert.False(IsWindowVisible(hit));

            Assert.True(server.Send(new MenuClosedMessage("dot")));
            await Until(() => IsWindowVisible(hit));

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
