using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KDesktop.Core;

namespace KDesktop.Views;

/// <summary>
/// Files dropped on a window from the Explorer. K-Desktop runs as administrator, and Windows blocks OLE drag and drop
/// from normal processes (UIPI); the old WM_DROPFILES does get through once it (and the two messages the shell uses
/// to hand over the files) is let through the window's message filter. WPF registers an OLE drop target on its
/// windows, which would take the drag first: it is removed, and put back only while a mouse button is held down
/// inside the window, which is how K-Desktop's own drags start (the QuickMenu editor, text in a box).
/// </summary>
internal static class FileDrop
{
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049;
    private const uint MSGFLT_ALLOW = 1;

    /// <summary>WPF's own (internal) way of registering its OLE target, to put it back for a drag of ours.</summary>
    private static readonly MethodInfo? RegisterWpfTarget =
        typeof(DragDrop).GetMethod("RegisterDropTarget", BindingFlags.Static | BindingFlags.NonPublic, [typeof(nint)]);

    /// <summary>Starts taking dropped files on <paramref name="window"/> (call once its handle exists).</summary>
    public static void Attach(Window window, Action<IReadOnlyList<string>> dropped)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        foreach (var message in new[] { WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA })
            ChangeWindowMessageFilterEx(hwnd, message, MSGFLT_ALLOW, 0);
        DragAcceptFiles(hwnd, true);
        HwndSource.FromHwnd(hwnd)?.AddHook((nint _, int msg, nint wParam, nint _, ref bool handled) =>
        {
            if (msg != WM_DROPFILES) return 0;
            handled = true;
            var files = Files(wParam);
            if (files.Count > 0) window.Dispatcher.BeginInvoke(() => dropped(files));
            return 0;
        });

        if (RegisterWpfTarget is null)
        {
            // Without it, WPF's own drags would stop working: keep its target (files from the Explorer won't drop).
            Log.Warn("No se puede quitar el destino OLE de WPF: no se podrán soltar archivos en la ventana");
            return;
        }
        var ole = new OleSwitch(hwnd);
        ole.Off();
        window.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => ole.On()), handledEventsToo: true);
        window.Closed += (_, _) => ole.Stop();
    }

    /// <summary>The paths of a drop (and the drop's memory freed).</summary>
    private static List<string> Files(nint drop)
    {
        var files = new List<string>();
        try
        {
            var count = DragQueryFile(drop, uint.MaxValue, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFile(drop, i, null, 0);
                var buffer = new char[length + 1];
                if (DragQueryFile(drop, i, buffer, length + 1) > 0) files.Add(new string(buffer, 0, (int)length));
            }
        }
        finally
        {
            DragFinish(drop);
        }
        return files;
    }

    /// <summary>WPF's OLE target on while a button is down in the window, off again once all are up.</summary>
    private sealed class OleSwitch
    {
        private readonly nint _hwnd;
        private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private bool _on = true;
        private int _upTicks;

        public OleSwitch(nint hwnd)
        {
            _hwnd = hwnd;
            _watch.Tick += (_, _) => Watch();
        }

        public void On()
        {
            _upTicks = 0;
            if (_on) return;
            try
            {
                RegisterWpfTarget!.Invoke(null, [_hwnd]);
                _on = true;
                _watch.Start();
            }
            catch (TargetInvocationException ex)
            {
                Log.Warn($"No se ha podido volver a poner el destino OLE de WPF ({ex.InnerException?.Message})");
            }
        }

        public void Off()
        {
            RevokeDragDrop(_hwnd);
            _on = false;
            _watch.Stop();
        }

        public void Stop() => _watch.Stop();

        /// <summary>Two looks in a row with every button up: the drag (if there was one) has ended.</summary>
        private void Watch()
        {
            var down = GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0 || GetAsyncKeyState(0x04) < 0;
            if (down) _upTicks = 0;
            else if (++_upTicks >= 2) Off();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint changeInfo);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, [Out] char[]? file, uint size);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(nint drop);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(nint hwnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
