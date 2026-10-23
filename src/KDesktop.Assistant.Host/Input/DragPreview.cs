using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static KDesktop.Assistant.Host.Interop.Native;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace KDesktop.Assistant.Host.Input;

/// <summary>
/// The picture of what is being dragged (SDK 1.17), as the shell keeps it in the data object: «DragImageBits» = a
/// SHDRAGIMAGE written in 32 bits whatever the process (width, height, offset x and y, bitmap handle, colour key: 24 bytes)
/// followed by its pixels, premultiplied BGRA, bottom-up, already faded to about 75 % for the screen (the fade is taken
/// back here so the assistant gets it opaque).
/// </summary>
internal static class DragPreview
{
    /// <summary>Larger pictures are not taken (the shell's are at most a few hundred pixels).</summary>
    private const int MaxSide = 1024;

    private const int Header = 24;

    private static uint _format;

    /// <summary>The picture, frozen, or null if the source gave none.</summary>
    public static BitmapSource? Of(IDataObject data)
    {
        if (_format == 0) _format = RegisterClipboardFormat("DragImageBits");
        if (_format == 0) return null;
        var format = new FORMATETC
        {
            cfFormat = unchecked((short)_format),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };
        try
        {
            data.GetData(ref format, out var medium);
            try
            {
                var memory = medium.unionmember;
                var size = (long)GlobalSize(memory);
                var pointer = GlobalLock(memory);
                if (pointer == 0) return null;
                try
                {
                    return Read(pointer, size);
                }
                finally
                {
                    GlobalUnlock(memory);
                }
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static BitmapSource? Read(nint pointer, long size)
    {
        if (size < Header) return null;
        int width = Marshal.ReadInt32(pointer), height = Marshal.ReadInt32(pointer, 4);
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide) return null;
        var stride = width * 4;
        if (size < Header + (long)stride * height) return null;
        var source = new byte[stride * height];
        Marshal.Copy(pointer + Header, source, 0, source.Length);

        // Top-down for WPF, and the fade taken back: the most opaque pixel becomes fully opaque.
        var pixels = new byte[source.Length];
        var max = 0;
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(source, (height - 1 - y) * stride, pixels, y * stride, stride);
        }
        for (var i = 3; i < pixels.Length; i += 4) max = Math.Max(max, pixels[i]);
        if (max == 0) return null;
        if (max < 255)
        {
            var gain = 255.0 / max;
            for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)Math.Min(255, Math.Round(pixels[i] * gain));
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
