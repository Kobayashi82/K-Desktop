using System.Runtime.InteropServices;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

/// <summary>
/// The mouse movement as the device reports it (Raw Input), on its own thread with a message-only window. Unlike the
/// pointer position, it keeps counting when a game locks the pointer in the middle of the screen, and it is not
/// affected by the Windows pointer speed or acceleration: played back with <see cref="InputSimulator.MoveRaw"/>,
/// a game sees the same movement. Only physical mice count (injected and absolute devices are ignored).
/// </summary>
public sealed class RawMouseListener : IDisposable
{
    private const uint WM_INPUT = 0x00FF, WM_CLOSE = 0x0010;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIDEV_REMOVE = 0x0001, RIDEV_INPUTSINK = 0x0100;
    private const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly WndProc _proc;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private IntPtr _hwnd;
    private IntPtr _buffer;
    private int _bufferSize;

    /// <summary>Raised on the listener's thread with each movement (device counts); keep it cheap.</summary>
    public event Action<int, int>? Moved;

    public RawMouseListener()
    {
        _proc = OnMessage;
        _thread = new Thread(Run) { IsBackground = true, Name = "K-Desktop raw mouse", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        var module = Win32.GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = module,
            lpszClassName = "KDesktopRawMouse",
        };
        RegisterClassEx(ref wc); // fails harmlessly when the class is already registered
        _hwnd = CreateWindowEx(0, wc.lpszClassName, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, module, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero || !Register(RIDEV_INPUTSINK, _hwnd))
        {
            Log.Error($"No se pudo leer el movimiento del ratón (error {Marshal.GetLastWin32Error()})");
            _ready.Set();
            return;
        }
        _ready.Set();
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        Register(RIDEV_REMOVE, IntPtr.Zero);
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
    }

    private static bool Register(uint flags, IntPtr hwnd)
    {
        var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = flags, hwndTarget = hwnd };
        return RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            try { Read(lParam); }
            catch (Exception ex) { Log.Error("Error leyendo el ratón", ex); }
        }
        else if (msg == WM_CLOSE)
        {
            DestroyWindow(hwnd);
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Read(IntPtr handle)
    {
        var headerSize = (uint)(8 + 2 * IntPtr.Size);
        uint size = 0;
        GetRawInputData(handle, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;
        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferSize = (int)size;
        }
        if (GetRawInputData(handle, RID_INPUT, _buffer, ref size, headerSize) != size) return;

        // RAWINPUTHEADER { dwType, dwSize, hDevice, wParam } then RAWMOUSE { usFlags, (buttons), ulRawButtons, lLastX, lLastY, … }
        if ((uint)Marshal.ReadInt32(_buffer, 0) != RIM_TYPEMOUSE) return;
        if (Marshal.ReadIntPtr(_buffer, 8) == IntPtr.Zero) return; // injected (SendInput): not a real mouse
        var mouse = (int)headerSize;
        if ((Marshal.ReadInt16(_buffer, mouse) & MOUSE_MOVE_ABSOLUTE) != 0) return; // tablets, remote desktop
        var dx = Marshal.ReadInt32(_buffer, mouse + 12);
        var dy = Marshal.ReadInt32(_buffer, mouse + 16);
        if (dx != 0 || dy != 0) Moved?.Invoke(dx, dy);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _ready.Dispose();
    }

    // ── interop ──────────────────────────────────────────────────────────

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage, usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
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
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
}
