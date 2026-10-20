using System.Runtime.InteropServices;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>A borderless Win32 window with its own message handler, which never takes the focus on a click (from Kobii).</summary>
/// <remarks>Created and used on one thread only (the engine's).</remarks>
internal sealed unsafe class NativeWindow : IDisposable
{
    private static readonly Dictionary<nint, NativeWindow> Instances = [];
    private static readonly HashSet<string> RegisteredClasses = [];

    public NativeWindow(string className, string title, uint exStyle, int x = 0, int y = 0, int width = 1, int height = 1)
    {
        EnsureClass(className);
        Handle = CreateWindowEx(exStyle, className, title, WS_POPUP, x, y, width, height, 0, 0, GetModuleHandle(null), 0);
        if (Handle == 0) throw new InvalidOperationException($"CreateWindowEx falló ({Marshal.GetLastPInvokeError()})");
        Instances[Handle] = this;
    }

    public nint Handle { get; private set; }

    public bool IsVisible { get; private set; }

    /// <summary>Message handler: a result, or null for the default handling.</summary>
    public Func<uint, nint, nint, nint?>? MessageHandler { get; set; }

    public void Show()
    {
        if (IsVisible) return;
        ShowWindow(Handle, SW_SHOWNOACTIVATE);
        IsVisible = true;
        BringToTop();
    }

    public void Hide()
    {
        if (!IsVisible) return;
        ShowWindow(Handle, SW_HIDE);
        IsVisible = false;
    }

    /// <summary>Takes back the first place among the «always on top» windows.</summary>
    public void BringToTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public void Move(int x, int y, int width, int height) =>
        SetWindowPos(Handle, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public void Dispose()
    {
        if (Handle == 0) return;
        Instances.Remove(Handle);
        DestroyWindow(Handle);
        Handle = 0;
    }

    private static void EnsureClass(string className)
    {
        if (!RegisteredClasses.Add(className)) return;
        fixed (char* name = className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = CS_DBLCLKS,
                lpfnWndProc = &WindowProcedure,
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(0, IDC_ARROW),
                lpszClassName = name,
            };
            if (RegisterClassEx(windowClass) == 0)
            {
                RegisteredClasses.Remove(className);
                throw new InvalidOperationException($"RegisterClassEx falló para {className}");
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (Instances.TryGetValue(hwnd, out var window) && window.MessageHandler?.Invoke(message, wParam, lParam) is { } result)
                return result;
            if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
            // Spanning monitors of different scaling: never let Windows resize it, the engine places it in pixels.
            if (message == WM_DPICHANGED) return 0;
        }
        catch (Exception ex)
        {
            // An exception must not cross into native code: log it and go on.
            Log.Error($"Asistente: error con el mensaje 0x{message:X4}", ex);
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
}
