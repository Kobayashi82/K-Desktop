using System.Runtime.InteropServices;
using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Osd;

namespace KDesktop.Services;

/// <summary>
/// «Encajar arrastrando» (E1b): while a window is dragged by its title bar, Shift shows the zones of every monitor with a
/// disposición put (<see cref="ZoneSnapping.ActiveZones"/>) and dropping it puts it into the zone under the cursor. Letting
/// Shift go hides them (the window stays where it is dropped, as always); Esc cancels. It listens to Windows' move/size
/// events, so it costs nothing while no window is dragged; resizing by an edge never snaps.
/// </summary>
public sealed class ZoneSnapper : IDisposable
{
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B, WINEVENT_OUTOFCONTEXT = 0;
    private const int VK_SHIFT = 0x10, VK_ESCAPE = 0x1B;

    private readonly Func<AppConfig> _config;
    private readonly WinEventProc _proc;
    private readonly DispatcherTimer _timer;
    private IntPtr _hook;
    private IntPtr _dragged;
    private IReadOnlyList<SnapZone> _zones = [];
    private List<(ScreenMonitor Monitor, ZoneOverlay Overlay)>? _overlays;
    private SnapZone? _target;

    public ZoneSnapper(Func<AppConfig> config)
    {
        _config = config;
        _proc = OnEvent; // kept: Windows calls it for as long as the hook lives
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Follow();
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero) Log.Warn("Encajar en zonas: no se pudo escuchar el arrastre de ventanas");
    }

    private void OnEvent(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero) return;
        if (type == EVENT_SYSTEM_MOVESIZESTART) Begin(hwnd);
        else if (type == EVENT_SYSTEM_MOVESIZEEND && hwnd == _dragged) End();
    }

    private void Begin(IntPtr hwnd)
    {
        End();
        var config = _config();
        if (!config.Settings.SnapWithShift || IsResize(hwnd)) return;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return;
        _zones = ZoneSnapping.ActiveZones(config, WindowArrangements.Monitors());
        if (_zones.Count == 0) return;
        _dragged = hwnd;
        _timer.Start();
    }

    /// <summary>The drag began on an edge or a corner (resizing), not on the title bar.</summary>
    private static bool IsResize(IntPtr hwnd)
    {
        GetCursorPos(out var p);
        var lParam = new IntPtr((p.Y << 16) | (p.X & 0xFFFF));
        if (SendMessageTimeout(hwnd, 0x0084 /* WM_NCHITTEST */, IntPtr.Zero, lParam, 0x0002 /* SMTO_ABORTIFHUNG */, 50, out var hit) == IntPtr.Zero) return false;
        return hit.ToInt64() is >= 10 and <= 17; // HTLEFT … HTBOTTOMRIGHT
    }

    private void Follow()
    {
        if (_dragged == IntPtr.Zero || !IsWindow(_dragged))
        {
            End();
            return;
        }
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0)
        {
            HideZones();
            return;
        }
        GetCursorPos(out var p);
        _target = ZoneSnapping.At(_zones, p.X, p.Y);
        if (_overlays is null)
        {
            _overlays = [];
            foreach (var group in _zones.GroupBy(z => z.Monitor))
            {
                var overlay = new ZoneOverlay(group.Key.WorkArea, [.. group]);
                _overlays.Add((group.Key, overlay));
                overlay.Show();
            }
        }
        foreach (var (monitor, overlay) in _overlays) overlay.Mark(_target is { } t && ReferenceEquals(t.Monitor, monitor) ? t : null);
    }

    private void End()
    {
        _timer.Stop();
        var (hwnd, target) = (_dragged, _overlays is null ? null : _target);
        var cancelled = (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
        HideZones();
        _dragged = IntPtr.Zero;
        _zones = [];
        if (hwnd == IntPtr.Zero || target is null || cancelled) return;
        // Once Windows has let the window go, or it would put it back where the drag left it.
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!IsWindow(hwnd)) return;
            ZoneSnapping.Put(hwnd, target);
            Log.Info($"Encajada en la zona {target.Number} de {target.Monitor.Name}");
        });
    }

    private void HideZones()
    {
        if (_overlays is null) return;
        foreach (var (_, overlay) in _overlays) overlay.Close();
        _overlays = null;
        _target = null;
    }

    public void Dispose()
    {
        End();
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private delegate void WinEventProc(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
