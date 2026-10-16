using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>Wake-on-LAN: the «magic packet», finding a machine's MAC address, and waiting until it answers.</summary>
public static class WakeOnLan
{
    /// <summary>«AA:BB:CC:DD:EE:FF», «AA-BB-…», «AABBCCDDEEFF» or «aabb.ccdd.eeff» → 6 bytes; null if it isn't one.</summary>
    public static byte[]? ParseMac(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var hex = new string(text.Where(c => !(c is ':' or '-' or '.' or ' ')).ToArray());
        if (hex.Length != 12) return null;
        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i])) return null;
        return bytes;
    }

    public static string FormatMac(byte[] mac) => string.Join(':', mac.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary>6 × 0xFF followed by the MAC 16 times.</summary>
    public static byte[] MagicPacket(byte[] mac)
    {
        var packet = new byte[6 + 16 * 6];
        for (var i = 0; i < 6; i++) packet[i] = 0xFF;
        for (var i = 0; i < 16; i++) Buffer.BlockCopy(mac, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    /// <summary>
    /// Sends the packet. Without <paramref name="address"/>, to the broadcast of every network this PC is on (with
    /// several adapters 255.255.255.255 leaves by just one); with it, there (another subnet, a router that forwards it).
    /// </summary>
    public static async Task<int> SendAsync(byte[] mac, string? address, int port, CancellationToken token = default)
    {
        var packet = MagicPacket(mac);
        port = port is > 0 and <= 65535 ? port : 9;
        var targets = new List<IPEndPoint>();
        if (!string.IsNullOrWhiteSpace(address))
        {
            var host = address.Trim();
            var ip = IPAddress.TryParse(host, out var parsed) ? parsed
                : (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, token).ConfigureAwait(false)).FirstOrDefault()
                  ?? throw new InvalidOperationException($"No se encuentra «{host}»");
            targets.Add(new IPEndPoint(ip, port));
        }
        else
        {
            targets.Add(new IPEndPoint(IPAddress.Broadcast, port));
            targets.AddRange(DirectedBroadcasts().Select(ip => new IPEndPoint(ip, port)));
        }

        var sent = 0;
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        foreach (var target in targets.DistinctBy(t => t.ToString()))
        {
            try
            {
                // Twice: it's UDP, a packet can get lost.
                for (var i = 0; i < 2; i++) await udp.SendAsync(packet, target, token).ConfigureAwait(false);
                sent++;
            }
            catch (SocketException ex)
            {
                Log.Warn($"Wake-on-LAN: no se pudo enviar a {target}: {ex.Message}");
            }
        }
        return sent;
    }

    /// <summary>The broadcast address of each IPv4 network this PC is connected to (192.168.1.255…).</summary>
    public static IReadOnlyList<IPAddress> DirectedBroadcasts()
    {
        var result = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask is null) continue;
                var ip = info.Address.GetAddressBytes();
                var mask = info.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                result.Add(new IPAddress(broadcast));
            }
        }
        return result;
    }

    /// <summary>The MAC of a machine of the local network (it must be on to answer): its IP or its name.</summary>
    public static async Task<byte[]?> FindMacAsync(string host)
    {
        host = host.Trim();
        if (host.Length == 0) return null;
        var ip = IPAddress.TryParse(host, out var parsed) ? parsed
            : (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork).ConfigureAwait(false)).FirstOrDefault();
        if (ip is null || ip.AddressFamily != AddressFamily.InterNetwork) return null;
        return await Task.Run(() =>
        {
            var mac = new byte[6];
            var length = 6u;
#pragma warning disable CS0618 // IPAddress.Address: the IPv4 address as SendARP wants it
            var result = SendARP((uint)ip.Address, 0, mac, ref length);
#pragma warning restore CS0618
            return result == 0 && length == 6 ? mac : null;
        }).ConfigureAwait(false);
    }

    /// <summary>Pings <paramref name="host"/> until it answers or the time runs out. Returns whether it answered.</summary>
    public static async Task<bool> WaitForHostAsync(string host, TimeSpan timeout, CancellationToken token)
    {
        var until = DateTime.UtcNow + timeout;
        using var ping = new Ping();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (await AnswersAsync(ping, host, 1000).ConfigureAwait(false)) return true;
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(1000, token).ConfigureAwait(false);
        }
    }

    /// <summary>Whether <paramref name="host"/> answers a ping within <paramref name="milliseconds"/>.</summary>
    public static async Task<bool> AnswersAsync(string host, int milliseconds = 1000)
    {
        using var ping = new Ping();
        return await AnswersAsync(ping, host, milliseconds).ConfigureAwait(false);
    }

    private static async Task<bool> AnswersAsync(Ping ping, string host, int milliseconds)
    {
        try
        {
            var reply = await ping.SendPingAsync(host.Trim(), milliseconds).ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException) { return false; }
        catch (SocketException) { return false; }
    }

    [DllImport("iphlpapi.dll")]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddress, ref uint macLength);
}
