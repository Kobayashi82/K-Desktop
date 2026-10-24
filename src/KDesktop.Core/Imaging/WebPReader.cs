using System.Buffers.Binary;

namespace KDesktop.Core.Imaging;

/// <summary>One picture of an animation: BGRA, not premultiplied, the whole canvas, and how long it shows.</summary>
public sealed record WebPFrame(byte[] Bgra, int Milliseconds);

/// <summary>A WebP read whole: its canvas and its frames (one for a still picture).</summary>
public sealed record WebPAnimation(int Width, int Height, IReadOnlyList<WebPFrame> Frames);

/// <summary>
/// Reads lossless WebP (VP8L, RFC 9649), still or animated (VP8X, ANIM, ANMF), into whole pictures, for the clips of the
/// catalog (stage 16.3). Lossy frames (VP8) are not read: <see cref="NotSupportedException"/>, and whoever shows it falls
/// back to Windows' own decoder (its first picture).
/// </summary>
public static class WebPReader
{
    /// <summary>The largest canvas read (pixels), so a broken file cannot ask for gigabytes.</summary>
    private const long MaxPixels = 4096L * 4096;

    private const int MaxFrames = 2000;

    public static bool IsWebP(ReadOnlySpan<byte> head) =>
        head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8);

    /// <summary>Every frame as a picture of its own (for small clips: a long one at full size takes a lot of memory; see <see cref="Open"/>).</summary>
    public static WebPAnimation Read(byte[] file)
    {
        var (width, height, frames) = Open(file);
        var all = frames.Select(f => f with { Bgra = (byte[])f.Bgra.Clone() }).ToList();
        if (all.Count == 0) throw new InvalidDataException("El WebP no tiene imágenes");
        return new WebPAnimation(width, height, all);
    }

    /// <summary>
    /// Its canvas and its frames, decoded one by one as they are walked (again from the start each time they are walked).
    /// Every frame is the same canvas, changed: copy it before taking the next one. Throws as <see cref="Read"/> when the
    /// file is not one it reads (before the first frame, or while walking them).
    /// </summary>
    public static (int Width, int Height, IEnumerable<WebPFrame> Frames) Open(byte[] file)
    {
        if (!IsWebP(file)) throw new InvalidDataException("No es un WebP");
        var end = (int)Math.Min(file.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)));
        var pos = 12;
        while (pos + 8 <= end)
        {
            var (fourCc, size) = ChunkAt(file, pos, end);
            var data = file.AsSpan(pos + 8, size);
            switch (fourCc)
            {
                case "VP8X":
                {
                    int width = Read24(data, 4) + 1, height = Read24(data, 7) + 1;
                    Check(width, height);
                    return (width, height, Animation(file, pos + 8 + size + (size & 1), end, width, height));
                }
                case "VP8L":
                {
                    var (w, h, argb) = Vp8lDecoder.Decode(data);
                    return (w, h, [new WebPFrame(ToBgra(argb), 0)]);
                }
                case "VP8 ":
                    throw new NotSupportedException("WebP con pérdida");
            }
            pos += 8 + size + (size & 1);
        }
        throw new InvalidDataException("El WebP no tiene imágenes");
    }

    private static (string FourCc, int Size) ChunkAt(byte[] file, int pos, int end) =>
        (System.Text.Encoding.ASCII.GetString(file, pos, 4), (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos + 4)), (uint)(end - pos - 8)));

    /// <summary>The frames after VP8X (a still picture there, as the only frame).</summary>
    private static IEnumerable<WebPFrame> Animation(byte[] file, int pos, int end, int width, int height)
    {
        var canvas = new byte[width * height * 4];
        var count = 0;
        while (pos + 8 <= end && count < MaxFrames)
        {
            var (fourCc, size) = ChunkAt(file, pos, end);
            if (fourCc == "ANMF")
            {
                var (duration, dispose, area) = DrawFrame(file, pos + 8, size, canvas, width, height);
                count++;
                yield return new WebPFrame(canvas, duration);
                // Disposed: the rectangle goes back to transparent for the next one.
                if (dispose) Clear(canvas, width, height, area.X, area.Y, area.W, area.H);
            }
            else if (fourCc is "VP8L" or "VP8 " or "ALPH")
            {
                // An extended still picture (with VP8X but no animation).
                var argb = FrameImage(file.AsSpan(pos, end - pos), width, height);
                count++;
                yield return new WebPFrame(ToBgra(argb), 0);
                yield break;
            }
            pos += 8 + size + (size & 1);
        }
        if (count == 0) throw new InvalidDataException("El WebP no tiene imágenes");
    }

    private static (int Duration, bool Dispose, (int X, int Y, int W, int H) Area) DrawFrame(byte[] file, int at, int size, byte[] canvas, int cw, int ch)
    {
        var data = file.AsSpan(at, size);
        int x = Read24(data, 0) * 2, y = Read24(data, 3) * 2, w = Read24(data, 6) + 1, h = Read24(data, 9) + 1;
        var duration = Read24(data, 12);
        var flags = data[15];
        var argb = FrameImage(data[16..], w, h);
        Draw(canvas, cw, ch, argb, x, y, w, h, blend: (flags & 2) == 0);
        return (duration, (flags & 1) != 0, (x, y, w, h));
    }

    private static uint[] FrameImage(ReadOnlySpan<byte> chunks, int w, int h)
    {
        var pos = 0;
        while (pos + 8 <= chunks.Length)
        {
            var fourCc = System.Text.Encoding.ASCII.GetString(chunks.Slice(pos, 4));
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(chunks[(pos + 4)..]), (uint)(chunks.Length - pos - 8));
            if (fourCc == "VP8L")
            {
                var (fw, fh, argb) = Vp8lDecoder.Decode(chunks.Slice(pos + 8, size));
                if (fw != w || fh != h) throw new InvalidDataException("El tamaño de la imagen no es el del fotograma");
                return argb;
            }
            if (fourCc is "VP8 " or "ALPH") throw new NotSupportedException("WebP con pérdida");
            pos += 8 + size + (size & 1);
        }
        throw new InvalidDataException("Fotograma sin imagen");
    }

    private static void Check(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) throw new InvalidDataException("El WebP es demasiado grande");
    }

    private static int Read24(ReadOnlySpan<byte> data, int at) => data[at] | data[at + 1] << 8 | data[at + 2] << 16;

    private static byte[] ToBgra(uint[] argb)
    {
        var bytes = new byte[argb.Length * 4];
        for (var i = 0; i < argb.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), argb[i]);
        return bytes;
    }

    /// <summary>A frame onto the canvas: replacing what is under it, or blended over it (straight alpha).</summary>
    private static void Draw(byte[] canvas, int cw, int ch, uint[] argb, int fx, int fy, int w, int h, bool blend)
    {
        for (var y = 0; y < h; y++)
        {
            var cy = fy + y;
            if (cy >= ch) break;
            for (var x = 0; x < w; x++)
            {
                var cx = fx + x;
                if (cx >= cw) break;
                var src = argb[y * w + x];
                var at = (cy * cw + cx) * 4;
                var sa = src >> 24;
                if (!blend || sa == 255)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(canvas.AsSpan(at), src);
                    continue;
                }
                if (sa == 0) continue;
                var dst = BinaryPrimitives.ReadUInt32LittleEndian(canvas.AsSpan(at));
                var da = dst >> 24;
                var outA = sa + da * (255 - sa) / 255;
                if (outA == 0) continue;
                uint Mix(int shift)
                {
                    var s = (src >> shift) & 0xFF;
                    var d = (dst >> shift) & 0xFF;
                    return (s * sa + d * da * (255 - sa) / 255) / outA;
                }
                BinaryPrimitives.WriteUInt32LittleEndian(canvas.AsSpan(at), outA << 24 | Mix(16) << 16 | Mix(8) << 8 | Mix(0));
            }
        }
    }

    private static void Clear(byte[] canvas, int cw, int ch, int fx, int fy, int w, int h)
    {
        for (var y = fy; y < Math.Min(ch, fy + h); y++)
            canvas.AsSpan((y * cw + fx) * 4, Math.Max(0, Math.Min(w, cw - fx)) * 4).Clear();
    }
}

/// <summary>The lossless WebP bitstream (VP8L, RFC 9649) into ARGB pixels: every transform, colour cache and meta prefix codes.</summary>
internal sealed class Vp8lDecoder
{
    private const int MaxPixels = 4096 * 4096;

    /// <summary>The two-dimensional distance codes (x, y), for the first 120 distance values.</summary>
    private static readonly sbyte[] Plane =
    [
        0, 1, 1, 0, 1, 1, -1, 1, 0, 2, 2, 0, 1, 2, -1, 2,
        2, 1, -2, 1, 2, 2, -2, 2, 0, 3, 3, 0, 1, 3, -1, 3,
        3, 1, -3, 1, 2, 3, -2, 3, 3, 2, -3, 2, 0, 4, 4, 0,
        1, 4, -1, 4, 4, 1, -4, 1, 3, 3, -3, 3, 2, 4, -2, 4,
        4, 2, -4, 2, 0, 5, 3, 4, -3, 4, 4, 3, -4, 3, 5, 0,
        1, 5, -1, 5, 5, 1, -5, 1, 2, 5, -2, 5, 5, 2, -5, 2,
        4, 4, -4, 4, 3, 5, -3, 5, 5, 3, -5, 3, 0, 6, 6, 0,
        1, 6, -1, 6, 6, 1, -6, 1, 2, 6, -2, 6, 6, 2, -6, 2,
        4, 5, -4, 5, 5, 4, -5, 4, 3, 6, -3, 6, 6, 3, -6, 3,
        0, 7, 7, 0, 1, 7, -1, 7, 5, 5, -5, 5, 7, 1, -7, 1,
        4, 6, -4, 6, 6, 4, -6, 4, 2, 7, -2, 7, 7, 2, -7, 2,
        3, 7, -3, 7, 7, 3, -7, 3, 5, 6, -5, 6, 6, 5, -6, 5,
        8, 0, 4, 7, -4, 7, 7, 4, -7, 4, 8, 1, 8, 2, 6, 6,
        -6, 6, 8, 3, 5, 7, -5, 7, 7, 5, -7, 5, 8, 4, 6, 7,
        -6, 7, 7, 6, -7, 6, 8, 5, 7, 7, -7, 7, 8, 6, 8, 7,
    ];

    private readonly byte[] _data;
    private int _pos;
    private ulong _bits;
    private int _count;

    private Vp8lDecoder(ReadOnlySpan<byte> data) => _data = data.ToArray();

    public static (int Width, int Height, uint[] Argb) Decode(ReadOnlySpan<byte> data)
    {
        var d = new Vp8lDecoder(data);
        if (d.Bits(8) != 0x2F) throw new InvalidDataException("No es VP8L");
        var width = (int)d.Bits(14) + 1;
        var height = (int)d.Bits(14) + 1;
        d.Bits(1);
        if (d.Bits(3) != 0) throw new InvalidDataException("Versión de VP8L desconocida");
        if ((long)width * height > MaxPixels) throw new InvalidDataException("La imagen es demasiado grande");
        return (width, height, d.ImageWithTransforms(width, height));
    }

    private uint Bits(int n)
    {
        if (n == 0) return 0;
        while (_count < n)
        {
            if (_pos >= _data.Length) throw new InvalidDataException("VP8L cortado");
            _bits |= (ulong)_data[_pos++] << _count;
            _count += 8;
        }
        var value = (uint)(_bits & ((1UL << n) - 1));
        _bits >>= n;
        _count -= n;
        return value;
    }

    private sealed record Transform(int Type, int Width, int Bits, uint[] Data);

    private uint[] ImageWithTransforms(int width, int height)
    {
        var transforms = new List<Transform>();
        var seen = 0;
        var xsize = width;
        while (Bits(1) == 1)
        {
            var type = (int)Bits(2);
            if ((seen & (1 << type)) != 0) throw new InvalidDataException("Transformación repetida");
            seen |= 1 << type;
            switch (type)
            {
                case 0:
                case 1:
                {
                    var bits = (int)Bits(3) + 2;
                    var sub = Image(Div(xsize, bits), Div(height, bits), top: false);
                    transforms.Add(new Transform(type, xsize, bits, sub));
                    break;
                }
                case 2:
                    transforms.Add(new Transform(2, xsize, 0, []));
                    break;
                default:
                {
                    var size = (int)Bits(8) + 1;
                    var table = Image(size, 1, top: false);
                    for (var i = 1; i < size; i++) table[i] = Add(table[i], table[i - 1]);
                    var bits = size <= 2 ? 3 : size <= 4 ? 2 : size <= 16 ? 1 : 0;
                    var full = new uint[256];
                    Array.Copy(table, full, size);
                    transforms.Add(new Transform(3, xsize, bits, full));
                    xsize = Div(xsize, bits);
                    break;
                }
            }
        }
        var pixels = Image(xsize, height, top: true);
        for (var t = transforms.Count - 1; t >= 0; t--) pixels = Invert(transforms[t], pixels, height);
        return pixels;
    }

    private static int Div(int size, int bits) => (size + (1 << bits) - 1) >> bits;

    private static uint Add(uint a, uint b) =>
        (((a >> 24) + (b >> 24)) & 0xFF) << 24 | (((a >> 16) + (b >> 16)) & 0xFF) << 16 | (((a >> 8) + (b >> 8)) & 0xFF) << 8 | ((a + b) & 0xFF);

    private static uint[] Invert(Transform t, uint[] pixels, int height)
    {
        var width = t.Width;
        switch (t.Type)
        {
            case 0:
            {
                var bw = Div(width, t.Bits);
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var mode = (int)((t.Data[(y >> t.Bits) * bw + (x >> t.Bits)] >> 8) & 0xF);
                        var i = y * width + x;
                        pixels[i] = Add(pixels[i], Vp8lPredictor.Predict(pixels, width, x, y, mode));
                    }
                return pixels;
            }
            case 1:
            {
                var bw = Div(width, t.Bits);
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var e = t.Data[(y >> t.Bits) * bw + (x >> t.Bits)];
                        sbyte greenToRed = (sbyte)(e & 0xFF), greenToBlue = (sbyte)((e >> 8) & 0xFF), redToBlue = (sbyte)((e >> 16) & 0xFF);
                        var i = y * width + x;
                        var p = pixels[i];
                        var green = (sbyte)((p >> 8) & 0xFF);
                        var red = (int)((p >> 16) & 0xFF) + (greenToRed * green >> 5);
                        var blue = (int)(p & 0xFF) + (greenToBlue * green >> 5);
                        blue += redToBlue * (sbyte)(red & 0xFF) >> 5;
                        pixels[i] = (p & 0xFF00FF00) | (uint)(red & 0xFF) << 16 | (uint)(blue & 0xFF);
                    }
                return pixels;
            }
            case 2:
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var g = (p >> 8) & 0xFF;
                    pixels[i] = (p & 0xFF00FF00) | ((((p >> 16) + g) & 0xFF) << 16) | ((p + g) & 0xFF);
                }
                return pixels;
            default:
            {
                var packedWidth = Div(width, t.Bits);
                var output = new uint[width * height];
                var perByte = 1 << t.Bits;
                var bitsPer = 8 >> t.Bits;
                var mask = (1u << bitsPer) - 1;
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var packed = (pixels[y * packedWidth + (x >> t.Bits)] >> 8) & 0xFF;
                        var index = (packed >> (bitsPer * (x & (perByte - 1)))) & mask;
                        output[y * width + x] = t.Data[index];
                    }
                return output;
            }
        }
    }

    /// <summary>A prefix code: canonical, read one bit at a time (the code's first bit first).</summary>
    private sealed class Prefix
    {
        private readonly int[] _count = new int[16];
        private readonly int[] _symbols;
        private readonly int _single = -1;

        public Prefix(int[] lengths)
        {
            var used = 0;
            foreach (var l in lengths)
                if (l > 0)
                {
                    _count[l]++;
                    used++;
                }
            if (used == 0) throw new InvalidDataException("Código vacío");
            if (used == 1) _single = Array.FindIndex(lengths, l => l > 0);
            var offsets = new int[16];
            for (var l = 1; l < 15; l++) offsets[l + 1] = offsets[l] + _count[l];
            _symbols = new int[used];
            for (var s = 0; s < lengths.Length; s++)
                if (lengths[s] > 0) _symbols[offsets[lengths[s]]++] = s;
        }

        public int Read(Vp8lDecoder d)
        {
            if (_single >= 0) return _single;
            int code = 0, first = 0, index = 0;
            for (var len = 1; len < 16; len++)
            {
                code |= (int)d.Bits(1);
                var count = _count[len];
                if (code - first < count) return _symbols[index + code - first];
                index += count;
                first = (first + count) << 1;
                code <<= 1;
            }
            throw new InvalidDataException("Código no válido");
        }
    }

    private Prefix ReadCode(int alphabet)
    {
        var lengths = new int[alphabet];
        if (Bits(1) == 1)
        {
            var two = Bits(1) == 1;
            var first = (int)Bits(Bits(1) == 1 ? 8 : 1);
            lengths[Check(first)] = 1;
            if (two) lengths[Check((int)Bits(8))] = 1;
            return new Prefix(lengths);
        }
        var clLengths = new int[19];
        var count = (int)Bits(4) + 4;
        for (var i = 0; i < count; i++) clLengths[Vp8lEncoder.CodeLengthOrder[i]] = (int)Bits(3);
        var cl = new Prefix(clLengths);
        var max = alphabet;
        if (Bits(1) == 1)
        {
            var bits = 2 + 2 * (int)Bits(3);
            max = 2 + (int)Bits(bits);
            if (max > alphabet) throw new InvalidDataException("Código no válido");
        }
        var symbol = 0;
        var previous = 8;
        while (symbol < alphabet)
        {
            if (max-- == 0) break;
            var c = cl.Read(this);
            if (c < 16)
            {
                lengths[symbol++] = c;
                if (c != 0) previous = c;
                continue;
            }
            var (repeat, value) = c switch
            {
                16 => (3 + (int)Bits(2), previous),
                17 => (3 + (int)Bits(3), 0),
                _ => (11 + (int)Bits(7), 0),
            };
            if (symbol + repeat > alphabet) throw new InvalidDataException("Código no válido");
            for (var k = 0; k < repeat; k++) lengths[symbol++] = value;
        }
        return new Prefix(lengths);

        int Check(int s) => s < alphabet ? s : throw new InvalidDataException("Símbolo fuera del alfabeto");
    }

    private int Value(int prefix)
    {
        if (prefix < 4) return prefix + 1;
        var extra = (prefix - 2) >> 1;
        var offset = (2 + (prefix & 1)) << extra;
        return offset + (int)Bits(extra) + 1;
    }

    private uint[] Image(int width, int height, bool top)
    {
        var cacheBits = 0;
        if (Bits(1) == 1)
        {
            cacheBits = (int)Bits(4);
            if (cacheBits is < 1 or > 11) throw new InvalidDataException("Caché de color no válida");
        }
        var metaBits = 0;
        uint[]? meta = null;
        var groups = 1;
        if (top && Bits(1) == 1)
        {
            metaBits = (int)Bits(3) + 2;
            meta = Image(Div(width, metaBits), Div(height, metaBits), top: false);
            for (var i = 0; i < meta.Length; i++)
            {
                meta[i] = (meta[i] >> 8) & 0xFFFF;
                groups = Math.Max(groups, (int)meta[i] + 1);
            }
        }
        var cacheSize = cacheBits > 0 ? 1 << cacheBits : 0;
        var codes = new Prefix[groups][];
        for (var g = 0; g < groups; g++)
            codes[g] = [ReadCode(256 + 24 + cacheSize), ReadCode(256), ReadCode(256), ReadCode(256), ReadCode(40)];
        var cache = cacheSize > 0 ? new uint[cacheSize] : null;
        var pixels = new uint[width * height];
        var metaWidth = meta is null ? 0 : Div(width, metaBits);
        var pos = 0;
        while (pos < pixels.Length)
        {
            int x = pos % width, y = pos / width;
            var group = meta is null ? codes[0] : codes[meta[(y >> metaBits) * metaWidth + (x >> metaBits)]];
            var s = group[0].Read(this);
            if (s < 256)
            {
                var red = (uint)group[1].Read(this);
                var blue = (uint)group[2].Read(this);
                var alpha = (uint)group[3].Read(this);
                var p = alpha << 24 | red << 16 | (uint)s << 8 | blue;
                pixels[pos++] = p;
                if (cache is not null) cache[(0x1E35A7BD * p) >> (32 - cacheBits)] = p;
            }
            else if (s < 256 + 24)
            {
                var length = Value(s - 256);
                var code = Value(group[4].Read(this));
                int distance;
                if (code > 120) distance = code - 120;
                else
                {
                    distance = Plane[(code - 1) * 2] + Plane[(code - 1) * 2 + 1] * width;
                    if (distance < 1) distance = 1;
                }
                if (distance > pos || pos + length > pixels.Length) throw new InvalidDataException("Referencia fuera de la imagen");
                for (var k = 0; k < length; k++)
                {
                    var p = pixels[pos - distance];
                    pixels[pos++] = p;
                    if (cache is not null) cache[(0x1E35A7BD * p) >> (32 - cacheBits)] = p;
                }
            }
            else
            {
                if (cache is null) throw new InvalidDataException("Caché de color no válida");
                var p = cache[s - 280];
                pixels[pos++] = p;
            }
        }
        return pixels;
    }
}
