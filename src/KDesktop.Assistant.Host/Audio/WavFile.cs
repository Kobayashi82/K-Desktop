using System.Buffers.Binary;

namespace KDesktop.Assistant.Host.Audio;

/// <summary>Reads WAV files (PCM of 8, 16, 24 or 32 bits, or 32-bit float; mono or stereo) into a <see cref="HostSound"/>.</summary>
internal static class WavFile
{
    private const ushort Pcm = 1, Float = 3, Extensible = 0xFFFE;

    public static HostSound Read(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Throws <see cref="InvalidDataException"/> when it is not a WAV this can read.</summary>
    public static HostSound Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("No es un archivo WAV");

        ushort format = 0, channels = 0, bits = 0;
        var rate = 0;
        ReadOnlySpan<byte> data = default;
        var found = false;
        for (var at = 12; at + 8 <= file.Length;)
        {
            var id = file.Slice(at, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(at + 4, 4)), (uint)(file.Length - at - 8));
            var body = file.Slice(at + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                // WAVE_FORMAT_EXTENSIBLE: the real format is the first two bytes of its sub-format GUID.
                if (format == Extensible && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = body;
                found = true;
            }
            at += 8 + size + (size & 1);
        }

        if (!found || rate <= 0) throw new InvalidDataException("WAV sin datos o sin formato");
        if (channels is not (1 or 2)) throw new InvalidDataException($"WAV con {channels} canales (solo mono o estéreo)");
        var bytes = bits / 8;
        if (!(format == Pcm && bits is 8 or 16 or 24 or 32) && !(format == Float && bits == 32))
            throw new InvalidDataException($"Formato de WAV no admitido ({format}, {bits} bits)");

        var count = data.Length / bytes / channels * channels;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var s = data.Slice(i * bytes, bytes);
            samples[i] = format == Float ? BinaryPrimitives.ReadSingleLittleEndian(s) : bits switch
            {
                8 => (s[0] - 128) / 128f,
                16 => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                24 => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
            };
        }
        return new HostSound(samples, rate, channels);
    }
}
