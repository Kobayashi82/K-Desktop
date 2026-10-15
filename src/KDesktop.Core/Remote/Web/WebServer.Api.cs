using System.Net;
using System.Text.Json;

namespace KDesktop.Core.Remote.Web;

public sealed partial class WebServer
{
    private const string CookieName = "kd_token";
    private const int MaxBody = 64 * 1024;

    private async Task HandleApiAsync(WebRequest request, HttpListenerContext context)
    {
        var response = context.Response;

        // Requests that change something must carry this header: a page on another site cannot add it
        // without a CORS preflight, which is never answered.
        if (request.Method != "GET" && context.Request.Headers["X-KD"] != "1")
        {
            await WriteJsonAsync(response, new { error = "badRequest" }, 400).ConfigureAwait(false);
            return;
        }

        switch (request.Method, request.Path)
        {
            case ("GET", "/api/ping"):
                await WriteJsonAsync(response, new { app = AppPaths.VersionName, machine = Environment.MachineName }).ConfigureAwait(false);
                return;
            case ("POST", "/api/pair"):
                await PairAsync(request, context).ConfigureAwait(false);
                return;
        }

        var device = Devices.Authenticate(TokenOf(context.Request), request.ClientIp.ToString());
        if (device is null)
        {
            await WriteJsonAsync(response, new { error = "unpaired" }, 401).ConfigureAwait(false);
            return;
        }

        switch (request.Method, request.Path)
        {
            case ("GET", "/api/session"):
                await WriteJsonAsync(response, new
                {
                    app = AppPaths.VersionName,
                    machine = Environment.MachineName,
                    device = new { device.Id, device.Name },
                }).ConfigureAwait(false);
                break;
            case ("POST", "/api/logout"):
                Devices.Revoke(device.Id);
                SetTokenCookie(response, request, null);
                await WriteJsonAsync(response, new { ok = true }).ConfigureAwait(false);
                break;
            default:
                await WriteJsonAsync(response, new { error = "notFound" }, 404).ConfigureAwait(false);
                break;
        }
    }

    private async Task PairAsync(WebRequest request, HttpListenerContext context)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        var code = body?.TryGetProperty("code", out var c) == true ? c.GetString() : null;
        var name = body?.TryGetProperty("name", out var n) == true ? n.GetString() : null;
        var ip = request.ClientIp.ToString();
        if (Devices.Pair(code, name, ip) is not var (device, token))
        {
            // A little patience for whoever is guessing.
            await Task.Delay(800).ConfigureAwait(false);
            await WriteJsonAsync(context.Response, new { error = "badCode" }, 403).ConfigureAwait(false);
            return;
        }
        SetTokenCookie(context.Response, request, token);
        await WriteJsonAsync(context.Response, new { token, device = new { device.Id, device.Name } }).ConfigureAwait(false);
    }

    /// <summary>The token travels in an HttpOnly cookie (so images load too) or, as a fallback, as a Bearer header.</summary>
    private static string? TokenOf(HttpListenerRequest raw)
    {
        if (raw.Headers["Authorization"] is { } auth && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth[7..].Trim();
        return raw.Cookies[CookieName]?.Value;
    }

    private static void SetTokenCookie(HttpListenerResponse response, WebRequest request, string? token)
    {
        var cookie = token is null
            ? $"{CookieName}=; Path={request.Prefix}/; Max-Age=0; HttpOnly; SameSite=Strict"
            : $"{CookieName}={token}; Path={request.Prefix}/; Max-Age=34560000; HttpOnly; SameSite=Strict";
        if (request.Scheme == "https") cookie += "; Secure";
        response.AppendHeader("Set-Cookie", cookie);
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpListenerRequest raw)
    {
        if (!raw.HasEntityBody || raw.ContentLength64 is < 0 or > MaxBody) return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(raw.InputStream).ConfigureAwait(false);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException) { return null; }
    }
}
