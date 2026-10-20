using System.Runtime.InteropServices;
using System.Windows;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Where the Recycle Bin icon is on the desktop (from Lander), asking the shell's desktop view (IShellWindows →
/// IShellBrowser → IFolderView), the documented way. On the engine thread (STA); a few milliseconds, only when the bin
/// is emptied.
/// </summary>
internal static class DesktopIcons
{
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const int SVGIO_ALLVIEW = 2;
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserIid = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>Centre of the Recycle Bin icon in screen pixels, or null (not on the desktop, icons hidden).</summary>
    public static Point? FindRecycleBin()
    {
        var listView = FindDesktopListView();
        if (listView == 0 || !IsWindowVisible(listView)) return null;

        object? shellWindows = null, dispatch = null, browser = null, view = null;
        try
        {
            if (Type.GetTypeFromCLSID(ShellWindowsClsid) is not { } type || (shellWindows = Activator.CreateInstance(type)) is not IShellWindows windows)
                return null;
            object location = 0;
            object root = null!;
            if (windows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out dispatch) != 0 || dispatch is not IServiceProvider provider)
                return null;
            var service = TopLevelBrowserService;
            var iid = ShellBrowserIid;
            if (provider.QueryService(ref service, ref iid, out browser) != 0 || browser is not IShellBrowser shellBrowser) return null;
            if (shellBrowser.QueryActiveShellView(out view) != 0 || view is not IFolderView folderView) return null;
            if (folderView.ItemCount(SVGIO_ALLVIEW, out var count) != 0) return null;

            _ = folderView.GetSpacing(out var spacing);
            for (var i = 0; i < count; i++)
            {
                if (folderView.Item(i, out var pidl) != 0 || pidl == 0) continue;
                try
                {
                    if (!IsRecycleBin(pidl) || folderView.GetItemPosition(pidl, out var position) != 0) continue;
                    // The position is the top-left of the item's cell, in the list's client area; the icon sits in its top part.
                    var point = new POINT { X = position.X + spacing.X / 2, Y = position.Y + spacing.Y / 3 };
                    ClientToScreen(listView, ref point);
                    return new Point(point.X, point.Y);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se encuentra la papelera en el escritorio ({ex.Message})");
        }
        finally
        {
            Release(view);
            Release(browser);
            Release(dispatch);
            Release(shellWindows);
        }
        return null;
    }

    private static bool IsRecycleBin(nint pidl)
    {
        if (SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, out var name) != 0 || name == 0) return false;
        try { return string.Equals(Marshal.PtrToStringUni(name), RecycleBinParsingName, StringComparison.OrdinalIgnoreCase); }
        finally { Marshal.FreeCoTaskMem(name); }
    }

    /// <summary>The list of desktop icons hangs from Progman or from a WorkerW, depending on the Windows version.</summary>
    private static nint FindDesktopListView()
    {
        var defView = FindWindowEx(FindWindow("Progman", null), 0, "SHELLDLL_DefView", null);
        nint worker = 0;
        while (defView == 0)
        {
            worker = FindWindowEx(0, worker, "WorkerW", null);
            if (worker == 0) return 0;
            defView = FindWindowEx(worker, 0, "SHELLDLL_DefView", null);
        }
        return FindWindowEx(defView, 0, "SysListView32", null);
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    // Only the members used are declared properly; the others hold their place in the vtable.
#pragma warning disable CA1822, IDE0051
    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void GetCount();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [PreserveSig]
        int FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object root,
            int windowClass, out int hwnd, int options, [MarshalAs(UnmanagedType.IDispatch)] out object dispatch);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();

        [PreserveSig]
        int Item(int index, out nint pidl);

        [PreserveSig]
        int ItemCount(int flags, out int count);

        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();

        [PreserveSig]
        int GetItemPosition(nint pidl, out POINT position);

        [PreserveSig]
        int GetSpacing(out POINT spacing);
    }
#pragma warning restore CA1822, IDE0051
}
