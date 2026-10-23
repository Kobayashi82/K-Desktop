using System.Net;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// How the web server is set up (built from the settings; a change restarts it). The base path is the one of the
/// internet address ("/kdesktop" in https://example.com/kdesktop): it only applies to what comes through the proxy.
/// </summary>
public sealed record WebServerOptions(int Port, string BasePath);

public enum WebServerState { Stopped, Running, LocalOnly, Error }

/// <summary>
/// HTTP server for the remote web app (PWA). Plain HTTP for the local network: HTTPS comes from a reverse proxy
/// (Caddy) in front of it. Built on HttpListener (http.sys), which is part of the runtime, so it adds nothing to the exe.
/// </summary>
/// <remarks>
/// It binds <c>http://+:port/</c> (any host name, so the proxy can pass its own Host header). That needs
/// administrator rights, which the main instance has; without them it falls back to localhost only.
/// Every URL of the web app is relative, so it works at the root of a domain or under a path.
/// </remarks>
public sealed partial class WebServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    private readonly IWebHost _host;
    private readonly WebCatalog _catalog;

    public WebServer(IWebHost host)
    {
        _host = host;
        _catalog = new WebCatalog(host);
        _host.Engine.Magnifier.Changed += BroadcastZoom;
        WatchAssistants();
    }

    /// <summary>Paired devices and pairing codes.</summary>
    public WebDevices Devices { get; } = new();

    public WebServerOptions? Options { get; private set; }
    public WebServerState State { get; private set; }
    public bool IsRunning => State is WebServerState.Running or WebServerState.LocalOnly;

    /// <summary>Last public address seen through the proxy (scheme://host/prefix/), used when none is configured.</summary>
    public string? SeenPublicUrl { get; private set; }

    /// <summary>The app's address on the internet: the configured one (Settings), else the one seen through the proxy.</summary>
    public string? PublicAddress =>
        _host.Engine.Config.Settings.WebPublicUrl.Trim() is { Length: > 0 } url ? url.TrimEnd('/') + "/" : SeenPublicUrl;

    /// <summary>
    /// Every address a device can reach the app at: the public one first, then this PC's on the local network. The
    /// Android app tries them all and uses the one that answers.
    /// </summary>
    public IReadOnlyList<string> Addresses()
    {
        var list = new List<string>();
        if (PublicAddress is { } address) list.Add(address);
        if (Options is { } o && State == WebServerState.Running) list.AddRange(LocalUrls(o.Port));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public event Action? StateChanged;

    /// <summary>A touchpad moved this PC's pointer (once per connection).</summary>
    public event Action? PointerMoved;

    public void Start(WebServerOptions options)
    {
        options = options with { BasePath = NormalizeBasePath(options.BasePath) };
        if (IsRunning && options == Options) return;
        Stop();
        Options = options;

        if (TryListen(["+"], options.Port, out var listener))
            State = WebServerState.Running;
        else if (TryListen(["localhost", "127.0.0.1"], options.Port, out listener) || TryListen(["localhost"], options.Port, out listener))
            State = WebServerState.LocalOnly;
        else
            State = WebServerState.Error;

        if (listener is not null)
        {
            _listener = listener;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenAsync(listener, _cts.Token));
            Log.Info(State == WebServerState.Running
                ? $"Web: escuchando en el puerto {options.Port}"
                : $"Web: escuchando solo en localhost:{options.Port} (sin permisos para la red)");
            if (State == WebServerState.Running) WebFirewall.Ensure(options);
        }
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _cts?.Cancel();
        CloseInputs();
        try { _listener?.Close(); } catch { /* ignore */ }
        _listener = null;
        _cts = null;
        if (State == WebServerState.Stopped) return;
        State = WebServerState.Stopped;
        StateChanged?.Invoke();
    }

    private static bool TryListen(string[] hosts, int port, out HttpListener? listener)
    {
        listener = new HttpListener { IgnoreWriteExceptions = true };
        foreach (var host in hosts) listener.Prefixes.Add($"http://{host}:{port}/");
        try
        {
            listener.Start();
            return true;
        }
        catch (HttpListenerException ex)
        {
            Log.Warn($"Web: no se pudo escuchar en {string.Join(", ", hosts)}:{port} ({ex.ErrorCode}: {ex.Message})");
            try { listener.Close(); } catch { /* ignore */ }
            listener = null;
            return false;
        }
    }

    private async Task ListenAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) when (token.IsCancellationRequested || !listener.IsListening) { return; }
            catch (HttpListenerException ex)
            {
                Log.Warn($"Web: {ex.Message}");
                continue;
            }
            _ = Task.Run(() => HandleAsync(context), token);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            if (Options is not { } options) return;
            var request = WebRequest.From(context, options);
            if (request is null)
            {
                await WriteTextAsync(response, 403, "Forbidden").ConfigureAwait(false);
                return;
            }
            if (request.PublicUrl is { } seen) SeenPublicUrl = seen;

            SetCommonHeaders(response);
            if (request.Path is null)
            {
                // "/kdesktop" without the trailing slash: relative URLs need the folder form.
                response.StatusCode = 308;
                response.RedirectLocation = request.Prefix + "/";
                return;
            }

            if (request.Path.StartsWith("/api/", StringComparison.Ordinal))
                await HandleApiAsync(request, context).ConfigureAwait(false);
            else
                await ServeStaticAsync(request, context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // Client went away.
        }
        catch (Exception ex)
        {
            Log.Error($"Web: error en {context.Request.HttpMethod} {context.Request.RawUrl}", ex);
            try { await WriteTextAsync(response, 500, "Internal error").ConfigureAwait(false); } catch { /* ignore */ }
        }
        finally
        {
            try { response.Close(); } catch { /* ignore */ }
        }
    }

    private static void SetCommonHeaders(HttpListenerResponse response)
    {
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    }

    // ── static files ─────────────────────────────────────────────────────

    private static async Task ServeStaticAsync(WebRequest request, HttpListenerContext context)
    {
        var response = context.Response;
        if (request.Method is not ("GET" or "HEAD"))
        {
            await WriteTextAsync(response, 405, "Method not allowed").ConfigureAwait(false);
            return;
        }

        var path = request.Path == "/" ? "index.html" : request.Path![1..];
        var asset = path.Equals("manifest.webmanifest", StringComparison.OrdinalIgnoreCase) ? WebAssets.Manifest() : WebAssets.Get(path);
        if (asset is null)
        {
            await WriteTextAsync(response, 404, "Not found").ConfigureAwait(false);
            return;
        }

        response.ContentType = asset.ContentType;
        // Always revalidate (cheap with the ETag): a new K-Desktop version must reach the phone at once.
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["ETag"] = asset.ETag;
        if (context.Request.Headers["If-None-Match"] == asset.ETag)
        {
            response.StatusCode = 304;
            return;
        }
        response.ContentLength64 = asset.Bytes.Length;
        if (request.Method == "GET") await response.OutputStream.WriteAsync(asset.Bytes).ConfigureAwait(false);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    internal static async Task WriteTextAsync(HttpListenerResponse response, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    internal static async Task WriteJsonAsync(HttpListenerResponse response, object value, int status = 200)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, WebJson.Options);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    /// <summary>"kdesktop/", "/kdesktop" → "/kdesktop"; empty → "".</summary>
    public static string NormalizeBasePath(string? path)
    {
        var p = (path ?? string.Empty).Trim().Trim('/');
        return p.Length == 0 ? string.Empty : "/" + p;
    }

    /// <summary>The path of an internet address: "https://example.com/kdesktop" → "/kdesktop"; none or invalid → "".</summary>
    public static string BasePathOf(string? publicUrl) =>
        Uri.TryCreate(publicUrl?.Trim(), UriKind.Absolute, out var uri) ? NormalizeBasePath(uri.AbsolutePath) : string.Empty;

    /// <summary>Addresses of this PC on the local network, as the web app URLs to type in a browser.</summary>
    public static IReadOnlyList<string> LocalUrls(int port) =>
        NetworkInfo.LocalIPv4().Select(ip => $"http://{ip}:{port}/").ToList();

    public void Dispose()
    {
        _host.Engine.Magnifier.Changed -= BroadcastZoom;
        Stop();
        Devices.Flush();
    }
}

/// <summary>JSON for the web API: camelCase, compact.</summary>
internal static class WebJson
{
    public static JsonSerializerOptions Options { get; } = new(Json.Options) { WriteIndented = false };
}
