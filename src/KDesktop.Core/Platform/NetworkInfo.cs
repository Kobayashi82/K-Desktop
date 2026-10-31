using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace KDesktop.Core.Platform;

public static class NetworkInfo
{
    /// <summary>
    /// IPv4 addresses of the network adapters that are up (no loopback). Adapters with a default gateway (the real
    /// LAN) come first, before VPNs and virtual switches.
    /// </summary>
    public static IReadOnlyList<string> LocalIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetIPProperties())
                .OrderByDescending(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .SelectMany(p => p.UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>
    /// Bytes received and sent, since they started, by the adapters that are up and have an address (no loopback).
    /// .NET also lists the filters stacked on an adapter (WFP, QoS, VirtualBox…) as interfaces that are up, each with
    /// the same figures: counting them made a 42 MB/s download look like 210.
    /// </summary>
    public static (long Down, long Up) Traffic()
    {
        try { return Sum(CountedAdapters()); }
        catch (NetworkInformationException) { return (0, 0); }
    }

    /// <summary>The adapters <see cref="Traffic"/> counts: up, with an address, no loopback (finding them costs ~30 ms).</summary>
    public static NetworkInterface[] CountedAdapters() =>
    [
        .. NetworkInterface.GetAllNetworkInterfaces().Where(a =>
            a.OperationalStatus == OperationalStatus.Up && a.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            a.GetIPProperties().UnicastAddresses.Count > 0),
    ];

    /// <summary>Bytes received and sent by <paramref name="adapters"/> (only their counters: almost nothing).</summary>
    public static (long Down, long Up) Sum(IEnumerable<NetworkInterface> adapters)
    {
        long down = 0, up = 0;
        foreach (var adapter in adapters)
        {
            var statistics = adapter.GetIPStatistics();
            down += statistics.BytesReceived;
            up += statistics.BytesSent;
        }
        return (down, up);
    }

    /// <summary>
    /// Names of the networks this PC is connected to, as Windows calls them (the Wi-Fi SSID, «Red 2» for a cable…),
    /// from the Network List Manager. Empty when there is none (or it can't be asked).
    /// </summary>
    public static IReadOnlyList<string> ConnectedNetworks()
    {
        var names = new List<string>();
        object? manager = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (type is null) return names;
            manager = Activator.CreateInstance(type);
            dynamic nlm = manager!;
            foreach (dynamic network in nlm.GetNetworks(1)) // NLM_ENUM_NETWORK_CONNECTED
            {
                try
                {
                    string name = network.GetName();
                    if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(network); }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron leer las redes conectadas: " + ex.Message);
        }
        finally
        {
            if (manager is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(manager);
        }
        return names;
    }
}
