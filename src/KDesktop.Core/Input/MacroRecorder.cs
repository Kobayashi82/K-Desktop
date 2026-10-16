using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

/// <summary>Receives the raw input while a macro is being recorded (see <see cref="HotkeyRouter.SetRecorder"/>).</summary>
public interface IInputRecorder
{
    /// <summary>Called on the hook thread. Return true to swallow the event.</summary>
    bool OnKey(KeyboardInput e);

    /// <summary>Called on the hook thread. Return true to swallow the event.</summary>
    bool OnMouse(MouseInput e);
}

/// <summary>
/// Records keyboard and mouse events with their timing. Runs on the hook thread (it only appends to a list).
/// SHIFT + Escape stops the recording; clicks on K-Desktop's own windows (the recording bar) are not recorded.
/// In game mode the mouse movement is recorded as the device reports it (relative, raw: <see cref="RawMouseListener"/>)
/// instead of pointer positions, which mean nothing while a game keeps the pointer locked.
/// </summary>
public sealed class MacroRecorder : IInputRecorder, IDisposable
{
    /// <summary>Consecutive mouse movements closer than this (ms) are merged into one step.</summary>
    private const int MoveInterval = 15;

    private readonly List<MacroStep> _steps = [];
    private readonly HashSet<MouseButton> _ignoredButtons = [];
    private readonly bool _recordMoves;
    private readonly RawMouseListener? _raw;
    private long _last = -1;
    private long _lastMove;
    private bool _shiftDown;
    private bool _swallowEscapeUp;
    private volatile bool _stopped;
    private int _count;

    public MacroRecorder(bool recordMoves, bool gameMode = false)
    {
        _recordMoves = recordMoves && !gameMode;
        if (recordMoves && gameMode)
        {
            _raw = new RawMouseListener();
            _raw.Moved += OnRawMove;
        }
    }

    /// <summary>While paused nothing is recorded (and the paused time does not count).</summary>
    public bool Paused { get; set; }

    /// <summary>Number of recorded steps (readable from any thread).</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Raised (on a worker thread) when SHIFT + Escape is pressed.</summary>
    public event Action? StopRequested;

    /// <summary>Raised (on the hook thread) after each recorded step; keep it cheap.</summary>
    public event Action? StepRecorded;

    public bool OnKey(KeyboardInput e)
    {
        if (e.Vk is KeyNames.VK_LSHIFT or KeyNames.VK_RSHIFT or KeyNames.VK_SHIFT) _shiftDown = e.IsDown;

        if (e.Vk == KeyNames.VK_ESCAPE)
        {
            if (e.IsDown && _shiftDown)
            {
                _swallowEscapeUp = true;
                RequestStop();
                return true;
            }
            if (!e.IsDown && _swallowEscapeUp)
            {
                _swallowEscapeUp = false;
                return true;
            }
        }
        if (_stopped || Paused) return false;

        Add(new MacroStep
        {
            Kind = e.IsDown ? MacroStepKind.KeyDown : MacroStepKind.KeyUp,
            Vk = e.Vk,
            Extended = e.Extended,
        });
        return false;
    }

    public bool OnMouse(MouseInput e)
    {
        if (_stopped) return false;

        // Clicks on our own windows (the recording bar) are not part of the macro.
        if (e.Message == MouseMessage.Down && IsOwnWindow(e.X, e.Y))
        {
            _ignoredButtons.Add(e.Button);
            return false;
        }
        if (e.Message == MouseMessage.Up && _ignoredButtons.Remove(e.Button)) return false;
        if (Paused) return false;

        switch (e.Message)
        {
            case MouseMessage.Move:
                if (!_recordMoves) return false;
                var now = Environment.TickCount64;
                lock (_steps)
                {
                    if (_steps.Count > 0 && _steps[^1].Kind == MacroStepKind.MouseMove && now - _lastMove < MoveInterval)
                    {
                        _steps[^1].X = e.X;
                        _steps[^1].Y = e.Y;
                        return false;
                    }
                }
                _lastMove = now;
                Add(new MacroStep { Kind = MacroStepKind.MouseMove, X = e.X, Y = e.Y });
                break;
            case MouseMessage.Down or MouseMessage.Up:
                Add(new MacroStep
                {
                    Kind = e.Message == MouseMessage.Down ? MacroStepKind.MouseDown : MacroStepKind.MouseUp,
                    Button = e.Button,
                    X = e.X,
                    Y = e.Y,
                });
                break;
            case MouseMessage.Wheel or MouseMessage.HWheel:
                Add(new MacroStep { Kind = MacroStepKind.Wheel, Wheel = e.WheelDelta, Horizontal = e.Message == MouseMessage.HWheel });
                break;
        }
        return false;
    }

    /// <summary>Game mode: relative movements, merged like the absolute ones (their offsets add up).</summary>
    private void OnRawMove(int dx, int dy)
    {
        if (_stopped || Paused) return;
        var now = Environment.TickCount64;
        lock (_steps)
        {
            if (_steps.Count > 0 && _steps[^1] is { Kind: MacroStepKind.MouseMoveBy, Raw: true } last && now - _lastMove < MoveInterval)
            {
                last.X += dx;
                last.Y += dy;
                return;
            }
        }
        _lastMove = now;
        Add(new MacroStep { Kind = MacroStepKind.MouseMoveBy, X = dx, Y = dy, Raw = true });
    }

    /// <summary>Restarts the clock after a pause so the paused time is not recorded as a delay.</summary>
    public void Resume()
    {
        Paused = false;
        lock (_steps) _last = _steps.Count == 0 ? -1 : Environment.TickCount64;
    }

    public void RequestStop()
    {
        if (_stopped) return;
        _stopped = true;
        ThreadPool.QueueUserWorkItem(_ => StopRequested?.Invoke());
    }

    /// <summary>Returns the recorded steps, without the keys that were still held at the end (the stop combination).</summary>
    public List<MacroStep> Finish()
    {
        _stopped = true;
        Dispose();
        List<MacroStep> steps;
        lock (_steps) steps = [.. _steps];

        // Remove trailing key-downs whose key-up never came (e.g. the SHIFT of SHIFT + Escape).
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var s = steps[i];
            if (s.Kind == MacroStepKind.KeyDown && !steps.Skip(i + 1).Any(x => x.Kind == MacroStepKind.KeyUp && x.Vk == s.Vk))
            {
                steps.RemoveAt(i);
                continue;
            }
            if (s.Kind is MacroStepKind.KeyDown or MacroStepKind.KeyUp && KeyNames.IsModifierVk(s.Vk)) continue;
            break;
        }
        // Key-ups at the start whose key-down happened before recording.
        while (steps.Count > 0 && steps[0].Kind == MacroStepKind.KeyUp) steps.RemoveAt(0);
        if (steps.Count > 0) steps[0].Delay = 0;
        return steps;
    }

    public void Dispose() => _raw?.Dispose();

    private void Add(MacroStep step)
    {
        var now = Environment.TickCount64;
        lock (_steps)
        {
            step.Delay = _last < 0 ? 0 : (int)Math.Min(int.MaxValue, now - _last);
            _last = now;
            _steps.Add(step);
            _count = _steps.Count;
        }
        StepRecorded?.Invoke();
    }

    private static bool IsOwnWindow(int x, int y)
    {
        var hwnd = Win32.WindowFromPoint(new Win32.POINT { X = x, Y = y });
        if (hwnd == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }
}
