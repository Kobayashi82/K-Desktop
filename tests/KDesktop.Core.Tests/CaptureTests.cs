using System.Buffers.Binary;
using System.IO.Compression;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

public class CaptureTests
{
    private static BgraImage Sample(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var random = new Random(7);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)random.Next(256);
            pixels[i + 1] = (byte)random.Next(256);
            pixels[i + 2] = (byte)random.Next(256);
            pixels[i + 3] = 255;
        }
        return new BgraImage(width, height, pixels);
    }

    /// <summary>Reads back what <see cref="Png.Encode"/> wrote (RGB, «Up» filter) as BGRA.</summary>
    private static (int Width, int Height, byte[] Bgra) Decode(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        int width = 0, height = 0;
        using var idat = new MemoryStream();
        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
            }
            if (type == "IDAT") idat.Write(data);
            at += 12 + length;
        }
        idat.Position = 0;
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        var bytes = raw.ToArray();
        var rowBytes = width * 3;
        var previous = new byte[rowBytes];
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var row = bytes.AsSpan(y * (rowBytes + 1), rowBytes + 1);
            Assert.Equal(2, row[0]);
            var current = new byte[rowBytes];
            for (var i = 0; i < rowBytes; i++) current[i] = (byte)(row[i + 1] + previous[i]);
            for (var x = 0; x < width; x++)
            {
                bgra[(y * width + x) * 4] = current[x * 3 + 2];
                bgra[(y * width + x) * 4 + 1] = current[x * 3 + 1];
                bgra[(y * width + x) * 4 + 2] = current[x * 3];
                bgra[(y * width + x) * 4 + 3] = 255;
            }
            previous = current;
        }
        return (width, height, bgra);
    }

    [Fact]
    public void PngKeepsEveryPixel()
    {
        var image = Sample(37, 23);
        var (w, h, bgra) = Decode(Png.Encode(image));
        Assert.Equal((37, 23), (w, h));
        Assert.Equal(image.Pixels, bgra);
    }

    [Fact]
    public void CropTakesTheRightPart()
    {
        var image = Sample(10, 8);
        var part = image.Crop(3, 2, 4, 5);
        Assert.Equal((4, 5), (part.Width, part.Height));
        for (var y = 0; y < 5; y++)
            Assert.Equal(image.Pixels.AsSpan(((y + 2) * 10 + 3) * 4, 16).ToArray(), part.Pixels.AsSpan(y * 16, 16).ToArray());
        var clamped = image.Crop(8, 6, 10, 10);
        Assert.Equal((2, 2), (clamped.Width, clamped.Height));
    }

    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(1720, 700, 2)]
    [InlineData(3439, 1399, 3)]
    public void ZoomKeepsThePointerWhereItIs(int x, int y, double zoom)
    {
        var (ox, oy) = Magnifier.Offset(x, y, zoom);
        // A point p is shown at (p − offset) × zoom: the pointer's must be the pointer.
        Assert.Equal(x, (x - ox) * zoom, 6);
        Assert.Equal(y, (y - oy) * zoom, 6);
        Assert.InRange(ox, 0, x);
    }
}
