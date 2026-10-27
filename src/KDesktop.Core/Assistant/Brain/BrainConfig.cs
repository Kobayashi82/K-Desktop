using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace KDesktop.Core.Assistant.Brain;

/// <summary>
/// Until 5.0.106, where the brain ran (one provider for everything). Only read from old settings, to move them to the
/// brain of 5.0.107 (<see cref="BrainConfig"/>).
/// </summary>
public enum BrainProvider
{
    Ollama,
    LlamaSwap,
    OpenAICompatible,
    None,
    LlamaServer,
}

/// <summary>The kind of another server with models (Options → Assistant → Brain → Other servers).</summary>
public enum RemoteKind
{
    /// <summary>Ollama: says what it has loaded and unloads with <c>keep_alive</c>.</summary>
    Ollama,

    /// <summary>
    /// llama-swap: OpenAI's API plus <c>/running</c> and <c>/unload</c>. Since 5.0.111 Options no longer offers it apart: an
    /// OpenAI one that answers <c>/running</c> is used as one (this stays for the servers added before).
    /// </summary>
    LlamaSwap,

    /// <summary>Any server with OpenAI's API (LM Studio, vLLM, a llama-server, llama-swap…): it lists and answers, and a llama-swap also unloads.</summary>
    OpenAI,
}

/// <summary>Where a server is, to say whether what is said to the assistant leaves the PC.</summary>
public enum EndpointPlace
{
    Invalid,
    ThisPc,
    /// <summary>Another computer of the home network (or a private one such as Tailscale).</summary>
    LocalNetwork,
    Internet,
}

/// <summary>
/// Another server with models. <paramref name="Id"/> is short and never changes (it goes in front of its models' names:
/// <c>r1/qwen3:4b</c>); <paramref name="Endpoint"/> empty: the usual one of its kind. The key is in plain text on the pipe
/// (only the user's) and protected with DPAPI in the settings.
/// </summary>
public sealed record BrainRemote(string Id, RemoteKind Kind, string Endpoint = "", string ApiKey = "")
{
    /// <summary>The address to use: the one typed, or the usual one of its kind.</summary>
    public string Address => Endpoint.Trim().TrimEnd('/') is { Length: > 0 } typed ? typed : Kind.DefaultEndpoint();

    /// <summary>For the user: «Ollama · localhost:11434».</summary>
    public string Name => $"{Kind.DisplayName()} · {(Uri.TryCreate(Address, UriKind.Absolute, out var uri) ? uri.Authority : Address)}";

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["kind"] = Kind.Key(),
        ["endpoint"] = Endpoint,
        ["apiKey"] = ApiKey,
    };

    public static BrainRemote? FromJson(JsonNode? node) =>
        node is JsonObject json && BrainConfig.Text(json["id"]) is { Length: > 0 } id
            ? new BrainRemote(id, BrainProviders.ParseKind(BrainConfig.Text(json["kind"])), BrainConfig.Text(json["endpoint"]), BrainConfig.Text(json["apiKey"]))
            : null;
}

/// <summary>
/// Options → Assistant → Brain, as the assistant process gets it (the <c>brain</c> section of <c>config</c>), 5.0.107: the
/// assistant process is its own llama-swap. It starts the <c>llama-server.exe</c> of <paramref name="Servers"/> (the first
/// one is the usual; a model near another one opens with that one: Bonsai's) with the <c>.gguf</c> found in
/// <paramref name="Folders"/> (the first one is K-Desktop's downloads), only when someone talks to an assistant, and
/// stops it when asked or after <paramref name="KeepMinutes"/> unused (0: it stays). <paramref name="Remotes"/> are other
/// servers whose models are listed too. Models are named by <see cref="ModelRefs"/>: the general one (empty: chosen
/// automatically; <see cref="ModelRefs.None"/>: no AI) and each assistant's own (stage 3.2). With
/// <paramref name="OneAtATime"/>, loading one model unloads the others; without it they stay while they fit in the
/// graphics card. <paramref name="Vision"/>: a <c>mmproj</c> next to the model is loaded with it.
/// <paramref name="ModelArgs"/>: each model's own llama-server options (its path → «-c 32768»), after the usual ones.
/// <paramref name="ThinkModel"/> (stage 12): the model that thinks harder when an order needs it («piénsalo bien», or the
/// usual model asks with <c>think_harder</c>); empty: the same model with its reasoning on; <see cref="ModelRefs.None"/>:
/// never. <paramref name="ThinkTokens"/>: how much it may think (tokens of reasoning, before its answer).
/// <paramref name="ThinkModels"/> (stage 13.10): each assistant's own model to think harder with (absent: the general one).
/// <paramref name="ThinkReason"/> (5.0.289): whether the model of «Para pensar más» reasons before answering (the same
/// model always does); <paramref name="ThinkReasons"/>: each assistant's own (false: its own model to think harder with
/// answers without reasoning; absent: it reasons). <see cref="ModelRefs.Usual"/> as an assistant's model to think harder
/// with: its usual one, reasoning.
/// <paramref name="FreeForGames"/>: a game on the whole screen takes the models of this PC out of the graphics card.
/// </summary>
public sealed record BrainConfig(
    string Model = "",
    IReadOnlyDictionary<string, string>? Models = null,
    IReadOnlyList<string>? Servers = null,
    IReadOnlyList<string>? Folders = null,
    IReadOnlyDictionary<string, string>? ModelArgs = null,
    int KeepMinutes = BrainConfig.DefaultKeepMinutes,
    bool OneAtATime = true,
    bool Vision = true,
    IReadOnlyList<BrainRemote>? Remotes = null,
    string ThinkModel = "",
    int ThinkTokens = BrainConfig.DefaultThinkTokens,
    bool FreeForGames = true,
    IReadOnlyDictionary<string, string>? ThinkModels = null,
    bool ThinkReason = true,
    IReadOnlyDictionary<string, bool>? ThinkReasons = null)
{
    /// <summary>Minutes a model stays loaded without use, by default.</summary>
    public const int DefaultKeepMinutes = 30;

    /// <summary>Tokens of reasoning when it thinks harder, by default (Bonsai, at ~50 a second: up to some 20 s).</summary>
    public const int DefaultThinkTokens = 1024;

    /// <summary>The fewest tokens of reasoning that are worth it.</summary>
    public const int LeastThinkTokens = 128;

    /// <summary>The user chose never to think harder.</summary>
    public bool ThinkOff => ThinkModel == ModelRefs.None;

    /// <summary>Each assistant's own model (never null).</summary>
    public IReadOnlyDictionary<string, string> AssistantModels => Models ?? new Dictionary<string, string>();

    /// <summary>Each assistant's own model to think harder with (never null).</summary>
    public IReadOnlyDictionary<string, string> AssistantThinkModels => ThinkModels ?? new Dictionary<string, string>();

    /// <summary>Whether each assistant with its own model to think harder with reasons with it (never null).</summary>
    public IReadOnlyDictionary<string, bool> AssistantThinkReasons => ThinkReasons ?? new Dictionary<string, bool>();

    public IReadOnlyList<string> ServerList => Servers ?? [];

    public IReadOnlyList<string> FolderList => Folders ?? [];

    public IReadOnlyList<BrainRemote> RemoteList => Remotes ?? [];

    /// <summary>Each model's own llama-server options (never null).</summary>
    public IReadOnlyDictionary<string, string> ModelArgList => ModelArgs ?? new Dictionary<string, string>();

    private JsonObject ArgsJson() => new(ModelArgList.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)));

    /// <summary>The user chose no AI: only the fixed rules and K-Desktop's commands.</summary>
    public bool IsOff => Model == ModelRefs.None;

    public static BrainConfig Default { get; } = new();

    /// <summary>What decides which models there are and how they run (not which one is chosen): when it changes, everything is looked at again.</summary>
    public string SourcesKey => new JsonObject
    {
        ["servers"] = new JsonArray([.. ServerList.Select(s => (JsonNode?)s)]),
        ["folders"] = new JsonArray([.. FolderList.Select(s => (JsonNode?)s)]),
        ["modelArgs"] = ArgsJson(),
        ["keepMinutes"] = KeepMinutes,
        ["oneAtATime"] = OneAtATime,
        ["vision"] = Vision,
        ["remotes"] = new JsonArray([.. RemoteList.Select(r => (JsonNode?)r.ToJson())]),
    }.ToJsonString();

    public JsonObject ToJson() => new()
    {
        ["model"] = Model,
        ["models"] = new JsonObject(AssistantModels.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        ["servers"] = new JsonArray([.. ServerList.Select(s => (JsonNode?)s)]),
        ["folders"] = new JsonArray([.. FolderList.Select(s => (JsonNode?)s)]),
        ["modelArgs"] = ArgsJson(),
        ["keepMinutes"] = KeepMinutes,
        ["oneAtATime"] = OneAtATime,
        ["vision"] = Vision,
        ["remotes"] = new JsonArray([.. RemoteList.Select(r => (JsonNode?)r.ToJson())]),
        ["thinkModel"] = ThinkModel,
        ["thinkTokens"] = ThinkTokens,
        ["freeForGames"] = FreeForGames,
        ["thinkModels"] = new JsonObject(AssistantThinkModels.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        ["thinkReason"] = ThinkReason,
        ["thinkReasons"] = new JsonObject(AssistantThinkReasons.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
    };

    public static BrainConfig FromJson(JsonObject? json)
    {
        if (json is null) return Default;
        static Dictionary<string, string> PerAssistant(JsonNode? node)
        {
            var models = new Dictionary<string, string>(StringComparer.Ordinal);
            if (node is JsonObject own)
                foreach (var (id, model) in own)
                    if (Text(model) is { Length: > 0 } name) models[id] = name;
            return models;
        }
        static Dictionary<string, bool> Reasons(JsonNode? node)
        {
            var reasons = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (node is JsonObject own)
                foreach (var (id, value) in own)
                    if (value is JsonValue v && v.TryGetValue<bool>(out var reason)) reasons[id] = reason;
            return reasons;
        }
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (json["modelArgs"] is JsonObject perModel)
            foreach (var (path, line) in perModel)
                if (Text(line) is { Length: > 0 } text) args[path] = text;
        static List<string> Texts(JsonNode? node) => node is JsonArray list ? [.. list.Select(Text).Where(t => t.Length > 0)] : [];
        var keep = json["keepMinutes"] is JsonValue k && k.TryGetValue<int>(out var minutes) ? Math.Max(0, minutes) : DefaultKeepMinutes;
        return new BrainConfig(Text(json["model"]), PerAssistant(json["models"]), Texts(json["servers"]), Texts(json["folders"]), args, keep,
            Flag(json["oneAtATime"], true), Flag(json["vision"], true),
            json["remotes"] is JsonArray remotes ? [.. remotes.Select(BrainRemote.FromJson).OfType<BrainRemote>()] : [],
            Text(json["thinkModel"]),
            json["thinkTokens"] is JsonValue t && t.TryGetValue<int>(out var tokens) ? Math.Max(LeastThinkTokens, tokens) : DefaultThinkTokens,
            Flag(json["freeForGames"], true), PerAssistant(json["thinkModels"]), Flag(json["thinkReason"], true), Reasons(json["thinkReasons"]));
    }

    internal static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static bool Flag(JsonNode? node, bool otherwise) => node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : otherwise;
}

/// <summary>
/// How a model is named (5.0.107), so two servers with a model of the same name never get mixed up: a <c>.gguf</c> on this
/// PC is its full path; another server's model is <c>id/name</c> (<c>r1/qwen3:4b</c>); <see cref="None"/> is no AI.
/// </summary>
public static class ModelRefs
{
    public const string None = "none";

    /// <summary>An assistant's model to think harder with (5.0.289): the one it usually thinks with, reasoning.</summary>
    public const string Usual = "usual";

    /// <summary>A <c>.gguf</c> file of this PC.</summary>
    public static bool IsLocal(string model) =>
        model.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(model);

    public static string Remote(string remoteId, string model) => remoteId + "/" + model;

    /// <summary>The server and the model's own name of another server's model; null for a local one, none or a bare name.</summary>
    public static (string Remote, string Model)? SplitRemote(string model)
    {
        if (IsLocal(model) || model == None) return null;
        var slash = model.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && slash < model.Length - 1 ? (model[..slash], model[(slash + 1)..]) : null;
    }

    /// <summary>Its name for the user: a <c>.gguf</c> without the folder and the extension, another server's without the server.</summary>
    public static string Label(string model) =>
        IsLocal(model) || model.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(model)
        : SplitRemote(model) is { } remote ? remote.Model
        : model;

    /// <summary>Whether two names are the same model (same file; same server and name, «x» being «x:latest» too).</summary>
    public static bool Same(string a, string b)
    {
        a = a.Trim();
        b = b.Trim();
        if (IsLocal(a) || IsLocal(b))
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }
        if (SplitRemote(a) is { } x && SplitRemote(b) is { } y)
            return string.Equals(x.Remote, y.Remote, StringComparison.Ordinal) && BrainProviders.SameModel(x.Model, y.Model);
        return BrainProviders.SameModel(a, b);
    }

    /// <summary>
    /// The model of <paramref name="available"/> that <paramref name="wanted"/> names: the same one, or —a bare name or a
    /// file name, as old settings and messages have— the first one with that name. Null: none.
    /// </summary>
    public static string? Find(IEnumerable<string> available, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted) || wanted == None || wanted == Usual) return null;
        var list = available as IReadOnlyCollection<string> ?? [.. available];
        return list.FirstOrDefault(m => Same(m, wanted))
            ?? (IsLocal(wanted) || SplitRemote(wanted) is not null ? null
                : list.FirstOrDefault(m => BrainProviders.SameModel(Label(m), wanted) || BrainProviders.SameModel(Label(m), Path.GetFileNameWithoutExtension(wanted))));
    }
}

public static class BrainProviders
{
    /// <summary>The usual address of each kind of server.</summary>
    public static string DefaultEndpoint(this RemoteKind kind) => kind switch
    {
        RemoteKind.LlamaSwap => "http://localhost:8080",
        RemoteKind.OpenAI => "http://localhost:1234",
        _ => "http://localhost:11434",
    };

    /// <summary>A model's name for the user (see <see cref="ModelRefs.Label"/>).</summary>
    public static string ModelLabel(string model) => ModelRefs.Label(model);

    /// <summary>Whether two model names are the same one («qwen3.5:4b» is «qwen3.5:4b:latest» too, as Ollama says).</summary>
    public static bool SameModel(string a, string b)
    {
        a = a.Trim();
        b = b.Trim();
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a + ":latest", b, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, b + ":latest", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the server may ask for an API key (Ollama never does).</summary>
    public static bool UsesApiKey(this RemoteKind kind) => kind is RemoteKind.LlamaSwap or RemoteKind.OpenAI;

    /// <summary>Whether two addresses are the same server («localhost» is «127.0.0.1», a final slash or «/v1» changes nothing).</summary>
    public static bool SameAddress(string a, string b)
    {
        static string Key(string address)
        {
            var text = address.Trim().TrimEnd('/');
            if (text.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) text = text[..^3];
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return text.ToLowerInvariant();
            var host = uri.IsLoopback ? "localhost" : uri.Host.ToLowerInvariant();
            return $"{uri.Scheme}://{host}:{uri.Port}{uri.AbsolutePath.TrimEnd('/')}";
        }
        return Key(a) == Key(b);
    }

    /// <summary>Its name on the pipe and in the settings.</summary>
    public static string Key(this RemoteKind kind) => kind switch
    {
        RemoteKind.LlamaSwap => "llamaSwap",
        RemoteKind.OpenAI => "openai",
        _ => "ollama",
    };

    public static RemoteKind ParseKind(string? key) => key switch
    {
        "llamaSwap" => RemoteKind.LlamaSwap,
        "openai" => RemoteKind.OpenAI,
        _ => RemoteKind.Ollama,
    };

    /// <summary>Its name for the user (the OpenAI-compatible one is translated by the interface).</summary>
    public static string DisplayName(this RemoteKind kind) => kind switch
    {
        RemoteKind.LlamaSwap => "llama-swap",
        RemoteKind.OpenAI => "OpenAI",
        _ => "Ollama",
    };

    /// <summary>Whether an address is this PC, the home network or the internet.</summary>
    public static EndpointPlace PlaceOf(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.Host.Length == 0)
            return EndpointPlace.Invalid;
        if (uri.IsLoopback) return EndpointPlace.ThisPc;

        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address))
        {
            // A name without a domain («server») or of the local network («nas.local», «pc.lan», «pc.home.arpa»).
            var host = uri.Host.TrimEnd('.');
            return !host.Contains('.', StringComparison.Ordinal)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)
                ? EndpointPlace.LocalNetwork
                : EndpointPlace.Internet;
        }

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (first & 0xFE) == 0xFC ? EndpointPlace.LocalNetwork : EndpointPlace.Internet;
        }

        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127)
            ? EndpointPlace.LocalNetwork
            : EndpointPlace.Internet;
    }
}
