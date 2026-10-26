using System.Net;
using System.Net.Sockets;

namespace KDesktop.Core.Remote;

/// <summary>Where the remote control (UDP, web app) accepts requests from.</summary>
public enum RemoteScope { ThisPc, LocalNetwork, Internet }

public static class RemoteAddresses
{
    /// <summary>Whether a request from <paramref name="ip"/> gets in with <paramref name="scope"/>.</summary>
    public static bool Allows(RemoteScope scope, IPAddress ip) => scope switch
    {
        RemoteScope.ThisPc => IPAddress.IsLoopback(Normalize(ip)),
        RemoteScope.LocalNetwork => IsLocal(ip),
        _ => true,
    };

    /// <summary>This PC or a private address (local network, the other end of a VPN tunnel).</summary>
    public static bool IsLocal(IPAddress ip)
    {
        ip = Normalize(ip);
        return IPAddress.IsLoopback(ip) || IsPrivate(ip);
    }

    /// <summary>Private ranges: 10/8, 172.16/12, 192.168/16, 100.64/10 (CG-NAT, VPNs), 169.254/16, IPv6 unique and link local.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        ip = Normalize(ip);
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6UniqueLocal || ip.IsIPv6LinkLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
               || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>"::ffff:1.2.3.4" (what a dual-mode socket reports) → "1.2.3.4".</summary>
    public static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
}
