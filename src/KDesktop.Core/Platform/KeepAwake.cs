using System.Runtime.InteropServices;
using KDesktop.Core.Input;

namespace KDesktop.Core.Platform;

/// <summary>
/// «Mantener despierto» (5.0.342): the PC does not go to sleep (and, if asked, the screen does not turn off) for some
/// minutes, while a program is open, or until it is turned off. With a power request of Windows (it shows in
/// <c>powercfg /requests</c> as K-Desktop's), not tied to a thread like <c>SetThreadExecutionState</c>. Not kept between
/// runs: closing K-Desktop lets the PC sleep again. Thread-safe.
/// </summary>
public sealed class KeepAwake : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private IntPtr _request;
    private bool _screenHeld;

    public KeepAwake() => _timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);

    public bool IsOn { get; private set; }

    /// <summary>When it ends by itself (null: until it is turned off, or while <see cref="WhileApp"/> is open).</summary>
    public DateTime? Until { get; private set; }

    /// <summary>The program (normalised name) it waits for to close; null: none.</summary>
    public string? WhileApp { get; private set; }

    /// <summary>The screen stays on too (not only the PC awake).</summary>
    public bool Screen { get; private set; }

    /// <summary>It turned on or off, or what it waits for changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>
    /// Keeps the PC awake, replacing what it was doing: for <paramref name="duration"/>, while <paramref name="app"/> is
    /// open, or (both null) until <see cref="Stop"/>. False when Windows refused it.
    /// </summary>
    public bool Start(TimeSpan? duration, string? app, bool screen)
    {
        lock (_gate)
        {
            if (!EnsureRequest()) return false;
            if (!PowerSetRequest(_request, PowerRequestSystemRequired))
            {
                Log.Warn($"Mantener despierto: Windows no lo ha aceptado (error {Marshal.GetLastPInvokeError()})");
                return false;
            }
            if (screen != _screenHeld)
            {
                if (screen) PowerSetRequest(_request, PowerRequestDisplayRequired);
                else PowerClearRequest(_request, PowerRequestDisplayRequired);
                _screenHeld = screen;
            }
            IsOn = true;
            Screen = screen;
            Until = duration is { } d ? DateTime.Now + d : null;
            WhileApp = string.IsNullOrWhiteSpace(app) ? null : ProcessNames.Normalize(app);
            _timer.Change(CheckEvery, CheckEvery);
        }
        Log.Info($"Mantener despierto: sí{(Until is { } until ? $" hasta las {until:HH:mm}" : "")}{(WhileApp is { } w ? $" mientras {w} esté abierto" : "")}{(screen ? " (con la pantalla)" : "")}");
        Changed?.Invoke();
        return true;
    }

    /// <summary>Some more time (from the tray): from when it would end, or from now when it had no end.</summary>
    public void Extend(TimeSpan more)
    {
        lock (_gate)
        {
            if (!IsOn) return;
            Until = (Until ?? DateTime.Now) + more;
            WhileApp = null;
        }
        Log.Info($"Mantener despierto: hasta las {Until:HH:mm}");
        Changed?.Invoke();
    }

    /// <summary>Lets the PC sleep again. False when it was not on.</summary>
    public bool Stop()
    {
        lock (_gate)
        {
            if (!IsOn) return false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            if (_request != IntPtr.Zero)
            {
                PowerClearRequest(_request, PowerRequestSystemRequired);
                if (_screenHeld) PowerClearRequest(_request, PowerRequestDisplayRequired);
            }
            _screenHeld = false;
            IsOn = false;
            Until = null;
            WhileApp = null;
        }
        Log.Info("Mantener despierto: no");
        Changed?.Invoke();
        return true;
    }

    /// <summary>Its time is up or its program closed: it ends.</summary>
    private void Check()
    {
        bool over;
        lock (_gate)
        {
            if (!IsOn) return;
            over = Until is { } until && DateTime.Now >= until;
            if (!over && WhileApp is { } app) over = !ProcessSnapshot.IsRunning(app);
        }
        if (over) Stop();
    }

    private bool EnsureRequest()
    {
        if (_request != IntPtr.Zero) return true;
        var reason = Marshal.StringToHGlobalUni("K-Desktop: mantener despierto");
        try
        {
            var context = new REASON_CONTEXT { Version = 0, Flags = PowerRequestContextSimpleString, SimpleReasonString = reason };
            var handle = PowerCreateRequest(ref context);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                Log.Warn($"Mantener despierto: no se pudo pedir a Windows (error {Marshal.GetLastPInvokeError()})");
                return false;
            }
            _request = handle;
            return true;
        }
        finally { Marshal.FreeHGlobal(reason); }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        lock (_gate)
        {
            if (_request != IntPtr.Zero) CloseHandle(_request);
            _request = IntPtr.Zero;
        }
    }

    private const int PowerRequestDisplayRequired = 0;
    private const int PowerRequestSystemRequired = 1;
    private const uint PowerRequestContextSimpleString = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
        // The rest of the union («Detailed»), unused with a simple string, so Windows never reads past the struct.
        public IntPtr Padding1;
        public IntPtr Padding2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(IntPtr request, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
