using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The picture of what is dragged (SDK 1.17): read from the data object as the shell leaves it there (the same helper the
/// Explorer uses), the right way up and opaque, and given to the assistant with the files while a label follows the cursor.
/// </summary>
public partial class FileDropTests
{
    private const int PictureWidth = 8;
    private const int PictureHeight = 6;

    [Fact]
    public void ThePictureComesTheRightWayUpAndOpaque()
    {
        BitmapSource? preview = null;
        OnSta(() =>
        {
            Assert.True(OleInitialize(0) >= 0);
            try
            {
                preview = DragPreview.Of(ShellData(files: []));
            }
            finally
            {
                OleUninitialize();
            }
        });
        Assert.NotNull(preview);
        Assert.Equal(PictureWidth, preview.PixelWidth);
        Assert.Equal(PictureHeight, preview.PixelHeight);
        var pixels = new uint[PictureWidth * PictureHeight];
        preview.CopyPixels(pixels, PictureWidth * 4, 0);
        // Red on top and blue below (half see-through), as it was drawn; the shell's fade is taken back.
        Assert.Equal(0xFFFF0000u, pixels[0]);
        Assert.Equal(0x800000FFu & 0xFF000000u, pixels[^1] & 0xFF000000u);
        Assert.InRange(pixels[^1] & 0xFFu, 0x7Eu, 0x82u);
    }

    [Fact]
    public void ThePictureReachesTheAssistantWhileALabelFollowsTheCursor()
    {
        var events = new List<PointerEvent>();
        var labels = new List<System.Windows.Point?>();
        OnSta(() =>
        {
            Assert.True(OleInitialize(0) >= 0);
            try
            {
                var data = ShellData([@"C:\Users\Ana\foto.jpg"]);
                var target = new FileDropTarget(0, (kind, files, at, preview) => events.Add(new PointerEvent(kind, "body", at) { Files = files, Preview = preview }),
                    labels.Add);
                var effect = DROPEFFECT_COPY;
                target.DragEnter(data, 0, new POINTL { X = 10, Y = 20 }, ref effect);
                target.DragOver(0, new POINTL { X = 12, Y = 22 }, ref effect);
                target.DragLeave();
                target.DragEnter(data, 0, new POINTL { X = 10, Y = 20 }, ref effect);
                target.Drop(data, 0, new POINTL { X = 11, Y = 21 }, ref effect);
            }
            finally
            {
                OleUninitialize();
            }
        });
        Assert.Equal([PointerEventKind.FilesOver, PointerEventKind.FilesAway, PointerEventKind.FilesOver, PointerEventKind.FilesDropped],
            events.Select(e => e.Kind));
        Assert.All(events, e => Assert.Equal([@"C:\Users\Ana\foto.jpg"], e.Files));
        Assert.All(events, e => Assert.Equal(PictureWidth, e.Preview?.PixelWidth));
        Assert.Equal([new(10, 20), new(12, 22), null, new(10, 20), null], labels);
    }

    /// <summary>
    /// A real drag that stays a moment over a hit area under the cursor, with mouse moves (posted to the drag: the real mouse
    /// does not move): the shell's window with the picture («SysDragImage») is never seen while the files are over it, not
    /// even for a moment (looked at all the time from another thread: telling the shell's helper about each move showed it
    /// again and it flickered). With text (not taken) it stays, so the check sees it at all.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WindowsPictureIsHiddenOverTheAssistant(bool files)
    {
        var shown = new List<bool>();
        var over = false;
        var entered = 0;
        var placed = false;
        OnSta(() =>
        {
            Assert.True(OleInitialize(0) >= 0);
            try
            {
                if (!GetCursorPos(out var cursor)) return;
                var events = new List<PointerEvent>();
                var isOver = false;
                using var pointer = new HostPointer(e =>
                {
                    events.Add(e);
                    // Right when it happens (the drag calls the target on this thread).
                    Volatile.Write(ref isOver, e.Kind == PointerEventKind.FilesOver);
                    over |= isOver;
                    if (isOver) entered++;
                }, (_, _) => { }, () => { }) { Name = "Dot" };
                using var area = pointer.CreateHitArea("body");
                area.Place(new HitShape.Rectangle(new PixelRect(cursor.X - 20, cursor.Y - 20, cursor.X + 20, cursor.Y + 20)));
                placed = true;
                var data = ShellData(files ? [@"C:\Users\Ana\foto.jpg"] : []);
                if (!files) SetText(data, "hola");
                var watching = true;
                var watcher = new Thread(() =>
                {
                    while (Volatile.Read(ref watching))
                    {
                        if (!files || Volatile.Read(ref isOver))
                        {
                            var seen = false;
                            for (nint w = 0; !seen && (w = FindWindowEx(0, w, "SysDragImage", null)) != 0;) seen = IsWindowVisible(w);
                            lock (shown) shown.Add(seen);
                        }
                        Thread.Sleep(0);
                    }
                }) { IsBackground = true };
                watcher.Start();
                var source = new WatchPicture(() =>
                {
                    // Where the cursor is, so the drag stays over the area.
                    if (GetCapture() is var capture and not 0 && GetCursorPos(out var on))
                    {
                        var at = ((long)on.Y << 32) | (uint)on.X;
                        if (ScreenToClient(capture, ref at)) PostMessage(capture, WM_MOUSEMOVE, 0, (nint)(((at >> 32) << 16) | (at & 0xFFFF)));
                    }
                });
                DoDragDrop(data, source, DROPEFFECT_COPY, out _);
                Volatile.Write(ref watching, false);
                watcher.Join();
            }
            finally
            {
                OleUninitialize();
            }
        });
        Assert.True(placed, "Sin cursor (¿sin escritorio?)");
        if (files)
        {
            Assert.True(over, "No llegó FilesOver");
            Assert.Equal(1, entered); // it stayed over the area all the time
            Assert.NotEmpty(shown);
            Assert.DoesNotContain(true, shown);
        }
        else Assert.Contains(true, shown);
    }

    /// <summary>A drag that looks at the picture on each turn of the loop and drops after a while.</summary>
    [ComVisible(true)]
    private sealed class WatchPicture(Action look) : IDropSource
    {
        private int _turns;

        public int QueryContinueDrag(int escapePressed, int keys)
        {
            look();
            Thread.Sleep(10);
            return ++_turns >= 80 ? DRAGDROP_S_DROP : 0;
        }

        public int GiveFeedback(int effect) => DRAGDROP_S_USEDEFAULTCURSORS;
    }

    private static void SetText(ComDataObject data, string text)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
        Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
        GlobalUnlock(memory);
        var format = new FORMATETC { cfFormat = 13, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        data.SetData(ref format, ref medium, true);
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string className, string? title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint hwnd, ref long point); // a POINT: x, then y

    /// <summary>
    /// A shell data object with these files (CF_HDROP) and a drag picture set by the shell's own helper (red on top, blue
    /// half see-through below), as the Explorer gives one.
    /// </summary>
    private static ComDataObject ShellData(IReadOnlyList<string> files)
    {
        var iid = typeof(ComDataObject).GUID;
        Assert.Equal(0, SHCreateDataObject(0, 0, 0, 0, ref iid, out var created));
        var data = (ComDataObject)created;
        if (files.Count > 0) SetFiles(data, files);

        var info = new BITMAPINFOHEADER { Size = 40, Width = PictureWidth, Height = -PictureHeight, Planes = 1, BitCount = 32 };
        var bitmap = CreateDIBSection(0, ref info, 0, out var bits, 0, 0);
        Assert.NotEqual(0, bitmap);
        var pixels = new int[PictureWidth * PictureHeight];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = i < pixels.Length / 2 ? unchecked((int)0xFFFF0000) : unchecked((int)0x800000FF);
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        var helper = (IDragSourceHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_DragDropHelper)!)!;
        var image = new SHDRAGIMAGE { Width = PictureWidth, Height = PictureHeight, OffsetX = 3, OffsetY = 4, Bitmap = bitmap, ColorKey = 0xFFFFFFFF };
        Assert.Equal(0, helper.InitializeFromBitmap(ref image, data)); // the helper owns the bitmap now
        return data;
    }

    private static void SetFiles(ComDataObject data, IReadOnlyList<string> files)
    {
        // DROPFILES (offset of the names, point, non-client, wide) and the names, each ending in a 0, and one 0 more.
        var names = System.Text.Encoding.Unicode.GetBytes(string.Concat(files.Select(f => f + "\0")) + "\0");
        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)(20 + names.Length));
        var target = GlobalLock(memory);
        var header = new byte[20];
        BitConverter.TryWriteBytes(header.AsSpan(0, 4), 20);
        BitConverter.TryWriteBytes(header.AsSpan(16, 4), 1);
        Marshal.Copy(header, 0, target, 20);
        Marshal.Copy(names, 0, target + 20, names.Length);
        GlobalUnlock(memory);
        var format = new FORMATETC { cfFormat = CF_HDROP, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        data.SetData(ref format, ref medium, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHDRAGIMAGE
    {
        public int Width, Height, OffsetX, OffsetY;
        public nint Bitmap;
        public uint ColorKey;
    }

    [ComImport]
    [Guid("DE5BF786-477A-11D2-839D-00C04FD918D0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDragSourceHelper
    {
        [PreserveSig]
        int InitializeFromBitmap(ref SHDRAGIMAGE image, [MarshalAs(UnmanagedType.Interface)] ComDataObject data);

        [PreserveSig]
        int InitializeFromWindow(nint window, nint point, [MarshalAs(UnmanagedType.Interface)] ComDataObject data);
    }

#pragma warning disable SYSLIB1054
    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(nint folder, uint count, nint items, nint inner, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object data);
#pragma warning restore SYSLIB1054

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);
}
