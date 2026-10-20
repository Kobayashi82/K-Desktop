using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace KDesktop.Core.Assistant.Brain;

/// <summary>Where the assistants' brain (the AI) runs.</summary>
public enum BrainProvider
{
    /// <summary>Ollama, on this PC by default.</summary>
    Ollama,

    /// <summary>llama-swap: starts and stops llama-server (llama.cpp) for the model asked.</summary>
    LlamaSwap,

    /// <summary>Any server with OpenAI's API (LM Studio, llama-server, vLLM…).</summary>
    OpenAICompatible,

    /// <summary>No AI: only the fixed rules and K-Desktop's commands.</summary>
    None,
}

/// <summary>Where the brain's server is, to say whether what is said to the assistant leaves the PC.</summary>
public enum EndpointPlace
{
    Invalid,
    ThisPc,
    /// <summary>Another computer of the home network (or a private one such as Tailscale).</summary>
    LocalNetwork,
    Internet,
}

/// <summary>
/// Options → Assistant → Brain, as the assistant process gets it (the <c>brain</c> section of <c>config</c>): the provider,
/// its address (the usual one when empty), its key in plain text (the pipe is only the user's), the general model
/// (empty: chosen automatically) and the model of each assistant that has its own (stage 3.2; when the server does not
/// have it, that assistant uses the general one).
/// </summary>
public sealed record BrainConfig(BrainProvider Provider, string Endpoint = "", string ApiKey = "", string Model = "",
    IReadOnlyDictionary<string, string>? Models = null)
{
    /// <summary>Each assistant's own model (never null).</summary>
    public IReadOnlyDictionary<string, string> AssistantModels => Models ?? new Dictionary<string, string>();

    public static BrainConfig Default { get; } = new(BrainProvider.Ollama);

    /// <summary>The address to use: the one typed, or the provider's usual one.</summary>
    public string Address => Endpoint.Trim() is { Length: > 0 } typed ? typed : Provider.DefaultEndpoint();

    public JsonObject ToJson() => new()
    {
        ["provider"] = Provider.Key(),
        ["endpoint"] = Address,
        ["apiKey"] = ApiKey,
        ["model"] = Model,
        ["models"] = new JsonObject(AssistantModels.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
    };

    public static BrainConfig FromJson(JsonObject? json)
    {
        if (json is null) return Default;
        static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;
        var models = new Dictionary<string, string>(StringComparer.Ordinal);
        if (json["models"] is JsonObject own)
            foreach (var (id, model) in own)
                if (Text(model) is { Length: > 0 } name) models[id] = name;
        return new BrainConfig(BrainProviders.Parse(Text(json["provider"])), Text(json["endpoint"]), Text(json["apiKey"]), Text(json["model"]), models);
    }
}

public static class BrainProviders
{
    /// <summary>The usual address of each server.</summary>
    public static string DefaultEndpoint(this BrainProvider provider) => provider switch
    {
        BrainProvider.LlamaSwap => "http://localhost:8080",
        BrainProvider.OpenAICompatible => "http://localhost:1234",
        _ => "http://localhost:11434",
    };

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
    public static bool UsesApiKey(this BrainProvider provider) => provider is BrainProvider.LlamaSwap or BrainProvider.OpenAICompatible;

    /// <summary>Its name on the pipe.</summary>
    public static string Key(this BrainProvider provider) => provider switch
    {
        BrainProvider.LlamaSwap => "llamaSwap",
        BrainProvider.OpenAICompatible => "openai",
        BrainProvider.None => "none",
        _ => "ollama",
    };

    public static BrainProvider Parse(string? key) => key switch
    {
        "llamaSwap" => BrainProvider.LlamaSwap,
        "openai" => BrainProvider.OpenAICompatible,
        "none" => BrainProvider.None,
        _ => BrainProvider.Ollama,
    };

    /// <summary>Its name for the user (the OpenAI-compatible one and «none» are translated by the interface).</summary>
    public static string DisplayName(this BrainProvider provider) => provider switch
    {
        BrainProvider.LlamaSwap => "llama-swap",
        BrainProvider.Ollama => "Ollama",
        _ => provider.Key(),
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
