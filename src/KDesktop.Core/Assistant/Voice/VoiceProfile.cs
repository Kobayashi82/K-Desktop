using System.Text.Json;
using System.Text.Json.Nodes;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>
/// How a voice sounds (plan §10, <c>docs/assistant/schemas/voice.schema.json</c>): the engine that makes it and the chain
/// of effects after it, plus the effects added in some situations (<see cref="Moods"/>: <c>error</c> after a crash,
/// <c>far</c> when it speaks from another monitor, <c>happy</c> when it celebrates…). K-Desktop has six of its own
/// (<see cref="BuiltIn"/>); each assistant may bring its own (<c>voice</c> of its manifest).
/// </summary>
/// <param name="Engine"><c>kokoro</c>, <c>piper</c>, <c>windows</c> or <c>beeps</c> (<c>pocket</c>, the voice until 5.0.93, is read as <c>kokoro</c>).</param>
/// <param name="Voice">The engine's voice (a Kokoro voice or mix, or a Piper voice); empty or <c>default</c>: the one chosen in Options.</param>
/// <param name="Chain">The effects, in order (each an object with <c>fx</c> and its values).</param>
public sealed record VoiceProfile(string Id, string Name, string Engine, string? Voice, double Speed, double Volume,
    JsonArray Chain, IReadOnlyDictionary<string, JsonArray> Moods)
{
    /// <summary>The profile when none is chosen and the assistant brings none.</summary>
    public const string DefaultId = "natural";

    private static readonly Lazy<IReadOnlyList<VoiceProfile>> Ours = new(() =>
    {
        using var stream = typeof(VoiceProfile).Assembly.GetManifestResourceStream("AssistantVoice/voice-profiles.json")
                           ?? throw new InvalidOperationException("Falta voice-profiles.json");
        var array = JsonNode.Parse(stream)!.AsArray();
        return [.. array.OfType<JsonObject>().Select(p => Parse(p, p["id"]!.GetValue<string>()))];
    });

    /// <summary>K-Desktop's own: Natural, Robot clásico, Marciano, Control de misión, 8 bits, Pitidos.</summary>
    public static IReadOnlyList<VoiceProfile> BuiltIn => Ours.Value;

    public static VoiceProfile Default => BuiltIn.First(p => p.Id == DefaultId);

    public static VoiceProfile? Find(string? id) => id is null ? null : BuiltIn.FirstOrDefault(p => p.Id == id);

    /// <summary>The voice of the engine, or null for the one chosen in Options.</summary>
    public string? OwnVoice => Voice is { Length: > 0 } v && v != "default" ? v : null;

    /// <summary>Reads a profile (the JSON of the schema); a broken one throws <see cref="JsonException"/>.</summary>
    public static VoiceProfile Parse(JsonObject json, string id)
    {
        try
        {
            var engine = json["engine"] as JsonObject ?? throw new JsonException("Falta «engine»");
            var moods = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
            if (json["moods"] is JsonObject m)
                foreach (var (mood, chain) in m)
                    if (chain is JsonArray list) moods[mood] = (JsonArray)list.DeepClone();
            return new VoiceProfile(id, json["name"]?.GetValue<string>() ?? id,
                engine["type"]?.GetValue<string>() is { } type && type != "pocket" ? type : "kokoro",
                engine["voice"]?.GetValue<string>(),
                Math.Clamp(Number(engine["speed"]) ?? 1, 0.5, 2),
                Math.Clamp(Number(engine["volume"]) ?? 1, 0, 1),
                json["chain"] is JsonArray chainList ? (JsonArray)chainList.DeepClone() : [],
                moods);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new JsonException($"Perfil de voz «{id}» no válido: {ex.Message}", ex);
        }
    }

    /// <summary>A number of the JSON, whole or not (one made in code may hold an int: «1» is 1.0).</summary>
    private static double? Number(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<double>(out var d) => d,
        JsonValue value when value.TryGetValue<int>(out var i) => i,
        JsonValue value when value.TryGetValue<long>(out var l) => l,
        _ => node.GetValue<double>(),
    };

    /// <summary>Reads a profile from a file (the one a package brings); null if it is missing or broken.</summary>
    public static VoiceProfile? Load(string path, string id)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) is JsonObject json
                ? Parse(json, id) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Asistente: perfil de voz {path} no válido: {ex.Message}");
            return null;
        }
    }

    /// <summary>As JSON (for the pipe and for exporting it).</summary>
    public JsonObject ToJson()
    {
        var engine = new JsonObject { ["type"] = Engine, ["speed"] = Speed, ["volume"] = Volume };
        if (Voice is { Length: > 0 }) engine["voice"] = Voice;
        var moods = new JsonObject();
        foreach (var (mood, chain) in Moods) moods[mood] = chain.DeepClone();
        return new JsonObject { ["id"] = Id, ["name"] = Name, ["engine"] = engine, ["chain"] = Chain.DeepClone(), ["moods"] = moods };
    }
}
