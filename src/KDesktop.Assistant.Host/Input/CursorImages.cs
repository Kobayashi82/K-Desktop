using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Input;

/// <summary>A cursor from a picture drawn by an assistant (<see cref="Sdk.IHitArea.SetCursorImage"/>).</summary>
internal static unsafe class CursorImages
{
    /// <summary>Bigger than this, Windows would scale it down anyway.</summary>
    private const int MaxSide = 256;

    /// <summary>An HCURSOR (to free with DestroyIcon), or 0 if it cannot be made.</summary>
    public static nint Create(BitmapSource image, Point hotspot)
    {
        var width = Math.Min(image.PixelWidth, MaxSide);
        var height = Math.Min(image.PixelHeight, MaxSide);
        if (width <= 0 || height <= 0) return 0;

        // Straight alpha, as cursors want it (WPF draws premultiplied).
        var straight = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        straight.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var screen = GetDC(0);
        var color = CreateDIBSection(screen, &header, 0, out var bits, 0, 0);
        ReleaseDC(0, screen);
        if (color == 0) return 0;
        pixels.AsSpan().CopyTo(new Span<byte>(bits, pixels.Length));

        var mask = CreateBitmap(width, height, 1, 1, null);
        try
        {
            var info = new ICONINFO
            {
                fIcon = 0,
                xHotspot = (uint)Math.Clamp((int)Math.Round(hotspot.X), 0, width - 1),
                yHotspot = (uint)Math.Clamp((int)Math.Round(hotspot.Y), 0, height - 1),
                hbmMask = mask,
                hbmColor = color,
            };
            return CreateIconIndirect(&info);
        }
        finally
        {
            DeleteObject(color);
            if (mask != 0) DeleteObject(mask);
        }
    }
}
