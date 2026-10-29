using System.Runtime.InteropServices;
using System.Windows.Automation;
using KDesktop.Core;

namespace KDesktop.Services;

/// <summary>
/// Whether the box with the keyboard focus is for a password: a classic edit box with ES_PASSWORD, or what UI Automation
/// says (browsers, modern apps). Any thread; a program that does not answer in time counts as not.
/// </summary>
internal static class PasswordFocus
{
    private const int ES_PASSWORD = 0x20;
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(400);

    public static bool IsPassword()
    {
        var front = GetForegroundWindow();
        if (front == IntPtr.Zero) return false;
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(GetWindowThreadProcessId(front, out _), ref info) && info.hwndFocus != IntPtr.Zero &&
            (GetWindowLongPtr(info.hwndFocus, -16 /* GWL_STYLE */).ToInt64() & ES_PASSWORD) != 0)
            return true;
        var ask = Task.Run(() =>
        {
            try { return AutomationElement.FocusedElement?.Current.IsPassword == true; }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException) { return false; }
        });
        if (ask.Wait(Wait)) return ask.Result;
        Log.Info("Textos: el programa no dice a tiempo si es una contraseña");
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
