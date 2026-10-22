using System.Numerics;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using SharpGen.Runtime;
using Vortice.Mathematics;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// <see cref="IRendering"/> on the overlay: the GPU devices, the layer, the replayer of WPF drawings and the pace of
/// the compositor. Everything on the engine thread. If the GPU is lost (driver update, remote session) everything is
/// made again and every surface replays its last WPF frame; the same when it is given back after a full-screen game
/// (<see cref="Suspend"/>).
/// </summary>
internal sealed class HostRendering : IRendering, IDisposable
{
    private readonly List<HostSurface> _surfaces = [];
    private bool _dirty;
    private int _losses;

    public HostRendering()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Gpu = GpuDevice.Create();
        var gpu = watch.ElapsedMilliseconds;
        Overlay = new Overlay(Gpu);
        Replayer = new DrawingReplayer(Gpu.D2DFactory);
        Log.Info($"Asistente: capa sobre {Overlay.Bounds} con {Gpu.AdapterName}{(Gpu.IsSoftware ? " (WARP)" : "")} " +
                 $"({gpu} + {watch.ElapsedMilliseconds - gpu} ms)");
    }

    public GpuDevice Gpu { get; private set; }

    public Overlay Overlay { get; private set; }

    public DrawingReplayer Replayer { get; private set; }

    public FramePacer Pacer { get; } = new();

    /// <summary>Goes up every time the devices are made again (<see cref="GpuTarget.Generation"/>).</summary>
    public int Generation { get; private set; } = 1;

    public bool HardwareAccelerated => !Gpu.IsSoftware;

    /// <summary>The GPU was freed for a full-screen game or presentation: nothing is drawn until <see cref="Resume"/>.</summary>
    public bool Suspended { get; private set; }

    public double FrameInterval => Pacer.SmoothInterval;

    /// <summary>The overlay's own window (left out of the world and of hit tests).</summary>
    public nint OverlayHandle => Overlay.Window.Handle;

    public ISurface CreateSurface(SurfaceLayer layer = SurfaceLayer.Main)
    {
        var surface = new HostSurface(this, layer);
        _surfaces.Add(surface);
        return surface;
    }

    internal void Forget(HostSurface surface) => _surfaces.Remove(surface);

    internal void MarkDirty() => _dirty = true;

    /// <summary>Sends this tick's changes to the compositor in one go (images and positions move together).</summary>
    public void Commit()
    {
        if (!_dirty || Suspended) return;
        _dirty = false;
        try { Overlay.Commit(); }
        catch (SharpGenException ex) { DeviceLost(ex); }
    }

    /// <summary>Monitors changed: the layer follows the virtual screen and the surfaces keep their place on screen.</summary>
    public void UpdateBounds()
    {
        if (Suspended || !Overlay.UpdateBounds()) return;
        foreach (var surface in _surfaces) surface.Reposition();
        MarkDirty();
    }

    /// <summary>Takes back the top of the «always on top» windows (others may have taken it).</summary>
    public void KeepOnTop()
    {
        if (!Suspended) Overlay.Window.BringToTop();
    }

    /// <summary>Frees the GPU and the layer (a DirectX game or a presentation holds the screen).</summary>
    public void Suspend()
    {
        if (Suspended) return;
        Teardown();
        Suspended = true;
        Log.Info("Asistente: pantalla completa exclusiva; se libera la GPU");
    }

    /// <summary>Makes the layer again and every surface shows its last frame.</summary>
    public void Resume()
    {
        if (!Suspended) return;
        Build(preferSoftware: _losses >= 3);
        Suspended = false;
        foreach (var surface in _surfaces) surface.Restore();
        MarkDirty();
        Log.Info("Asistente: se recupera la GPU");
    }

    internal void DeviceLost(Exception ex)
    {
        _losses++;
        Log.Warn($"Asistente: se ha perdido la GPU ({ex.Message}); se rehace la capa ({_losses})");
        Teardown();
        // After three losses, stay on WARP: a GPU that keeps failing is worse than a slower one.
        Build(preferSoftware: _losses >= 3);
        foreach (var surface in _surfaces) surface.Restore();
        MarkDirty();
    }

    public void Dispose()
    {
        foreach (var surface in _surfaces.ToList()) surface.Dispose();
        if (!Suspended) Teardown();
    }

    private void Teardown()
    {
        foreach (var surface in _surfaces) surface.DropStage();
        Replayer.Dispose();
        Overlay.Dispose();
        Gpu.Dispose();
    }

    private void Build(bool preferSoftware)
    {
        Generation++;
        Gpu = GpuDevice.Create(preferSoftware);
        Overlay = new Overlay(Gpu);
        Replayer = new DrawingReplayer(Gpu.D2DFactory);
    }
}

/// <summary>A canvas of the assistant on the overlay (<see cref="ISurface"/>).</summary>
internal sealed class HostSurface(HostRendering rendering, SurfaceLayer layer) : ISurface
{
    private StageLayer? _stage;
    private PixelRect _area;
    private DrawingGroup? _last;
    private bool _gpuFrame;
    private bool _disposed;

    internal bool IsDisposed => _disposed;

    public bool NeedsFrame { get; private set; }

    public bool IsVisible => _stage is { IsAttached: true };

    public void Present(PixelRect area, Action<DrawingContext> draw)
    {
        if (_disposed) return;
        if (area.IsEmpty)
        {
            Hide();
            return;
        }
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open()) draw(dc);
        drawing.Freeze();
        _last = drawing;
        _gpuFrame = false;
        NeedsFrame = false;
        if (rendering.Suspended)
        {
            _area = area; // shown when the GPU comes back
            return;
        }
        Draw(area, drawing);
    }

    public bool PresentGpu(PixelRect area, Action<GpuTarget> draw)
    {
        if (_disposed) return false;
        if (rendering.Suspended)
        {
            // Drawn when the GPU comes back (NeedsFrame).
            _area = area;
            _gpuFrame = true;
            return true;
        }
        if (area.IsEmpty)
        {
            Hide();
            return true;
        }
        // Nothing to replay if the GPU is lost: NeedsFrame asks the assistant to draw again.
        _last = null;
        _gpuFrame = true;
        NeedsFrame = false;
        try
        {
            var stage = Stage(area);
            stage.Clear();
            var (target, bitmap) = stage.Targets();
            var gpu = rendering.Gpu;
            draw(new GpuTarget(gpu.Device.NativePointer, gpu.Context.NativePointer, target.NativePointer, gpu.D2D.NativePointer,
                area.Width, area.Height, new System.Windows.Point(area.Left, area.Top), bitmap.NativePointer, rendering.Generation));
            if (gpu.D2D.Target is not null) gpu.D2D.Target = null;
            Show(stage, area);
        }
        catch (SharpGenException ex) { rendering.DeviceLost(ex); }
        return true;
    }

    public void Hide()
    {
        if (_stage is not { IsAttached: true } stage) return;
        stage.Detach();
        rendering.MarkDirty();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_stage is { IsAttached: true }) rendering.MarkDirty();
        _stage?.Dispose();
        _stage = null;
        _last = null;
        rendering.Forget(this);
    }

    internal void Reposition()
    {
        if (_stage is { IsAttached: true } stage) Place(stage, _area);
    }

    internal void DropStage()
    {
        try { _stage?.Dispose(); } catch { /* the device is gone anyway */ }
        _stage = null;
    }

    internal void Restore()
    {
        if (_gpuFrame) NeedsFrame = !_area.IsEmpty;
        else if (_last is not null && !_area.IsEmpty) Draw(_area, _last);
    }

    private void Draw(PixelRect area, DrawingGroup drawing)
    {
        try
        {
            var stage = Stage(area);
            var (_, bitmap) = stage.Targets();
            var d2d = rendering.Gpu.D2D;
            d2d.Target = bitmap;
            d2d.BeginDraw();
            d2d.Clear(new Color4(0, 0, 0, 0));
            d2d.PushAxisAlignedClip(new Vortice.RawRectF(0, 0, area.Width, area.Height), Vortice.Direct2D1.AntialiasMode.Aliased);
            rendering.Replayer.Replay(d2d, drawing, Matrix3x2.CreateTranslation(-area.Left, -area.Top));
            d2d.PopAxisAlignedClip();
            var result = d2d.EndDraw();
            d2d.Target = null;
            if (result.Failure) throw new SharpGenException(result);
            Show(stage, area);
        }
        catch (SharpGenException ex) { rendering.DeviceLost(ex); }
    }

    private StageLayer Stage(PixelRect area)
    {
        _stage ??= new StageLayer(rendering.Gpu, rendering.Overlay, layer, area.Width, area.Height);
        _stage.Fit(area.Width, area.Height);
        return _stage;
    }

    private void Show(StageLayer stage, PixelRect area)
    {
        stage.Present();
        _area = area;
        Place(stage, area);
    }

    private void Place(StageLayer stage, PixelRect area)
    {
        var origin = rendering.Overlay.Bounds;
        stage.Place(area.Left - origin.Left, area.Top - origin.Top);
        rendering.MarkDirty();
    }
}
