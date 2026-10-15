using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace KDesktop.Services;

/// <summary>
/// Message-only window used by a second instance to talk to the running one (show the window, or forward
/// command-line requests such as <c>--menu</c> / <c>--run</c>). The messages are whitelisted with
/// ChangeWindowMessageFilterEx so they also work when the running instance is elevated and the new one is not.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string WindowName = "KDesktop5.Messenger" + KDesktop.Core.AppPaths.InstanceSuffix;
    private const int WM_COPYDATA = 0x004A;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly uint ShowMessage = RegisterWindowMessage("KDesktop5.Show");

    private readonly HwndSource _source;
    private readonly Action _onShow;
    private readonly Action<string[]> _onArguments;

    public SingleInstance(Action onShow, Action<string[]> onArguments)
    {
        _onShow = onShow;
        _onArguments = onArguments;
        var p = new HwndSourceParameters(WindowName) { ParentWindow = HWND_MESSAGE, Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        ChangeWindowMessageFilterEx(_source.Handle, ShowMessage, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);
        ChangeWindowMessageFilterEx(_source.Handle, WM_COPYDATA, 1, IntPtr.Zero);
    }

    /// <summary>
    /// If another instance exists, forwards <paramref name="args"/> to it (or asks it to show its window)
    /// and returns true.
    /// </summary>
    public static bool SignalExisting(string[]? args = null)
    {
        var hwnd = FindWindowEx(HWND_MESSAGE, IntPtr.Zero, null, WindowName);
        if (hwnd == IntPtr.Zero) return false;
        AllowSetForegroundWindow(-1);
        if (args is { Length: > 0 })
        {
            var payload = string.Join('\u001F', args);
            var ptr = Marshal.StringToHGlobalUni(payload);
            try
            {
                var cds = new COPYDATASTRUCT { dwData = new IntPtr(0x4B44), cbData = (payload.Length + 1) * 2, lpData = ptr };
                SendMessage(hwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        else
        {
            PostMessage(hwnd, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        }
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == ShowMessage)
        {
            handled = true;
            _onShow();
        }
        else if (msg == WM_COPYDATA)
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (cds.dwData == new IntPtr(0x4B44) && cds.lpData != IntPtr.Zero)
            {
                var text = Marshal.PtrToStringUni(cds.lpData) ?? string.Empty;
                _onArguments(text.Split('\u001F'));
                handled = true;
                return new IntPtr(1);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
