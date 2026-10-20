using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KDesktop.Core.Assistant.Pipe;

/// <summary>
/// A message of the pipe between K-Desktop and the assistant process (docs/assistant/protocolo.md). On the wire:
/// one compact JSON object per line, its type in <c>t</c>, camelCase fields.
/// </summary>
public abstract record PipeMessage
{
    /// <summary>The <c>t</c> of the message.</summary>
    [JsonIgnore]
    public abstract string Type { get; }
}

/// <summary>A message this side does not know (a newer protocol): ignored, kept only for the log.</summary>
public sealed record UnknownMessage(string UnknownType, JsonObject Body) : PipeMessage
{
    public override string Type => UnknownType;
}

// ── connection ──────────────────────────────────────────────────────────

public sealed record HelloMessage(int Protocol, string Sdk, int Pid) : PipeMessage
{
    public override string Type => "hello";
}

public sealed record WelcomeMessage(string Lang, string Theme, bool Paused, JsonObject? Config = null) : PipeMessage
{
    public override string Type => "welcome";
}

/// <param name="Reason"><c>protocol</c>, <c>client</c> or <c>busy</c>.</param>
public sealed record RejectMessage(string Reason) : PipeMessage
{
    public override string Type => "reject";
}

public sealed record PingMessage : PipeMessage
{
    public override string Type => "ping";
}

public sealed record PongMessage : PipeMessage
{
    public override string Type => "pong";
}

/// <param name="Reason"><c>disable</c>, <c>exit</c> or <c>restart</c>.</param>
public sealed record ByeMessage(string Reason) : PipeMessage
{
    public override string Type => "bye";
}

public sealed record ErrorMessage(string Code, string Message, string? Id = null) : PipeMessage
{
    public override string Type => "error";
}

// ── configuration and state of K-Desktop ────────────────────────────────

/// <param name="Assistant">Whose settings, for the section <c>assistantSettings</c> (the others are the module's).</param>
public sealed record ConfigMessage(string Section, JsonNode? Value = null, string? Assistant = null) : PipeMessage
{
    public override string Type => "config";
}

public sealed record PauseMessage(bool Paused) : PipeMessage
{
    public override string Type => "pause";
}

/// <summary>Puts an assistant on the desktop, next to those already there; one that is there already (another version) is replaced.</summary>
/// <param name="Entrance"><c>startup</c> or <c>switched</c>.</param>
public sealed record LoadMessage(string Assistant, string Version, string Path, string Data, string Entrance) : PipeMessage
{
    public override string Type => "load";
}

/// <summary>Takes one assistant off the desktop (with its exit); the others stay.</summary>
/// <param name="Reason"><c>disabled</c>, <c>uninstalling</c>, <c>hiding</c> or <c>reloading</c> (it comes back at once).</param>
public sealed record UnloadMessage(string Assistant, string Reason) : PipeMessage
{
    public override string Type => "unload";
}

public sealed record LoadedMessage(string Assistant, bool Ok, string? Error = null) : PipeMessage
{
    public override string Type => "loaded";
}

/// <summary>«Assistant: action», or a button of its settings or its panel: run one of its extras.</summary>
public sealed record ExtraMessage(string Assistant, string Action) : PipeMessage
{
    public override string Type => "extra";
}

/// <summary>
/// The hotkeys of the commands that run an assistant's extras, as K-Desktop shows them (extra id → «CTRL + ALT + L»;
/// extras without one are left out). Sent after its <c>load</c> and whenever the commands change.
/// </summary>
public sealed record ExtraKeysMessage(string Assistant, IReadOnlyDictionary<string, string> Keys) : PipeMessage
{
    public override string Type => "extraKeys";
}

// ── keyboard and wheel (K-Desktop's hook, which also works over elevated windows) ──

/// <summary>
/// K-Desktop swallows these keys (WPF <c>Key</c> names: «Up», «A», «Escape»…) while pressed without Ctrl, Alt or Win,
/// and sends their changes to that assistant (the Lander's game). Empty: let them go.
/// </summary>
public sealed record KeysMessage(string Assistant, IReadOnlyList<string> Capture) : PipeMessage
{
    public override string Type => "keys";
}

/// <summary>A captured key went down or up.</summary>
public sealed record KeyMessage(string Assistant, string Key, bool Down) : PipeMessage
{
    public override string Type => "key";
}

/// <summary>Whether the process wants every turn of the mouse wheel (<see cref="WheelMessage"/>): only while an assistant asks.</summary>
public sealed record WatchWheelMessage(bool Watch) : PipeMessage
{
    public override string Type => "watchWheel";
}

/// <summary>
/// The wheel turned: where (screen pixels), how much (120 per notch; positive is up or to the right, as Windows says)
/// and over which top-level window.
/// </summary>
public sealed record WheelMessage(int X, int Y, int Delta, bool Horizontal, long Hwnd) : PipeMessage
{
    public override string Type => "wheel";
}

/// <summary>The user scrolled hard (always sent: it is rare). <paramref name="Strength"/> 0…1, positive downwards.</summary>
public sealed record FastScrollMessage(double Strength, int X, int Y) : PipeMessage
{
    public override string Type => "fastScroll";
}

// ── from the assistant to K-Desktop's panel ─────────────────────────────

/// <param name="Assistant">Whose state; null: the process's (the model, the memory).</param>
public sealed record StatusMessage(
    string State,
    string? Model = null,
    bool? ModelLoaded = null,
    bool? Busy = null,
    int? MemoryMb = null,
    string? LastError = null,
    string? Assistant = null) : PipeMessage
{
    public override string Type => "status";
}

/// <param name="Assistant">Who says it (filled in by the host).</param>
public sealed record NotifyMessage(string Title, string Text, string? Assistant = null) : PipeMessage
{
    public override string Type => "notify";
}

/// <param name="Assistant">The assistant the page is about (its panel, its settings), if any.</param>
public sealed record OpenMessage(string Page, string? Assistant = null) : PipeMessage
{
    public override string Type => "open";
}

/// <summary>
/// Data for the assistant's own panel (the binds of its panel.json), merged with what it published before (objects key
/// by key; anything else replaces). <paramref name="Assistant"/> is whose it is, filled in by the host.
/// </summary>
public sealed record PanelMessage(JsonObject Data, string? Assistant = null) : PipeMessage
{
    public override string Type => "panel";
}

/// <summary>
/// Right click on an assistant: K-Desktop shows its menu (panel, settings, its extras, hide) at that point, in screen
/// pixels, like the rest of its menus.
/// </summary>
public sealed record MenuMessage(string Assistant, int X, int Y) : PipeMessage
{
    public override string Type => "menu";
}

/// <summary>
/// An assistant shows, greys out or hides one of its extras in its menu and its panel buttons (all are shown after each
/// <c>load</c>). <paramref name="State"/>: <c>shown</c>, <c>disabled</c> or <c>hidden</c>.
/// </summary>
public sealed record ExtraStateMessage(string Assistant, string Extra, string State) : PipeMessage
{
    public override string Type => "extraState";
}

/// <summary>Right-click menu → «Hide»: K-Desktop takes that assistant off the desktop (the others stay).</summary>
public sealed record HideMessage(string Assistant) : PipeMessage
{
    public override string Type => "hide";
}

/// <summary>Reading and writing messages as lines.</summary>
public static class PipeJson
{
    /// <summary>Longest line accepted (protocolo.md): a longer one closes the connection.</summary>
    public const int MaxLine = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        // A missing field, or null where the message says it cannot be: a bad request, not a half-filled message.
        // (Optional fields are the parameters with a default.)
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipType } },
    };

    private static readonly Dictionary<string, Type> Types = new(StringComparer.Ordinal)
    {
        ["hello"] = typeof(HelloMessage),
        ["welcome"] = typeof(WelcomeMessage),
        ["reject"] = typeof(RejectMessage),
        ["ping"] = typeof(PingMessage),
        ["pong"] = typeof(PongMessage),
        ["bye"] = typeof(ByeMessage),
        ["error"] = typeof(ErrorMessage),
        ["config"] = typeof(ConfigMessage),
        ["pause"] = typeof(PauseMessage),
        ["load"] = typeof(LoadMessage),
        ["unload"] = typeof(UnloadMessage),
        ["loaded"] = typeof(LoadedMessage),
        ["status"] = typeof(StatusMessage),
        ["notify"] = typeof(NotifyMessage),
        ["open"] = typeof(OpenMessage),
        ["panel"] = typeof(PanelMessage),
        ["hide"] = typeof(HideMessage),
        ["extra"] = typeof(ExtraMessage),
        ["extraKeys"] = typeof(ExtraKeysMessage),
        ["extraState"] = typeof(ExtraStateMessage),
        ["menu"] = typeof(MenuMessage),
        ["keys"] = typeof(KeysMessage),
        ["key"] = typeof(KeyMessage),
        ["watchWheel"] = typeof(WatchWheelMessage),
        ["wheel"] = typeof(WheelMessage),
        ["fastScroll"] = typeof(FastScrollMessage),
    };

    /// <summary>[JsonIgnore] on the abstract Type is not inherited by the overrides: drop it here.</summary>
    private static void SkipType(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || !typeof(PipeMessage).IsAssignableFrom(info.Type)) return;
        for (var i = info.Properties.Count - 1; i >= 0; i--)
            if (info.Properties[i].Name == "type") info.Properties.RemoveAt(i);
    }

    /// <summary>The message types this build understands.</summary>
    public static IReadOnlyCollection<string> KnownTypes => Types.Keys;

    /// <summary>One line, without the final newline.</summary>
    public static string Serialize(PipeMessage message)
    {
        var body = message is UnknownMessage unknown
            ? (JsonObject)unknown.Body.DeepClone()
            : JsonSerializer.SerializeToNode(message, message.GetType(), Options)!.AsObject();
        var line = new JsonObject { ["t"] = message.Type };
        foreach (var (key, value) in body.ToList())
        {
            if (key == "t") continue;
            body.Remove(key);
            line[key] = value;
        }
        return line.ToJsonString(Options);
    }

    /// <summary>
    /// Parses one line. Throws <see cref="JsonException"/> when it is not a JSON object with a <c>t</c>, or when a
    /// known message has fields of the wrong type; unknown messages come back as <see cref="UnknownMessage"/>.
    /// </summary>
    public static PipeMessage Parse(string line)
    {
        var node = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("No es un objeto JSON");
        var type = node["t"] is JsonValue t && t.TryGetValue<string>(out var name) ? name : throw new JsonException("Falta «t»");
        if (!Types.TryGetValue(type, out var clr)) return new UnknownMessage(type, node);
        node.Remove("t");
        try
        {
            return (PipeMessage)(node.Deserialize(clr, Options) ?? throw new JsonException($"«{type}» vacío"));
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"«{type}» no válido: {ex.Message}", ex);
        }
    }
}
