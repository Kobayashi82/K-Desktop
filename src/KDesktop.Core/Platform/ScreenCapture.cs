using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>A picture in memory: 32-bit BGRA pixels, top row first.</summary>
public sealed class BgraImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
    public int Stride => Width * 4;

    /// <summary>A part of it (clamped to the picture).</summary>
    public BgraImage Crop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        width = Math.Clamp(width, 1, Width - x);
        height = Math.Clamp(height, 1, Height - y);
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(Pixels, (y + row) * Stride + x * 4, pixels, row * width * 4, width * 4);
        return new BgraImage(width, height, pixels);
    }
}

/// <summary>Copies what is on the screen (physical pixels: K-Desktop is per-monitor DPI aware).</summary>
public static class ScreenCapture
{
    /// <summary>The whole desktop, every monitor (its top-left corner is <see cref="VirtualScreen"/>.Left/Top).</summary>
    public static ScreenRect VirtualScreen => new(
        GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    /// <summary>The whole monitor (not just its work area) the mouse is on.</summary>
    public static ScreenRect MonitorAtCursor()
    {
        GetCursorPos(out var p);
        return MonitorAt(p.X, p.Y);
    }

    /// <summary>The whole monitor at (or nearest to) a point.</summary>
    public static ScreenRect MonitorAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, 2 /* NEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(monitor, ref info)
            ? new ScreenRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top)
            : VirtualScreen;
    }

    /// <summary>The monitors, whole (for the region selector, one overlay each).</summary>
    public static IReadOnlyList<ScreenRect> Monitors()
    {
        var result = new List<ScreenRect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
                result.Add(new ScreenRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>What the screen shows in <paramref name="area"/> (layered windows included).</summary>
    public static BgraImage Capture(ScreenRect area)
    {
        var width = Math.Max(1, area.Width);
        var height = Math.Max(1, area.Height);
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt falló (" + Marshal.GetLastWin32Error() + ")");
            SelectObject(memory, old);
            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new byte[width * height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) == 0)
                throw new InvalidOperationException("GetDIBits falló");
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // the screen has no transparency
            return new BgraImage(width, height, pixels);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The mouse pointer as it is now (null: hidden), to draw it on a picture taken now or later.</summary>
    public static PointerShot? CurrentPointer()
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & 1 /* SHOWING */) == 0 || info.hCursor == IntPtr.Zero) return null;
        var copy = CopyIcon(info.hCursor);
        if (copy == IntPtr.Zero) return null;
        var (hotX, hotY) = (0, 0);
        if (GetIconInfo(copy, out var icon))
        {
            (hotX, hotY) = (icon.xHotspot, icon.yHotspot);
            if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
        }
        return new PointerShot(copy, info.ptScreenPos.X - hotX, info.ptScreenPos.Y - hotY);
    }

    /// <summary>A pointer and where its corner was on the screen.</summary>
    public sealed class PointerShot(IntPtr icon, int left, int top) : IDisposable
    {
        private IntPtr _icon = icon;

        /// <summary>Draws it on <paramref name="image"/>, a picture of the screen from <paramref name="area"/>'s corner.</summary>
        public void DrawOn(BgraImage image, ScreenRect area)
        {
            int x = left - area.Left, y = top - area.Top;
            if (_icon == IntPtr.Zero || x >= image.Width || y >= image.Height || x < -64 || y < -64) return;
            var screen = GetDC(IntPtr.Zero);
            var memory = CreateCompatibleDC(screen);
            var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = image.Width, biHeight = -image.Height, biPlanes = 1, biBitCount = 32 };
            var bitmap = CreateDIBSection(memory, ref header, 0, out var bits, IntPtr.Zero, 0);
            try
            {
                if (bitmap == IntPtr.Zero) return;
                var old = SelectObject(memory, bitmap);
                Marshal.Copy(image.Pixels, 0, bits, image.Pixels.Length);
                DrawIconEx(memory, x, y, _icon, 0, 0, 0, IntPtr.Zero, 3 /* DI_NORMAL */);
                GdiFlush();
                Marshal.Copy(bits, image.Pixels, 0, image.Pixels.Length);
                SelectObject(memory, old);
                for (var i = 3; i < image.Pixels.Length; i += 4) image.Pixels[i] = 255;
            }
            finally
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        public void Dispose()
        {
            if (_icon != IntPtr.Zero) DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────

    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
}

/// <summary>A minimal PNG writer (8-bit RGB, no filters lost: «Up» filter per row), enough for screenshots.</summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(BgraImage image)
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: RGB
        header[10] = 0; // compression
        header[11] = 0; // filter
        header[12] = 0; // interlace
        Chunk(output, "IHDR", header);

        // Rows with the «Up» filter: screens have many repeated rows, which compress far better that way.
        var rowBytes = image.Width * 3;
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            var filtered = new byte[rowBytes + 1];
            for (var y = 0; y < image.Height; y++)
            {
                var src = y * image.Stride;
                for (var x = 0; x < image.Width; x++)
                {
                    current[x * 3] = image.Pixels[src + x * 4 + 2];
                    current[x * 3 + 1] = image.Pixels[src + x * 4 + 1];
                    current[x * 3 + 2] = image.Pixels[src + x * 4];
                }
                filtered[0] = 2; // Up
                for (var i = 0; i < rowBytes; i++) filtered[i + 1] = (byte)(current[i] - previous[i]);
                zlib.Write(filtered);
                (previous, current) = (current, previous);
            }
        }
        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
