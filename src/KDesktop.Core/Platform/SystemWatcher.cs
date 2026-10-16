using System.Runtime.InteropServices;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>
/// Notices from Windows for the events, on its own thread with a hidden window (a message-only window doesn't get
/// broadcasts such as going to sleep): the window in front changed, the session was locked or unlocked, the PC goes
/// to sleep or wakes up. It only exists while some event needs it.
/// </summary>
public sealed class SystemWatcher : IDisposable
{
    private const uint WM_CLOSE = 0x0010, WM_WTSSESSION_CHANGE = 0x02B1, WM_POWERBROADCAST = 0x0218, WM_SETFOREGROUNDHOOK = 0x8001;
    private const int WTS_SESSION_LOCK = 7, WTS_SESSION_UNLOCK = 8;
    private const int PBT_APMSUSPEND = 4, PBT_APMRESUMESUSPEND = 7, PBT_APMRESUMEAUTOMATIC = 0x12;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x00000080;

    private readonly WndProc _proc;
    private readonly WinEventProc _winEvent;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private IntPtr _hwnd;
    private IntPtr _foregroundHook;
    private long _lastResume;

    /// <summary>The window in front changed (on the watcher's thread; keep it short).</summary>
    public event Action<IntPtr>? ForegroundChanged;

    /// <summary>
    /// Lock, unlock, sleep or wake. For <see cref="SessionEvent.Suspend"/> the returned task is waited for up to 2 s
    /// (Windows goes to sleep right after the notice).
    /// </summary>
    public Func<SessionEvent, Task?>? SessionChanged { get; set; }

    public SystemWatcher()
    {
        _proc = OnMessage;
        _winEvent = OnWinEvent;
        _thread = new Thread(Run) { IsBackground = true, Name = "K-Desktop events" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Starts or stops watching the window in front.</summary>
    public void WatchForeground(bool on)
    {
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_SETFOREGROUNDHOOK, on ? 1 : 0, IntPtr.Zero);
    }

    private void Run()
    {
        var module = Win32.GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = module,
            lpszClassName = "KDesktopEvents",
        };
        RegisterClassEx(ref wc); // fails harmlessly when the class is already registered
        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW, wc.lpszClassName, "K-Desktop events", WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            Log.Error($"No se pudo crear la ventana de los eventos (error {Marshal.GetLastWin32Error()})");
            _ready.Set();
            return;
        }
        if (!WTSRegisterSessionNotification(_hwnd, 0)) Log.Warn("Eventos: no se recibirán los bloqueos de la sesión");
        _ready.Set();
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
    }

    private IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_SETFOREGROUNDHOOK:
                    if (wParam != 0 && _foregroundHook == IntPtr.Zero)
                        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEvent, 0, 0, WINEVENT_OUTOFCONTEXT);
                    else if (wParam == 0 && _foregroundHook != IntPtr.Zero)
                    {
                        UnhookWinEvent(_foregroundHook);
                        _foregroundHook = IntPtr.Zero;
                    }
                    return IntPtr.Zero;

                case WM_WTSSESSION_CHANGE:
                    if ((int)wParam == WTS_SESSION_LOCK) Raise(SessionEvent.Lock);
                    else if ((int)wParam == WTS_SESSION_UNLOCK) Raise(SessionEvent.Unlock);
                    break;

                case WM_POWERBROADCAST:
                    switch ((int)wParam)
                    {
                        case PBT_APMSUSPEND:
                            Raise(SessionEvent.Suspend)?.Wait(TimeSpan.FromSeconds(2));
                            break;
                        case PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND:
                            // Both arrive when someone wakes the PC by hand: once is enough.
                            var now = Environment.TickCount64;
                            if (now - Interlocked.Exchange(ref _lastResume, now) > 15_000) Raise(SessionEvent.Resume);
                            break;
                    }
                    return 1; // TRUE

                case WM_CLOSE:
                    WTSUnRegisterSessionNotification(hwnd);
                    DestroyWindow(hwnd);
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error en un aviso de Windows para los eventos", ex);
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private Task? Raise(SessionEvent change)
    {
        try { return SessionChanged?.Invoke(change); }
        catch (Exception ex)
        {
            Log.Error("Error en un evento de la sesión", ex);
            return null;
        }
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0) return; // OBJID_WINDOW
        try { ForegroundChanged?.Invoke(hwnd); }
        catch (Exception ex) { Log.Error("Error en el cambio de ventana en primer plano", ex); }
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _ready.Dispose();
    }

    // ── interop ──────────────────────────────────────────────────────────

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, nint wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
}
