using System.Runtime.InteropServices;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Listens (out of process, injecting nothing) to window events: they appear, move, get minimized, close or change
/// virtual desktop (from Kobii). They arrive through the message loop of the thread that creates the watcher (the
/// engine's).
/// </summary>
internal sealed unsafe class WinEventWatcher : IDisposable
{
    [ThreadStatic]
    private static WinEventWatcher? _current;

    private readonly List<nint> _hooks = [];

    public WinEventWatcher()
    {
        _current = this;
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND);
        Hook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE);
        Hook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);
    }

    /// <summary>An event of a window (event type, window).</summary>
    public event Action<uint, nint>? WindowEvent;

    public void Dispose()
    {
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
        if (_current == this) _current = null;
    }

    private void Hook(uint min, uint max)
    {
        var hook = SetWinEventHook(min, max, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook != 0) _hooks.Add(hook);
        else Log.Warn($"Asistente: SetWinEventHook falló para 0x{min:X4}-0x{max:X4}");
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == 0 || idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
        try { _current?.WindowEvent?.Invoke(eventType, hwnd); }
        catch (Exception ex) { Log.Error("Asistente: error con un evento de ventana", ex); }
    }
}
