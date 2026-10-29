using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using KDesktop.Core.Imaging;
using KDesktop.Core.Model;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace KDesktop.Core.Platform;

/// <summary>
/// «Grabar vídeo» in GIF or animated WebP (F3): the same capture, but each frame is made smaller on the graphics card, read
/// back and encoded on another thread (a frame equal to the one before only makes it last longer). Up to
/// <see cref="AnimationLimit"/>: then it stops by itself.
/// </summary>
public sealed partial class ScreenRecorder
{
    /// <summary>The longest a GIF or a WebP can be.</summary>
    public static readonly TimeSpan AnimationLimit = TimeSpan.FromSeconds(30);

    private int _outWidth, _outHeight;
    private ID2D1Bitmap1? _scaled, _readback;
    private int _encoded; // frames handed to the encoder (different ones)

    /// <summary>A GIF or a WebP reached <see cref="AnimationLimit"/> (it stopped recording; whoever started it closes it).</summary>
    public event Action? LimitReached;

    private bool IsAnimation => _options.Format != VideoFormat.Mp4;

    private void BeginAnimation()
    {
        var scale = _options.MaxWidth > 0 && _width > _options.MaxWidth ? _options.MaxWidth / (double)_width : 1;
        _outWidth = Math.Max(1, (int)Math.Round(_width * scale));
        _outHeight = Math.Max(1, (int)Math.Round(_height * scale));
        // The circles of the clicks keep their size in the smaller picture.
        _clickScale /= (float)scale;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _session!.StartCapture();
        _start = Stopwatch.GetTimestamp();
        _transcode = Task.Factory.StartNew(Animate, TaskCreationOptions.LongRunning);
        Log.Info($"Grabación de vídeo: empieza {_options.Format} de {_target.Kind} {_width}×{_height} → {_outWidth}×{_outHeight} a {_options.Fps} fps → {Path}");
    }

    /// <summary>Takes the frames at a fixed rate until it stops (or the limit), and waits for the encoder to write the file.</summary>
    private void Animate()
    {
        var limit = (long)(AnimationLimit.TotalSeconds * Stopwatch.Frequency);
        using var queue = new BlockingCollection<(byte[] Pixels, long Due)>(boundedCapacity: 60);
        var encoder = Task.Factory.StartNew(() => Encode(queue), TaskCreationOptions.LongRunning);
        try
        {
            byte[]? previous = null;
            while (true)
            {
                var slot = _nextSlot;
                while (!_stopping && _end is null && (RecordedTicks() < slot * _interval || !_hasFrame) && slot * _interval < limit) Thread.Sleep(1);
                if (_stopping || (_end is { } end && slot * _interval >= end)) break;
                if (slot * _interval >= limit)
                {
                    _end ??= limit;
                    Log.Info("Grabación de vídeo: llega al límite de la animación");
                    ThreadPool.QueueUserWorkItem(_ => LimitReached?.Invoke());
                    break;
                }
                slot = Math.Max(slot, RecordedTicks() / _interval);
                var due = slot * _interval;
                var pixels = ReadFrame(due);
                _framesWritten++;
                _nextSlot = slot + 1;
                if (pixels is null || (previous is not null && pixels.AsSpan().SequenceEqual(previous))) continue;
                // Full (the encoder is slow): this frame waits for room; the picture keeps its times.
                queue.Add((pixels, due));
                previous = pixels;
            }
        }
        catch (Exception ex) when (ex is COMException or SharpGen.Runtime.SharpGenException or ObjectDisposedException)
        {
            Log.Warn($"Grabación de vídeo: se para ({ex.Message})");
        }
        finally
        {
            queue.CompleteAdding();
        }
        encoder.GetAwaiter().GetResult();
    }

    private void Encode(BlockingCollection<(byte[] Pixels, long Due)> queue)
    {
        var gif = _options.Format == VideoFormat.Gif ? new AnimatedGifWriter(_outWidth, _outHeight) : null;
        var webp = gif is null ? new AnimatedWebPWriter(_outWidth, _outHeight) : null;
        void Add(byte[] pixels, long ticks)
        {
            var ms = (int)Math.Max(1, ticks * 1000 / Stopwatch.Frequency);
            if (gif is not null) gif.Add(pixels, ms);
            else webp!.Add(pixels, ms);
            _encoded++;
        }
        try
        {
            (byte[] Pixels, long Due)? pending = null;
            foreach (var frame in queue.GetConsumingEnumerable())
            {
                if (pending is { } before) Add(before.Pixels, frame.Due - before.Due);
                pending = frame;
            }
            if (pending is not { } last) return; // nothing was recorded: no file
            Add(last.Pixels, Math.Max(_interval, (_end ?? RecordedTicks()) - last.Due));
            File.WriteAllBytes(Path, gif?.ToArray() ?? webp!.ToArray());
            Log.Info($"Grabación de vídeo: {_encoded} imágenes distintas codificadas");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Log.Error("Grabación de vídeo", ex);
            Failed?.Invoke(ex);
        }
        finally
        {
            // Whatever is left (a failure) is not waited for.
            foreach (var _ in queue.GetConsumingEnumerable()) { }
        }
    }

    /// <summary>The frame at <paramref name="due"/> with its clicks, at the size of the file, as BGRA rows.</summary>
    private byte[]? ReadFrame(long due)
    {
        ID3D11Texture2D copy;
        lock (_lock)
        {
            if (_latest is null || _context is null) return null;
            copy = NewTexture();
            _context.CopyResource(copy, _latest);
        }
        try
        {
            DrawClicks(copy, due);
            lock (_lock)
            {
                if (_device is null) return null;
                var d2d = Direct2D();
                var format = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
                _scaled ??= d2d.CreateBitmap(new SizeI(_outWidth, _outHeight), IntPtr.Zero, 0, new BitmapProperties1(format, 96, 96, BitmapOptions.Target));
                _readback ??= d2d.CreateBitmap(new SizeI(_outWidth, _outHeight), IntPtr.Zero, 0, new BitmapProperties1(format, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
                using (var surface = copy.QueryInterface<IDXGISurface>())
                using (var source = d2d.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(format, 96, 96, BitmapOptions.None)))
                {
                    d2d.Target = _scaled;
                    d2d.BeginDraw();
                    d2d.DrawBitmap(source, new Rect(0, 0, _outWidth, _outHeight), 1, InterpolationMode.HighQualityCubic, null, null);
                    var result = d2d.EndDraw();
                    d2d.Target = null;
                    if (result.Failure) throw new SharpGen.Runtime.SharpGenException(result);
                }
                _readback.CopyFromBitmap(_scaled);
                var map = _readback.Map(MapOptions.Read);
                try
                {
                    var row = _outWidth * 4;
                    var pixels = new byte[row * _outHeight];
                    for (var y = 0; y < _outHeight; y++) Marshal.Copy(map.Bits + y * (int)map.Pitch, pixels, y * row, row);
                    return pixels;
                }
                finally { _readback.Unmap(); }
            }
        }
        finally
        {
            copy.Dispose();
        }
    }

    private void DisposeAnimation()
    {
        _scaled?.Dispose();
        _scaled = null;
        _readback?.Dispose();
        _readback = null;
    }
}
