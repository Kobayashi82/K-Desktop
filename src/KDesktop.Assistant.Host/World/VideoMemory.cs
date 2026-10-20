using Vortice.DXGI;

namespace KDesktop.Assistant.Host.World;

/// <summary>The graphics card's own memory, to recommend a model that fits (stage 3.1).</summary>
internal static class VideoMemory
{
    private static double? _gb;
    private static bool _measured;

    /// <summary>The largest dedicated video memory of the real adapters, in GB (null: none, or it could not be read). Measured once.</summary>
    public static double? Gigabytes()
    {
        if (_measured) return _gb;
        _measured = true;
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            ulong best = 0;
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var description = adapter.Description1;
                    if ((description.Flags & AdapterFlags.Software) == 0) best = Math.Max(best, (ulong)description.DedicatedVideoMemory);
                }
            }
            _gb = best > 0 ? Math.Round(best / (1024.0 * 1024 * 1024), 1) : null;
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException)
        {
            _gb = null;
        }
        return _gb;
    }
}
