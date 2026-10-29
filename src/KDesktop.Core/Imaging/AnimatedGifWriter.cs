using System.Buffers.Binary;

namespace KDesktop.Core.Imaging;

/// <summary>
/// An animated GIF for «Grabar vídeo» in GIF (F3), made like <see cref="AnimatedWebPWriter"/>: the first frame is the whole
/// picture; each one after it only the rectangle that changed, with what stayed the same transparent over the frame before.
/// A frame equal to the one before only makes that one last longer. Each frame has its own palette: its exact colours when
/// they fit in 256 (a desktop usually does), or a median cut of them otherwise, without dithering (text stays sharp).
/// </summary>
public sealed class AnimatedGifWriter(int width, int height)
{
    private readonly List<Frame> _frames = [];
    private uint[]? _previous;

    private sealed class Frame
    {
        public int X, Y, Width, Height, Duration, Transparent = -1;
        public byte[] Palette = [];
        public int TableBits;
        public byte[] Data = [];
    }

    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>Frames in the file (a picture equal to the last one does not count).</summary>
    public int FrameCount => _frames.Count;

    /// <summary>Adds a picture (BGRA, rows of Width × 4 bytes) that shows for <paramref name="milliseconds"/>.</summary>
    public void Add(ReadOnlySpan<byte> bgra, int milliseconds)
    {
        if (bgra.Length < Width * Height * 4) throw new ArgumentException("La imagen es más pequeña que el GIF", nameof(bgra));
        milliseconds = Math.Max(1, milliseconds);
        var pixels = new uint[Width * Height];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = BinaryPrimitives.ReadUInt32LittleEndian(bgra.Slice(i * 4, 4)) | 0xFF000000u;

        int left = 0, top = 0, right = Width - 1, bottom = Height - 1;
        if (_previous is not null)
        {
            left = Width;
            top = Height;
            right = bottom = -1;
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
                _frames[^1].Duration += milliseconds;
                return;
            }
        }

        int w = right - left + 1, h = bottom - top + 1;
        var part = new uint[w * h];
        var anySame = false;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (top + y) * Width + left + x;
                // 0 (transparent black) never comes from the screen: every pixel is opaque.
                if (_previous is not null && pixels[i] == _previous[i]) anySame = true;
                else part[y * w + x] = pixels[i];
            }

        var (palette, indices, transparent) = Quantize(part, anySame);
        var bits = 1;
        while (1 << bits < palette.Length + (transparent >= 0 ? 1 : 0)) bits++;
        var table = new byte[(1 << bits) * 3];
        for (var i = 0; i < palette.Length; i++)
        {
            table[i * 3] = (byte)(palette[i] >> 16);
            table[i * 3 + 1] = (byte)(palette[i] >> 8);
            table[i * 3 + 2] = (byte)palette[i];
        }
        var codeSize = Math.Max(2, bits);
        _frames.Add(new Frame
        {
            X = left, Y = top, Width = w, Height = h, Duration = milliseconds, Transparent = transparent,
            Palette = table, TableBits = bits, Data = Lzw(indices, codeSize),
        });
        _previous = pixels;
    }

    /// <summary>The file, looping forever.</summary>
    public byte[] ToArray()
    {
        if (_frames.Count == 0) throw new InvalidOperationException("El GIF no tiene imágenes");
        using var file = new MemoryStream();
        file.Write("GIF89a"u8);
        Write16(file, Width);
        Write16(file, Height);
        file.Write([0, 0, 0]); // no global colour table
        // Loop forever (NETSCAPE2.0).
        file.Write([0x21, 0xFF, 0x0B]);
        file.Write("NETSCAPE2.0"u8);
        file.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        // GIF counts in hundredths: what is lost rounding one frame goes to the next, so the length stays right.
        var owed = 0;
        foreach (var frame in _frames)
        {
            var total = frame.Duration + owed;
            var delay = Math.Max(2, (int)Math.Round(total / 10.0)); // under 2, browsers play it slower
            owed = total - delay * 10;
            // Graphic control: keep the frame under it (disposal 1), and its transparent colour.
            file.Write([0x21, 0xF9, 0x04, (byte)(0x04 | (frame.Transparent >= 0 ? 1 : 0))]);
            Write16(file, Math.Min(0xFFFF, delay));
            file.WriteByte((byte)Math.Max(0, frame.Transparent));
            file.WriteByte(0);
            file.WriteByte(0x2C);
            Write16(file, frame.X);
            Write16(file, frame.Y);
            Write16(file, frame.Width);
            Write16(file, frame.Height);
            file.WriteByte((byte)(0x80 | (frame.TableBits - 1)));
            file.Write(frame.Palette);
            file.Write(frame.Data);
        }
        file.WriteByte(0x3B);
        return file.ToArray();
    }

    // ── colours ────────────────────────────────────────────────────────────

    /// <summary>The palette, each pixel's index in it, and the transparent index (−1 when none; 0 pixels are transparent).</summary>
    private static (uint[] Palette, byte[] Indices, int Transparent) Quantize(uint[] pixels, bool transparent)
    {
        var room = transparent ? 255 : 256;
        var indices = new byte[pixels.Length];

        // The exact colours, when they fit (a desktop usually has few).
        var exact = new Dictionary<uint, byte>();
        foreach (var p in pixels)
        {
            if (p == 0 || exact.ContainsKey(p)) continue;
            if (exact.Count == room) { exact = null; break; }
            exact[p] = (byte)exact.Count;
        }
        if (exact is not null)
        {
            var colors = exact.Count;
            for (var i = 0; i < pixels.Length; i++) indices[i] = pixels[i] == 0 ? (byte)colors : exact[pixels[i]];
            var palette = new uint[colors];
            foreach (var (color, index) in exact) palette[index] = color;
            return (palette, indices, transparent ? colors : -1);
        }

        // Too many: a median cut of a 5-bit-per-channel histogram.
        var counts = new int[32768];
        var sums = new long[32768 * 3];
        foreach (var p in pixels)
        {
            if (p == 0) continue;
            var bin = Bin(p);
            counts[bin]++;
            sums[bin * 3] += (p >> 16) & 0xFF;
            sums[bin * 3 + 1] += (p >> 8) & 0xFF;
            sums[bin * 3 + 2] += p & 0xFF;
        }
        var bins = new List<int>();
        for (var b = 0; b < counts.Length; b++) if (counts[b] > 0) bins.Add(b);
        var boxes = MedianCut(bins, counts, room);
        var lut = new byte[32768];
        var result = new uint[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            long r = 0, g = 0, b = 0, n = 0;
            foreach (var bin in boxes[i])
            {
                lut[bin] = (byte)i;
                r += sums[bin * 3];
                g += sums[bin * 3 + 1];
                b += sums[bin * 3 + 2];
                n += counts[bin];
            }
            result[i] = 0xFF000000u | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
        }
        for (var i = 0; i < pixels.Length; i++) indices[i] = pixels[i] == 0 ? (byte)result.Length : lut[Bin(pixels[i])];
        return (result, indices, transparent ? result.Length : -1);
    }

    private static int Bin(uint p) => (int)(((p >> 19) & 0x1F) << 10 | ((p >> 11) & 0x1F) << 5 | ((p >> 3) & 0x1F));

    private static int Channel(int bin, int channel) => (bin >> (10 - channel * 5)) & 0x1F;

    /// <summary>Splits the colours in up to <paramref name="most"/> boxes: always the busiest box that still has a spread, at its median.</summary>
    private static List<List<int>> MedianCut(List<int> bins, int[] counts, int most)
    {
        var boxes = new List<List<int>> { bins };
        while (boxes.Count < most)
        {
            int best = -1, channel = 0;
            long score = 0;
            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Count < 2) continue;
                long population = 0;
                var (range, widest) = (0, 0);
                for (var c = 0; c < 3; c++)
                {
                    int lo = 31, hi = 0;
                    foreach (var bin in box)
                    {
                        var v = Channel(bin, c);
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (hi - lo > range) (range, widest) = (hi - lo, c);
                }
                foreach (var bin in box) population += counts[bin];
                if (range > 0 && population * range > score) (score, best, channel) = (population * range, i, widest);
            }
            if (best < 0) break;
            var split = boxes[best];
            split.Sort((a, b) => Channel(a, channel).CompareTo(Channel(b, channel)));
            long half = 0, total = 0;
            foreach (var bin in split) total += counts[bin];
            var at = 0;
            while (at < split.Count - 1 && (half += counts[split[at]]) < total / 2) at++;
            at = Math.Clamp(at + 1, 1, split.Count - 1);
            boxes[best] = split.GetRange(0, at);
            boxes.Add(split.GetRange(at, split.Count - at));
        }
        return boxes;
    }

    // ── LZW, as GIF wants it ───────────────────────────────────────────────

    /// <summary>The image data: the minimum code size, the codes in blocks of up to 255 bytes and the empty block.</summary>
    private static byte[] Lzw(byte[] indices, int minCodeSize)
    {
        const int HashSize = 5003, MaxCode = 4096;
        var output = new MemoryStream();
        output.WriteByte((byte)minCodeSize);
        var block = new byte[255];
        var blockLength = 0;
        int bits = 0, bitCount = 0;
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        var codeSize = minCodeSize + 1;
        var next = clear + 2;
        var resetting = false;
        var keys = new int[HashSize];
        var codes = new int[HashSize];
        Array.Fill(keys, -1);

        void Flush()
        {
            if (blockLength == 0) return;
            output.WriteByte((byte)blockLength);
            output.Write(block, 0, blockLength);
            blockLength = 0;
        }

        void Emit(int code)
        {
            bits |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                block[blockLength++] = (byte)bits;
                if (blockLength == 255) Flush();
                bits >>= 8;
                bitCount -= 8;
            }
            // As the decoder: the code size grows once the next code no longer fits (and goes back after a clear).
            if (resetting)
            {
                codeSize = minCodeSize + 1;
                resetting = false;
            }
            else if (next > (1 << codeSize) - 1 && codeSize < 12) codeSize++;
        }

        Emit(clear);
        if (indices.Length > 0)
        {
            var prefix = (int)indices[0];
            for (var i = 1; i < indices.Length; i++)
            {
                int k = indices[i];
                var key = (k << 12) | prefix;
                var slot = (int)((uint)(key * 2654435761u) % HashSize);
                var found = false;
                while (keys[slot] >= 0)
                {
                    if (keys[slot] == key)
                    {
                        prefix = codes[slot];
                        found = true;
                        break;
                    }
                    if (++slot == HashSize) slot = 0;
                }
                if (found) continue;
                Emit(prefix);
                prefix = k;
                if (next < MaxCode)
                {
                    keys[slot] = key;
                    codes[slot] = next++;
                }
                else
                {
                    // The table is full: start it again.
                    Array.Fill(keys, -1);
                    next = clear + 2;
                    resetting = true;
                    Emit(clear);
                }
            }
            Emit(prefix);
        }
        Emit(end);
        if (bitCount > 0) block[blockLength++] = (byte)bits;
        Flush();
        output.WriteByte(0);
        return output.ToArray();
    }

    private static void Write16(Stream s, int value)
    {
        s.WriteByte((byte)value);
        s.WriteByte((byte)(value >> 8));
    }
}
