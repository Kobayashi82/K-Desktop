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
    /// <summary>True when frames go through the GPU; false on the software fallback (no GPU, remote session).</summary>
    bool HardwareAccelerated { get; }

    /// <summary>Seconds between two frames of the compositor (1/60 on a 60 Hz monitor).</summary>
    double FrameInterval { get; }

    /// <summary>Creates a surface. Dispose it when it is no longer needed.</summary>
    ISurface CreateSurface(SurfaceLayer layer = SurfaceLayer.Main);
}

/// <summary>
/// A canvas on the overlay. Call <see cref="Present"/> only when something changed: the last frame stays on screen (also
/// after the GPU is lost or comes back from a full-screen game), so an assistant at rest presents nothing and costs nothing.
/// </summary>
public interface ISurface : IDisposable
{
    bool IsVisible { get; }

    /// <summary>
    /// Draws a frame with WPF's drawing API: <paramref name="draw"/> records into a DrawingContext
    /// whose coordinates are screen pixels; anything outside <paramref name="area"/> is clipped. The host replays it
    /// with Direct2D, antialiased by the GPU. Frozen brushes, pens and geometries are cached between frames.
    /// </summary>
    void Present(PixelRect area, Action<DrawingContext> draw);

    void Hide();
}
