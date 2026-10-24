using System.Runtime.InteropServices;
using KDesktop.Assistant.Sdk;
using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// The transparent layer over every monitor where DirectComposition places the assistant's surfaces (as in Kobii).
/// It has no bitmap of its own (<c>WS_EX_NOREDIRECTIONBITMAP</c>) and always lets the mouse through
/// (<c>WS_EX_TRANSPARENT</c>): its content is small composition surfaces the compositor moves without copying anything on
/// the CPU. Clicks are picked up by separate windows (<see cref="HitWindow"/>).
/// </summary>
internal sealed class Overlay : IDisposable
{
    public const string ClassName = "KDesktopAssistantOverlay";

    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual2[] _layers;

    public Overlay(GpuDevice gpu)
    {
        Bounds = VirtualScreen();
        Window = new NativeWindow(ClassName, "K-Desktop assistant",
            WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);
        SetLayeredWindowAttributes(Window.Handle, 0, 255, LWA_ALPHA);

        // With the Direct2D device: its surfaces are drawn with a Direct2D context straight away.
        Marshal.ThrowExceptionForHR(DCompositionCreateDevice2(gpu.D2DDevice.NativePointer, typeof(IDCompositionDesktopDevice).GUID, out var device));
        Composition = new IDCompositionDesktopDevice(device);
        Composition.CreateTargetForHwnd(Window.Handle, true, out _target).CheckError();
        Root = Composition.CreateVisual();
        // One group per SurfaceLayer, back to front.
        _layers = [Composition.CreateVisual(), Composition.CreateVisual(), Composition.CreateVisual()];
        IDCompositionVisual? below = null;
        foreach (var layer in _layers)
        {
            Root.AddVisual(layer, true, below).CheckError();
            below = layer;
        }
        _target.SetRoot(Root).CheckError();
        Composition.Commit().CheckError();
        Window.Show();
    }

    public NativeWindow Window { get; }

    public IDCompositionDesktopDevice Composition { get; }

    public IDCompositionVisual2 Root { get; }

    /// <summary>The virtual screen (every monitor) in physical pixels.</summary>
    public PixelRect Bounds { get; private set; }

    public IDCompositionVisual Layer(SurfaceLayer layer) => _layers[(int)layer];

    /// <summary>Follows a change of monitors or resolution. True when the bounds changed.</summary>
    public bool UpdateBounds()
    {
        var bounds = VirtualScreen();
        if (bounds == Bounds) return false;
        Bounds = bounds;
        Window.Move(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        return true;
    }

    public void Commit() => Composition.Commit().CheckError();

    public void Dispose()
    {
        foreach (var layer in _layers) layer.Dispose();
        Root.Dispose();
        _target.Dispose();
        Composition.Dispose();
        Window.Dispose();
    }

    private static PixelRect VirtualScreen() => PixelRect.FromSize(
        GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN)), Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN)));
}

/// <summary>
/// The canvas of one surface: a composition surface on its own visual, drawn with Direct2D (the replayed WPF drawing).
/// </summary>
/// <remarks>
/// Not a swap chain: what is presented to a swap chain shows at once, while moving its visual waits for the commit, so a
/// frame drawn for the new place could show for a moment in the old one (a flash nearby, when the area jumps). A
/// composition surface shows its new content in the same commit as its new place. It costs the same.
/// </remarks>
internal sealed class StageLayer : IDisposable
{
    private readonly Overlay _overlay;
    private readonly IDCompositionVisual _parent;
    private IDCompositionSurface _surface;
    private bool _attached;
    private int _x;
    private int _y;

    public StageLayer(Overlay overlay, SurfaceLayer layer, int width, int height)
    {
        _overlay = overlay;
        _parent = overlay.Layer(layer);
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _surface = CreateSurface(Width, Height);
        Visual = overlay.Composition.CreateVisual();
        Visual.SetContent(_surface).CheckError();
    }

    public IDCompositionVisual2 Visual { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsAttached => _attached;

    /// <summary>
    /// Makes sure it measures at least <paramref name="width"/> × <paramref name="height"/>, in steps of
    /// <paramref name="step"/> pixels, and only shrinks when much is left over: a new surface costs, so it is not made
    /// every frame while something grows a little (moving to a monitor with another scaling).
    /// </summary>
    public void Fit(int width, int height, int step = 32)
    {
        static int Round(int value, int step) => Math.Max(step, (value + step - 1) / step * step);
        if (width > Width || height > Height || width * 4 < Width * 3 || height * 4 < Height * 3)
            Resize(Round(width, step), Round(height, step));
    }

    /// <summary>
    /// Starts drawing the whole surface: the Direct2D context to draw with (release it after <see cref="EndDraw"/>) and
    /// where the surface starts in it (the compositor keeps several surfaces in one texture).
    /// </summary>
    public ID2D1DeviceContext BeginDraw(out Int2 offset) => _surface.BeginDraw<ID2D1DeviceContext>(null, out offset);

    /// <summary>The new content shows with the next commit.</summary>
    public void EndDraw() => _surface.EndDraw().CheckError();

    /// <summary>
    /// Places the canvas (relative to the overlay) and shows it. False when it was already there.
    /// </summary>
    public bool Place(int x, int y)
    {
        if (_attached && x == _x && y == _y) return false;
        Visual.SetOffsetX(x);
        Visual.SetOffsetY(y);
        (_x, _y) = (x, y);
        if (_attached) return true;
        _parent.AddVisual(Visual, true, null).CheckError();
        _attached = true;
        return true;
    }

    public void Detach()
    {
        if (!_attached) return;
        _parent.RemoveVisual(Visual);
        _attached = false;
    }

    public void Dispose()
    {
        Detach();
        Visual.Dispose();
        _surface.Dispose();
    }

    private IDCompositionSurface CreateSurface(int width, int height) =>
        _overlay.Composition.CreateSurface((uint)width, (uint)height, Format.B8G8R8A8_UNorm, Vortice.DXGI.AlphaMode.Premultiplied);

    private void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        var surface = CreateSurface(width, height);
        Visual.SetContent(surface).CheckError();
        _surface.Dispose();
        _surface = surface;
        Width = width;
        Height = height;
    }
}
