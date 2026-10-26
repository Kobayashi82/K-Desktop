using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KDesktop.Core.Imaging;

/// <summary>A screenshot as a file (Settings › Capturas): PNG, JPG with Windows' encoder, or lossless WebP.</summary>
public static class ImageFiles
{
    public static string Extension(ImageFileFormat format) => format switch
    {
        ImageFileFormat.Jpg => ".jpg",
        ImageFileFormat.WebP => ".webp",
        _ => ".png",
    };

    /// <summary>The picture in <paramref name="format"/>; <paramref name="quality"/> (30–100) only matters to JPG.</summary>
    public static async Task<byte[]> EncodeAsync(BgraImage image, ImageFileFormat format, int quality, CancellationToken token)
    {
        switch (format)
        {
            case ImageFileFormat.Jpg:
                return await JpegAsync(image, Math.Clamp(quality, 30, 100)).ConfigureAwait(false);
            // The format has 14 bits for each side.
            case ImageFileFormat.WebP when image.Width <= 16384 && image.Height <= 16384:
                return await Task.Run(() => WebP(image), token).ConfigureAwait(false);
            default:
                return await Task.Run(() => Png.Encode(image), token).ConfigureAwait(false);
        }
    }

    /// <summary>The extension a picture in <paramref name="format"/> really gets (a WebP too big for the format is a PNG).</summary>
    public static string ExtensionFor(BgraImage image, ImageFileFormat format) =>
        format == ImageFileFormat.WebP && (image.Width > 16384 || image.Height > 16384) ? ".png" : Extension(format);

    private static async Task<byte[]> JpegAsync(BgraImage image, int quality)
    {
        using var stream = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(quality / 100f, Windows.Foundation.PropertyType.Single) };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    /// <summary>A still lossless WebP: the VP8L stream of the showcase's clips in a RIFF of its own.</summary>
    private static byte[] WebP(BgraImage image)
    {
        var argb = new uint[image.Width * image.Height];
        for (var i = 0; i < argb.Length; i++) argb[i] = BinaryPrimitives.ReadUInt32LittleEndian(image.Pixels.AsSpan(i * 4, 4)) | 0xFF000000u;
        var data = Vp8lEncoder.Encode(argb, image.Width, image.Height, alpha: false);
        var padded = data.Length + (data.Length & 1);
        var file = new byte[20 + padded];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 12 + padded);
        "WEBP"u8.CopyTo(file.AsSpan(8));
        "VP8L"u8.CopyTo(file.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(16), data.Length);
        data.CopyTo(file, 20);
        return file;
    }
}
