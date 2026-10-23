using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace KDesktop.Assistant.Host.Interop;

/// <summary>Files dragged onto an assistant (stage 13.8): OLE drag and drop on its hit windows.</summary>
internal static partial class Native
{
    public const short CF_HDROP = 15;
    public const int DROPEFFECT_NONE = 0;
    public const int DROPEFFECT_COPY = 1;
    public const int DROPEFFECT_LINK = 4;

    /// <summary>The kinds of icon of a <c>DROPDESCRIPTION</c>: the default one, a copy, a link, and «what you say».</summary>
    public const int DROPIMAGE_INVALID = -1;
    public const int DROPIMAGE_COPY = 1;
    public const int DROPIMAGE_LINK = 4;
    public const uint GMEM_MOVEABLE = 0x0002;

    public static readonly Guid CLSID_DragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");

    [LibraryImport("ole32.dll")]
    public static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll")]
    public static partial void OleUninitialize();

    // The drop target is a managed COM object (built-in COM interop): LibraryImport does not marshal those.
#pragma warning disable SYSLIB1054
    [DllImport("ole32.dll")]
    public static extern int RegisterDragDrop(nint hwnd, [MarshalAs(UnmanagedType.Interface)] IDropTarget target);
#pragma warning restore SYSLIB1054

    [LibraryImport("ole32.dll")]
    public static partial int RevokeDragDrop(nint hwnd);

#pragma warning disable SYSLIB1054
    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref STGMEDIUM medium);
#pragma warning restore SYSLIB1054

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    public static unsafe partial uint DragQueryFile(nint drop, uint index, char* file, uint length);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterClipboardFormat(string format);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint memory);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int X;
        public int Y;
    }

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig]
        int DragEnter([In, MarshalAs(UnmanagedType.Interface)] IDataObject data, int keys, POINTL at, ref int effect);

        [PreserveSig]
        int DragOver(int keys, POINTL at, ref int effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([In, MarshalAs(UnmanagedType.Interface)] IDataObject data, int keys, POINTL at, ref int effect);
    }

    /// <summary>The shell's drag image over our window (without it the picture of the files vanishes over the assistant).</summary>
    [ComImport]
    [Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTargetHelper
    {
        [PreserveSig]
        int DragEnter(nint hwnd, [In, MarshalAs(UnmanagedType.Interface)] IDataObject data, ref POINT at, int effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int DragOver(ref POINT at, int effect);

        [PreserveSig]
        int Drop([In, MarshalAs(UnmanagedType.Interface)] IDataObject data, ref POINT at, int effect);

        [PreserveSig]
        int Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }
}
