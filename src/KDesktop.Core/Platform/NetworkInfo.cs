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
}
