using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Remote.Web;

namespace KDesktop.Core.Remote;

/// <summary>How the UDP server is set up (built from the settings).</summary>
/// <param name="Key">Shared key (null: none). With one, other devices put it in front of each order or sign with it.</param>
public sealed record UdpOptions(
    int Port,
    RemoteScope Scope = RemoteScope.LocalNetwork,
    string? Key = null,
    bool AllowKeys = false,
    bool AllowHidden = false,
    bool AllowEvents = true,
    bool AllowAssistants = false);

/// <summary>
/// UDP remote control (default port 2501). One order per datagram, in two forms (protocol in the documentation, «Control
/// remoto»):
/// <list type="bullet">
/// <item>Text, for people and simple senders (netcat, Tasker…): <c>[key ]order</c>, the reply starts with <c>OK</c> or
/// <c>ERROR</c>. Orders: <c>list</c>, <c>help</c>, <c>assistants</c>, a remote command's name or number, <c>run</c>,
/// <c>event</c>, <c>ask</c>, <c>keys</c>, <c>type</c>, <c>media</c>, <c>volume</c>, <c>show</c>, also in K-Desktop's
/// language («lista», «teclas»…); a remote command called exactly like the whole text wins.</item>
/// <item>JSON, for programs: <c>{"cmd":"run","arg":"…"}</c>, signed as <c>&lt;hex HMAC-SHA256 of the JSON&gt;.&lt;JSON&gt;</c>
/// with <c>"t"</c> (Unix seconds) and a one-time <c>"id"</c> when there is a key. The reply is JSON.</item>
/// </list>
/// From this PC the key is never needed; from the internet only signed orders get in. What is not allowed gets no reply
/// (no answers to strangers, and nothing to bounce off this PC), and an address that fails too often is ignored for a while.
/// </summary>
public sealed partial class UdpServer : IDisposable
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BlockTime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);

    private readonly CommandEngine _engine;
    private readonly IUiServices _ui;
    private readonly Lock _gate = new();
    private readonly Dictionary<IPAddress, Failures> _failures = [];
    private readonly Dictionary<string, DateTime> _usedIds = new(StringComparer.Ordinal);
    private UdpClient? _client;
    private CancellationTokenSource? _cts;

    public UdpServer(CommandEngine engine, IUiServices ui)
    {
        _engine = engine;
        _ui = ui;
    }

    public UdpOptions Options { get; private set; } = new(2501);
    public bool IsRunning => _client is not null;

    /// <summary>The port could not be opened (another program has it).</summary>
    public bool Failed { get; private set; }

    /// <summary>Started, stopped or failed.</summary>
    public event Action? StateChanged;

    /// <summary>The active assistants, for <c>ask</c> and <c>assistants</c> (null: the module is not there).</summary>
    public IWebAssistants? Assistants { get; set; }

    /// <summary>Runs on the UI thread (the assistants live there).</summary>
    public Func<Func<object?>, object?> OnUi { get; set; } = f => f();

    /// <summary>Internet without a key is not allowed: it stays on the local network until there is one.</summary>
    private RemoteScope Scope => Options.Scope == RemoteScope.Internet && Options.Key is null ? RemoteScope.LocalNetwork : Options.Scope;

    public void Start(UdpOptions options)
    {
        var samePort = IsRunning && options.Port == Options.Port;
        Configure(options);
        if (samePort) return;
        Stop();
        try
        {
            _client = Open(options.Port);
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenAsync(_client, _cts.Token));
            Failed = false;
            Log.Info($"Servidor UDP escuchando en el puerto {options.Port}");
        }
        catch (SocketException ex)
        {
            Log.Error($"No se pudo abrir el puerto UDP {options.Port}", ex);
            _client = null;
            Failed = true;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Takes the options without touching the socket (tests use it alone).</summary>
    internal void Configure(UdpOptions options) => Options = options with { Key = string.IsNullOrEmpty(options.Key) ? null : options.Key };

    /// <summary>IPv6 and IPv4 on the same socket; IPv4 only where IPv6 is off.</summary>
    private static UdpClient Open(int port)
    {
        if (Socket.OSSupportsIPv6)
        {
            var dual = new UdpClient(AddressFamily.InterNetworkV6);
            try
            {
                dual.Client.DualMode = true;
                dual.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                return dual;
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.AddressAlreadyInUse)
            {
                dual.Dispose();
            }
            catch
            {
                dual.Dispose();
                throw;
            }
        }
        var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        var was = IsRunning || Failed;
        _cts?.Cancel();
        _client?.Dispose();
        _client = null;
        _cts = null;
        Failed = false;
        if (was) StateChanged?.Invoke();
    }

    private async Task ListenAsync(UdpClient client, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try
            {
                packet = await client.ReceiveAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException ex)
            {
                // ICMP "port unreachable" from a previous reply shows up here; keep listening.
                Log.Warn($"UDP: {ex.SocketErrorCode}");
                continue;
            }

            var remote = packet.RemoteEndPoint;
            var text = Encoding.UTF8.GetString(packet.Buffer);
            _ = Task.Run(async () =>
            {
                try
                {
                    var reply = Receive(text, remote.Address);
                    if (reply is not null) await SendAsync(client, reply, remote).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error($"UDP: error procesando una orden de {remote.Address}", ex);
                }
            }, token);
        }
    }

    private static async Task SendAsync(UdpClient client, string message, IPEndPoint to)
    {
        var data = Encoding.UTF8.GetBytes(message);
        try { await client.SendAsync(data, to).ConfigureAwait(false); }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { Log.Warn($"UDP: no se pudo responder a {to}: {ex.Message}"); }
    }

    // ── who gets in ──────────────────────────────────────────────────────

    /// <summary>Checks where a datagram came from and its key or signature, runs it and returns the reply (null: none).</summary>
    internal string? Receive(string message, IPAddress from)
    {
        from = RemoteAddresses.Normalize(from);
        if (!RemoteAddresses.Allows(Scope, from) || IsBlocked(from)) return null;
        message = message.Trim().TrimEnd('\0');
        if (message.Length == 0) return null;

        var thisPc = IPAddress.IsLoopback(from);
        var local = RemoteAddresses.IsLocal(from);
        var key = Options.Key;

        Request? request;
        bool json;
        if (SignedMessage().Match(message) is { Success: true } signed)
        {
            json = true;
            request = key is null ? null : Verified(signed.Groups[1].Value, signed.Groups[2].Value, key);
        }
        else if (message.StartsWith('{'))
        {
            json = true;
            request = (key is null || thisPc) && local ? ParseJson(message) : null;
        }
        else
        {
            json = false;
            message = message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (key is not null && !thisPc)
                message = local && StartsWithKey(message, key) ? message[key.Length..].Trim() : string.Empty;
            request = message.Length == 0 ? null : IsCommandName(message) ? new Request("run", message) : ParseText(message);
        }

        if (request is null)
        {
            Fail(from);
            return null;
        }

        Reply reply;
        using (ExecutionHistory.Describe(from.ToString())) reply = Handle(request);
        return json ? reply.ToJson() : reply.ToText();
    }

    /// <summary>The key in front, then a space; compared in constant time.</summary>
    private static bool StartsWithKey(string message, string key) =>
        message.Length > key.Length && message[key.Length] == ' '
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(message[..key.Length]), Encoding.UTF8.GetBytes(key));

    /// <summary>A signed order: the signature matches, it is recent and its id was not used before.</summary>
    private Request? Verified(string signature, string payload, string key)
    {
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature))) return null;
        if (ParseJson(payload) is not { } request) return null;
        if (request.Time is not { } t || request.Id is not { Length: > 0 and <= 100 } id) return null;
        var now = DateTime.UtcNow;
        if ((DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime - now).Duration() > ClockSkew) return null;
        lock (_gate)
        {
            foreach (var old in _usedIds.Where(p => p.Value < now).Select(p => p.Key).ToList()) _usedIds.Remove(old);
            if (!_usedIds.TryAdd(id, now + ClockSkew + ClockSkew)) return null;
        }
        return request;
    }

    private bool IsBlocked(IPAddress from)
    {
        lock (_gate) return _failures.TryGetValue(from, out var f) && f.BlockedUntil > DateTime.UtcNow;
    }

    private void Fail(IPAddress from)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!_failures.TryGetValue(from, out var f) || now - f.Since > FailureWindow) f = new Failures(now, 0, DateTime.MinValue);
            f = f with { Count = f.Count + 1 };
            if (f.Count >= MaxFailures)
            {
                f = f with { BlockedUntil = now + BlockTime, Count = 0, Since = now };
                Log.Warn($"UDP: {from} ignorado durante {BlockTime.TotalMinutes:0} min por órdenes sin clave o mal firmadas");
            }
            _failures[from] = f;
            if (_failures.Count > 1000)
                foreach (var old in _failures.Where(p => p.Value.BlockedUntil < now && now - p.Value.Since > FailureWindow).Select(p => p.Key).ToList())
                    _failures.Remove(old);
        }
    }

    private sealed record Failures(DateTime Since, int Count, DateTime BlockedUntil);

    [GeneratedRegex(@"^([0-9a-fA-F]{64})\.(\{.*\})$", RegexOptions.Singleline)]
    private static partial Regex SignedMessage();

    // ── orders ───────────────────────────────────────────────────────────

    /// <summary>An order: what (<c>run</c>, <c>list</c>…), its argument and, for <c>ask</c>, the assistant.</summary>
    internal sealed record Request(string Cmd, string Arg = "", string Assistant = "", long? Time = null, string? Id = null);

    private static readonly string[] Words = ["list", "help", "assistants", "run", "events", "event", "ask", "keys", "type", "media", "volume", "show"];
    private static readonly string[] MediaWords = ["play", "pause", "next", "previous", "stop"];
    private static readonly string[] VolumeWords = ["up", "down", "mute"];

    /// <summary>
    /// The English word for <paramref name="word"/>: itself, or its translation in K-Desktop's language (<c>udp.word.*</c>:
    /// «lista», «teclas»…). Capitals and accents don't matter. Null when it is none of them.
    /// </summary>
    private static string? Canonical(string word, string[] words) =>
        words.FirstOrDefault(w => SameName(word, w) || SameName(word, Loc.T("udp.word." + w)));

    /// <summary>A remote command called exactly that: it wins over the words («Mostrar escritorio» is not «show»).</summary>
    private bool IsCommandName(string text) => RemoteCommands().Any(c => SameName(c.Name, text));

    /// <summary>"run Abrir Chrome", "ask Lander qué hora es", "lista", "21", "Abrir Chrome"…</summary>
    internal static Request ParseText(string text)
    {
        var space = text.IndexOf(' ');
        var rest = space < 0 ? string.Empty : text[(space + 1)..].Trim();
        if (Canonical(space < 0 ? text : text[..space], Words) is not { } word) return new Request("run", text);
        if (word != "ask") return new Request(word, rest);
        var gap = rest.IndexOf(' ');
        return gap < 0 ? new Request("ask", string.Empty, rest) : new Request("ask", rest[(gap + 1)..].Trim(), rest[..gap]);
    }

    /// <summary><c>{"cmd":"run","arg":"Abrir Chrome"}</c>; null when it is not a JSON object with a known <c>cmd</c>.</summary>
    internal static Request? ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;
            var cmd = Str("cmd").ToLowerInvariant();
            if (!Words.Contains(cmd)) return null;
            long? time = root.TryGetProperty("t", out var t) && t.TryGetInt64(out var seconds) ? seconds : null;
            return new Request(cmd, Str("arg"), Str("assistant"), time, Str("id"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Runs an order that already got in.</summary>
    internal Reply Handle(Request request)
    {
        var o = Options;
        switch (request.Cmd)
        {
            case "list":
                return Reply.List(RemoteCommands().Select(c => c.Name).ToList());
            case "help":
                return Reply.Done(Loc.T("udp.help"));
            case "show":
                _ui.ShowMainWindow();
                return Reply.Done(Loc.T("udp.done"));
            case "run":
                return Run(request.Arg);
            case "events":
                if (!o.AllowEvents) return Reply.Error("off", Loc.T("udp.off"));
                return Reply.EventNames(RemoteCalls());
            case "event":
                if (!o.AllowEvents) return Reply.Error("off", Loc.T("udp.off"));
                if (request.Arg.Length == 0) return Reply.Error("badRequest", Loc.T("udp.missing", "event"));
                return (_engine.RemoteCall?.Invoke(request.Arg, "UDP") ?? 0) > 0
                    ? Reply.Done(Loc.T("udp.eventFired", request.Arg))
                    : Reply.Error("noEvent", Loc.T("udp.noEvent", request.Arg));
            case "ask":
                return Ask(request.Assistant, request.Arg);
            case "assistants":
                if (Assistants is not { } a || !(bool)OnUi(() => a.Enabled)!) return Reply.Error("assistantsOff", Loc.T("udp.assistantsOff"));
                return Reply.Assistants((IReadOnlyList<WebAssistantInfo>)OnUi(a.List)!);
            case "keys":
            case "type":
                if (!o.AllowKeys) return Reply.Error("off", Loc.T("udp.off"));
                if (request.Arg.Length == 0) return Reply.Error("badRequest", Loc.T("udp.missing", request.Cmd));
                if (DisabledHere()) return Reply.Error("disabledHere", Loc.T("udp.disabledHere"));
                if (request.Cmd == "type") InputSimulator.SendText(request.Arg);
                else if (!InputSimulator.SendCombo(request.Arg)) return Reply.Error("badRequest", Loc.T("udp.badKeys", request.Arg));
                return Reply.Done(Loc.T("udp.done"));
            case "media":
            case "volume":
                var arg = request.Arg.Trim().ToLowerInvariant();
                arg = Canonical(arg, request.Cmd == "media" ? MediaWords : VolumeWords) ?? arg;
                if (Key(request.Cmd, arg) is not { } vk)
                    return Reply.Error("badRequest", Loc.T("udp.badArg", request.Cmd, request.Arg));
                if (DisabledHere()) return Reply.Error("disabledHere", Loc.T("udp.disabledHere"));
                InputSimulator.KeyPress(vk);
                return Reply.Done(Loc.T("udp.done"));
            default:
                return Reply.Error("badRequest", Loc.T("udp.notFound", request.Cmd));
        }
    }

    private static int? Key(string cmd, string arg) => (cmd, arg) switch
    {
        ("media", "play" or "pause" or "playpause") => 0xB3,
        ("media", "next") => 0xB0,
        ("media", "previous" or "prev") => 0xB1,
        ("media", "stop") => 0xB2,
        ("volume", "up" or "+") => 0xAF,
        ("volume", "down" or "-") => 0xAE,
        ("volume", "mute") => 0xAD,
        _ => null,
    };

    /// <summary>K-Desktop is disabled in the window in front («Desactivar en»): no keys go to it.</summary>
    private bool DisabledHere() => _engine.Config.Settings.DisabledIn.Contains(ForegroundWindow.Capture().ProcessName);

    /// <summary>The calls the enabled «Llamada desde fuera» events wait for (their own name when they don't set one).</summary>
    private List<string> RemoteCalls() =>
        _engine.Config.Events.Where(e => e.Enabled && e.Trigger is RemoteCallTrigger)
            .Select(e => ((RemoteCallTrigger)e.Trigger).Name.Trim() is { Length: > 0 } call ? call : e.Name.Trim())
            .Where(n => n.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Remote commands in a stable order (by name); their number is their place, from 1.</summary>
    private List<Command> RemoteCommands() =>
        _engine.Config.Commands.Where(c => c.Remote && c.Enabled && (Options.AllowHidden || !c.Hidden))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>By number or by name (capitals and accents don't matter).</summary>
    private Reply Run(string target)
    {
        if (target.Length == 0) return Reply.Error("badRequest", Loc.T("udp.missing", "run"));
        var list = RemoteCommands();
        Command? command = null;
        if (int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            command = number >= 1 && number <= list.Count ? list[number - 1] : null;
        command ??= list.FirstOrDefault(c => SameName(c.Name, target));
        if (command is null)
        {
            // Disabled or hidden ones say so; the rest don't exist (for this server).
            var other = _engine.Config.Commands.FirstOrDefault(c => c.Remote && SameName(c.Name, target) && (Options.AllowHidden || !c.Hidden));
            return other is not null ? Reply.Error("disabled", Loc.T("udp.disabled", other.Name)) : Reply.Error("notFound", Loc.T("udp.notFound", target));
        }
        _ = _engine.RunAsync(command, TriggerSource.Remote);
        return Reply.Done(Loc.T("udp.ran", command.Name));
    }

    private const string Sent = "sent:";

    private Reply Ask(string assistant, string text)
    {
        if (!Options.AllowAssistants) return Reply.Error("off", Loc.T("udp.off"));
        if (assistant.Length == 0 || text.Length == 0) return Reply.Error("badRequest", Loc.T("udp.missing", "ask"));
        if (text.Length > 2000) return Reply.Error("badRequest", Loc.T("udp.tooLong"));
        if (Assistants is not { } assistants) return Reply.Error("assistantsOff", Loc.T("udp.assistantsOff"));
        var result = (string?)OnUi(() =>
        {
            if (!assistants.Enabled) return "assistantsOff";
            var who = assistants.List().FirstOrDefault(a => SameName(a.Id, assistant) || SameName(a.Name, assistant));
            if (who is null) return "noAssistant";
            return assistants.Send(who.Id, text) is { } error ? error : Sent + who.Name;
        });
        if (result?.StartsWith(Sent, StringComparison.Ordinal) == true) return Reply.Done(Loc.T("udp.asked", result[Sent.Length..]));
        return result switch
        {
            "assistantsOff" => Reply.Error("assistantsOff", Loc.T("udp.assistantsOff")),
            "notRunning" => Reply.Error("notRunning", Loc.T("udp.assistantStarting")),
            _ => Reply.Error("noAssistant", Loc.T("udp.noAssistant", assistant)),
        };
    }

    private static bool SameName(string a, string b) =>
        CultureInfo.InvariantCulture.CompareInfo.Compare(a.Trim(), b.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    /// <summary>What goes back: as text (<c>OK …</c> / <c>ERROR …</c>) or as JSON, the way the order came.</summary>
    internal sealed record Reply(bool Ok, string Message, string? Code = null, IReadOnlyList<string>? Commands = null,
        IReadOnlyList<WebAssistantInfo>? Active = null, IReadOnlyList<string>? Events = null)
    {
        public static Reply Done(string message) => new(true, message);
        public static Reply Error(string code, string message) => new(false, message, code);
        public static Reply List(IReadOnlyList<string> commands) => new(true, string.Empty, Commands: commands);
        public static Reply Assistants(IReadOnlyList<WebAssistantInfo> active) => new(true, string.Empty, Active: active);
        public static Reply EventNames(IReadOnlyList<string> events) => new(true, string.Empty, Events: events);

        public string ToText()
        {
            var sb = new StringBuilder(Ok ? "OK" : "ERROR");
            if (Message.Length > 0) sb.Append(' ').Append(Message);
            if (Commands is not null)
                for (var i = 0; i < Commands.Count; i++) sb.Append(CultureInfo.InvariantCulture, $"\n{i + 1}. {Commands[i]}");
            if (Events is not null)
                foreach (var e in Events) sb.Append('\n').Append(e);
            if (Active is not null)
                foreach (var a in Active) sb.Append(CultureInfo.InvariantCulture, $"\n{a.Name} ({a.Id})");
            return sb.ToString();
        }

        public string ToJson()
        {
            var node = new System.Text.Json.Nodes.JsonObject { ["ok"] = Ok };
            if (Code is not null) node["error"] = Code;
            if (Message.Length > 0) node["message"] = Message;
            if (Commands is not null) node["commands"] = new System.Text.Json.Nodes.JsonArray([.. Commands.Select(c => (System.Text.Json.Nodes.JsonNode?)c)]);
            if (Events is not null) node["events"] = new System.Text.Json.Nodes.JsonArray([.. Events.Select(e => (System.Text.Json.Nodes.JsonNode?)e)]);
            if (Active is not null)
                node["assistants"] = new System.Text.Json.Nodes.JsonArray([.. Active.Select(a => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject { ["id"] = a.Id, ["name"] = a.Name })]);
            return node.ToJsonString();
        }
    }

    public void Dispose() => Stop();
}
