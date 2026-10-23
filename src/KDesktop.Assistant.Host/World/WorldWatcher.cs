using System.Runtime.InteropServices;
using System.Windows;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Keeps <see cref="HostWorld"/> up to date and raises the <see cref="SystemEvent"/>s (stage 1.5), on the engine
/// thread. Windows are rescanned when a window event says something changed (once per tick at most), every frame
/// while one is being moved or the assistant watches closely, and every 2 s as a safety net. A move is reported
/// once the window settles; a window that is minimized and restored is not «opened» again.
/// </summary>
internal sealed class WorldWatcher : IDisposable
{
    private const double SafetyScan = 2;
    private const double SettleSeconds = 0.3;
    private const double SystemInterval = 1;

    /// <summary>A new window that did not count yet is looked at again after this long.</summary>
    private const double PendingCheck = 0.4;

    private readonly HostWorld _world;
    private readonly Func<double> _clock;
    private readonly Func<IEnumerable<nint>> _ownWindows;
    private readonly Action<SystemEvent> _raise;
    private readonly Action _requestTick;
    private readonly HashSet<nint> _own = [];
    private readonly HashSet<long> _seen = [];
    private readonly Dictionary<long, (PixelRect From, double Since)> _moving = [];
    private readonly Dictionary<nint, double> _pending = [];
    private readonly SystemStatus _status = new();
    private NativeWindow? _events;
    private WinEventWatcher? _winEvents;
    private RecycleBinWatcher? _recycleBin;
    private VirtualDesktopKeeper? _desktops;
    private readonly WindowScanner _scanner;
    private NotificationWatcher? _notifications;
    private Dictionary<long, WindowInfo> _windows = [];
    private List<MonitorInfo> _monitors = [];
    private bool _dirty = true;
    private bool _monitorsDirty = true;
    private bool _dragging;
    private long _activated;
    private double _nextScan;
    private double _nextSystem;
    private Point? _lastCursor;
    private double _lastCursorAt;
    private Vector _velocity;
    private double _idleRaisedAt = -1;
    private double _lastIdle;
    private int _hour = -1;
    private (bool, bool, bool) _power;
    private bool _dark;
    private bool _quiet;
    private HashSet<int> _fullscreen = [];
    private bool _started;

    /// <param name="clock">The engine clock (seconds), the same as <see cref="Update"/>'s.</param>
    public WorldWatcher(HostWorld world, Func<double> clock, Func<IEnumerable<nint>> ownWindows, Action<SystemEvent> raise, Action requestTick)
    {
        _world = world;
        _clock = clock;
        _ownWindows = ownWindows;
        _raise = raise;
        _requestTick = requestTick;
        _scanner = new WindowScanner(_own);
    }

    /// <summary>The scanner (its process cache also serves the window services).</summary>
    public WindowScanner Scanner => _scanner;

    public bool IsStarted => _started;

    /// <summary>K-Desktop is paused (an order of K-Desktop); raised as <see cref="SystemEvent.PauseChanged"/>.</summary>
    public bool Paused { get; private set; }

    /// <summary>A DirectX game or a presentation holds the screen: free the GPU.</summary>
    public bool ExclusiveFullscreen => _status.ExclusiveFullscreen;

    /// <summary>The monitors changed (the overlay must follow).</summary>
    public event Action? MonitorsChanged;

    /// <summary>Starts watching (on the engine thread), with a first picture of the desktop and no events for it.</summary>
    public void Start(double now, Action<Action> post)
    {
        _events = new NativeWindow("KDesktopAssistantEvents", "K-Desktop assistant events", WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)
        {
            MessageHandler = OnMessage,
        };
        RefreshOwn();
        _notifications = new NotificationWatcher(_scanner);
        _winEvents = new WinEventWatcher();
        _winEvents.WindowEvent += OnWindowEvent;
        try
        {
            _recycleBin = new RecycleBinWatcher(_events.Handle, post);
            _recycleBin.Emptied += () =>
            {
                var icon = DesktopIcons.FindRecycleBin();
                Log.Info($"Asistente: papelera vaciada (icono {(icon is { } p ? $"en {p.X:0},{p.Y:0}" : "no visible")})");
                _raise(new SystemEvent.RecycleBinEmptied(icon));
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            Log.Warn($"Asistente: no se puede vigilar la papelera ({ex.Message})");
        }
        _desktops = new VirtualDesktopKeeper();
        Update(now);
        _started = true;
        Log.Info($"Asistente: mundo con {_monitors.Count} monitor(es) y {_windows.Count} ventanas");
    }

    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused;
        PublishSystem();
        if (_started) _raise(new SystemEvent.PauseChanged(paused));
    }

    /// <summary>Brings the world up to date. Returns the seconds until it needs to look again.</summary>
    public double Update(double now)
    {
        var close = _world.CloseWatchUntil > now;
        if (_monitorsDirty) ReadMonitors();
        var pending = CheckPending(now);
        if (_dirty || _dragging || close || now >= _nextScan) Rescan(now);
        SettleMoves(now);
        if (now >= _nextSystem) UpdateSystem(now);
        else UpdateCursor(now);
        _recycleBin?.Update(now);

        var wait = Math.Min(Math.Min(_nextScan, _nextSystem), pending) - now;
        if (_dragging || close || _moving.Count > 0) wait = Math.Min(wait, 1.0 / 60);
        if (_recycleBin is { } bin) wait = Math.Min(wait, bin.CountIn(now));
        return Math.Max(wait, 0);
    }

    public void Dispose()
    {
        _winEvents?.Dispose();
        _recycleBin?.Dispose();
        _desktops?.Dispose();
        _events?.Dispose();
    }

    // ── windows ──────────────────────────────────────────────────────────

    /// <summary>
    /// Most events are of windows that change nothing (child windows, tooltips and menus, overlays that report moves
    /// without moving): only the ones that may change the picture rescan it (as the Lander does). Each rescan goes
    /// through every window, and these events come by the dozen per second while the PC is in use.
    /// </summary>
    private void OnWindowEvent(uint type, nint hwnd)
    {
        if (_own.Contains(hwnd)) return;
        var known = _windows.TryGetValue(hwnd, out var window);
        if (type == EVENT_OBJECT_DESTROY && !known)
        {
            // A minimized one (or on another desktop) is not in the picture: reported here.
            _notifications?.OnWindowEvent(type, hwnd, _clock(), _monitors);
            if (_seen.Remove(hwnd)) _raise(new SystemEvent.WindowClosed(hwnd));
            return;
        }
        if (!known && GetAncestor(hwnd, GA_ROOT) != hwnd) return;

        var now = _clock();
        if (_notifications?.OnWindowEvent(type, hwnd, now, _monitors) is { } toast) _raise(new SystemEvent.NotificationShown(toast));
        switch (type)
        {
            case EVENT_OBJECT_LOCATIONCHANGE:
                if (!known || (WindowScanner.TryGetBounds(hwnd, out var bounds) && bounds.ToPixelRect() == window!.Bounds)) return;
                break;
            case EVENT_OBJECT_HIDE or EVENT_OBJECT_CLOAKED or EVENT_SYSTEM_MINIMIZESTART or EVENT_OBJECT_DESTROY:
                if (!known) return;
                break;
            case EVENT_OBJECT_SHOW or EVENT_OBJECT_UNCLOAKED or EVENT_SYSTEM_MINIMIZEEND when !known:
                // A new one counts if it would be in the picture; some get their size or title a moment later.
                if (!_scanner.TryDescribe(hwnd, out _))
                {
                    _pending.TryAdd(hwnd, now + PendingCheck);
                    _requestTick();
                    return;
                }
                break;
        }
        switch (type)
        {
            case EVENT_SYSTEM_MOVESIZESTART:
                _dragging = true;
                break;
            case EVENT_SYSTEM_MOVESIZEEND:
                _dragging = false;
                if (_moving.TryGetValue(hwnd, out var move)) _moving[hwnd] = move with { Since = double.NegativeInfinity };
                break;
            case EVENT_SYSTEM_FOREGROUND:
                _activated = hwnd;
                _status.InvalidateFullscreen();
                _nextSystem = 0;
                // Our windows show on every desktop; just in case a Windows version leaves them behind.
                _desktops?.Follow([.. _own], hwnd);
                break;
        }
        _dirty = true;
        _requestTick();
    }

    /// <summary>Looks again at the new windows that did not count when shown. Returns when the next one is due.</summary>
    private double CheckPending(double now)
    {
        var next = double.PositiveInfinity;
        if (_pending.Count == 0) return next;
        foreach (var (hwnd, due) in _pending.ToList())
        {
            if (due > now)
            {
                next = Math.Min(next, due);
                continue;
            }
            _pending.Remove(hwnd);
            if (!_windows.ContainsKey(hwnd) && IsWindow(hwnd) && _scanner.TryDescribe(hwnd, out _)) _dirty = true;
        }
        return next;
    }

    private void Rescan(double now)
    {
        _dirty = false;
        _nextScan = now + SafetyScan;
        RefreshOwn();
        var list = _scanner.Scan();
        var previous = _windows;
        var current = new Dictionary<long, WindowInfo>(list.Count);
        foreach (var w in list) current[w.Handle] = w;
        _windows = current;

        // Only a picture that changed is news: rescanning every frame (an assistant standing on a window) would wake the
        // assistants every frame for nothing.
        var taskbars = Monitors.Taskbars(_monitors);
        var shown = _world.Current;
        if (!shown.Windows.SequenceEqual(list) || !shown.Monitors.SequenceEqual(_monitors) || !shown.Taskbars.SequenceEqual(taskbars))
            _world.Publish(new WorldSnapshot(_monitors, list, taskbars, now));
        if (!_started)
        {
            foreach (var handle in current.Keys) _seen.Add(handle);
            return;
        }

        foreach (var (handle, window) in current)
        {
            if (_seen.Add(handle)) _raise(new SystemEvent.WindowOpened(window));
            else if (previous.TryGetValue(handle, out var before) && before.Bounds != window.Bounds)
            {
                // Remember where it started; reported when it stops.
                var from = _moving.TryGetValue(handle, out var move) ? move.From : before.Bounds;
                _moving[handle] = (from, now);
            }
        }
        // Every one seen that no longer exists is closed: also one that went (hidden first) between two looks, whose
        // destruction came before or without this rescan.
        foreach (var handle in _seen.Where(h => !current.ContainsKey(h) && !IsWindow((nint)h)).ToList())
            if (_seen.Remove(handle)) _raise(new SystemEvent.WindowClosed(handle));

        if (_activated != 0 && current.TryGetValue(_activated, out var active))
        {
            _activated = 0;
            _raise(new SystemEvent.WindowActivated(active));
        }
        else if (_activated != 0 && !_windows.ContainsKey(_activated)) _activated = 0;
    }

    private void SettleMoves(double now)
    {
        if (_moving.Count == 0) return;
        foreach (var (handle, (from, since)) in _moving.ToList())
        {
            if (!_windows.TryGetValue(handle, out var window))
            {
                _moving.Remove(handle);
                continue;
            }
            if (now - since < SettleSeconds || (_dragging && since > double.NegativeInfinity)) continue;
            _moving.Remove(handle);
            if (window.Bounds != from) _raise(new SystemEvent.WindowMoved(window, from));
        }
    }

    private void ReadMonitors()
    {
        _monitorsDirty = false;
        var monitors = Monitors.Read();
        var changed = monitors.Count != _monitors.Count || monitors.Zip(_monitors).Any(p => p.First != p.Second);
        _monitors = monitors;
        _dirty = true;
        if (!changed || !_started) return;
        MonitorsChanged?.Invoke();
        _raise(new SystemEvent.MonitorsChanged(monitors));
    }

    private void RefreshOwn()
    {
        _own.Clear();
        foreach (var handle in _ownWindows()) _own.Add(handle);
        if (_events is not null) _own.Add(_events.Handle);
    }

    // ── the system ───────────────────────────────────────────────────────

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case RecycleBinWatcher.Message:
                _recycleBin?.OnMessage(wParam, lParam, _clock());
                _requestTick();
                return 0;
            case WM_SETTINGCHANGE:
                if (lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") _status.InvalidateTheme();
                _monitorsDirty = true; // the work area (taskbar) may have changed too
                _nextSystem = 0;
                _requestTick();
                return null;
            case WM_DISPLAYCHANGE:
                _monitorsDirty = true;
                _requestTick();
                return null;
            case WM_POWERBROADCAST:
                _status.InvalidatePower();
                _nextSystem = 0;
                _requestTick();
                return null;
        }
        return null;
    }

    private void UpdateSystem(double now)
    {
        _nextSystem = now + SystemInterval;
        _status.Update(now, _monitors, _own);

        if (_started)
        {
            var power = (_status.OnBattery, _status.BatteryLow, _status.PowerSaver);
            if (power != _power) _raise(new SystemEvent.PowerChanged(power.OnBattery, power.BatteryLow, power.PowerSaver));
            if (_status.DarkMode != _dark) _raise(new SystemEvent.ThemeChanged(_status.DarkMode));
            if (_status.QuietMode != _quiet) _raise(new SystemEvent.QuietModeChanged(_status.QuietMode));
            if (!_fullscreen.SetEquals(_status.FullscreenMonitors)) _raise(new SystemEvent.FullscreenChanged(new HashSet<int>(_status.FullscreenMonitors)));
            var hour = DateTime.Now.Hour;
            if (_hour >= 0 && hour != _hour) _raise(new SystemEvent.HourStruck(hour));
            _hour = hour;
        }
        else _hour = DateTime.Now.Hour;
        _power = (_status.OnBattery, _status.BatteryLow, _status.PowerSaver);
        _dark = _status.DarkMode;
        _quiet = _status.QuietMode;
        _fullscreen = [.. _status.FullscreenMonitors];

        // Idle: at 60 s, then every 5 minutes; back as soon as there is input.
        var idle = SystemStatus.UserIdleSeconds();
        if (_idleRaisedAt >= 0 && idle < _lastIdle)
        {
            _raise(new SystemEvent.UserReturned(_lastIdle));
            _idleRaisedAt = -1;
        }
        if (idle >= (_idleRaisedAt < 0 ? 60 : _idleRaisedAt + 300))
        {
            _idleRaisedAt = idle;
            _raise(new SystemEvent.UserIdle(idle));
        }
        _lastIdle = idle;
        UpdateCursor(now);
    }

    private void UpdateCursor(double now)
    {
        Point? cursor = GetCursorPos(out var p) ? new Point(p.X, p.Y) : null;
        if (cursor is { } c && _lastCursor is { } last && now > _lastCursorAt)
        {
            var dt = now - _lastCursorAt;
            var instant = (c - last) / dt;
            var k = Math.Min(1, dt / 0.15); // smoothed over ~150 ms
            _velocity += (instant - _velocity) * k;
        }
        _lastCursor = cursor;
        _lastCursorAt = now;
        PublishSystem();
    }

    private void PublishSystem() => _world.Publish(new SystemState
    {
        Cursor = _lastCursor,
        CursorVelocity = _velocity,
        UserIdleSeconds = SystemStatus.UserIdleSeconds(),
        ActiveWindow = GetForegroundWindow(),
        DarkMode = _status.DarkMode,
        BatteryLow = _status.BatteryLow,
        OnBattery = _status.OnBattery,
        PowerSaver = _status.PowerSaver,
        QuietMode = _status.QuietMode,
        FullscreenMonitors = new HashSet<int>(_status.FullscreenMonitors),
        Paused = Paused,
        LocalTime = DateTime.Now,
    });
}
