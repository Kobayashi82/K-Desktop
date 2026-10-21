using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KDesktop.Core.Assistant;

/// <summary>
/// What one assistant has of its own (Options → Assistants → it → «Personalidad y voz», 5.0.105): its personality, the
/// language it speaks in and its voice. What is left empty is the general one (Options → Assistant module), as before.
/// The same record travels to the assistant process in the <c>personal</c> section of <c>config</c>, with its language
/// and its Kokoro and Piper voices already resolved by K-Desktop.
/// </summary>
public sealed record AssistantPersonal
{
    /// <summary>Its language («es», «en»…); empty: the one the assistants speak in.</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>Its instructions for the AI, written by the user; null: those of its package.</summary>
    public string? Prompt { get; init; }

    /// <summary>Who says it aloud (<c>auto</c>, <c>kokoro</c>, <c>piper</c>, <c>windows</c>, <c>beeps</c>, <c>off</c>); empty: the general one.</summary>
    public string Engine { get; init; } = string.Empty;

    /// <summary>Its Kokoro voice or mix of two; empty: the general one (if it is of its language, else the language's first).</summary>
    public string KokoroVoice { get; init; } = string.Empty;

    /// <summary>Its Piper voice («key» or «key#speaker»); empty: the general one (if it is of its language, else the language's default).</summary>
    public string PiperVoice { get; init; } = string.Empty;

    /// <summary>Its Windows voice by name; empty: the general one.</summary>
    public string WindowsVoice { get; init; } = string.Empty;

    /// <summary>
    /// Its voice profile: one of K-Desktop's (<c>robot</c>…), <see cref="OwnProfile"/> (the one its package brings, even
    /// when all of them have one fixed) or empty (the general choice).
    /// </summary>
    public string Profile { get; init; } = string.Empty;

    /// <summary>How fast it speaks (0.5 … 2); null: the general speed.</summary>
    public double? Speed { get; init; }

    /// <summary><see cref="Profile"/>: the one its package brings (or «Natural»), whatever is fixed for all.</summary>
    public const string OwnProfile = "own";

    /// <summary>Nothing of its own: it is not kept.</summary>
    [JsonIgnore]
    public bool IsEmpty => Language.Length == 0 && Prompt is null && !HasOwnVoice;

    /// <summary>Something of its voice is its own.</summary>
    [JsonIgnore]
    public bool HasOwnVoice => Engine.Length > 0 || KokoroVoice.Length > 0 || PiperVoice.Length > 0 || WindowsVoice.Length > 0 || Profile.Length > 0 || Speed is not null;

    /// <summary>The voice as the general one again (its language and its personality stay).</summary>
    public AssistantPersonal WithoutVoice() => this with { Engine = string.Empty, KokoroVoice = string.Empty, PiperVoice = string.Empty, WindowsVoice = string.Empty, Profile = string.Empty, Speed = null };

    /// <summary>The <c>personal</c> section: assistant id → what it has of its own.</summary>
    public static JsonObject ToJson(IReadOnlyDictionary<string, AssistantPersonal> all) =>
        new(all.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, JsonSerializer.SerializeToNode(p.Value, Json.Options))));

    /// <summary>Reads the <c>personal</c> section (an entry that cannot be read is left out).</summary>
    public static Dictionary<string, AssistantPersonal> FromJson(JsonObject? section)
    {
        var all = new Dictionary<string, AssistantPersonal>(StringComparer.Ordinal);
        if (section is null) return all;
        foreach (var (id, node) in section)
        {
            if (node is not JsonObject value) continue;
            try
            {
                if (value.Deserialize<AssistantPersonal>(Json.Options) is { } personal) all[id] = personal;
            }
            catch (JsonException ex)
            {
                Log.Warn($"Asistente: no se ha podido leer lo propio de «{id}»: {ex.Message}");
            }
        }
        return all;
    }
}
