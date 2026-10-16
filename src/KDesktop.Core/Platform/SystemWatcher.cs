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
    private const uint WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2, DBT_DEVTYP_DEVICEINTERFACE = 5;
    private const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 4;
    private const uint WM_CLOSE = 0x0010, WM_WTSSESSION_CHANGE = 0x02B1, WM_POWERBROADCAST = 0x0218, WM_SETFOREGROUNDHOOK = 0x8001, WM_SETPOWERWATCH = 0x8002, WM_SETDEVICEWATCH = 0x8003;
    private const int WTS_SESSION_LOCK = 7, WTS_SESSION_UNLOCK = 8;
    private const int PBT_APMSUSPEND = 4, PBT_APMRESUMESUSPEND = 7, PBT_APMRESUMEAUTOMATIC = 0x12, PBT_POWERSETTINGCHANGE = 0x8013;
    private static readonly Guid GUID_ACDC_POWER_SOURCE = new("5d3e9a59-e9D5-4b00-a6bd-ff34ff516548");
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x00000080;

    private readonly WndProc _proc;
    private readonly WinEventProc _winEvent;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private IntPtr _hwnd;
    private IntPtr _foregroundHook;
    private long _lastResume;
    private IntPtr _powerNotify;
    private IntPtr _deviceNotify;
    private int _onBattery = -1; // unknown until Windows tells the current state

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

    /// <summary>A drive letter appeared (true) or went away (false): USB sticks, disks, cards, discs.</summary>
    public event Action<char, bool>? DriveChanged;

    /// <summary>Some device (controller, keyboard, USB gadget, Bluetooth…) was connected or disconnected.</summary>
    public event Action? DevicesChanged;

    /// <summary>Starts or stops the notices of every device that comes and goes (drives arrive anyway).</summary>
    public void WatchDevices(bool on)
    {
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_SETDEVICEWATCH, on ? 1 : 0, IntPtr.Zero);
    }

    /// <summary>The PC went on battery (true) or on mains power (false).</summary>
    public event Action<bool>? PowerSourceChanged;

    /// <summary>Starts or stops watching battery / mains power.</summary>
    public void WatchPower(bool on)
    {
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_SETPOWERWATCH, on ? 1 : 0, IntPtr.Zero);
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
        if (_powerNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_powerNotify);
        if (_deviceNotify != IntPtr.Zero) UnregisterDeviceNotification(_deviceNotify);
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

                case WM_SETPOWERWATCH:
                    if (wParam != 0 && _powerNotify == IntPtr.Zero)
                    {
                        var guid = GUID_ACDC_POWER_SOURCE;
                        _powerNotify = RegisterPowerSettingNotification(hwnd, ref guid, 0);
                    }
                    else if (wParam == 0 && _powerNotify != IntPtr.Zero)
                    {
                        UnregisterPowerSettingNotification(_powerNotify);
                        _powerNotify = IntPtr.Zero;
                        _onBattery = -1;
                    }
                    return IntPtr.Zero;

                case WM_DEVICECHANGE when (int)wParam is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE && lParam != IntPtr.Zero:
                    // DEV_BROADCAST_HDR { size, devicetype, reserved } then, for a volume, the mask of its letters.
                    if (Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_DEVICEINTERFACE) DevicesChanged?.Invoke();
                    if (Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_VOLUME)
                    {
                        var mask = (uint)Marshal.ReadInt32(lParam, 12);
                        for (var i = 0; i < 26; i++)
                            if ((mask & (1u << i)) != 0) DriveChanged?.Invoke((char)('A' + i), (int)wParam == DBT_DEVICEARRIVAL);
                    }
                    return 1;

                case WM_SETDEVICEWATCH:
                    if (wParam != 0 && _deviceNotify == IntPtr.Zero)
                    {
                        // DEV_BROADCAST_DEVICEINTERFACE with no class: every interface (DEVICE_NOTIFY_ALL_INTERFACE_CLASSES).
                        var filter = Marshal.AllocHGlobal(32);
                        try
                        {
                            for (var i = 0; i < 32; i += 4) Marshal.WriteInt32(filter, i, 0);
                            Marshal.WriteInt32(filter, 0, 32);
                            Marshal.WriteInt32(filter, 4, DBT_DEVTYP_DEVICEINTERFACE);
                            _deviceNotify = RegisterDeviceNotification(hwnd, filter, DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);
                        }
                        finally { Marshal.FreeHGlobal(filter); }
                    }
                    else if (wParam == 0 && _deviceNotify != IntPtr.Zero)
                    {
                        UnregisterDeviceNotification(_deviceNotify);
                        _deviceNotify = IntPtr.Zero;
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
                        case PBT_POWERSETTINGCHANGE when lParam != IntPtr.Zero:
                            // POWERBROADCAST_SETTING: GUID, DataLength, then a DWORD (0 mains, 1 battery, 2 UPS).
                            if (Marshal.PtrToStructure<Guid>(lParam) != GUID_ACDC_POWER_SOURCE) break;
                            var battery = Marshal.ReadInt32(lParam, 20) != 0 ? 1 : 0;
                            var before = Interlocked.Exchange(ref _onBattery, battery);
                            // The first notice right after registering is just the current state.
                            if (before != -1 && before != battery) PowerSourceChanged?.Invoke(battery == 1);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr filter, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
}
