using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace KDesktop.Core.Platform;

/// <summary>How the internet is doing, from what <see cref="NetworkCheck.InternetAsync"/> can tell.</summary>
public enum InternetState
{
    /// <summary>It works.</summary>
    Online,
    /// <summary>Addresses answer but names don't resolve: web pages won't open (DNS).</summary>
    NoDns,
    /// <summary>A sign-in page (hotel, train…) answers instead of the internet.</summary>
    Portal,
    /// <summary>On a network, but nothing outside answers.</summary>
    NoInternet,
    /// <summary>Not on any network.</summary>
    NoNetwork,
}

/// <summary>The Wi-Fi this PC is on: its name (null when Windows doesn't tell it), signal 0–100 and speed in Mb/s.</summary>
public sealed record WifiStatus(string? Name, int? Signal, int? Megabits);

/// <summary>How this PC is connected: networks by name, cable and/or Wi-Fi, local addresses and the router.</summary>
public sealed record LinkStatus(IReadOnlyList<string> Networks, bool Cable, WifiStatus? Wifi, IReadOnlyList<string> Addresses, string? Router);

/// <summary>The internet: its state, the best ping out (ms) and, when it doesn't work, whether the router answers.</summary>
public sealed record InternetStatus(InternetState State, int? Milliseconds, bool? RouterAnswers);

/// <summary>
/// One machine, website or address: whether its name exists (<paramref name="Found"/>), its address, its ping, the web
/// answer (HTTP status, tried when it is a website or doesn't answer pings) and the port when one was given.
/// </summary>
public sealed record HostStatus(string Host, bool Found, string? Address, int? PingMilliseconds, int? WebStatus, int? Port, bool? PortOpen);

/// <summary>What <see cref="NetworkCheck.RunAsync"/> found (only the parts asked for).</summary>
public sealed record NetworkReport(LinkStatus Link, InternetStatus? Internet, string? PublicIp, HostStatus? Host, bool PublicIpAsked = false);

/// <summary>
/// «¿Hay internet?», «¿responde el NAS?», «¿cuál es mi IP?» (B6, <c>network_check</c>): pings like «Equipo accesible»
/// (<see cref="WakeOnLan.AnswersAsync"/>), the networks of <see cref="NetworkInfo"/>, Windows' own connectivity test
/// (msftconnecttest) to tell a sign-in page or broken DNS apart, and the Wi-Fi signal from the WLAN service.
/// </summary>
public static class NetworkCheck
{
    private static readonly Lazy<HttpClient> Client = new(() =>
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("K-Desktop");
        return client;
    });

    /// <summary>Words that mean the router (the default gateway) instead of a machine's name.</summary>
    private static readonly string[] RouterWords = ["router", "gateway", "rúter", "routeur", "roteador", "puerta de enlace", "modem", "módem"];

    /// <summary>Reads what is asked: always the connection, and the internet, public IP and one host when wanted.</summary>
    public static async Task<NetworkReport> RunAsync(bool internet, bool publicIp, string? host, CancellationToken cancellationToken = default)
    {
        var link = await Task.Run(Link, cancellationToken).ConfigureAwait(false);
        var internetTask = internet ? MaybeAsync(InternetAsync(link, cancellationToken)) : Task.FromResult<InternetStatus?>(null);
        var ipTask = publicIp ? PublicIpAsync(cancellationToken) : Task.FromResult<string?>(null);
        var hostTask = string.IsNullOrWhiteSpace(host) ? Task.FromResult<HostStatus?>(null) : HostAsync(host, link.Router, cancellationToken);
        await Task.WhenAll(internetTask, ipTask, hostTask).ConfigureAwait(false);
        return new NetworkReport(link, await internetTask.ConfigureAwait(false), await ipTask.ConfigureAwait(false), await hostTask.ConfigureAwait(false), publicIp);
    }

    private static async Task<InternetStatus?> MaybeAsync(Task<InternetStatus> check) => await check.ConfigureAwait(false);

    /// <summary>The connection now: what Windows calls the networks, cable or Wi-Fi, addresses and the router.</summary>
    public static LinkStatus Link()
    {
        var cable = false;
        var wireless = false;
        string? router = null;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var gateway = adapter.GetIPProperties().GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                // Only adapters with a router are the way out (VPNs and virtual switches usually have none).
                if (gateway is null) continue;
                router ??= gateway.ToString();
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) wireless = true;
                else if (adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx) cable = true;
            }
        }
        catch (NetworkInformationException) { /* nothing to tell */ }
        var wifi = Wifi();
        if (wifi is null && wireless) wifi = new WifiStatus(null, null, null);
        // 169.254.x.x: an adapter that got no address (it says nothing useful).
        var addresses = NetworkInfo.LocalIPv4().Where(a => !a.StartsWith("169.254.", StringComparison.Ordinal)).ToList();
        return new LinkStatus(NetworkInfo.ConnectedNetworks(), cable, wifi, addresses, router);
    }

    /// <summary>
    /// Whether the internet works: Windows' connectivity test page (its exact text, or a sign-in page instead), pings to
    /// two public DNS servers and a name lookup, all at once. When nothing answers, whether the router does.
    /// </summary>
    public static async Task<InternetStatus> InternetAsync(LinkStatus link, CancellationToken cancellationToken = default)
    {
        var pings = new[] { PingAsync("1.1.1.1", 1500), PingAsync("8.8.8.8", 1500) };
        var names = ResolvesAsync("www.msftconnecttest.com", cancellationToken);
        var page = TestPageAsync(cancellationToken);
        await Task.WhenAll(pings[0], pings[1], names, page).ConfigureAwait(false);
        var best = pings.Select(p => p.Result).Where(ms => ms is not null).Min();
        if (page.Result == true) return new InternetStatus(InternetState.Online, best, null);
        if (page.Result == false) return new InternetStatus(InternetState.Portal, best, null);
        if (best is not null) return new InternetStatus(names.Result ? InternetState.Online : InternetState.NoDns, best, null);
        if (link.Router is null && link.Networks.Count == 0) return new InternetStatus(InternetState.NoNetwork, null, null);
        return new InternetStatus(InternetState.NoInternet, null, link.Router is { } router ? await PingAsync(router, 1000).ConfigureAwait(false) is not null : null);
    }

    /// <summary>The address the internet sees for this PC (ipify, or icanhazip); null without internet.</summary>
    public static async Task<string?> PublicIpAsync(CancellationToken cancellationToken = default)
    {
        foreach (var url in new[] { "https://api.ipify.org", "https://ipv4.icanhazip.com" })
        {
            try
            {
                var text = (await Client.Value.GetStringAsync(new Uri(url), cancellationToken).ConfigureAwait(false)).Trim();
                if (IPAddress.TryParse(text, out _)) return text;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested) { /* the next one */ }
        }
        return null;
    }

    /// <summary>
    /// What to check in what the user said: «google.com», «https://web.com/x» (a website), «192.168.1.10:22» (with a port),
    /// «el router» (the router's address, <paramref name="router"/>). Null when nothing is left.
    /// </summary>
    public static (string Host, int? Port, bool Web)? Target(string text, string? router)
    {
        text = text.Trim().Trim('"', '\'', '«', '»', '.', ',', '?', '¿').Trim();
        if (text.Length == 0) return null;
        if (router is not null && RouterWords.Any(w => Regex.IsMatch(text, @"(?<![\w.])" + w + @"(?![\w.])", RegexOptions.IgnoreCase))) return (router, null, false);
        if (text.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            return (uri.Host, uri.IsDefaultPort ? null : uri.Port, uri.Scheme is "http" or "https");
        var host = text.Split('/', 2)[0];
        int? port = null;
        var colon = host.LastIndexOf(':');
        // «host:22», but not an IPv6 address (several colons).
        if (colon > 0 && host.IndexOf(':') == colon && int.TryParse(host.AsSpan(colon + 1), out var number) && number is > 0 and <= 65535)
        {
            port = number;
            host = host[..colon];
        }
        if (host.Length == 0 || host.Any(char.IsWhiteSpace)) return null;
        // A name with a dot that isn't an address is a website («google.com»); «NAS» or «192.168.1.5» are machines.
        var web = host.Contains('.', StringComparison.Ordinal) && !IPAddress.TryParse(host, out _) || text.Contains('/', StringComparison.Ordinal);
        return (host, port, web);
    }

    /// <summary>
    /// Checks one machine or website: its name, a ping (two tries), its port when given, and the web when it is a website
    /// or it doesn't answer pings (many servers ignore them).
    /// </summary>
    public static async Task<HostStatus?> HostAsync(string text, string? router, CancellationToken cancellationToken = default)
    {
        if (Target(text, router) is not { } target) return null;
        IPAddress? address;
        if (IPAddress.TryParse(target.Host, out var parsed)) address = parsed;
        else
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(TimeSpan.FromSeconds(4));
                var all = await Dns.GetHostAddressesAsync(target.Host, limit.Token).ConfigureAwait(false);
                address = all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? all.FirstOrDefault();
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                address = null;
            }
            if (address is null) return new HostStatus(target.Host, false, null, null, null, target.Port, null);
        }

        var ip = address.ToString();
        // All at once: a machine that is off would otherwise take the ping, the port and the web one after another.
        var pingTask = PingTwiceAsync(ip);
        var portTask = target.Port is { } port ? PortOpenAsync(address, port, cancellationToken) : Task.FromResult(false);
        var webTask = target.Web || target.Port is null ? WebStatusAsync(target.Host, target.Port, cancellationToken) : Task.FromResult<int?>(null);
        await Task.WhenAll(pingTask, portTask, webTask).ConfigureAwait(false);
        var ping = pingTask.Result;
        // The web of a machine that answers pings is only told when a website was asked for.
        var web = target.Web || ping is null ? webTask.Result : null;
        return new HostStatus(target.Host, true, ip, ping, web, target.Port, target.Port is null ? null : portTask.Result);

        static async Task<int?> PingTwiceAsync(string ip) => await PingAsync(ip, 2000).ConfigureAwait(false) ?? await PingAsync(ip, 2000).ConfigureAwait(false);
    }

    /// <summary>The ping in ms (at least 1), or null when it doesn't answer.</summary>
    public static async Task<int?> PingAsync(string host, int milliseconds)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, milliseconds).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? (int)Math.Max(1, reply.RoundtripTime) : null;
        }
        catch (PingException) { return null; }
        catch (SocketException) { return null; }
    }

    private static async Task<bool> ResolvesAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(3));
            return (await Dns.GetHostAddressesAsync(name, limit.Token).ConfigureAwait(false)).Length > 0;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Windows' test page: true with its exact text, false when something else answers (a sign-in page), null when nothing does.</summary>
    private static async Task<bool?> TestPageAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.Value.GetAsync(new Uri("http://www.msftconnecttest.com/connecttest.txt"), cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK && text.Trim() == "Microsoft Connect Test";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<bool> PortOpenAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await socket.ConnectAsync(address, port, limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// The HTTP status of the website (https and http at once, https preferred; up to 4 s); a redirect counts as an
    /// answer. Null when nothing answers.
    /// </summary>
    private static async Task<int?> WebStatusAsync(string host, int? port, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(4));
        var secure = AskAsync("https");
        var plain = AskAsync("http");
        return await secure.ConfigureAwait(false) ?? await plain.ConfigureAwait(false);

        async Task<int?> AskAsync(string scheme)
        {
            try
            {
                var url = new UriBuilder(scheme, host, port ?? -1).Uri;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await Client.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
                return (int)response.StatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or UriFormatException && !cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The Wi-Fi connection from the WLAN service: name, signal and speed. Windows 11 hides the name (and sometimes all of
    /// it) from programs without location permission: then only that it is connected (the name comes from
    /// <see cref="NetworkInfo.ConnectedNetworks"/>). Null without a connected Wi-Fi.
    /// </summary>
    public static WifiStatus? Wifi()
    {
        var client = IntPtr.Zero;
        var list = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != 0) return null;
            if (WlanEnumInterfaces(client, IntPtr.Zero, out list) != 0) return null;
            var count = Marshal.ReadInt32(list);
            for (var i = 0; i < count; i++)
            {
                // WLAN_INTERFACE_INFO_LIST: two DWORDs, then items of GUID (16) + WCHAR[256] (512) + state (4).
                var item = list + 8 + i * 532;
                if (Marshal.ReadInt32(item + 528) != 1) continue; // wlan_interface_state_connected
                var guid = Marshal.PtrToStructure<Guid>(item);
                if (WlanQueryInterface(client, ref guid, 7, IntPtr.Zero, out _, out var data, out _) != 0) return new WifiStatus(null, null, null); // current_connection
                try
                {
                    // WLAN_CONNECTION_ATTRIBUTES: state, mode, profile WCHAR[256] at 8; the SSID at 520 (length + 32 bytes),
                    // signal quality at 576 and receive rate (kb/s) at 580.
                    var length = Math.Clamp(Marshal.ReadInt32(data + 520), 0, 32);
                    var ssid = new byte[length];
                    Marshal.Copy(data + 524, ssid, 0, length);
                    var name = Encoding.UTF8.GetString(ssid).TrimEnd('\0');
                    if (name.Length == 0) name = Marshal.PtrToStringUni(data + 8) ?? string.Empty;
                    var signal = Marshal.ReadInt32(data + 576);
                    var rate = Marshal.ReadInt32(data + 580);
                    return new WifiStatus(name.Length > 0 ? name : null, signal is >= 0 and <= 100 ? signal : null, rate > 0 ? rate / 1000 : null);
                }
                finally { WlanFreeMemory(data); }
            }
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null; // no WLAN service on this Windows
        }
        finally
        {
            if (list != IntPtr.Zero) WlanFreeMemory(list);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(int clientVersion, IntPtr reserved, out int negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out int dataSize, out IntPtr data, out int valueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
