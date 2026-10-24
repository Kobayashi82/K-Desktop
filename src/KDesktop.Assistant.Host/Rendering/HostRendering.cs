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
/// <para>
/// Off the screen (<see cref="Offscreen"/>, the showcase's stage, phase 16) there is no layer at all: each surface only keeps its
/// last drawing and where it goes, and the stage composes them into its own picture (<see cref="Shown"/>).
/// </para>
/// </summary>
internal sealed class HostRendering : IRendering, IDisposable
{
    private readonly List<HostSurface> _surfaces = [];
    private bool _dirty;
    private int _losses;
    private long _shownCount;

    public HostRendering(bool offscreen = false)
    {
        Offscreen = offscreen;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Gpu = GpuDevice.Create();
        var gpu = watch.ElapsedMilliseconds;
        Overlay = offscreen ? null : new Overlay(Gpu);
        Replayer = new DrawingReplayer(Gpu.D2DFactory);
        Log.Info($"Asistente: {(Overlay is { } overlay ? $"capa sobre {overlay.Bounds}" : "dibujo fuera de la pantalla")} con {Gpu.AdapterName}" +
                 $"{(Gpu.IsSoftware ? " (WARP)" : "")} ({gpu} + {watch.ElapsedMilliseconds - gpu} ms)");
    }

    /// <summary>Nothing goes on the screen: the stage composes the surfaces itself (phase 16).</summary>
    public bool Offscreen { get; }

    /// <summary>Something changed since the stage last composed (off the screen).</summary>
    public bool Changed { get; set; }

    /// <summary>The surfaces shown off the screen, back to front (the order the compositor would stack them in).</summary>
    public IEnumerable<HostSurface> Shown => _surfaces.Where(s => s.ShownAt > 0).OrderBy(s => s.Layer).ThenBy(s => s.ShownAt);

    internal long NextShown() => ++_shownCount;

    public GpuDevice Gpu { get; private set; }

    public Overlay? Overlay { get; private set; }

    public DrawingReplayer Replayer { get; private set; }

    public FramePacer Pacer { get; } = new();

    public bool HardwareAccelerated => !Gpu.IsSoftware;

    /// <summary>The GPU was freed for a full-screen game or presentation: nothing is drawn until <see cref="Resume"/>.</summary>
    public bool Suspended { get; private set; }

    public double FrameInterval => Pacer.SmoothInterval;

    /// <summary>The overlay's own window (left out of the world and of hit tests).</summary>
    public nint OverlayHandle => Overlay?.Window.Handle ?? 0;

    public ISurface CreateSurface(SurfaceLayer layer = SurfaceLayer.Main)
    {
        var surface = new HostSurface(this, layer);
        _surfaces.Add(surface);
        return surface;
    }

    internal void Forget(HostSurface surface) => _surfaces.Remove(surface);

    internal void MarkDirty()
    {
        _dirty = true;
        Changed = true;
    }

    /// <summary>Sends this tick's changes to the compositor in one go (images and positions move together).</summary>
    public void Commit()
    {
        if (!_dirty || Suspended || Overlay is null) return;
        _dirty = false;
        var commit = RenderStats.Now();
        try { Overlay.Commit(); }
        catch (SharpGenException ex) { DeviceLost(ex); }
        RenderStats.Add(RenderStats.Part.Commit, commit);
    }

    /// <summary>Monitors changed: the layer follows the virtual screen and the surfaces keep their place on screen.</summary>
    public void UpdateBounds()
    {
        if (Suspended || Overlay is null || !Overlay.UpdateBounds()) return;
        foreach (var surface in _surfaces) surface.Reposition();
        MarkDirty();
    }

    /// <summary>Takes back the top of the «always on top» windows (others may have taken it).</summary>
    public void KeepOnTop()
    {
        if (!Suspended) Overlay?.Window.BringToTop();
    }

    /// <summary>Frees the GPU and the layer (a DirectX game or a presentation holds the screen).</summary>
    public void Suspend()
    {
        if (Suspended || Offscreen) return;
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
        Overlay?.Dispose();
        Gpu.Dispose();
    }

    private void Build(bool preferSoftware)
    {
        Gpu = GpuDevice.Create(preferSoftware);
        Overlay = Offscreen ? null : new Overlay(Gpu);
        Replayer = new DrawingReplayer(Gpu.D2DFactory);
    }
}

/// <summary>A canvas of the assistant on the overlay (<see cref="ISurface"/>).</summary>
internal sealed class HostSurface(HostRendering rendering, SurfaceLayer layer) : ISurface
{
    private StageLayer? _stage;
    private PixelRect _area;
    private DrawingGroup? _last;
    private bool _disposed;

    internal bool IsDisposed => _disposed;

    public bool IsVisible => rendering.Offscreen ? ShownAt > 0 : _stage is { IsAttached: true };

    internal SurfaceLayer Layer => layer;

    /// <summary>Off the screen: when it was last shown (its place in the stack, as with the compositor), 0 while hidden.</summary>
    internal long ShownAt { get; private set; }

    /// <summary>Where it is and what it shows (its last drawing, in screen pixels).</summary>
    internal PixelRect Area => _area;

    internal DrawingGroup? Last => _last;

    public void Present(PixelRect area, Action<DrawingContext> draw)
    {
        if (_disposed) return;
        if (area.IsEmpty)
        {
            Hide();
            return;
        }
        var record = RenderStats.Now();
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open()) draw(dc);
        drawing.Freeze();
        RenderStats.Add(RenderStats.Part.Record, record);
        _last = drawing;
        if (rendering.Offscreen)
        {
            _area = area;
            if (ShownAt == 0) ShownAt = rendering.NextShown();
            rendering.MarkDirty();
            return;
        }
        if (rendering.Suspended)
        {
            _area = area; // shown when the GPU comes back
            return;
        }
        Draw(area, drawing);
    }

    public void Hide()
    {
        if (rendering.Offscreen)
        {
            if (ShownAt == 0) return;
            ShownAt = 0;
            rendering.MarkDirty();
            return;
        }
        if (_stage is not { IsAttached: true } stage) return;
        stage.Detach();
        rendering.MarkDirty();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_stage is { IsAttached: true } || ShownAt > 0) rendering.MarkDirty();
        ShownAt = 0;
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
        if (rendering.Offscreen) return;
        if (_last is not null && !_area.IsEmpty) Draw(_area, _last);
    }

    private void Draw(PixelRect area, DrawingGroup drawing)
    {
        try
        {
            var stage = Stage(area);
            var d2d = stage.BeginDraw(out var offset);
            try
            {
                d2d.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
                // The whole surface clean (it may have room left from a bigger frame), then the drawing inside its area.
                d2d.PushAxisAlignedClip(new Vortice.RawRectF(offset.X, offset.Y, offset.X + stage.Width, offset.Y + stage.Height), Vortice.Direct2D1.AntialiasMode.Aliased);
                d2d.Clear(new Color4(0, 0, 0, 0));
                d2d.PopAxisAlignedClip();
                d2d.PushAxisAlignedClip(new Vortice.RawRectF(offset.X, offset.Y, offset.X + area.Width, offset.Y + area.Height), Vortice.Direct2D1.AntialiasMode.Aliased);
                var replay = RenderStats.Now();
                rendering.Replayer.Replay(d2d, drawing, Matrix3x2.CreateTranslation(offset.X - area.Left, offset.Y - area.Top));
                RenderStats.Add(RenderStats.Part.Replay, replay);
                d2d.PopAxisAlignedClip();
                var end = RenderStats.Now();
                stage.EndDraw();
                RenderStats.Add(RenderStats.Part.EndDraw, end);
            }
            finally
            {
                d2d.Dispose();
            }

            var place = RenderStats.Now();
            _area = area;
            Place(stage, area);
            // The new content (and its new place, if it moved) show together in this tick's commit.
            rendering.MarkDirty();
            RenderStats.Add(RenderStats.Part.Present, place);
            RenderStats.Frame();
        }
        catch (SharpGenException ex) { rendering.DeviceLost(ex); }
    }

    private StageLayer Stage(PixelRect area)
    {
        _stage ??= new StageLayer(rendering.Overlay!, layer, area.Width, area.Height);
        _stage.Fit(area.Width, area.Height);
        return _stage;
    }

    private void Place(StageLayer stage, PixelRect area)
    {
        var origin = rendering.Overlay!.Bounds;
        if (stage.Place(area.Left - origin.Left, area.Top - origin.Top)) rendering.MarkDirty();
    }
}
