using System.Net;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// One request as the web app sees it, after applying (only from this PC or a private address, where the proxy
/// lives) the X-Forwarded-* headers and, when it came through the proxy, the base path.
/// </summary>
internal sealed class WebRequest
{
    public required HttpListenerRequest Raw { get; init; }
    public required string Method { get; init; }

    /// <summary>Path inside the app ("/", "/api/…"); null when the base path came without its trailing slash.</summary>
    public required string? Path { get; init; }

    /// <summary>Public prefix of the app without the trailing slash ("" or "/kdesktop").</summary>
    public required string Prefix { get; init; }

    /// <summary>The device's address: the connection's, or the one the proxy reports.</summary>
    public required IPAddress ClientIp { get; init; }

    /// <summary>"https" when the proxy says so.</summary>
    public required string Scheme { get; init; }

    /// <summary>Host the browser asked for (the proxy's public one when it came through it).</summary>
    public string? Host { get; init; }

    /// <summary>
    /// Opened in a browser on this same PC (loopback, not through a proxy, Host "localhost"): no pairing needed.
    /// The Host check stops DNS-rebinding tricks from web pages.
    /// </summary>
    public bool IsLocal { get; init; }

    /// <summary>Public URL of the app (scheme://host/prefix/) when the request came through the proxy.</summary>
    public string? PublicUrl { get; init; }

    /// <summary>Builds the request, or returns null when its origin is not allowed.</summary>
    public static WebRequest? From(HttpListenerContext context, WebServerOptions options)
    {
        var raw = context.Request;
        var remote = raw.RemoteEndPoint.Address;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        var loopback = IPAddress.IsLoopback(remote);

        // Forwarded headers are only believed from this PC or a private address (the proxy: on the local network, or
        // at the other end of a VPN tunnel); from the internet they could only come through it anyway.
        var trusted = loopback || RemoteAddresses.IsPrivate(remote);
        var client = remote;
        var scheme = "http";
        string? forwardedHost = null;
        var forwardedPrefix = string.Empty;
        if (trusted)
        {
            if (LastForwardedFor(raw.Headers["X-Forwarded-For"]) is { } ip) client = ip;
            if (raw.Headers["X-Forwarded-Proto"] is { Length: > 0 } proto) scheme = proto.Split(',')[0].Trim().ToLowerInvariant();
            forwardedHost = raw.Headers["X-Forwarded-Host"]?.Split(',')[0].Trim();
            forwardedPrefix = WebServer.NormalizeBasePath(raw.Headers["X-Forwarded-Prefix"]);
        }

        var viaProxy = trusted && (forwardedHost is not null || raw.Headers["X-Forwarded-For"] is not null);

        // Through the proxy the app lives under its prefix: the one it reports, else the internet address's. Strip it
        // from the path if it is still there (Caddy "handle" vs "handle_path"). Straight from the network: the root.
        var path = raw.Url?.AbsolutePath ?? "/";
        var prefix = viaProxy ? (forwardedPrefix.Length > 0 ? forwardedPrefix : options.BasePath) : string.Empty;
        string? appPath;
        if (prefix.Length == 0) appPath = path;
        else if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)) appPath = null;
        else if (path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) appPath = path[prefix.Length..];
        else appPath = path; // the proxy already removed it

        // Where it really comes from (through the proxy: the device behind it) against where the server accepts from.
        if (!RemoteAddresses.Allows(options.Scope, client)) return null;

        var host = forwardedHost ?? raw.Headers["Host"];
        return new WebRequest
        {
            Raw = raw,
            Method = raw.HttpMethod.ToUpperInvariant(),
            Path = appPath is null ? null : Uri.UnescapeDataString(appPath),
            Prefix = prefix,
            ClientIp = client,
            Scheme = scheme,
            Host = host,
            PublicUrl = viaProxy && !string.IsNullOrEmpty(host) ? $"{scheme}://{host}{prefix}/" : null,
            IsLocal = loopback && raw.Headers["X-Forwarded-For"] is null && forwardedHost is null && IsLoopbackHost(raw.Headers["Host"]),
        };
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var name = host.StartsWith('[') ? host[..(host.IndexOf(']') + 1)] : host.Split(':')[0];
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name is "127.0.0.1" or "[::1]";
    }

    /// <summary>The right-most address: the one the proxy itself added.</summary>
    private static IPAddress? LastForwardedFor(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var last = header.Split(',')[^1].Trim();
        return ParseIp(last);
    }

    private static IPAddress? ParseIp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        // "[::1]:1234" / "1.2.3.4:1234"
        if (IPEndPoint.TryParse(text, out var ep)) return ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address;
        return IPAddress.TryParse(text, out var ip) ? ip : null;
    }
}
