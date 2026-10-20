using KDesktop.Assistant.Sdk;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// The transparent layer over every monitor where DirectComposition places the assistant's surfaces (as in Kobii).
/// It has no bitmap of its own (<c>WS_EX_NOREDIRECTIONBITMAP</c>) and always lets the mouse through
/// (<c>WS_EX_TRANSPARENT</c>): its content is small swap chains the compositor moves without copying anything on
/// the CPU. Clicks are picked up by separate windows (<see cref="HitWindow"/>).
/// </summary>
internal sealed class Overlay : IDisposable
{
    public const string ClassName = "KDesktopAssistantOverlay";

    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual[] _layers;

    public Overlay(GpuDevice gpu)
    {
        Bounds = VirtualScreen();
        Window = new NativeWindow(ClassName, "K-Desktop assistant",
            WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
            Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);
        SetLayeredWindowAttributes(Window.Handle, 0, 255, LWA_ALPHA);

        Composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(gpu.DxgiDevice);
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

    public IDCompositionDevice Composition { get; }

    public IDCompositionVisual Root { get; }

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

    public void Commit() => Composition.Commit();

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
/// The canvas of one surface: a small swap chain that DirectComposition moves by changing its visual's offset, and
/// that is drawn with Direct3D and Direct2D (from Kobii).
/// </summary>
internal sealed class StageLayer : IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly IDCompositionVisual _parent;
    private readonly IDXGISwapChain1 _swapChain;
    private ID3D11RenderTargetView? _renderTarget;
    private ID2D1Bitmap1? _bitmap;
    private bool _attached;

    public StageLayer(GpuDevice gpu, Overlay overlay, SurfaceLayer layer, int width, int height)
    {
        _gpu = gpu;
        _parent = overlay.Layer(layer);
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        var description = new SwapChainDescription1
        {
            Width = (uint)Width,
            Height = (uint)Height,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
        };
        _swapChain = gpu.Factory.CreateSwapChainForComposition(gpu.Device, description);
        Visual = overlay.Composition.CreateVisual();
        Visual.SetContent(_swapChain).CheckError();
    }

    public IDCompositionVisual Visual { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsAttached => _attached;

    /// <summary>
    /// Makes sure it measures at least <paramref name="width"/> × <paramref name="height"/>, in steps of
    /// <paramref name="step"/> pixels, and only shrinks when much is left over: resizing costs, so it does not
    /// happen every frame while something grows a little (moving to a monitor with another scaling).
    /// </summary>
    public void Fit(int width, int height, int step = 32)
    {
        static int Round(int value, int step) => Math.Max(step, (value + step - 1) / step * step);
        if (width > Width || height > Height || width * 4 < Width * 3 || height * 4 < Height * 3)
            Resize(Round(width, step), Round(height, step));
    }

    /// <summary>Render targets of the current buffer (made on first use).</summary>
    public (ID3D11RenderTargetView Target, ID2D1Bitmap1 Bitmap) Targets()
    {
        if (_renderTarget is null || _bitmap is null)
        {
            using var texture = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _renderTarget = _gpu.Device.CreateRenderTargetView(texture);
            using var surface = _swapChain.GetBuffer<IDXGISurface>(0);
            _bitmap = _gpu.D2D.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw));
        }
        return (_renderTarget, _bitmap);
    }

    public void Clear() => _gpu.Context.ClearRenderTargetView(Targets().Target, new Color4(0, 0, 0, 0));

    /// <summary>Presents without waiting for the vertical sync: the compositor takes the latest frame.</summary>
    public void Present() => _swapChain.Present(0, PresentFlags.None).CheckError();

    /// <summary>Places the canvas (relative to the overlay) and shows it.</summary>
    public void Place(int x, int y)
    {
        Visual.SetOffsetX(x);
        Visual.SetOffsetY(y);
        if (_attached) return;
        _parent.AddVisual(Visual, true, null).CheckError();
        _attached = true;
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
        ReleaseTargets();
        Visual.Dispose();
        _swapChain.Dispose();
    }

    private void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        ReleaseTargets();
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        Width = width;
        Height = height;
    }

    private void ReleaseTargets()
    {
        if (_gpu.D2D.Target is not null) _gpu.D2D.Target = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _renderTarget?.Dispose();
        _renderTarget = null;
        _gpu.Context.ClearState();
        _gpu.Context.Flush();
    }
}
