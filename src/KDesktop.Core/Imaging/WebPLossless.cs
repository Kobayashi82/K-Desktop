using System.Buffers.Binary;
using System.Numerics;

namespace KDesktop.Core.Imaging;

/// <summary>
/// An animated WebP made of lossless frames (VP8L), for the showcase's clips (stage 16.2). The first frame is the whole
/// picture; each one after it only the rectangle that changed, with what stayed the same left transparent and blended over
/// the frame before (so a character moving over a still desktop costs little). A frame equal to the one before only makes
/// that one last longer. Pictures are BGRA (premultiplied or not: they are opaque).
/// </summary>
public sealed class AnimatedWebPWriter(int width, int height)
{
    private readonly List<Frame> _frames = [];
    private uint[]? _previous;

    private sealed class Frame
    {
        public int X, Y, Width, Height, Duration;
        public bool Blend;
        public byte[] Data = [];
    }

    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>Frames in the file (a picture equal to the last one does not count).</summary>
    public int FrameCount => _frames.Count;

    /// <summary>
    /// Adds a picture (BGRA, rows of Width × 4 bytes) that shows for <paramref name="milliseconds"/>.
    /// </summary>
    public void Add(ReadOnlySpan<byte> bgra, int milliseconds)
    {
        if (bgra.Length < Width * Height * 4) throw new ArgumentException("La imagen es más pequeña que el clip", nameof(bgra));
        milliseconds = Math.Clamp(milliseconds, 1, 0xFFFFFF);
        var pixels = new uint[Width * Height];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = BinaryPrimitives.ReadUInt32LittleEndian(bgra.Slice(i * 4, 4)) | 0xFF000000u;
        if (_previous is null)
        {
            _frames.Add(new Frame { Width = Width, Height = Height, Duration = milliseconds, Data = Vp8lEncoder.Encode(pixels, Width, Height, alpha: false) });
            _previous = pixels;
            return;
        }
        // The rectangle that changed (its corner on even pixels, as the format wants).
        int left = Width, top = Height, right = -1, bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            for (var x = 0; x < Width; x++)
            {
                if (pixels[row + x] == _previous[row + x]) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < 0)
        {
            var last = _frames[^1];
            last.Duration = Math.Min(0xFFFFFF, last.Duration + milliseconds);
            return;
        }
        left &= ~1;
        top &= ~1;
        int w = right - left + 1, h = bottom - top + 1;
        var part = new uint[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (top + y) * Width + left + x;
                part[y * w + x] = pixels[i] == _previous[i] ? 0 : pixels[i];
            }
        _frames.Add(new Frame { X = left, Y = top, Width = w, Height = h, Duration = milliseconds, Blend = true, Data = Vp8lEncoder.Encode(part, w, h, alpha: true) });
        _previous = pixels;
    }

    /// <summary>The file, looping forever.</summary>
    public byte[] ToArray()
    {
        if (_frames.Count == 0) throw new InvalidOperationException("El clip no tiene imágenes");
        using var body = new MemoryStream();
        // VP8X: animation and alpha, and the canvas.
        Chunk(body, "VP8X", w =>
        {
            w.WriteByte(0x02 | 0x10);
            w.Write(new byte[3]);
            Write24(w, Width - 1);
            Write24(w, Height - 1);
        });
        Chunk(body, "ANIM", w =>
        {
            w.Write([0, 0, 0, 0]);
            w.Write([0, 0]);
        });
        foreach (var frame in _frames)
            Chunk(body, "ANMF", w =>
            {
                Write24(w, frame.X / 2);
                Write24(w, frame.Y / 2);
                Write24(w, frame.Width - 1);
                Write24(w, frame.Height - 1);
                Write24(w, frame.Duration);
                // Bit 1: do not blend (the first, whole one); bit 0: dispose (never).
                w.WriteByte(frame.Blend ? (byte)0 : (byte)2);
                Chunk(w, "VP8L", inner => inner.Write(frame.Data));
            });
        using var file = new MemoryStream();
        file.Write("RIFF"u8);
        Write32(file, (int)body.Length + 4);
        file.Write("WEBP"u8);
        body.Position = 0;
        body.CopyTo(file);
        return file.ToArray();
    }

    private static void Chunk(Stream stream, string fourCc, Action<Stream> write)
    {
        using var payload = new MemoryStream();
        write(payload);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));
        Write32(stream, (int)payload.Length);
        payload.Position = 0;
        payload.CopyTo(stream);
        if (payload.Length % 2 == 1) stream.WriteByte(0);
    }

    private static void Write24(Stream s, int value)
    {
        s.WriteByte((byte)value);
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)(value >> 16));
    }

    private static void Write32(Stream s, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        s.Write(bytes);
    }
}

/// <summary>
/// A lossless WebP image stream (VP8L, RFC 9649): subtract-green and predictor transforms (a mode for every 16 × 16 block,
/// the one that leaves the smallest remainders), backward references found with hash chains, and length-limited prefix
/// codes. No colour cache and no meta prefix codes: simple, and good enough for pictures of a desktop.
/// </summary>
internal static class Vp8lEncoder
{
    private const int MaxLength = 4096;
    private const int MinMatch = 3;
    private const int ChainDepth = 24;
    private const int HashBits = 16;
    private const int Window = (1 << 20) - 120;
    private const int PredictorBits = 4;

    /// <summary>Order in which the lengths of the code-length code are written.</summary>
    internal static readonly int[] CodeLengthOrder = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    public static byte[] Encode(uint[] argb, int width, int height, bool alpha)
    {
        var w = new BitWriter();
        w.Write(8, 0x2F);
        w.Write(14, (uint)(width - 1));
        w.Write(14, (uint)(height - 1));
        w.Write(1, alpha ? 1u : 0u);
        w.Write(3, 0);

        var pixels = (uint[])argb.Clone();
        // Subtract green.
        w.Write(1, 1);
        w.Write(2, 2);
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            uint g = (p >> 8) & 0xFF;
            var r = ((p >> 16) - g) & 0xFF;
            var b = (p - g) & 0xFF;
            pixels[i] = (p & 0xFF00FF00) | (r << 16) | b;
        }
        // Predictor, when the picture is big enough for it to pay.
        if (width >= 16 && height >= 16)
        {
            w.Write(1, 1);
            w.Write(2, 0);
            w.Write(3, PredictorBits - 2);
            var modes = Predict(pixels, width, height, out var residuals);
            int bw = (width + (1 << PredictorBits) - 1) >> PredictorBits, bh = (height + (1 << PredictorBits) - 1) >> PredictorBits;
            WriteImage(w, modes, bw, bh, top: false);
            pixels = residuals;
        }
        w.Write(1, 0);
        WriteImage(w, pixels, width, height, top: true);
        return w.ToArray();
    }

    /// <summary>An entropy-coded image: no colour cache, (at the top level) no meta prefix codes, five prefix codes and its data.</summary>
    private static void WriteImage(BitWriter w, uint[] pixels, int width, int height, bool top)
    {
        w.Write(1, 0); // no colour cache
        if (top) w.Write(1, 0); // no meta prefix codes
        var tokens = Lz77(pixels, width);
        var green = new int[256 + 24];
        var red = new int[256];
        var blue = new int[256];
        var alphaFreq = new int[256];
        var distance = new int[40];
        foreach (var t in tokens)
        {
            if (t.Length == 0)
            {
                green[(t.Value >> 8) & 0xFF]++;
                red[(t.Value >> 16) & 0xFF]++;
                blue[t.Value & 0xFF]++;
                alphaFreq[t.Value >> 24]++;
            }
            else
            {
                green[256 + Prefix(t.Length, out _, out _)]++;
                distance[Prefix((int)t.Value + 120, out _, out _)]++;
            }
        }
        var g = WriteCode(w, green);
        var r = WriteCode(w, red);
        var b = WriteCode(w, blue);
        var a = WriteCode(w, alphaFreq);
        var d = WriteCode(w, distance);
        foreach (var t in tokens)
        {
            if (t.Length == 0)
            {
                Put(w, g, (int)((t.Value >> 8) & 0xFF));
                Put(w, r, (int)((t.Value >> 16) & 0xFF));
                Put(w, b, (int)(t.Value & 0xFF));
                Put(w, a, (int)(t.Value >> 24));
                continue;
            }
            var lengthPrefix = Prefix(t.Length, out var lengthBits, out var lengthExtra);
            Put(w, g, 256 + lengthPrefix);
            w.Write(lengthBits, (uint)lengthExtra);
            var distancePrefix = Prefix((int)t.Value + 120, out var distanceBits, out var distanceExtra);
            Put(w, d, distancePrefix);
            w.Write(distanceBits, (uint)distanceExtra);
        }
    }

    private readonly record struct Token(uint Value, int Length);

    /// <summary>Literals and backward references (Value = distance in pixels, Length &gt; 0).</summary>
    private static List<Token> Lz77(uint[] pixels, int width)
    {
        var tokens = new List<Token>(pixels.Length / 2);
        var head = new int[1 << HashBits];
        Array.Fill(head, -1);
        var previous = new int[pixels.Length];
        var n = pixels.Length;
        static int Hash(uint a, uint b) => (int)(((a * 0x9E3779B1u) ^ (b * 0x85EBCA77u)) >> (32 - HashBits));
        void Insert(int at)
        {
            if (at + 1 >= n) return;
            var h = Hash(pixels[at], pixels[at + 1]);
            previous[at] = head[h];
            head[h] = at;
        }
        var i = 0;
        while (i < n)
        {
            int bestLength = 0, bestDistance = 0;
            if (i + MinMatch <= n)
            {
                var max = Math.Min(MaxLength, n - i);
                // The pixel before and the one above first: runs and repeated rows are the usual matches.
                foreach (var candidate in new[] { i - 1, i - width })
                    if (candidate >= 0) Try(candidate);
                var chain = head[Hash(pixels[i], pixels[i + 1])];
                for (var depth = 0; chain >= 0 && depth < ChainDepth && bestLength < max; depth++, chain = previous[chain])
                    Try(chain);

                void Try(int from)
                {
                    if (i - from > Window || pixels[from] != pixels[i]) return;
                    var length = 1;
                    while (length < max && pixels[from + length] == pixels[i + length]) length++;
                    if (length > bestLength)
                    {
                        bestLength = length;
                        bestDistance = i - from;
                    }
                }
            }
            if (bestLength >= MinMatch)
            {
                tokens.Add(new Token((uint)bestDistance, bestLength));
                for (var k = 0; k < bestLength; k++) Insert(i + k);
                i += bestLength;
            }
            else
            {
                tokens.Add(new Token(pixels[i], 0));
                Insert(i);
                i++;
            }
        }
        return tokens;
    }

    /// <summary>The prefix code of a value (lengths and distances, ≥ 1), with its extra bits.</summary>
    internal static int Prefix(int value, out int extraBits, out int extra)
    {
        var d = value - 1;
        if (d < 4)
        {
            extraBits = 0;
            extra = 0;
            return d;
        }
        var high = 31 - BitOperations.LeadingZeroCount((uint)d);
        var second = (d >> (high - 1)) & 1;
        extraBits = high - 1;
        extra = d & ((1 << extraBits) - 1);
        return 2 * high + second;
    }

    /// <summary>A mode for every block (the one with the smallest remainders) and the remainders it leaves.</summary>
    private static uint[] Predict(uint[] pixels, int width, int height, out uint[] residuals)
    {
        int size = 1 << PredictorBits, bw = (width + size - 1) / size, bh = (height + size - 1) / size;
        var modes = new uint[bw * bh];
        int[] tried = [1, 2, 11, 12, 13, 7];
        residuals = new uint[pixels.Length];
        for (var by = 0; by < bh; by++)
            for (var bx = 0; bx < bw; bx++)
            {
                int best = 1;
                long bestCost = long.MaxValue;
                foreach (var mode in tried)
                {
                    long cost = 0;
                    for (var y = by * size; y < Math.Min(height, (by + 1) * size) && cost < bestCost; y++)
                        for (var x = bx * size; x < Math.Min(width, (bx + 1) * size); x++)
                            cost += Cost(Sub(pixels[y * width + x], Vp8lPredictor.Predict(pixels, width, x, y, mode)));
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = mode;
                    }
                }
                modes[by * bw + bx] = 0xFF000000u | ((uint)best << 8);
            }
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var mode = (int)((modes[(y >> PredictorBits) * bw + (x >> PredictorBits)] >> 8) & 0xF);
                residuals[y * width + x] = Sub(pixels[y * width + x], Vp8lPredictor.Predict(pixels, width, x, y, mode));
            }
        return modes;

        static long Cost(uint r)
        {
            static int C(uint v) => (int)Math.Min(v & 0xFF, 256 - (v & 0xFF));
            return C(r) + C(r >> 8) + C(r >> 16) + C(r >> 24);
        }
    }

    private static uint Sub(uint a, uint b) =>
        (((a >> 24) - (b >> 24)) & 0xFF) << 24 | (((a >> 16) - (b >> 16)) & 0xFF) << 16 | (((a >> 8) - (b >> 8)) & 0xFF) << 8 | ((a - b) & 0xFF);

    // ── prefix codes ────────────────────────────────────────────────────────

    /// <summary>What a prefix code writes: each symbol's code (bits already reversed) and length.</summary>
    private sealed record Code(uint[] Codes, int[] Lengths);

    private static void Put(BitWriter w, Code code, int symbol)
    {
        if (code.Lengths[symbol] > 0) w.Write(code.Lengths[symbol], code.Codes[symbol]);
    }

    /// <summary>Writes the code for these frequencies (simple when it can) and returns it.</summary>
    private static Code WriteCode(BitWriter w, int[] frequencies)
    {
        var used = Enumerable.Range(0, frequencies.Length).Where(s => frequencies[s] > 0).ToList();
        var lengths = new int[frequencies.Length];
        if (used.Count <= 2 && used.All(s => s < 256))
        {
            // Simple code: one symbol costs no bits; two, one bit each.
            if (used.Count == 0) used.Add(0);
            w.Write(1, 1);
            w.Write(1, (uint)(used.Count - 1));
            var first8 = used[0] > 1;
            w.Write(1, first8 ? 1u : 0u);
            w.Write(first8 ? 8 : 1, (uint)used[0]);
            if (used.Count == 2)
            {
                w.Write(8, (uint)used[1]);
                lengths[used[0]] = lengths[used[1]] = 1;
            }
            return new Code(Canonical(lengths), lengths);
        }
        if (used.Count == 1)
        {
            lengths[used[0]] = 1;
            lengths[used[0] == 0 ? 1 : 0] = 1;
        }
        else lengths = Lengths(frequencies, 15);

        // Normal code: the lengths, run-length coded with the code-length code.
        w.Write(1, 0);
        var tokens = new List<(int Symbol, int Extra, int Bits)>();
        for (var i = 0; i < lengths.Length;)
        {
            var value = lengths[i];
            var run = 1;
            while (i + run < lengths.Length && lengths[i + run] == value) run++;
            if (value == 0)
            {
                var left = run;
                while (left >= 11)
                {
                    var take = Math.Min(left, 138);
                    tokens.Add((18, take - 11, 7));
                    left -= take;
                }
                if (left >= 3)
                {
                    tokens.Add((17, left - 3, 3));
                    left = 0;
                }
                for (; left > 0; left--) tokens.Add((0, 0, 0));
            }
            else
            {
                tokens.Add((value, 0, 0));
                var left = run - 1;
                while (left >= 3)
                {
                    var take = Math.Min(left, 6);
                    tokens.Add((16, take - 3, 2));
                    left -= take;
                }
                for (; left > 0; left--) tokens.Add((value, 0, 0));
            }
            i += run;
        }
        var clFrequencies = new int[19];
        foreach (var t in tokens) clFrequencies[t.Symbol]++;
        var clUsed = clFrequencies.Count(f => f > 0);
        int[] clLengths;
        if (clUsed == 1)
        {
            clLengths = new int[19];
            var only = Array.FindIndex(clFrequencies, f => f > 0);
            clLengths[only] = 1;
            clLengths[only == 0 ? 1 : 0] = 1;
        }
        else clLengths = Lengths(clFrequencies, 7);
        var clCodes = Canonical(clLengths);
        var count = 19;
        while (count > 4 && clLengths[CodeLengthOrder[count - 1]] == 0) count--;
        w.Write(4, (uint)(count - 4));
        for (var i = 0; i < count; i++) w.Write(3, (uint)clLengths[CodeLengthOrder[i]]);
        w.Write(1, 0); // every symbol of the alphabet is listed
        foreach (var (symbol, extra, bits) in tokens)
        {
            w.Write(clLengths[symbol], clCodes[symbol]);
            if (bits > 0) w.Write(bits, (uint)extra);
        }
        return new Code(Canonical(lengths), lengths);
    }

    /// <summary>Huffman code lengths of at most <paramref name="maxBits"/> (frequencies halved until they fit).</summary>
    internal static int[] Lengths(int[] frequencies, int maxBits)
    {
        var counts = (int[])frequencies.Clone();
        while (true)
        {
            var lengths = new int[counts.Length];
            var queue = new PriorityQueue<int, (long, int)>();
            var weights = new List<long>();
            var parents = new List<int>();
            var order = 0;
            for (var s = 0; s < counts.Length; s++)
            {
                if (counts[s] <= 0) continue;
                weights.Add(counts[s]);
                parents.Add(-1);
                queue.Enqueue(weights.Count - 1, (counts[s], order++));
            }
            var leaves = weights.Count;
            while (queue.Count > 1)
            {
                queue.TryDequeue(out var a, out var pa);
                queue.TryDequeue(out var b, out var pb);
                weights.Add(pa.Item1 + pb.Item1);
                parents.Add(-1);
                var node = weights.Count - 1;
                parents[a] = node;
                parents[b] = node;
                queue.Enqueue(node, (weights[node], order++));
            }
            var maxDepth = 0;
            var leaf = 0;
            for (var s = 0; s < counts.Length; s++)
            {
                if (counts[s] <= 0) continue;
                var depth = 0;
                for (var n = leaf; parents[n] >= 0; n = parents[n]) depth++;
                lengths[s] = depth;
                maxDepth = Math.Max(maxDepth, depth);
                leaf++;
            }
            if (leaves > 0 && maxDepth <= maxBits) return lengths;
            for (var s = 0; s < counts.Length; s++)
                if (counts[s] > 0) counts[s] = Math.Max(1, counts[s] / 2);
        }
    }

    /// <summary>Canonical codes for these lengths, with their bits reversed (the stream is read from its lowest bit).</summary>
    internal static uint[] Canonical(int[] lengths)
    {
        var max = lengths.Length == 0 ? 0 : lengths.Max();
        var count = new int[max + 2];
        foreach (var l in lengths)
            if (l > 0) count[l]++;
        var next = new uint[max + 2];
        uint code = 0;
        for (var bits = 1; bits <= max; bits++)
        {
            code = (code + (uint)count[bits - 1]) << 1;
            next[bits] = code;
        }
        var codes = new uint[lengths.Length];
        for (var s = 0; s < lengths.Length; s++)
        {
            var l = lengths[s];
            if (l == 0) continue;
            var c = next[l]++;
            uint reversed = 0;
            for (var k = 0; k < l; k++) reversed |= ((c >> k) & 1) << (l - 1 - k);
            codes[s] = reversed;
        }
        return codes;
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new(1024);
        private ulong _bits;
        private int _count;

        public void Write(int bits, uint value)
        {
            if (bits == 0) return;
            _bits |= (ulong)(value & (bits == 32 ? uint.MaxValue : (1u << bits) - 1)) << _count;
            _count += bits;
            while (_count >= 8)
            {
                _bytes.Add((byte)_bits);
                _bits >>= 8;
                _count -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_count > 0) _bytes.Add((byte)_bits);
            _bits = 0;
            _count = 0;
            return _bytes.ToArray();
        }
    }
}

/// <summary>The predictors of VP8L (shared by the encoder and the decoder).</summary>
internal static class Vp8lPredictor
{
    /// <summary>What mode <paramref name="mode"/> predicts for the pixel at x, y (from the pixels already there).</summary>
    public static uint Predict(uint[] p, int width, int x, int y, int mode)
    {
        if (y == 0) return x == 0 ? 0xFF000000u : p[x - 1];
        if (x == 0) return p[(y - 1) * width];
        var i = y * width + x;
        uint l = p[i - 1], t = p[i - width], tl = p[i - width - 1], tr = p[i - width + 1];
        return mode switch
        {
            0 => 0xFF000000u,
            1 => l,
            2 => t,
            3 => tr,
            4 => tl,
            5 => Average(Average(l, tr), t),
            6 => Average(l, tl),
            7 => Average(l, t),
            8 => Average(tl, t),
            9 => Average(t, tr),
            10 => Average(Average(l, tl), Average(t, tr)),
            11 => Select(l, t, tl),
            12 => ClampFull(l, t, tl),
            13 => ClampHalf(Average(l, t), tl),
            _ => 0xFF000000u,
        };
    }

    private static uint Average(uint a, uint b) => (((a ^ b) & 0xFEFEFEFEu) >> 1) + (a & b);

    private static int C(uint v, int shift) => (int)((v >> shift) & 0xFF);

    private static uint Select(uint l, uint t, uint tl)
    {
        int pL = 0, pT = 0;
        for (var s = 0; s < 32; s += 8)
        {
            var estimate = C(l, s) + C(t, s) - C(tl, s);
            pL += Math.Abs(estimate - C(l, s));
            pT += Math.Abs(estimate - C(t, s));
        }
        return pL < pT ? l : t;
    }

    private static uint ClampFull(uint a, uint b, uint c)
    {
        uint result = 0;
        for (var s = 0; s < 32; s += 8) result |= (uint)Math.Clamp(C(a, s) + C(b, s) - C(c, s), 0, 255) << s;
        return result;
    }

    private static uint ClampHalf(uint a, uint b)
    {
        uint result = 0;
        for (var s = 0; s < 32; s += 8) result |= (uint)Math.Clamp(C(a, s) + (C(a, s) - C(b, s)) / 2, 0, 255) << s;
        return result;
    }
}
