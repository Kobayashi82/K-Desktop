using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Sdk;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace KDesktop.Assistant.Host.Stage;

/// <summary>
/// The picture of the stage (phase 16): its desktop, every surface of the assistants on top in the compositor's order, and
/// the cursor it draws while recording, drawn with the same Direct2D replayer as the overlay into a bitmap that is read back
/// (BGRA, premultiplied, top-down; opaque, the desktop is under everything). Engine thread.
/// </summary>
internal sealed class StageCompositor : IDisposable
{
    private ID2D1Bitmap1? _target;
    private ID2D1Bitmap1? _readback;
    private GpuDevice? _device;

    public StageCompositor(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The last picture (BGRA, <see cref="Width"/> × <see cref="Height"/>, rows of Width × 4 bytes).</summary>
    public byte[] Pixels { get; }

    /// <summary>Draws the stage <paramref name="area"/> (screen pixels) into <see cref="Pixels"/>. False when the GPU failed.</summary>
    public bool Compose(HostRendering rendering, PixelRect area, Drawing backdrop, Drawing? cursor)
    {
        try
        {
            Ensure(rendering.Gpu);
            var d2d = rendering.Gpu.D2D;
            d2d.Target = _target;
            d2d.BeginDraw();
            d2d.Clear(new Color4(0, 0, 0, 1));
            var origin = Matrix3x2.CreateTranslation(-area.Left, -area.Top);
            rendering.Replayer.Replay(d2d, backdrop, origin);
            foreach (var surface in rendering.Shown)
            {
                if (surface.Last is not { } drawing || !surface.Area.Intersects(area)) continue;
                var clip = surface.Area;
                d2d.PushAxisAlignedClip(new Vortice.RawRectF(clip.Left - area.Left, clip.Top - area.Top, clip.Right - area.Left, clip.Bottom - area.Top), AntialiasMode.Aliased);
                rendering.Replayer.Replay(d2d, drawing, origin);
                d2d.PopAxisAlignedClip();
            }
            if (cursor is not null) rendering.Replayer.Replay(d2d, cursor, origin);
            var result = d2d.EndDraw();
            d2d.Target = null;
            if (result.Failure) throw new SharpGenException(result);
            _readback!.CopyFromBitmap(_target!);
            var map = _readback.Map(MapOptions.Read);
            try
            {
                var row = Width * 4;
                for (var y = 0; y < Height; y++) Marshal.Copy(map.Bits + y * (int)map.Pitch, Pixels, y * row, row);
            }
            finally { _readback.Unmap(); }
            return true;
        }
        catch (SharpGenException ex)
        {
            KDesktop.Core.Log.Warn($"Asistente: el escenario no se ha podido dibujar ({ex.Message})");
            Release();
            rendering.DeviceLost(ex);
            return false;
        }
    }

    private void Ensure(GpuDevice device)
    {
        if (_device == device && _target is not null) return;
        Release();
        _device = device;
        var size = new SizeI(Width, Height);
        var format = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
        _target = device.D2D.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(format, 96, 96, BitmapOptions.Target));
        _readback = device.D2D.CreateBitmap(size, IntPtr.Zero, 0, new BitmapProperties1(format, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
    }

    private void Release()
    {
        _target?.Dispose();
        _readback?.Dispose();
        _target = _readback = null;
        _device = null;
    }

    public void Dispose() => Release();
}
