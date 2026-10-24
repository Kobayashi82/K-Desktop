using KDesktop.Core.Imaging;

namespace KDesktop.Core.Tests;

/// <summary>
/// The showcase's clips (stage 16.2): animated lossless WebP written by K-Desktop and read back exactly, and lossless WebP made
/// by libwebp (Pillow, every feature of the format: colour cache, meta prefix codes, palettes, colour transform, distances
/// on the plane) read the same as libwebp reads them.
/// </summary>
public class WebPTests
{
    private static byte[] Picture(int width, int height, Func<int, int, uint> argb)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                BitConverter.TryWriteBytes(bytes.AsSpan((y * width + x) * 4), argb(x, y));
        return bytes;
    }

    public static TheoryData<string> Kinds => ["gradient", "noise", "flat", "stripes", "tiny"];

    private static (int, int, Func<int, int, uint>) Kind(string kind)
    {
        var random = new Random(kind.GetHashCode(StringComparison.Ordinal));
        var noise = Enumerable.Range(0, 300 * 200).Select(_ => (uint)random.Next() | 0xFF000000u).ToArray();
        return kind switch
        {
            "gradient" => (300, 200, (x, y) => 0xFF000000u | (uint)(x * 255 / 299) << 16 | (uint)(y * 255 / 199) << 8 | (uint)((x + y) % 256)),
            "noise" => (300, 200, (x, y) => noise[y * 300 + x]),
            "flat" => (64, 48, (_, _) => 0xFF3C8CE6u),
            "stripes" => (257, 33, (x, y) => (x / 7 + y / 3) % 2 == 0 ? 0xFFFFFFFFu : 0xFF101820u),
            _ => (1, 1, (_, _) => 0xFF123456u),
        };
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AStillPictureComesBackExactly(string kind)
    {
        var (width, height, argb) = Kind(kind);
        var writer = new AnimatedWebPWriter(width, height);
        var picture = Picture(width, height, argb);
        writer.Add(picture, 100);
        var read = WebPReader.Read(writer.ToArray());
        Assert.Equal((width, height), (read.Width, read.Height));
        Assert.Equal(picture, Assert.Single(read.Frames).Bgra);
    }

    [Fact]
    public void AnAnimationKeepsOnlyWhatChangedAndComesBackExactly()
    {
        const int width = 320, height = 180;
        var writer = new AnimatedWebPWriter(width, height);
        var expected = new List<(byte[] Picture, int Ms)>();
        uint Background(int x, int y) => 0xFF000000u | (uint)(40 + x / 3) << 16 | (uint)(90 + y / 2) << 8 | 200;
        for (var f = 0; f < 12; f++)
        {
            // A ball that moves over a still background; frame 5 is the same as frame 4.
            var cx = 40 + Math.Min(f, 4) * 20 + Math.Max(0, f - 5) * 20;
            var picture = Picture(width, height, (x, y) => (x - cx) * (x - cx) + (y - 90) * (y - 90) < 400 ? 0xFFE0713Cu : Background(x, y));
            writer.Add(picture, 40);
            if (f == 5) expected[^1] = (expected[^1].Picture, expected[^1].Ms + 40);
            else expected.Add((picture, 40));
        }
        var file = writer.ToArray();
        var read = WebPReader.Read(file);
        Assert.Equal(expected.Count, read.Frames.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Ms, read.Frames[i].Milliseconds);
            Assert.Equal(expected[i].Picture, read.Frames[i].Bgra);
        }
        // Only the first picture is whole: the rest are small.
        Assert.True(file.Length < 60_000, $"{file.Length} bytes");
    }

    [Fact]
    public void AClipIsWalkedOnOneCanvasAndAgainFromTheStart()
    {
        const int width = 64, height = 40;
        var writer = new AnimatedWebPWriter(width, height);
        var pictures = Enumerable.Range(0, 5).Select(f => Picture(width, height, (x, _) => x / 8 == f ? 0xFFFF0000u : 0xFF202020u)).ToList();
        foreach (var picture in pictures) writer.Add(picture, 50);
        var (w, h, frames) = WebPReader.Open(writer.ToArray());
        Assert.Equal((width, height), (w, h));
        for (var walk = 0; walk < 2; walk++)
        {
            var seen = new List<byte[]>();
            var i = 0;
            foreach (var frame in frames)
            {
                Assert.Equal(pictures[i++], frame.Bgra);
                seen.Add(frame.Bgra);
            }
            Assert.Equal(pictures.Count, i);
            // The same array each time: never every frame in memory.
            Assert.Single(seen.Distinct());
        }
    }

    [Fact]
    public void LossyPicturesAreLeftToWindows()
    {
        var file = "RIFF\0\0\0\0WEBPVP8 \0\0\0\0"u8.ToArray();
        BitConverter.TryWriteBytes(file.AsSpan(4), file.Length - 8);
        Assert.Throws<NotSupportedException>(() => WebPReader.Read(file));
    }

    private static byte[] Unzip(string path)
    {
        using var input = new System.IO.Compression.GZipStream(File.OpenRead(path), System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void WhatLibwebpWritesIsReadAsLibwebpReadsIt()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Data", "webp");
        var files = Directory.GetFiles(folder, "*.webp");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var read = WebPReader.Read(File.ReadAllBytes(file));
            var reference = Unzip(Path.ChangeExtension(file, ".bgra.gz"));
            var frames = reference.Length / (read.Width * read.Height * 4);
            Assert.True(frames == read.Frames.Count, $"{Path.GetFileName(file)}: {read.Frames.Count} de {frames} imágenes");
            for (var i = 0; i < frames; i++)
            {
                var want = reference.AsSpan(i * read.Width * read.Height * 4, read.Width * read.Height * 4);
                var got = read.Frames[i].Bgra;
                // Fully transparent pixels may keep any colour.
                for (var p = 0; p < got.Length; p += 4)
                    if (want[p + 3] != 0 || got[p + 3] != 0)
                        Assert.True(want.Slice(p, 4).SequenceEqual(got.AsSpan(p, 4)), $"{Path.GetFileName(file)}: imagen {i}, píxel {p / 4}");
            }
        }
    }
}
