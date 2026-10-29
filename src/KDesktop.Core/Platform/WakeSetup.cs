using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace KDesktop.Core.Platform;

/// <summary>A network card of this PC that could wake it: its MAC, the broadcast of its network and its name.</summary>
public sealed record WakeAdapter(string Mac, string Broadcast, string Name, bool Wired);

/// <summary>
/// How this PC can be woken from another device (the phone app keeps it to wake the PC while it is off), and whether
/// Windows lets it: the cards armed to wake it (<c>powercfg /devicequery wake_armed</c>) and the fast start-up, which
/// shuts down into a state many cards don't wake from.
/// </summary>
public static class WakeSetup
{
    private static readonly string[] Virtual = ["virtual", "hyper-v", "vmware", "virtualbox", "tap-", "wireguard", "wintun", "bluetooth", "loopback", "vpn"];

    /// <summary>
    /// The physical cards with an IPv4 address: the one <paramref name="arrivedAt"/> belongs to first (a request came
    /// in through it, so the phone can reach that network), then the wired ones (Wi-Fi rarely wakes a PC).
    /// </summary>
    public static IReadOnlyList<WakeAdapter> Adapters(IPAddress? arrivedAt = null)
    {
        var found = new List<(WakeAdapter Adapter, bool Arrived)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Wireless80211)) continue;
                var description = nic.Description;
                if (Virtual.Any(v => description.Contains(v, StringComparison.OrdinalIgnoreCase) || nic.Name.Contains(v, StringComparison.OrdinalIgnoreCase))) continue;
                var mac = nic.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length != 6 || mac.All(b => b == 0)) continue;
                foreach (var info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask is null) continue;
                    var ip = info.Address.GetAddressBytes();
                    var mask = info.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (var i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    var adapter = new WakeAdapter(WakeOnLan.FormatMac(mac), new IPAddress(broadcast).ToString(), description,
                        nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211);
                    found.Add((adapter, arrivedAt is not null && info.Address.Equals(arrivedAt)));
                    break;
                }
            }
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn("No se pudieron leer las tarjetas de red: " + ex.Message);
        }
        return found.OrderByDescending(f => f.Arrived).ThenByDescending(f => f.Adapter.Wired)
            .Select(f => f.Adapter).DistinctBy(a => a.Mac).ToList();
    }

    /// <summary>Windows' fast start-up is on: «Apagar» leaves the PC half hibernated.</summary>
    public static bool FastStartup()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return key?.GetValue("HiberbootEnabled") is int value && value != 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>The devices allowed to wake this PC, by Windows' name for them (the card's description).</summary>
    public static IReadOnlyList<string> WakeArmed()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg.exe", "/devicequery wake_armed")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn("powercfg: " + ex.Message);
            return [];
        }
    }

    /// <summary>Whether <paramref name="adapter"/> may wake the PC (by its name, among <paramref name="armed"/>).</summary>
    public static bool IsArmed(WakeAdapter adapter, IReadOnlyList<string> armed) =>
        armed.Any(a => a.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase));
}
