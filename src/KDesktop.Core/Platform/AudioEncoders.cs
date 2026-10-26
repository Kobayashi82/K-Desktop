using System.Buffers;
using System.Buffers.Binary;
using Concentus;
using Concentus.Enums;
using KDesktop.Core.Model;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace KDesktop.Core.Platform;

/// <summary>
/// A finished recording, from the .wav it was recorded to into the format of Settings › Grabación de audio: MP3 with
/// Windows' encoder (Media Foundation), Opus with Concentus (in an Ogg file of our own). The .wav goes when it worked.
/// </summary>
public static class AudioEncoders
{
    private const int Mp3Bitrate = 192_000;
    private const int OpusBitrate = 128_000;

    public static string Extension(AudioFileFormat format) => format switch
    {
        AudioFileFormat.Mp3 => ".mp3",
        AudioFileFormat.Opus => ".opus",
        _ => ".wav",
    };

    /// <summary>
    /// <paramref name="wav"/> as <paramref name="format"/> next to it (same name); returns the file that stays (the .wav
    /// itself when the format is WAV or the conversion failed).
    /// </summary>
    public static string Convert(string wav, AudioFileFormat format)
    {
        if (format == AudioFileFormat.Wav) return wav;
        var output = Path.ChangeExtension(wav, Extension(format));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (format == AudioFileFormat.Mp3) Mp3(wav, output);
            else Opus(wav, output);
            File.Delete(wav);
            Log.Info($"Grabación de audio: {Path.GetFileName(output)} ({new FileInfo(output).Length / 1024} KB) en {watch.ElapsedMilliseconds} ms");
            return output;
        }
        catch (Exception ex)
        {
            Log.Warn($"Grabación de audio: no se pudo pasar a {format}, se queda en WAV ({ex.Message})");
            try { File.Delete(output); } catch { /* not there */ }
            return wav;
        }
    }

    private static bool _mediaFoundation;

    private static void Mp3(string wav, string output)
    {
        if (!_mediaFoundation)
        {
            MediaFoundationApi.Startup();
            _mediaFoundation = true;
        }
        using var reader = new WaveFileReader(wav);
        MediaFoundationEncoder.EncodeToMp3(reader, output, Mp3Bitrate);
    }

    // ── Opus in Ogg (RFC 7845) ───────────────────────────────────────────

    /// <summary>20 ms at 48 kHz.</summary>
    private const int Frame = 960;

    private static void Opus(string wav, string output)
    {
        using var reader = new WaveFileReader(wav);
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16 || reader.WaveFormat.SampleRate != 48000)
            throw new NotSupportedException(reader.WaveFormat.ToString());
        var channels = reader.WaveFormat.Channels;
        var encoder = OpusCodecFactory.CreateEncoder(48000, channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.Bitrate = OpusBitrate;
        // 6 of 10: about twice as fast as the most, and sounds the same at this bitrate.
        encoder.Complexity = 6;
        var preSkip = encoder.Lookahead;

        using var file = File.Create(output);
        var ogg = new OggWriter(file);
        ogg.Packet(Head(channels, preSkip), 0, last: false, flush: true);
        ogg.Packet(Tags(), 0, last: false, flush: true);

        var bytes = new byte[Frame * channels * 2];
        var pcm = new short[Frame * channels];
        var packet = new byte[4000];
        long packets = 0, samples = 0;
        var ended = false;
        bool last;
        do
        {
            // After the end, silence: the encoder runs pre-skip samples behind, and an Ogg Opus file needs a packet.
            var read = ended ? 0 : Math.Max(0, reader.Read(bytes, 0, bytes.Length));
            ended = read == 0 || reader.Position >= reader.Length;
            var frames = read / (2 * channels);
            for (var i = 0; i < frames * channels; i++) pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2));
            Array.Clear(pcm, frames * channels, pcm.Length - frames * channels);
            var length = encoder.Encode(pcm, Frame, packet, packet.Length);
            packets++;
            samples += frames;
            last = ended && packets * Frame >= preSkip + samples;
            // The position at the end of the page: what the packets decode to, but the last one says where the sound ends.
            ogg.Packet(packet.AsSpan(0, length), last ? preSkip + samples : packets * Frame, last, flush: last);
        }
        while (!last);
    }

    private static byte[] Head(int channels, int preSkip)
    {
        var head = new byte[19];
        "OpusHead"u8.CopyTo(head);
        head[8] = 1;
        head[9] = (byte)channels;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), (ushort)preSkip);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), 48000);
        return head; // gain 0, mapping family 0
    }

    private static byte[] Tags()
    {
        var vendor = "K-Desktop (Concentus)"u8;
        var tags = new byte[8 + 4 + vendor.Length + 4];
        "OpusTags"u8.CopyTo(tags);
        BinaryPrimitives.WriteInt32LittleEndian(tags.AsSpan(8), vendor.Length);
        vendor.CopyTo(tags.AsSpan(12));
        return tags; // no comments
    }

    /// <summary>Ogg pages of one stream: packets go in until a page has about a second or it is flushed.</summary>
    private sealed class OggWriter(Stream output)
    {
        private readonly uint _serial = (uint)Random.Shared.Next();
        private readonly ArrayBufferWriter<byte> _body = new();
        private readonly List<byte> _segments = [];
        private uint _sequence;
        private long _granule;
        private bool _first = true;

        public void Packet(ReadOnlySpan<byte> data, long granule, bool last, bool flush)
        {
            var length = data.Length;
            while (true)
            {
                var piece = Math.Min(255, length);
                _segments.Add((byte)piece);
                length -= piece;
                if (piece < 255) break;
            }
            _body.Write(data);
            _granule = granule;
            if (flush || last || _segments.Count > 200) Page(last);
        }

        private void Page(bool last)
        {
            var header = new byte[27 + _segments.Count];
            "OggS"u8.CopyTo(header);
            header[5] = (byte)((_first ? 2 : 0) | (last ? 4 : 0));
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(6), _granule);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(14), _serial);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), _sequence++);
            header[26] = (byte)_segments.Count;
            _segments.CopyTo(header, 27);
            var body = _body.WrittenSpan;
            var crc = Crc(Crc(0, header), body);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), crc);
            output.Write(header);
            output.Write(body);
            _body.ResetWrittenCount();
            _segments.Clear();
            _first = false;
        }

        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var r = i << 24;
                for (var k = 0; k < 8; k++) r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
                table[i] = r;
            }
            return table;
        }

        private static uint Crc(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = (crc << 8) ^ Table[((crc >> 24) ^ b) & 0xFF];
            return crc;
        }
    }
}
