using System.Runtime.InteropServices;
using System.Text;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

/// <summary>
/// The text expander's side of the hook: remembers the last characters typed in the window in front and, when they end
/// with an abbreviation of «Textos», swallows its last key and asks for the text. Never in a game or a film covering the
/// screen, nor in the programs of «Desactivar en» (but the texts limited to them).
/// </summary>
public sealed partial class HotkeyRouter
{
    private volatile SnippetIndex _snippets = SnippetIndex.Empty;

    // Only touched on the hook thread.
    private readonly StringBuilder _typed = new();
    private IntPtr _typedIn;
    private readonly byte[] _keyState = new byte[256];
    private readonly char[] _chars = new char[8];

    /// <summary>An abbreviation was typed (its last key was swallowed); on a worker thread.</summary>
    public event Action<SnippetHit>? SnippetTyped;

    public void UpdateSnippets(SnippetIndex index) => _snippets = index;

    /// <summary>In K-Desktop's own windows only while true (the box to try them in «Textos»), never while editing them.</summary>
    public bool SnippetsInKDesktop { get; set; }

    /// <summary>What was typed is forgotten (a click, another window…). Hook thread.</summary>
    private void ForgetTyped() => _typed.Clear();

    /// <summary>A key going down that is not a modifier nor a hotkey; true swallows it.</summary>
    private bool SnippetKey(KeyboardInput e, Modifiers mods)
    {
        var index = _snippets;
        if (index.IsEmpty)
        {
            if (_typed.Length > 0) _typed.Clear();
            return false;
        }
        var front = Win32.GetForegroundWindow();
        if (front != _typedIn)
        {
            _typed.Clear();
            _typedIn = front;
        }
        var vk = e.Vk;
        var shortcut = (mods & Modifiers.Win) != 0 || ((mods & Modifiers.Ctrl) != 0) != ((mods & Modifiers.Alt) != 0);
        if (vk == KeyNames.VK_BACK)
        {
            if (shortcut) _typed.Clear(); // Ctrl+Backspace takes a whole word
            else if (_typed.Length > 0) _typed.Length--;
            return false;
        }
        if (vk is KeyNames.VK_SPACE or KeyNames.VK_TAB or KeyNames.VK_RETURN)
        {
            var typedEnd = string.Empty;
            var ended = shortcut ? null : index.Ended(_typed.ToString(), () => ProgramOf(front), out typedEnd);
            if (ended is not null && Allowed(front))
            {
                var hit = new SnippetHit(ended, typedEnd, index.Prefix.Length + typedEnd.Length, vk, '\0', ForegroundWindow.FromHandle(front));
                _typed.Clear();
                Post(() => SnippetTyped?.Invoke(hit));
                return true;
            }
            _typed.Clear();
            if (vk == KeyNames.VK_SPACE && !shortcut) _typed.Append(' ');
            return false;
        }
        if (shortcut)
        {
            _typed.Clear();
            return false;
        }
        switch (CharOf(vk, front, mods))
        {
            case < 0: return false; // a dead key (´ ` ^ ¨): the next one makes the letter
            case 0:
                _typed.Clear(); // arrows, Home, Delete…: the cursor may have moved
                return false;
        }
        _typed.Append(_chars[0]);
        if (_typed.Length > index.Keep) _typed.Remove(0, _typed.Length - index.Keep);
        if (index.Typed(_typed.ToString(), () => ProgramOf(front), out var typed) is not { } snippet || !Allowed(front)) return false;
        var typedHit = new SnippetHit(snippet, typed, index.Prefix.Length + typed.Length - 1, 0, _chars[0], ForegroundWindow.FromHandle(front));
        _typed.Clear();
        Post(() => SnippetTyped?.Invoke(typedHit));
        return true;
    }

    /// <summary>The character the key writes with the keyboard layout of the window in front: 1, 0 none, -1 a dead key.</summary>
    private int CharOf(int vk, IntPtr front, Modifiers mods)
    {
        var layout = GetKeyboardLayout(Win32.GetWindowThreadProcessId(front, out _));
        Array.Clear(_keyState);
        if ((mods & Modifiers.Shift) != 0) _keyState[KeyNames.VK_SHIFT] = 0x80;
        if ((mods & Modifiers.Ctrl) != 0) _keyState[KeyNames.VK_CONTROL] = 0x80;
        if ((mods & Modifiers.Alt) != 0) _keyState[KeyNames.VK_MENU] = 0x80;
        if ((Win32.GetKeyState(0x14 /* VK_CAPITAL */) & 1) != 0) _keyState[0x14] = 0x01;
        var scan = MapVirtualKeyEx((uint)vk, 0, layout);
        // 4: without touching the keyboard state, so a dead key still makes its accent in the program.
        var count = ToUnicodeEx((uint)vk, scan, _keyState, _chars, _chars.Length, 4, layout);
        if (count < 0) return -1;
        return count == 1 && !char.IsControl(_chars[0]) ? 1 : 0;
    }

    /// <summary>
    /// The program in front, for the texts limited to some programs (and «Desactivar en»). In K-Desktop's box to try
    /// them, any program: every text can be tried there.
    /// </summary>
    private static string ProgramOf(IntPtr front)
    {
        var window = ForegroundWindow.FromHandle(front);
        return window.ProcessId == Environment.ProcessId ? SnippetIndex.AnyProgram : window.ProcessName;
    }

    /// <summary>Not in a full-screen game or film; in K-Desktop only in the box to try them. Hook thread: cheap.</summary>
    private bool Allowed(IntPtr front)
    {
        if (ForegroundWindow.FromHandle(front).ProcessId == Environment.ProcessId) return SnippetsInKDesktop;
        if (SHQueryUserNotificationState(out var state) == 0 && state is 3 /* D3D full screen */ or 4 /* presentation */) return false;
        return !CoversMonitor(front);
    }

    private static bool CoversMonitor(IntPtr window)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var r)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2 /* nearest */), ref info)) return false;
        var m = info.rcMonitor;
        if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return false;
        // The desktop covers it too, and it is not a game.
        var name = new char[32];
        var length = GetClassName(window, name, name.Length);
        return new string(name, 0, Math.Max(0, length)) is not ("Progman" or "WorkerW");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);

    // Unicode: without it the buffer goes as one byte per char and Windows writes two past its end (heap corruption).
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, [Out] char[] buffer, int size, uint flags, IntPtr layout);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, [Out] char[] name, int max);
}
