using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KDesktop.Services;

/// <summary>The notification-area icon: K-Desktop's own, or one the user picked (Settings › Appearance).</summary>
public static class TrayIcons
{
    public static ImageSource Default() =>
        BitmapFrame.Create(new Uri("pack://application:,,,/KDesktop;component/Assets/Icons/KDesktop.ico"));

    /// <summary>
    /// The chosen icon (same references as command icons) drawn at the size the tray uses for the current DPI, or null
    /// when there is none or it cannot be loaded (then the default is used).
    /// </summary>
    public static System.Drawing.Icon? Custom(string? reference, bool listening = false)
    {
        ImageSource? image;
        if (string.IsNullOrWhiteSpace(reference)) image = listening ? Default() : null;
        // As is: the taskbar may be dark while the app is light, so no theme adaptation here.
        else image = IconProvider.FromConfigIcon(reference, adaptToTheme: false) ?? (listening ? Default() : null);
        if (image is null) return null;
        try
        {
            var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
            if (size <= 0) size = 16;
            var bitmap = Render(image, size, listening);
            return new System.Drawing.Icon(new MemoryStream(IcoBytes(bitmap)), size, size);
        }
        catch (Exception ex)
        {
            Core.Log.Warn($"No se pudo crear el icono de la bandeja: {ex.Message}");
            return null;
        }
    }

    /// <param name="listening">The voice is listening: a red dot in the bottom-right corner, like a recording light.</param>
    private static BitmapSource Render(ImageSource image, int size, bool listening)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            // Keep the aspect ratio, centred.
            var w = image.Width > 0 ? image.Width : 1;
            var h = image.Height > 0 ? image.Height : 1;
            var scale = Math.Min(size / w, size / h);
            var rect = new Rect((size - w * scale) / 2, (size - h * scale) / 2, w * scale, h * scale);
            dc.DrawImage(image, rect);
            if (listening)
            {
                var r = size * 0.24;
                var centre = new Point(size - r - 0.5, size - r - 0.5);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE8, 0x2A, 0x2A)), new Pen(Brushes.White, Math.Max(1, size / 16.0)), centre, r, r);
            }
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        return new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>A one-image .ico (32-bpp DIB with an empty AND mask), the format every Windows icon API reads.</summary>
    private static byte[] IcoBytes(BitmapSource bmp)
    {
        int s = bmp.PixelWidth;
        var px = new byte[s * s * 4];
        bmp.CopyPixels(px, s * 4, 0);
        var maskRow = (s + 31) / 32 * 4;
        var dibSize = 40 + px.Length + maskRow * s;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)1); w.Write((short)1);
        w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)0); w.Write((byte)0);
        w.Write((short)1); w.Write((short)32); w.Write(dibSize); w.Write(22);
        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (var y = s - 1; y >= 0; y--) w.Write(px, y * s * 4, s * 4);
        w.Write(new byte[maskRow * s]);
        w.Flush();
        return ms.ToArray();
    }

    private const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
