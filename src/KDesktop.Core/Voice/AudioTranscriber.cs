using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Concentus;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KDesktop.Core.Voice;

/// <summary>
/// An audio file to text with Parakeet (D5, 5.0.373), without the AI: «Grabar audio» › «Al terminar, transcribir» and the
/// assistants' <c>transcribe_audio</c>. The file is read as it goes (WAV, MP3, M4A… with Media Foundation; Opus with
/// Concentus), at 16 kHz mono; Silero finds the speech and each piece is transcribed. A long pause starts a paragraph.
/// </summary>
public static class AudioTranscriber
{
    /// <summary>The audio files it reads (what «Grabar audio» makes, and the usual ones).</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".opus", ".ogg", ".m4a", ".aac", ".wma", ".flac",
    };

    /// <summary>A pause this long between two pieces of speech starts a paragraph.</summary>
    private static readonly TimeSpan Paragraph = TimeSpan.FromSeconds(3);

    /// <summary>Silero starts a piece a little late (the first word lost its start): this much before it goes too.</summary>
    private const int Before = MicCapture.SampleRate * 6 / 10;

    private const int After = MicCapture.SampleRate / 10;

    /// <summary>What is kept of the last audio read, to take each piece with its margins (a piece is 20 s at most).</summary>
    private const int Kept = MicCapture.SampleRate * 30;

    /// <summary>
    /// What is said in <paramref name="file"/>: empty when nobody speaks in it. Throws when the voice models are missing
    /// (<see cref="VoiceModels.Ready"/>) or the file cannot be read.
    /// </summary>
    public static async Task<string> TranscribeAsync(SpeechToText speech, string file, CancellationToken token)
    {
        if (!VoiceModels.Ready) throw new FileNotFoundException("Faltan los modelos de voz", VoiceModels.ParakeetDir);
        return await Task.Run(() => Transcribe(speech, file, token), token).ConfigureAwait(false);
    }

    private static string Transcribe(SpeechToText speech, string file, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        using var source = Open(file);
        var samples = source.ToMono16k();
        using var vad = new VoiceActivity(TimeSpan.FromSeconds(0.5), keepSpeech: true);
        var text = new StringBuilder();
        var lastEnd = -1L;
        var buffer = new float[MicCapture.SampleRate];
        var heard = 0d;
        // The last audio read, from the sample <recentStart> on.
        var recent = new List<float>(Kept + buffer.Length);
        var recentStart = 0L;
        float[] WithMargins(long start, int length)
        {
            var from = Math.Max(start - Before, recentStart);
            var to = Math.Min(start + length + After, recentStart + recent.Count);
            return from <= start && to >= start + length ? recent.GetRange((int)(from - recentStart), (int)(to - from)).ToArray() : [];
        }
        void TakeSpeech()
        {
            while (vad.Speech.TryDequeue(out var piece))
            {
                token.ThrowIfCancellationRequested();
                var audio = WithMargins(piece.Start, piece.Samples.Length);
                var said = speech.Transcribe(audio.Length > 0 ? audio : piece.Samples);
                heard += piece.Samples.Length / (double)MicCapture.SampleRate;
                if (said.Length == 0) continue;
                if (text.Length > 0)
                    text.Append(lastEnd >= 0 && piece.Start - lastEnd >= Paragraph.TotalSeconds * MicCapture.SampleRate ? "\n\n" : " ");
                text.Append(said);
                lastEnd = piece.Start + piece.Samples.Length;
            }
        }
        int read;
        while ((read = samples.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            var block = read == buffer.Length ? buffer : buffer[..read];
            recent.AddRange(block);
            vad.Accept(block);
            TakeSpeech();
            if (recent.Count > Kept)
            {
                var drop = recent.Count - Kept;
                recent.RemoveRange(0, drop);
                recentStart += drop;
            }
        }
        vad.Flush();
        TakeSpeech();
        Log.Info($"Transcripción: {Path.GetFileName(file)} → {text.Length} letras ({heard:0} s de voz, {watch.Elapsed.TotalSeconds:0.0} s)");
        return text.ToString();
    }

    /// <summary>The file as samples: Opus with our reader, the rest with Media Foundation (a WAV without it).</summary>
    private static Source Open(string file)
    {
        var extension = Path.GetExtension(file);
        if (extension.Equals(".opus", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            var opus = new OggOpusReader(File.OpenRead(file));
            return new Source(opus, opus);
        }
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var wav = new WaveFileReader(file);
            return new Source(wav.ToSampleProvider(), wav);
        }
        MediaFoundationApi.Startup();
        var reader = new MediaFoundationReader(file);
        return new Source(reader.ToSampleProvider(), reader);
    }

    private sealed class Source(ISampleProvider samples, IDisposable owner) : IDisposable
    {
        /// <summary>Mixed to one channel and resampled to what Parakeet and Silero take.</summary>
        public ISampleProvider ToMono16k()
        {
            var mono = samples.WaveFormat.Channels == 1 ? samples : new MonoMix(samples);
            return mono.WaveFormat.SampleRate == MicCapture.SampleRate ? mono : new WdlResamplingSampleProvider(mono, MicCapture.SampleRate);
        }

        public void Dispose() => owner.Dispose();
    }

    /// <summary>Any number of channels, averaged.</summary>
    private sealed class MonoMix(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var count = buffer.Length;
            if (_buffer.Length < count * _channels) _buffer = new float[count * _channels];
            var read = source.Read(_buffer.AsSpan(0, count * _channels)) / _channels;
            for (var i = 0; i < read; i++)
            {
                var sum = 0f;
                for (var c = 0; c < _channels; c++) sum += _buffer[i * _channels + c];
                buffer[i] = sum / _channels;
            }
            return read;
        }
    }

    /// <summary>
    /// An Ogg Opus file (RFC 7845, what «Grabar audio» writes) decoded at 48 kHz as it is read: pages into packets, the
    /// head and tags skipped, the pre-skip dropped.
    /// </summary>
    private sealed class OggOpusReader : ISampleProvider, IDisposable
    {
        /// <summary>The longest Opus packet: 120 ms at 48 kHz.</summary>
        private const int MaxFrame = 5760;

        private readonly Stream _stream;
        private readonly IOpusDecoder _decoder;
        private readonly int _channels;
        private readonly Queue<byte[]> _packets = new();
        private readonly MemoryStream _partial = new();
        private readonly float[] _pcm;
        private int _pcmStart, _pcmEnd;
        private int _skip;
        private bool _ended;

        public OggOpusReader(Stream stream)
        {
            _stream = stream;
            var head = NextPacket() ?? throw new InvalidDataException("Ogg vacío");
            if (head.Length < 19 || !head.AsSpan(0, 8).SequenceEqual("OpusHead"u8)) throw new InvalidDataException("No es Opus");
            _channels = Math.Clamp((int)head[9], 1, 2);
            _skip = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(10));
            _ = NextPacket(); // OpusTags
            _decoder = OpusCodecFactory.CreateDecoder(48000, _channels);
            _pcm = new float[MaxFrame * _channels];
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, _channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var written = 0;
            while (written < buffer.Length)
            {
                if (_pcmStart == _pcmEnd && !Decode()) break;
                var take = Math.Min(buffer.Length - written, _pcmEnd - _pcmStart);
                _pcm.AsSpan(_pcmStart, take).CopyTo(buffer[written..]);
                _pcmStart += take;
                written += take;
            }
            return written;
        }

        /// <summary>The next packet into <see cref="_pcm"/>; false at the end.</summary>
        private bool Decode()
        {
            while (NextPacket() is { } packet)
            {
                if (packet.Length == 0) continue;
                var frames = _decoder.Decode(packet, _pcm, MaxFrame, false);
                _pcmStart = 0;
                _pcmEnd = frames * _channels;
                if (_skip > 0)
                {
                    var skipped = Math.Min(_skip, frames);
                    _skip -= skipped;
                    _pcmStart = skipped * _channels;
                }
                if (_pcmStart < _pcmEnd) return true;
            }
            return false;
        }

        private byte[]? NextPacket()
        {
            while (_packets.Count == 0)
                if (_ended || !ReadPage()) return null;
            return _packets.Dequeue();
        }

        /// <summary>One page: its segments into whole packets (one may go on in the next page).</summary>
        private bool ReadPage()
        {
            var header = new byte[27];
            if (!Fill(header))
            {
                _ended = true;
                return false;
            }
            if (!header.AsSpan(0, 4).SequenceEqual("OggS"u8)) throw new InvalidDataException("Página Ogg rota");
            var segments = new byte[header[26]];
            if (!Fill(segments)) throw new EndOfStreamException();
            foreach (var size in segments)
            {
                var piece = new byte[size];
                if (!Fill(piece)) throw new EndOfStreamException();
                _partial.Write(piece);
                if (size == 255) continue;
                _packets.Enqueue(_partial.ToArray());
                _partial.SetLength(0);
            }
            if ((header[5] & 4) != 0) _ended = true;
            return true;
        }

        private bool Fill(byte[] into)
        {
            var done = 0;
            while (done < into.Length)
            {
                var read = _stream.Read(into, done, into.Length - done);
                if (read == 0) return false;
                done += read;
            }
            return true;
        }

        public void Dispose()
        {
            _stream.Dispose();
            _partial.Dispose();
        }
    }
}
