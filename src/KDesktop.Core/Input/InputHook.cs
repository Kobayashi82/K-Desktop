using System.Runtime.InteropServices;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

public enum MouseButton { None, Left, Right, Middle, X1, X2 }
public enum MouseMessage { Move, Down, Up, Wheel, HWheel }

public readonly record struct KeyboardInput(int Vk, bool IsDown, bool Extended, bool Injected);
public readonly record struct MouseInput(MouseMessage Message, MouseButton Button, int WheelDelta, int X, int Y, bool Injected);

/// <summary>
/// Low-level keyboard and mouse hooks running on a dedicated thread with its own message loop,
/// so a busy UI thread can never delay input (Windows silently removes hooks that time out).
/// Callbacks run on the hook thread and must be fast; they return true to swallow the event.
/// </summary>
public sealed class InputHook : IDisposable
{
    private const uint WM_REINSTALL = Win32.WM_USER + 1;

    private readonly Win32.LowLevelProc _keyboardProc;
    private readonly Win32.LowLevelProc _mouseProc;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _keyboardHook, _mouseHook;

    public Func<KeyboardInput, bool>? KeyboardHandler { get; set; }
    public Func<MouseInput, bool>? MouseHandler { get; set; }

    /// <summary>When false, mouse-move events are not forwarded (saves work on every pixel moved).</summary>
    public bool ForwardMouseMove { get; set; }

    public bool IsRunning => _thread is { IsAlive: true };

    public InputHook()
    {
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    public void Start()
    {
        if (IsRunning) return;
        _ready.Reset();
        _thread = new Thread(Run) { IsBackground = true, Name = "K-Desktop input hook", Priority = ThreadPriority.Highest };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Removes and re-installs both hooks. Keeps K-Desktop first in the hook chain and recovers
    /// from the hooks being silently dropped by Windows.
    /// </summary>
    public void Reinstall()
    {
        if (_threadId != 0) Win32.PostThreadMessage(_threadId, WM_REINSTALL, IntPtr.Zero, IntPtr.Zero);
    }

    public void Stop()
    {
        if (_threadId != 0) Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(2000);
        _thread = null;
        _threadId = 0;
    }

    private void Run()
    {
        _threadId = Win32.GetCurrentThreadId();
        Install();
        _ready.Set();
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_REINSTALL)
            {
                Uninstall();
                Install();
                continue;
            }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        Uninstall();
    }

    private void Install()
    {
        var module = Win32.GetModuleHandle(null);
        _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            Log.Error($"No se pudieron instalar los hooks (error {Marshal.GetLastWin32Error()})");
    }

    private void Uninstall()
    {
        if (_keyboardHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && KeyboardHandler is { } handler)
        {
            try
            {
                var data = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                var msg = (int)wParam;
                var isDown = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                var input = new KeyboardInput(
                    (int)data.vkCode,
                    isDown,
                    (data.flags & Win32.LLKHF_EXTENDED) != 0,
                    (data.flags & Win32.LLKHF_INJECTED) != 0);
                if (handler(input)) return 1;
            }
            catch (Exception ex)
            {
                Log.Error("Error en el hook de teclado", ex);
            }
        }
        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && MouseHandler is { } handler)
        {
            var msg = (int)wParam;
            if (msg == Win32.WM_MOUSEMOVE && !ForwardMouseMove)
                return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            try
            {
                var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                var injected = (data.flags & Win32.LLMHF_INJECTED) != 0;
                var high = (short)(data.mouseData >> 16);
                MouseInput? input = msg switch
                {
                    Win32.WM_MOUSEMOVE => new MouseInput(MouseMessage.Move, MouseButton.None, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_LBUTTONDOWN => new MouseInput(MouseMessage.Down, MouseButton.Left, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_LBUTTONUP => new MouseInput(MouseMessage.Up, MouseButton.Left, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_RBUTTONDOWN => new MouseInput(MouseMessage.Down, MouseButton.Right, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_RBUTTONUP => new MouseInput(MouseMessage.Up, MouseButton.Right, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_MBUTTONDOWN => new MouseInput(MouseMessage.Down, MouseButton.Middle, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_MBUTTONUP => new MouseInput(MouseMessage.Up, MouseButton.Middle, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_XBUTTONDOWN => new MouseInput(MouseMessage.Down, high == 1 ? MouseButton.X1 : MouseButton.X2, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_XBUTTONUP => new MouseInput(MouseMessage.Up, high == 1 ? MouseButton.X1 : MouseButton.X2, 0, data.pt.X, data.pt.Y, injected),
                    Win32.WM_MOUSEWHEEL => new MouseInput(MouseMessage.Wheel, MouseButton.None, high, data.pt.X, data.pt.Y, injected),
                    Win32.WM_MOUSEHWHEEL => new MouseInput(MouseMessage.HWheel, MouseButton.None, high, data.pt.X, data.pt.Y, injected),
                    _ => null,
                };
                if (input is { } i && handler(i)) return 1;
            }
            catch (Exception ex)
            {
                Log.Error("Error en el hook de ratón", ex);
            }
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }
}
