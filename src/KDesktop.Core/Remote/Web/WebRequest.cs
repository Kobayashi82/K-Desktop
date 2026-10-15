using System.Net;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// One request as the web app sees it, after applying the base path and (only from a trusted proxy) the
/// X-Forwarded-* headers.
/// </summary>
internal sealed class WebRequest
{
    public required HttpListenerRequest Raw { get; init; }
    public required string Method { get; init; }

    /// <summary>Path inside the app ("/", "/api/…"); null when the base path came without its trailing slash.</summary>
    public required string? Path { get; init; }

    /// <summary>Public prefix of the app without the trailing slash ("" or "/kdesktop").</summary>
    public required string Prefix { get; init; }

    /// <summary>The device's address: the connection's, or the one the trusted proxy reports.</summary>
    public required IPAddress ClientIp { get; init; }

    /// <summary>"https" when the proxy says so.</summary>
    public required string Scheme { get; init; }

    /// <summary>Public URL of the app (scheme://host/prefix/) when the request came through the proxy.</summary>
    public string? PublicUrl { get; init; }

    /// <summary>Builds the request, or returns null when its origin is not allowed.</summary>
    public static WebRequest? From(HttpListenerContext context, WebServerOptions options)
    {
        var raw = context.Request;
        var remote = raw.RemoteEndPoint.Address;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        var loopback = IPAddress.IsLoopback(remote);
        var proxy = ParseIp(options.TrustedProxy);
        var fromProxy = proxy is not null && remote.Equals(proxy);

        if (!loopback)
        {
            if (!options.AllowRemote) return null;
            if (options.ProxyOnly && proxy is not null && !fromProxy) return null;
        }

        // Forwarded headers are only believed from the proxy (or this PC, where a local proxy would run).
        var trusted = loopback || fromProxy;
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

        // The path the proxy left: strip the configured base path if it is still there (Caddy "handle" vs "handle_path").
        var path = raw.Url?.AbsolutePath ?? "/";
        var basePath = options.BasePath;
        string? appPath;
        if (basePath.Length == 0) appPath = path;
        else if (path.Equals(basePath, StringComparison.OrdinalIgnoreCase)) appPath = null;
        else if (path.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase)) appPath = path[basePath.Length..];
        else appPath = path; // the proxy already removed it
        var prefix = forwardedPrefix + basePath;

        var host = forwardedHost ?? raw.Headers["Host"];
        var viaProxy = trusted && (forwardedHost is not null || raw.Headers["X-Forwarded-For"] is not null);
        return new WebRequest
        {
            Raw = raw,
            Method = raw.HttpMethod.ToUpperInvariant(),
            Path = appPath is null ? null : Uri.UnescapeDataString(appPath),
            Prefix = prefix,
            ClientIp = client,
            Scheme = scheme,
            PublicUrl = viaProxy && !string.IsNullOrEmpty(host) ? $"{scheme}://{host}{prefix}/" : null,
        };
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
