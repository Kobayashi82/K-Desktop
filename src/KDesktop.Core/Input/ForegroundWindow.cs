using System.Collections.Concurrent;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

/// <summary>Snapshot of the window that was in the foreground when something was triggered.</summary>
public sealed record ForegroundWindow(IntPtr Handle, uint ProcessId, string ProcessName)
{
    public static readonly ForegroundWindow None = new(IntPtr.Zero, 0, string.Empty);

    public string Title => Handle == IntPtr.Zero ? string.Empty : Win32.GetWindowTitle(Handle);

    /// <summary>Top-level window (the one that should be made topmost, activated, etc.).</summary>
    public IntPtr RootHandle => Handle == IntPtr.Zero ? IntPtr.Zero : Win32.GetAncestor(Handle, Win32.GA_ROOT);

    public static ForegroundWindow Capture() => FromHandle(Win32.GetForegroundWindow());

    public static ForegroundWindow FromHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return None;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        var name = ProcessNames.Get(pid);

        // UWP apps live inside ApplicationFrameHost: report the real app process instead.
        if (name == "applicationframehost")
        {
            var inner = FindUwpChild(hwnd, pid);
            if (inner != 0) return new ForegroundWindow(hwnd, inner, ProcessNames.Get(inner));
        }
        return new ForegroundWindow(hwnd, pid, name);
    }

    private static uint FindUwpChild(IntPtr frame, uint framePid)
    {
        uint found = 0;
        var child = IntPtr.Zero;
        while ((child = Win32.FindWindowEx(frame, child, null, null)) != IntPtr.Zero)
        {
            Win32.GetWindowThreadProcessId(child, out var pid);
            if (pid != framePid) { found = pid; break; }
        }
        return found;
    }
}

/// <summary>Cached pid → lowercase process name (without .exe). Cheap enough to call from the input hook.</summary>
public static class ProcessNames
{
    private static readonly ConcurrentDictionary<uint, (string Name, long Expires)> Cache = new();

    public static string Get(uint pid)
    {
        if (pid == 0) return string.Empty;
        var now = Environment.TickCount64;
        if (Cache.TryGetValue(pid, out var entry) && entry.Expires > now) return entry.Name;

        var path = Win32.GetProcessPath(pid);
        var name = path is null ? string.Empty : Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        Cache[pid] = (name, now + 30_000);
        if (Cache.Count > 512)
            foreach (var kv in Cache.Where(kv => kv.Value.Expires <= now).ToList()) Cache.TryRemove(kv.Key, out _);
        return name;
    }

    /// <summary>Normalises user-typed process names: "Chrome.exe" → "chrome".</summary>
    public static string Normalize(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }
}
