using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Input;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// Touchpad and keyboard of the web app: a WebSocket (api/input) with small JSON messages.
/// </summary>
/// <remarks>
/// Messages from the phone ("t" is the type):
/// <c>auth {token}</c> (only when the cookie did not come), <c>m {x, y}</c> move by pixels,
/// <c>b {b: "l"|"r"|"m", d: bool}</c> button down/up, <c>c {b, n}</c> click n times, <c>w {x, y}</c> wheel
/// (120 = one notch), <c>t {s}</c> type text, <c>k {k, m: ["CTRL"…]}</c> press a key with modifiers,
/// <c>kd {k}</c> / <c>ku {k}</c> hold / release a key (locked modifiers), <c>p</c> ping (answered with <c>p</c>),
/// <c>z {a: "toggle"|"in"|"out"|"off"|"state"}</c> the zoom («Lupa»). To the phone: <c>z {on, z}</c> whenever the zoom
/// changes (whoever changed it), and <c>p</c>.
/// Whatever the phone holds down is released when it goes away.
/// </remarks>
public sealed partial class WebServer
{
    private const int MaxInputMessage = 8 * 1024;
    private const int MaxInputText = 4000;
    private static readonly TimeSpan InputAuthWait = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<WebSocket, byte> _inputSockets = new();

    /// <summary>One message at a time per socket (the ping answer and the zoom state may cross).</summary>
    private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _sendLocks = new();

    private async Task SendInputAsync(WebSocket socket, byte[] message, CancellationToken token)
    {
        var gate = _sendLocks.GetOrAdd(socket, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (socket.State == WebSocketState.Open) await socket.SendAsync(message, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private byte[] ZoomState()
    {
        var magnifier = _host.Engine.Magnifier;
        return JsonSerializer.SerializeToUtf8Bytes(new { t = "z", on = magnifier.IsOn, z = Math.Round(magnifier.IsOn ? magnifier.Zoom : _host.Engine.Config.Settings.ZoomLevel, 2) });
    }

    /// <summary>Every open touchpad shows the zoom as it is now.</summary>
    private void BroadcastZoom()
    {
        var state = ZoomState();
        foreach (var socket in _inputSockets.Keys)
            _ = Task.Run(async () =>
            {
                try { await SendInputAsync(socket, state, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException) { }
            });
    }

    /// <summary>«z» from the phone: the zoom around the mouse.</summary>
    private async Task ZoomAsync(WebSocket socket, string? action, CancellationToken token)
    {
        var magnifier = _host.Engine.Magnifier;
        var level = _host.Engine.Config.Settings.ZoomLevel;
        switch (action)
        {
            case "toggle": await Task.Run(() => magnifier.Toggle(level), token).ConfigureAwait(false); break;
            case "in": await Task.Run(() => magnifier.Step(+1), token).ConfigureAwait(false); break;
            case "out": await Task.Run(() => magnifier.Step(-1), token).ConfigureAwait(false); break;
            case "off": await Task.Run(magnifier.Off, token).ConfigureAwait(false); break;
            default: await SendInputAsync(socket, ZoomState(), token).ConfigureAwait(false); break; // "state"
        }
    }

    /// <summary>Touchpad and keyboard allowed (setting). Turning it off closes the open ones.</summary>
    public bool InputAllowed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            if (!value) CloseInputs();
            Broadcast("session");
        }
    } = true;

    private void CloseInputs()
    {
        foreach (var socket in _inputSockets.Keys) socket.Abort();
        _inputSockets.Clear();
    }

    /// <summary>GET api/input (WebSocket upgrade).</summary>
    private async Task InputAsync(WebRequest request, HttpListenerContext context)
    {
        var response = context.Response;
        if (!context.Request.IsWebSocketRequest)
        {
            await WriteJsonAsync(response, new { error = "badRequest" }, 400).ConfigureAwait(false);
            return;
        }
        // Cookies go with WebSockets from any page, even another port of the same server:
        // only the app's own pages may open one.
        if (!SameOrigin(context.Request.Headers["Origin"], request.Host))
        {
            Log.Warn($"Web: conexión de entrada rechazada desde {context.Request.Headers["Origin"]}");
            await WriteJsonAsync(response, new { error = "forbidden" }, 403).ConfigureAwait(false);
            return;
        }
        if (!InputAllowed)
        {
            await WriteJsonAsync(response, new { error = "disabled" }, 403).ConfigureAwait(false);
            return;
        }

        var ip = request.ClientIp.ToString();
        var device = request.IsLocal ? WebDevices.Local : Devices.Authenticate(TokenOf(context.Request), ip);

        HttpListenerWebSocketContext ws;
        try { ws = await context.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(20)).ConfigureAwait(false); }
        catch (Exception ex)
        {
            Log.Warn("Web: no se pudo abrir el WebSocket: " + ex.Message);
            return;
        }

        var socket = ws.WebSocket;
        var session = new InputSession();
        var token = _cts?.Token ?? CancellationToken.None;
        _inputSockets[socket] = 0;
        try
        {
            var buffer = new byte[MaxInputMessage];
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                // Without the cookie, the first message has to be the token, and soon.
                using var wait = device is null ? new CancellationTokenSource(InputAuthWait) : null;
                using var linked = wait is null ? null : CancellationTokenSource.CreateLinkedTokenSource(token, wait.Token);

                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, linked?.Token ?? token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxInputMessage) return;
                } while (!result.EndOfMessage);
                if (result.MessageType != WebSocketMessageType.Text) continue;

                JsonElement msg;
                try
                {
                    using var doc = JsonDocument.Parse(message.ToArray());
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                    msg = doc.RootElement.Clone();
                }
                catch (JsonException) { continue; }

                var type = Str(msg, "t");
                if (device is null)
                {
                    device = type == "auth" ? Devices.Authenticate(Str(msg, "token"), ip) : null;
                    if (device is null)
                    {
                        await socket.CloseAsync((WebSocketCloseStatus)4401, "unpaired", CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                    Log.Info($"Web: touchpad conectado ({device.Name})");
                    continue;
                }
                if (type == "p")
                {
                    await SendInputAsync(socket, "{\"t\":\"p\"}"u8.ToArray(), token).ConfigureAwait(false);
                    continue;
                }
                if (type == "z")
                {
                    await ZoomAsync(socket, Str(msg, "a"), token).ConfigureAwait(false);
                    continue;
                }
                try { session.Handle(type, msg); }
                catch (Exception ex) { Log.Warn($"Web: error en la entrada remota ({type}): {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or HttpListenerException)
        {
            // Phone gone, locked or out of range.
        }
        finally
        {
            _inputSockets.TryRemove(socket, out _);
            if (_sendLocks.TryRemove(socket, out var gate)) gate.Dispose();
            session.ReleaseAll();
            socket.Dispose();
        }
    }

    private static bool SameOrigin(string? origin, string? host)
    {
        // Not a browser (no Origin): nothing a web page could abuse.
        if (string.IsNullOrEmpty(origin)) return true;
        if (string.IsNullOrEmpty(host) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One open touchpad: runs its messages and remembers what it holds down.</summary>
    private sealed class InputSession
    {
        private readonly HashSet<MouseButton> _buttons = [];
        private readonly HashSet<int> _keys = [];

        public void Handle(string? type, JsonElement msg)
        {
            switch (type)
            {
                case "m":
                    var dx = Math.Clamp(Int(msg, "x"), -4000, 4000);
                    var dy = Math.Clamp(Int(msg, "y"), -4000, 4000);
                    if (dx != 0 || dy != 0) InputSimulator.MovePointer(dx, dy);
                    break;
                case "b":
                    if (Button(Str(msg, "b")) is not { } held) break;
                    var down = msg.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.True;
                    if (down && _buttons.Add(held)) InputSimulator.MouseDown(held);
                    else if (!down && _buttons.Remove(held)) InputSimulator.MouseUp(held);
                    break;
                case "c":
                    if (Button(Str(msg, "b")) is not { } button) break;
                    for (var i = Math.Clamp(Int(msg, "n", 1), 1, 3); i > 0; i--) InputSimulator.MouseClick(button);
                    break;
                case "w":
                    var wy = Math.Clamp(Int(msg, "y"), -2400, 2400);
                    var wx = Math.Clamp(Int(msg, "x"), -2400, 2400);
                    if (wy != 0) InputSimulator.Wheel(wy);
                    if (wx != 0) InputSimulator.Wheel(wx, horizontal: true);
                    break;
                case "t":
                    if (Str(msg, "s") is { Length: > 0 and <= MaxInputText } text) InputSimulator.SendText(text, releaseModifiers: _keys.Count == 0);
                    break;
                case "k":
                    if (Str(msg, "k") is not { Length: > 0 } key) break;
                    var mods = Modifiers.None;
                    if (msg.TryGetProperty("m", out var m) && m.ValueKind == JsonValueKind.Array)
                        foreach (var e in m.EnumerateArray())
                            mods |= e.GetString()?.ToUpperInvariant() switch
                            {
                                "CTRL" => Modifiers.Ctrl, "ALT" => Modifiers.Alt, "SHIFT" => Modifiers.Shift, "WIN" => Modifiers.Win,
                                _ => Modifiers.None,
                            };
                    InputSimulator.SendCombo(Hotkey.Format(mods, KeyNames.Canonical(key)), releaseModifiers: _keys.Count == 0);
                    break;
                case "kd" or "ku":
                    if (KeyVk(Str(msg, "k")) is not (> 0 and var vk)) break;
                    if (type == "kd" && _keys.Add(vk)) InputSimulator.KeyDown(vk);
                    else if (type == "ku" && _keys.Remove(vk)) InputSimulator.KeyUp(vk);
                    break;
            }
        }

        public void ReleaseAll()
        {
            foreach (var vk in _keys) InputSimulator.KeyUp(vk);
            foreach (var b in _buttons) InputSimulator.MouseUp(b);
            _keys.Clear();
            _buttons.Clear();
        }

        private static int KeyVk(string? name) => name?.ToUpperInvariant() switch
        {
            null or "" => 0,
            "CTRL" => KeyNames.VK_LCONTROL,
            "ALT" => KeyNames.VK_LMENU,
            "SHIFT" => KeyNames.VK_LSHIFT,
            "WIN" => KeyNames.VK_LWIN,
            _ => KeyNames.ToVk(KeyNames.Canonical(name)),
        };

        private static MouseButton? Button(string? name) => name switch
        {
            "l" => MouseButton.Left,
            "r" => MouseButton.Right,
            "m" => MouseButton.Middle,
            _ => null,
        };

        private static int Int(JsonElement msg, string name, int fallback = 0) =>
            msg.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n)
                ? (int)Math.Round(Math.Clamp(n, int.MinValue / 2.0, int.MaxValue / 2.0))
                : fallback;
    }
}
