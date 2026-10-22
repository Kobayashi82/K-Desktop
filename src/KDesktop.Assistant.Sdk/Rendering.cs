using System.Windows;
using System.Windows.Media;

namespace KDesktop.Assistant.Sdk;

/// <summary>Where a surface goes in the stacking order of the overlay.</summary>
public enum SurfaceLayer
{
    /// <summary>Behind the main layer (props, toys, beacons on the ground).</summary>
    Back,

    /// <summary>The character itself.</summary>
    Main,

    /// <summary>In front of everything (bubbles, HUD, effects).</summary>
    Front,
}

/// <summary>
/// Drawing services. The host keeps one transparent overlay over every monitor that never takes the mouse or the
/// focus; each surface is a small canvas on it that the compositor moves for free.
/// </summary>
public interface IRendering
{
    /// <summary>True when frames go through the GPU; false on the CPU fallback (no GPU, remote session).</summary>
    bool HardwareAccelerated { get; }

    /// <summary>Seconds between two frames of the compositor (1/60 on a 60 Hz monitor).</summary>
    double FrameInterval { get; }

    /// <summary>Creates a surface. Dispose it when it is no longer needed.</summary>
    ISurface CreateSurface(SurfaceLayer layer = SurfaceLayer.Main);
}

/// <summary>
/// A canvas on the overlay. Call one of the Present methods only when something changed: the last frame stays on
/// screen, so an assistant at rest presents nothing and costs nothing.
/// </summary>
public interface ISurface : IDisposable
{
    bool IsVisible { get; }

    /// <summary>
    /// Draws a frame with WPF's drawing API (the usual way): <paramref name="draw"/> records into a DrawingContext
    /// whose coordinates are screen pixels; anything outside <paramref name="area"/> is clipped. The host replays it
    /// with Direct2D, antialiased by the GPU. Frozen brushes, pens and geometries are cached between frames.
    /// </summary>
    void Present(PixelRect area, Action<DrawingContext> draw);

    /// <summary>
    /// Draws a frame straight on the GPU (shaders, effects). Also on the software fallback (WARP: Direct3D 11 on the
    /// processor, much slower). False only when the surface was disposed.
    /// </summary>
    bool PresentGpu(PixelRect area, Action<GpuTarget> draw);

    /// <summary>
    /// The last frame presented with <see cref="PresentGpu"/> is gone (the GPU was lost and made again, or it is back
    /// after a full-screen game): present again even if nothing changed. False again after the next Present. (SDK 1.5.)
    /// </summary>
    bool NeedsFrame => false;

    void Hide();
}

/// <summary>
/// Native pointers for <see cref="ISurface.PresentGpu"/>, valid only during the call. They are COM pointers: wrap
/// them with the binding of your choice (for example Vortice: <c>new ID3D11Device(ptr)</c> after AddRef), so
/// packages never depend on the host's version of any binding. The target is cleared to transparent before the
/// call and holds premultiplied BGRA.
/// </summary>
/// <param name="D2D1DeviceContext">Direct2D on the same device. It has no target: set <paramref name="D2D1TargetBitmap"/> to draw
/// over what the shaders drew, and set it back to null before returning.</param>
/// <param name="Origin">Screen position of the top-left pixel of the target.</param>
/// <param name="D2D1TargetBitmap">The same target as an ID2D1Bitmap1 for Direct2D (SDK 1.5).</param>
/// <param name="Generation">Changes whenever the devices are made again (the GPU was lost, or freed for a full-screen game):
/// release whatever was made on the old ones (shaders, buffers, brushes) and make it again (SDK 1.5).</param>
public readonly record struct GpuTarget(
    nint D3D11Device,
    nint D3D11DeviceContext,
    nint RenderTargetView,
    nint D2D1DeviceContext,
    int Width,
    int Height,
    Point Origin,
    nint D2D1TargetBitmap = 0,
    int Generation = 0);
