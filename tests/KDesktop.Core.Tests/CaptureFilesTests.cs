using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using Concentus;
using KDesktop.Core.Engine;
using KDesktop.Core.Imaging;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using NAudio.Wave;

namespace KDesktop.Core.Tests;

/// <summary>
/// The files of Settings › Capturas and › Grabación de audio (5.0.262): screenshots as WebP (exact) and JPG, their names
/// from the template, and recordings turned into MP3 and Opus (a valid Ogg file that decodes to the same length).
/// </summary>
public sealed class CaptureFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kd-capture-files-" + Guid.NewGuid().ToString("N"));

    public CaptureFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* still open: the temp folder */ }
    }

    private static BgraImage Sample(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 3);
                pixels[i + 1] = (byte)(y * 5);
                pixels[i + 2] = (byte)((x ^ y) * 7);
                pixels[i + 3] = 255;
            }
        return new BgraImage(width, height, pixels);
    }

    [Fact]
    public async Task WebPKeepsEveryPixel()
    {
        var image = Sample(160, 90);
        var file = await ImageFiles.EncodeAsync(image, ImageFileFormat.WebP, 90, CancellationToken.None);
        var read = WebPReader.Read(file);
        Assert.Equal((160, 90), (read.Width, read.Height));
        Assert.Equal(image.Pixels, Assert.Single(read.Frames).Bgra);
        Assert.Equal(".webp", ImageFiles.ExtensionFor(image, ImageFileFormat.WebP));
    }

    [Fact]
    public async Task JpgIsAJpgOfTheSameSizeAndQualityMakesItSmaller()
    {
        var image = Sample(320, 200);
        var good = await ImageFiles.EncodeAsync(image, ImageFileFormat.Jpg, 100, CancellationToken.None);
        var small = await ImageFiles.EncodeAsync(image, ImageFileFormat.Jpg, 30, CancellationToken.None);
        Assert.Equal([0xFF, 0xD8], good[..2]);
        Assert.True(small.Length < good.Length, $"{small.Length} ≥ {good.Length}");
        using var stream = new MemoryStream(good).AsRandomAccessStream();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        Assert.Equal((320u, 200u), (decoder.PixelWidth, decoder.PixelHeight));
    }

    [Fact]
    public void TheNameFollowsTheTemplateWithoutWhatAFileNameCannotHave()
    {
        var when = new DateTime(2026, 10, 26, 9, 5, 7, DateTimeKind.Local);
        Assert.Equal("Google Chrome - Mail a b 2026-10-26 09.05.07",
            CommandEngine.CaptureName("{app} - {TITLE} {date} {time}", () => "Google Chrome", () => "Mail: a/b?", when));
        // Empty, or nothing left once cleaned: the usual name.
        var usual = CommandEngine.CaptureName("", () => "x", () => "y", when);
        Assert.EndsWith(" 2026-10-26 09.05.07", usual, StringComparison.Ordinal);
        Assert.Equal(usual, CommandEngine.CaptureName("{title}", () => "", () => "???", when));
        // The program is asked only when the template has it.
        CommandEngine.CaptureName("{date}", () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), when);
    }

    [Fact]
    public void BorderColoursAreWhatWindowsWants() =>
        Assert.Equal((0x332211u, 0x15A8E0u), (Magnifier.ColorRef("#112233"), Magnifier.ColorRef("nonsense")));

    /// <summary>A 1.3 s tone, 48 kHz stereo 16-bit, as recordings are written.</summary>
    private string Tone()
    {
        var path = Path.Combine(_dir, "Audio " + Guid.NewGuid().ToString("N")[..6] + ".wav");
        using var writer = Wav.Create(path);
        var samples = new float[48000 * 13 / 10 * 2];
        for (var i = 0; i < samples.Length / 2; i++)
            samples[i * 2] = samples[i * 2 + 1] = 0.4f * MathF.Sin(2 * MathF.PI * 440 * i / 48000f);
        Wav.Write(writer, samples);
        return path;
    }

    [Fact]
    public void OpusIsAValidOggFileOfTheSameLength()
    {
        var wav = Tone();
        var opus = AudioEncoders.Convert(wav, AudioFileFormat.Opus);
        Assert.Equal(Path.ChangeExtension(wav, ".opus"), opus);
        Assert.False(File.Exists(wav));

        var file = File.ReadAllBytes(opus);
        var packets = new List<byte[]>();
        var pending = new List<byte>();
        long granule = 0;
        var pages = 0;
        for (var at = 0; at < file.Length; pages++)
        {
            Assert.Equal("OggS"u8.ToArray(), file[at..(at + 4)]);
            var flags = file[at + 5];
            granule = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(at + 6));
            var segments = file[at + 26];
            var header = 27 + segments;
            var body = 0;
            for (var s = 0; s < segments; s++) body += file[at + 27 + s];
            // The page's checksum, worked out with it zeroed.
            var page = file[at..(at + header + body)];
            var stored = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(22));
            page[22] = page[23] = page[24] = page[25] = 0;
            Assert.Equal(stored, OggCrc(page));
            if (pages == 0) Assert.Equal(2, flags & 2);
            var offset = at + header;
            for (var s = 0; s < segments; s++)
            {
                int length = file[at + 27 + s];
                pending.AddRange(file.AsSpan(offset, length).ToArray());
                offset += length;
                if (length < 255)
                {
                    packets.Add([.. pending]);
                    pending.Clear();
                }
            }
            at += header + body;
            if (at >= file.Length) Assert.Equal(4, flags & 4);
        }
        Assert.Equal("OpusHead"u8.ToArray(), packets[0][..8]);
        Assert.Equal("OpusTags"u8.ToArray(), packets[1][..8]);
        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(packets[0].AsSpan(10));
        Assert.Equal(preSkip + 48000 * 13 / 10, granule);

        var decoder = OpusCodecFactory.CreateDecoder(48000, 2);
        var pcm = new short[5760 * 2];
        var decoded = 0;
        double energy = 0;
        foreach (var packet in packets.Skip(2))
        {
            var frames = decoder.Decode(packet, pcm, 5760);
            for (var i = 0; i < frames * 2; i++) energy += (double)pcm[i] * pcm[i];
            decoded += frames;
        }
        Assert.InRange(decoded - preSkip, 48000 * 13 / 10, 48000 * 13 / 10 + 960);
        Assert.True(Math.Sqrt(energy / (decoded * 2)) > 3000, "it should sound");
    }

    [Fact]
    public void Mp3HasTheSameLength()
    {
        var wav = Tone();
        var mp3 = AudioEncoders.Convert(wav, AudioFileFormat.Mp3);
        Assert.Equal(Path.ChangeExtension(wav, ".mp3"), mp3);
        Assert.False(File.Exists(wav));
        using var reader = new MediaFoundationReader(mp3);
        Assert.InRange(reader.TotalTime.TotalSeconds, 1.25, 1.4);
    }

    private static uint OggCrc(byte[] data)
    {
        uint crc = 0;
        foreach (var b in data)
        {
            crc ^= (uint)b << 24;
            for (var k = 0; k < 8; k++) crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }
        return crc;
    }
}
