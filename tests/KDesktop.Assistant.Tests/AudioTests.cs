using KDesktop.Assistant.Host.Audio;

namespace KDesktop.Assistant.Tests;

/// <summary>The host's sound: the mixer's arithmetic and reading WAV files (no device is opened).</summary>
public class AudioTests
{
    private static HostSound Constant(float value, int frames, int rate = Mixer.Rate, int channels = 1) =>
        new(Enumerable.Repeat(value, frames * channels).ToArray(), rate, channels);

    private static float[] MixOnce(Mixer mixer, int frames = 256)
    {
        var output = new float[frames * 2];
        mixer.Mix(output);
        return output;
    }

    [Fact]
    public void AOneShotSoundPlaysOnceInTheCentre()
    {
        var mixer = new Mixer();
        mixer.Play(Constant(0.5f, 100), gain: 1, pan: 0);
        Assert.True(mixer.HasSound);

        var output = MixOnce(mixer);
        Assert.Equal(0.5f, output[0], 3);
        Assert.Equal(0.5f, output[1], 3);
        Assert.Equal(0f, output[2 * 150]); // past its end
        Assert.False(mixer.HasSound);
    }

    [Fact]
    public void PanAndVolumeShapeTheSound()
    {
        var mixer = new Mixer { Volume = 0.5 };
        mixer.Play(Constant(1f, 1000), gain: 1, pan: -1);
        var output = MixOnce(mixer);
        Assert.Equal(0.25f * MathF.Sqrt(2), output[0], 3); // all left, under 0.5² of volume
        Assert.Equal(0f, output[1], 3);
    }

    [Fact]
    public void OtherRatesAreResampled()
    {
        var mixer = new Mixer();
        // 100 frames at half the rate last 200 frames of output.
        mixer.Play(Constant(0.5f, 100, Mixer.Rate / 2), 1, 0);
        var output = MixOnce(mixer, 300);
        Assert.NotEqual(0f, output[2 * 190]);
        Assert.Equal(0f, output[2 * 210]);
    }

    [Fact]
    public void ALoopRepeatsGlidesAndStopsWhenDisposed()
    {
        var mixer = new Mixer();
        var loop = mixer.Loop(Constant(0.5f, 10));
        Assert.False(mixer.HasSound); // a loop starts silent

        loop.Gain = 1;
        Assert.True(mixer.HasSound);
        var first = MixOnce(mixer, 100);
        Assert.True(first[0] < first[2 * 99]); // glides up over the first block
        var second = MixOnce(mixer, 100);
        Assert.Equal(0.5f, second[2 * 55], 3); // and keeps going past its 10 frames

        loop.Dispose();
        Assert.False(mixer.HasSound);
        Assert.All(MixOnce(mixer), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void MutedNothingIsMixed()
    {
        var mixer = new Mixer();
        var loop = mixer.Loop(Constant(0.5f, 10));
        loop.Gain = 1;
        mixer.Muted = true;
        mixer.Play(Constant(0.5f, 100), 1, 0);
        Assert.False(mixer.HasSound);
        Assert.All(MixOnce(mixer), s => Assert.Equal(0f, s));

        mixer.Muted = false;
        Assert.True(mixer.HasSound); // the loop comes back; the one-shot sound was forgotten
    }

    [Fact]
    public void TheSoftClipIsTransparentBelowTheKneeAndNeverReachesOne()
    {
        Assert.Equal(0.5f, SoundOutput.SoftClip(0.5f));
        Assert.Equal(-0.3f, SoundOutput.SoftClip(-0.3f));
        Assert.InRange(SoundOutput.SoftClip(3f), 0.9f, 1f);
        Assert.InRange(SoundOutput.SoftClip(-50f), -1f, -0.99f);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void WavFilesAreRead(int bits)
    {
        var bytes = bits / 8;
        // Stereo: left at +half, right at −half, three frames.
        var data = new List<byte>();
        for (var frame = 0; frame < 3; frame++)
            foreach (var sign in new[] { 1, -1 })
            {
                var value = sign * (1L << (bits - 2));
                for (var b = 0; b < bytes; b++) data.Add((byte)(value >> (8 * b)));
            }
        var file = Wav(1, 2, 22050, (ushort)bits, [.. data]);

        var sound = WavFile.Parse(file);
        Assert.Equal(2, sound.Channels);
        Assert.Equal(22050, sound.SampleRate);
        Assert.Equal(3, sound.Frames);
        Assert.Equal(0.5f, sound.Samples[0], 4);
        Assert.Equal(-0.5f, sound.Samples[1], 4);
    }

    [Fact]
    public void FloatWavFilesAreRead()
    {
        var data = BitConverter.GetBytes(0.25f).Concat(BitConverter.GetBytes(-1f)).ToArray();
        var sound = WavFile.Parse(Wav(3, 1, 44100, 32, data));
        Assert.Equal([0.25f, -1f], sound.Samples);
    }

    [Fact]
    public void WhatIsNotAWavIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => WavFile.Parse("hola"u8));
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(Wav(1, 6, 44100, 16, new byte[12])));
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(Wav(2, 1, 44100, 4, new byte[12]))); // ADPCM
    }

    private static byte[] Wav(ushort format, ushort channels, int rate, ushort bits, byte[] data)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + data.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write(format);
        w.Write(channels);
        w.Write(rate);
        w.Write(rate * channels * bits / 8);
        w.Write((ushort)(channels * bits / 8));
        w.Write(bits);
        w.Write("data"u8);
        w.Write(data.Length);
        w.Write(data);
        w.Flush();
        return stream.ToArray();
    }
}
