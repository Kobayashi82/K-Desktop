using System.Net;
using System.Text.Json;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Remote.Web;

public sealed partial class WebServer
{
    private const string CookieName = "kd_token";
    private const int MaxBody = 64 * 1024;

    /// <summary>How long a run request waits for the command before answering "still running".</summary>
    private static readonly TimeSpan RunWait = TimeSpan.FromSeconds(8);

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

        // Open to everyone: what the pairing screen needs.
        switch (request.Method, request.Path)
        {
            case ("GET", "/api/ping"):
                await WriteJsonAsync(response, new { app = AppPaths.VersionName, machine = Environment.MachineName, pc = Platform.PcIdentity.Id, api = ApiVersion }).ConfigureAwait(false);
                return;
            case ("GET", "/api/i18n"):
                await WriteJsonAsync(response, Texts(context.Request.QueryString["lang"])).ConfigureAwait(false);
                return;
            case ("POST", "/api/pair"):
                await PairAsync(request, context).ConfigureAwait(false);
                return;
            case ("GET", "/api/input"):
                // Authenticated inside: by cookie, or by its first message.
                await InputAsync(request, context).ConfigureAwait(false);
                return;
        }

        var trusted = Trusted(request);
        var device = trusted ? WebDevices.Local : Devices.Authenticate(TokenOf(context.Request), request.ClientIp.ToString());
        if (device is null)
        {
            await WriteJsonAsync(response, new { error = "unpaired" }, 401).ConfigureAwait(false);
            return;
        }

        if (request.Method == "GET" && request.Path!.StartsWith("/api/icon/", StringComparison.Ordinal))
        {
            await IconAsync(request.Path["/api/icon/".Length..], response).ConfigureAwait(false);
            return;
        }
        if (await AssistantsAsync(request, context, device).ConfigureAwait(false)) return;
        if (await InboxAsync(request, context, device).ConfigureAwait(false)) return;
        if (await CalendarAsync(request, context).ConfigureAwait(false)) return;

        switch (request.Method, request.Path)
        {
            case ("GET", "/api/session"):
                // Signed in by the header alone (the app moved to another address with its token): the cookie too,
                // which images and the event stream need.
                var cookie = !trusted && TokenOf(context.Request) is { } token && context.Request.Cookies[CookieName]?.Value != token;
                if (cookie) SetTokenCookie(response, request, TokenOf(context.Request));
                await WriteJsonAsync(response, new
                {
                    app = AppPaths.VersionName,
                    version = AppPaths.FullVersion,
                    api = ApiVersion,
                    pc = Platform.PcIdentity.Id,
                    machine = Environment.MachineName,
                    device = new { device.Id, Name = trusted ? Loc.T("pwa.thisPc") : device.Name },
                    local = trusted,
                    input = InputAllowed,
                    voice = _host.VoiceReady,
                    assistants = AssistantsOn,
                    power = PowerAllowed,
                    wake = WakeInfo(context),
                    addresses = Addresses(),
                    android = AndroidApp.Current is { } apk ? new { apk.Version, apk.Code } : null,
                    cookie,
                }).ConfigureAwait(false);
                break;
            case ("POST", "/api/link"):
                // Pair this same device at another address (see WebDevices.NewLinkCode).
                if (trusted) await WriteJsonAsync(response, new { error = "local" }, 400).ConfigureAwait(false);
                else await WriteJsonAsync(response, new { code = Devices.NewLinkCode(device) }).ConfigureAwait(false);
                break;
            case ("GET", "/api/events"):
                await EventsAsync(response, device).ConfigureAwait(false);
                break;
            case ("GET", "/api/catalog"):
                await WriteJsonAsync(response, _catalog.Build()).ConfigureAwait(false);
                break;
            case ("GET", "/api/android"):
                await AndroidAsync(response).ConfigureAwait(false);
                break;
            case ("POST", "/api/run"):
                await RunAsync(context, device.Name).ConfigureAwait(false);
                break;
            case ("POST", "/api/event"):
                await RemoteEventAsync(context, request).ConfigureAwait(false);
                break;
            case ("POST", "/api/voice"):
                await VoiceAsync(context, device.Id).ConfigureAwait(false);
                break;
            case ("GET", "/api/audio"):
                await AudioStateAsync(response).ConfigureAwait(false);
                break;
            case ("POST", "/api/audio"):
                await AudioChangeAsync(context).ConfigureAwait(false);
                break;
            case ("GET", "/api/media"):
                await MediaStateAsync(response).ConfigureAwait(false);
                break;
            case ("POST", "/api/media"):
                await MediaChangeAsync(context).ConfigureAwait(false);
                break;
            case ("GET", "/api/media/art"):
                await MediaArtAsync(context).ConfigureAwait(false);
                break;
            case ("POST", "/api/power"):
                await PowerAsync(context, device).ConfigureAwait(false);
                break;
            case ("POST", "/api/wake"):
                await WakeAsync(context, device).ConfigureAwait(false);
                break;
            case ("POST", "/api/share"):
                await ShareTextAsync(context, device).ConfigureAwait(false);
                break;
            case ("POST", "/api/share/file"):
                await ShareFileAsync(context, device).ConfigureAwait(false);
                break;
            case ("POST", "/api/logout"):
                if (!trusted) Devices.Revoke(device.Id);
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
        var ip = request.ClientIp.ToString();
        if (Devices.Pair(Str(body, "code"), Str(body, "name"), ip, request.IsLocal) is not var (device, token))
        {
            // A little patience for whoever is guessing.
            await Task.Delay(800).ConfigureAwait(false);
            await WriteJsonAsync(context.Response, new { error = "badCode" }, 403).ConfigureAwait(false);
            return;
        }
        SetTokenCookie(context.Response, request, token);
        await WriteJsonAsync(context.Response, new { token, device = new { device.Id, device.Name } }).ConfigureAwait(false);
    }

    /// <summary>«Llamada remota»: {"name": "…"} fires the events waiting for it. 404 when none is.</summary>
    private async Task RemoteEventAsync(HttpListenerContext context, WebRequest request)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        var name = (Str(body, "name") ?? string.Empty).Trim();
        var fired = name.Length == 0 ? 0 : _host.Engine.RemoteCall?.Invoke(name, "web " + request.ClientIp) ?? 0;
        await WriteJsonAsync(context.Response, new { fired }, fired > 0 ? 200 : 404).ConfigureAwait(false);
    }

    /// <summary>Runs a command and answers with what happened (or "running" when it takes long).</summary>
    private async Task RunAsync(HttpListenerContext context, string deviceName)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        var id = Str(body, "id");
        var command = _host.Invoke(() => _host.Engine.Config.FindCommand(id));
        if (command is null || !WebCatalog.Available(command))
        {
            await WriteJsonAsync(context.Response, new { status = "error", error = command is null ? "notFound" : "unavailable" }, 404).ConfigureAwait(false);
            return;
        }

        if (_host.Engine.IsPaused)
        {
            await WriteJsonAsync(context.Response, new { status = "error", messages = new[] { Loc.T("pause.refused") } }).ConfigureAwait(false);
            return;
        }

        var feedback = new ExecutionFeedback();
        Task run;
        using (feedback.Begin())
        using (ExecutionHistory.Describe(deviceName))
            run = _host.Engine.RunAsync(command, TriggerSource.Web);
        var finished = await Task.WhenAny(run, Task.Delay(RunWait)).ConfigureAwait(false) == run;
        var status = !finished ? "running" : feedback.HasError ? "error" : feedback.HasWarning ? "warning" : "ok";
        await WriteJsonAsync(context.Response, new { status, messages = feedback.Messages }).ConfigureAwait(false);
    }

    /// <summary>
    /// A voice order said into the phone: the body is the recording as 16-bit little-endian PCM, 16 kHz, mono.
    /// The PC transcribes it and runs it like its own orders, and answers with what it heard and did. With
    /// <c>?assistant=id</c> it goes to that assistant.
    /// </summary>
    private async Task VoiceAsync(HttpListenerContext context, string deviceId)
    {
        var max = (int)(VoiceService.RemoteMax.TotalSeconds * MicCapture.SampleRate * 2);
        var bytes = new byte[max];
        var length = 0;
        var input = context.Request.InputStream;
        int read;
        while (length < max && (read = await input.ReadAsync(bytes.AsMemory(length, max - length)).ConfigureAwait(false)) > 0) length += read;
        if (length == max && input.ReadByte() >= 0)
        {
            await WriteJsonAsync(context.Response, new { status = "error", error = "tooLong" }, 413).ConfigureAwait(false);
            return;
        }
        var samples = new float[length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;

        var result = await VoiceFor(context.Request.QueryString["assistant"], samples, deviceId, context.Request.QueryString["speak"]).ConfigureAwait(false);
        var status = result.Kind switch { OsdKind.Success => "ok", OsdKind.Warning => "warning", _ => "info" };
        await WriteJsonAsync(context.Response, new { status, text = result.Text, message = result.Message }).ConfigureAwait(false);
    }

    private async Task IconAsync(string file, HttpListenerResponse response)
    {
        var hash = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        if (_catalog.IconPng(hash) is not { } png)
        {
            await WriteJsonAsync(response, new { error = "notFound" }, 404).ConfigureAwait(false);
            return;
        }
        // The name is the hash of the content: it never changes.
        response.ContentType = "image/png";
        response.Headers["Cache-Control"] = "private, max-age=31536000, immutable";
        response.ContentLength64 = png.Length;
        await response.OutputStream.WriteAsync(png).ConfigureAwait(false);
    }

    /// <summary>The Android app this K-Desktop carries, for an older one to update itself.</summary>
    private static async Task AndroidAsync(HttpListenerResponse response)
    {
        if (AndroidApp.Current is not { } apk)
        {
            await WriteJsonAsync(response, new { error = "notFound" }, 404).ConfigureAwait(false);
            return;
        }
        response.ContentType = "application/vnd.android.package-archive";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Content-Disposition"] = $"attachment; filename=\"KDesktop-{apk.Version}.apk\"";
        response.ContentLength64 = apk.Bytes.Length;
        await response.OutputStream.WriteAsync(apk.Bytes).ConfigureAwait(false);
    }

    private static readonly string[] WebTextPrefixes = ["pwa.", "agenda.", "calendar.", "common."];

    /// <summary>The web app's texts ("pwa.*") in the requested language, or in the language of the PC.</summary>
    private static object Texts(string? lang)
    {
        var available = Loc.Available();
        var code = available.FirstOrDefault(l => l.Code.Equals(lang, StringComparison.OrdinalIgnoreCase))?.Code ?? Loc.Instance.Language;
        return new
        {
            lang = code,
            languages = available.Select(l => new { l.Code, l.Name }),
            // The agenda (calendar, phase 9) shares the texts of the PC's.
            strings = WebTextPrefixes.SelectMany(prefix => Loc.Strings(code, prefix)).ToDictionary(p => p.Key, p => p.Value),
        };
    }

    /// <summary>
    /// A browser on this same PC that needs no pairing: only when the setting allows it (by default a local program
    /// has to pair like a phone).
    /// </summary>
    private bool Trusted(WebRequest request) => request.IsLocal && !_host.Engine.Config.Settings.WebLocalPairing;

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

    private static string? Str(JsonElement? body, string name) =>
        body?.TryGetProperty(name, out var v) == true && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

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
