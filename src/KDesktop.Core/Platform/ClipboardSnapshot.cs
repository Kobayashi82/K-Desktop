using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>
/// What the clipboard holds, every format that is plain memory (text, rich text, HTML, files, pictures as DIB…), to put
/// it back as it was after borrowing it (an assistant's «transform_text» copies the selection and pastes the result).
/// Handles of GDI objects (bitmaps, metafiles, palettes) and private formats are left out: Windows makes the bitmap again
/// from the DIB. Any thread.
/// </summary>
public sealed class ClipboardSnapshot
{
    /// <summary>Beyond this the clipboard is not kept (a huge picture): it is left as the borrowing leaves it.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    private readonly List<(uint Format, byte[] Data)> _formats;

    private ClipboardSnapshot(List<(uint, byte[])> formats) => _formats = formats;

    /// <summary>Empty clipboard, or one too big to keep, has no formats.</summary>
    public int Count => _formats.Count;

    /// <summary>What it holds now (null: the clipboard could not be opened, or holds too much).</summary>
    public static ClipboardSnapshot? Take()
    {
        if (!Open()) return null;
        try
        {
            var formats = new List<(uint, byte[])>();
            long total = 0;
            for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                if (!IsMemory(format)) continue;
                var handle = GetClipboardData(format);
                if (handle == IntPtr.Zero) continue;
                var size = (long)GlobalSize(handle);
                if (size <= 0) continue;
                total += size;
                if (total > MaxBytes) return null;
                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) continue;
                try
                {
                    var data = new byte[size];
                    Marshal.Copy(pointer, data, 0, (int)size);
                    formats.Add((format, data));
                }
                finally { GlobalUnlock(handle); }
            }
            return new ClipboardSnapshot(formats);
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Puts it back as it was (an empty one empties the clipboard).</summary>
    public bool Restore()
    {
        if (!Open()) return false;
        try
        {
            EmptyClipboard();
            foreach (var (format, data) in _formats) Put(format, data);
            return true;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// Leaves <paramref name="text"/> in the clipboard. <paramref name="passing"/>: only for a moment (to paste it), so
    /// Windows' clipboard history and the cloud clipboard do not keep it.
    /// </summary>
    public static bool SetText(string text, bool passing = false)
    {
        if (!Open()) return false;
        try
        {
            EmptyClipboard();
            var bytes = new byte[(text.Length + 1) * 2];
            System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
            if (!Put(CF_UNICODETEXT, bytes)) return false;
            if (passing)
            {
                Put(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), [0]);
                Put(RegisterClipboardFormat("CanIncludeInClipboardHistory"), [0, 0, 0, 0]);
                Put(RegisterClipboardFormat("CanUploadToCloudClipboard"), [0, 0, 0, 0]);
            }
            return true;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>The files and folders copied in the Explorer (full paths; empty: none).</summary>
    public static IReadOnlyList<string> Files()
    {
        if (!Open()) return [];
        try
        {
            var drop = GetClipboardData(CF_HDROP);
            if (drop == IntPtr.Zero) return [];
            var count = DragQueryFile(drop, uint.MaxValue, null, 0);
            var files = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFile(drop, i, null, 0);
                var name = new char[length + 1];
                if (DragQueryFile(drop, i, name, (uint)name.Length) > 0) files.Add(new string(name, 0, (int)length));
            }
            return files;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// The picture copied, written to <paramref name="folder"/> as it came (a PNG when the program gave one, else the DIB as
    /// a .bmp, which any decoder reads); null when there is none.
    /// </summary>
    public static string? SaveImage(string folder)
    {
        byte[]? png = null, dib = null;
        if (!Open()) return null;
        try
        {
            png = Bytes(RegisterClipboardFormat("PNG"));
            if (png is null) dib = Bytes(CF_DIBV5) ?? Bytes(CF_DIB);
        }
        finally { CloseClipboard(); }
        if (png is null && (dib is null || dib.Length < 40)) return null;

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + (png is null ? ".bmp" : ".png"));
        if (png is not null)
        {
            File.WriteAllBytes(path, png);
            return path;
        }
        File.WriteAllBytes(path, BmpFromDib(dib!));
        return path;
    }

    /// <summary>A .bmp file of a DIB (CF_DIB, CF_DIBV5): the DIB behind a 14-byte header that says where its pixels start.</summary>
    public static byte[] BmpFromDib(byte[] dib)
    {
        var header = BitConverter.ToInt32(dib, 0);
        var bits = BitConverter.ToUInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);
        var used = BitConverter.ToInt32(dib, 32);
        var colors = used != 0 ? used : bits <= 8 ? 1 << bits : 0;
        var masks = header == 40 && compression == 3 ? 12 : 0; // BI_BITFIELDS after a plain header
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
        BitConverter.TryWriteBytes(bmp.AsSpan(10), 14 + header + masks + colors * 4);
        dib.CopyTo(bmp, 14);
        return bmp;
    }

    /// <summary>A memory format's bytes (the clipboard already open), or null.</summary>
    private static byte[]? Bytes(uint format)
    {
        if (format == 0) return null;
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;
        var size = (long)GlobalSize(handle);
        if (size <= 0 || size > MaxBytes) return null;
        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var data = new byte[size];
            Marshal.Copy(pointer, data, 0, (int)size);
            return data;
        }
        finally { GlobalUnlock(handle); }
    }

    private static bool Put(uint format, byte[] data)
    {
        var memory = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)Math.Max(1, data.Length));
        if (memory == IntPtr.Zero) return false;
        var pointer = GlobalLock(memory);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(memory);
            return false;
        }
        try { Marshal.Copy(data, 0, pointer, data.Length); }
        finally { GlobalUnlock(memory); }
        if (SetClipboardData(format, memory) != IntPtr.Zero) return true; // the clipboard owns it now
        GlobalFree(memory);
        return false;
    }

    /// <summary>Formats whose data is a block of memory (not a GDI handle nor a program's private one).</summary>
    private static bool IsMemory(uint format) => format switch
    {
        CF_BITMAP or CF_METAFILEPICT or CF_PALETTE or CF_ENHMETAFILE or CF_OWNERDISPLAY or CF_DSPBITMAP or CF_DSPMETAFILEPICT or CF_DSPENHMETAFILE => false,
        >= CF_PRIVATEFIRST and <= CF_GDIOBJLAST => false,
        _ => true,
    };

    /// <summary>Another program may have it open for a moment.</summary>
    private static bool Open()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(20);
        }
        Log.Warn("Portapapeles: otro programa lo tiene abierto");
        return false;
    }

    private const uint CF_BITMAP = 2, CF_METAFILEPICT = 3, CF_DIB = 8, CF_PALETTE = 9, CF_UNICODETEXT = 13, CF_ENHMETAFILE = 14, CF_HDROP = 15, CF_DIBV5 = 17, CF_OWNERDISPLAY = 0x80,
        CF_DSPBITMAP = 0x82, CF_DSPMETAFILEPICT = 0x83, CF_DSPENHMETAFILE = 0x8E, CF_PRIVATEFIRST = 0x200, CF_GDIOBJLAST = 0x3FF;

    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFile(IntPtr drop, uint index, [Out] char[]? file, uint length);
}
