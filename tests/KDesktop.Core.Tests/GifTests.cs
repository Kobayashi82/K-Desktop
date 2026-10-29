using System.Runtime.InteropServices.WindowsRuntime;
using KDesktop.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KDesktop.Core.Tests;

/// <summary>
/// «Grabar vídeo» in GIF (F3): what <see cref="AnimatedGifWriter"/> writes is read back by Windows' own decoder (exactly when
/// the colours fit in a palette, closely when they are cut), only what changes goes in each frame, and the times add up.
/// </summary>
public class GifTests
{
    private static byte[] Picture(int width, int height, Func<int, int, uint> argb)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                BitConverter.TryWriteBytes(bytes.AsSpan((y * width + x) * 4), argb(x, y));
        return bytes;
    }

    /// <summary>The first frame as Windows reads it, BGRA.</summary>
    private static async Task<(byte[] Pixels, uint Frames)> DecodeFirstAsync(byte[] gif)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(gif.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.GifDecoderId, stream);
        var frame = await decoder.GetFrameAsync(0);
        var data = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return (data.DetachPixelData(), decoder.FrameCount);
    }

    /// <summary>Each frame's rectangle and delay (hundredths), walking the file's blocks.</summary>
    private static List<(int X, int Y, int Width, int Height, int Delay)> Frames(byte[] gif)
    {
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
        var frames = new List<(int, int, int, int, int)>();
        var p = 13;
        var delay = 0;
        int Read16(int at) => gif[at] | gif[at + 1] << 8;
        int SkipBlocks(int at)
        {
            while (gif[at] != 0) at += gif[at] + 1;
            return at + 1;
        }
        while (gif[p] != 0x3B)
        {
            if (gif[p] == 0x21)
            {
                if (gif[p + 1] == 0xF9) delay = Read16(p + 4);
                p = SkipBlocks(p + 2);
            }
            else
            {
                Assert.Equal(0x2C, gif[p]);
                frames.Add((Read16(p + 1), Read16(p + 3), Read16(p + 5), Read16(p + 7), delay));
                var packed = gif[p + 9];
                p += 10 + ((packed & 0x80) != 0 ? 3 << ((packed & 7) + 1) : 0);
                p = SkipBlocks(p + 1);
            }
        }
        return frames;
    }

    [Fact]
    public async Task FewColoursComeBackExactly()
    {
        // 16 colours at random: thousands of codes, so the LZW table fills and starts again more than once.
        var random = new Random(7);
        var colours = Enumerable.Range(0, 16).Select(_ => (uint)random.Next() | 0xFF000000u).ToArray();
        var cells = Enumerable.Range(0, 300 * 200).Select(_ => colours[random.Next(16)]).ToArray();
        var picture = Picture(300, 200, (x, y) => cells[y * 300 + x]);
        var writer = new AnimatedGifWriter(300, 200);
        writer.Add(picture, 100);

        var (pixels, count) = await DecodeFirstAsync(writer.ToArray());
        Assert.Equal(1u, count);
        Assert.Equal(picture, pixels);
    }

    [Fact]
    public async Task ManyColoursComeBackClose()
    {
        var picture = Picture(300, 200, (x, y) => 0xFF000000u | (uint)(x * 255 / 299) << 16 | (uint)(y * 255 / 199) << 8 | (uint)((x + y) % 256));
        var writer = new AnimatedGifWriter(300, 200);
        writer.Add(picture, 100);

        var (pixels, _) = await DecodeFirstAsync(writer.ToArray());
        long error = 0;
        for (var i = 0; i < picture.Length; i++) error += Math.Abs(picture[i] - pixels[i]);
        Assert.True(error / (double)picture.Length < 6, $"error medio {error / (double)picture.Length:0.0}");
    }

    [Fact]
    public void OnlyWhatChangesGoesInAndTheTimesAddUp()
    {
        var first = Picture(120, 80, (x, y) => (x + y) % 2 == 0 ? 0xFF202020u : 0xFFE0E0E0u);
        var second = (byte[])first.Clone();
        // A 10 × 6 patch at (31, 21) changes.
        for (var y = 21; y < 27; y++)
            for (var x = 31; x < 41; x++)
                BitConverter.TryWriteBytes(second.AsSpan((y * 120 + x) * 4), 0xFF3C8CE6u);
        var writer = new AnimatedGifWriter(120, 80);
        writer.Add(first, 66);
        writer.Add(first, 67); // the same: the first lasts longer
        writer.Add(second, 66);
        writer.Add(second, 1001);

        var frames = Frames(writer.ToArray());
        Assert.Equal(2, writer.FrameCount);
        Assert.Equal(2, frames.Count);
        Assert.Equal((0, 0, 120, 80), (frames[0].X, frames[0].Y, frames[0].Width, frames[0].Height));
        Assert.Equal((31, 21, 10, 6), (frames[1].X, frames[1].Y, frames[1].Width, frames[1].Height));
        // 133 ms and 1067 ms: in hundredths, what one loses rounding the other keeps.
        Assert.Equal(120, frames.Sum(f => f.Delay));
    }
}
