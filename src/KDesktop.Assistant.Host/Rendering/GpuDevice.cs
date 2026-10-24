using KDesktop.Core;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// The shared graphics devices (from Kobii): Direct3D 11 (for assistants that draw with shaders), Direct2D on the
/// same device (DirectComposition draws its surfaces with it), and the DXGI factory. Without a Direct3D 11 GPU (virtual machines, remote
/// desktop) it uses WARP, Windows' software rasterizer.
/// </summary>
/// <remarks>Used from one thread only (the engine's).</remarks>
internal sealed class GpuDevice : IDisposable
{
    private static readonly FeatureLevel[] Levels =
        [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    private GpuDevice(ID3D11Device device, ID3D11DeviceContext context, bool software)
    {
        Device = device;
        Context = context;
        IsSoftware = software;
        DxgiDevice = device.QueryInterface<IDXGIDevice1>();
        DxgiDevice.MaximumFrameLatency = 1;
        using var adapter = DxgiDevice.GetAdapter();
        AdapterName = adapter.Description.Description;
        Factory = adapter.GetParent<IDXGIFactory2>();
        D2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        D2DDevice = D2DFactory.CreateDevice(DxgiDevice);
        D2D = D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
        D2D.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    public IDXGIDevice1 DxgiDevice { get; }

    public IDXGIFactory2 Factory { get; }

    public ID2D1Factory1 D2DFactory { get; }

    public ID2D1Device D2DDevice { get; }

    public ID2D1DeviceContext D2D { get; }

    public bool IsSoftware { get; }

    public string AdapterName { get; }

    /// <summary>Creates the devices (with <paramref name="preferSoftware"/>, WARP straight away).</summary>
    public static GpuDevice Create(bool preferSoftware = false)
    {
        const DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
        if (!preferSoftware)
        {
            var result = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, flags, Levels, out var device, out var context);
            if (result.Success && device is not null && context is not null) return new GpuDevice(device, context, false);
            Log.Warn($"Asistente: Direct3D 11 por hardware no disponible ({result}): se usa WARP");
        }
        D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, flags, Levels, out var warp, out var warpContext).CheckError();
        return new GpuDevice(warp!, warpContext!, true);
    }

    public void Dispose()
    {
        D2D.Dispose();
        D2DDevice.Dispose();
        D2DFactory.Dispose();
        Factory.Dispose();
        DxgiDevice.Dispose();
        Context.ClearState();
        Context.Dispose();
        Device.Dispose();
    }
}
