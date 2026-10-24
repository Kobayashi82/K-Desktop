using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KDesktop.Core.Assistant;

/// <summary>
/// What every assistant has the same way (stage 13.13), set in the «Basics» tab of its card instead of each one in its
/// own units: its size, the volume of its sounds, how much it moves about on its own and whether it can be picked up with
/// the mouse. Its manifest says which of them it takes (<see cref="Packages.AssistantManifest.Takes"/>). The same record
/// travels to the assistant process in the <c>common</c> section of <c>config</c>.
/// </summary>
public sealed record AssistantCommon
{
    /// <summary>Its size as a factor of the one it was drawn for (<see cref="MinSize"/> … <see cref="MaxSize"/>).</summary>
    public double Size { get; init; } = 1;

    /// <summary>The volume of its sounds (0 … 1; the host applies it, over the one of all the assistants).</summary>
    public double Volume { get; init; } = 1;

    /// <summary>How much it moves about on its own: 0 only when asked, 1 as usual, up to <see cref="MaxLiveliness"/>.</summary>
    public double Liveliness { get; init; } = 1;

    /// <summary>Whether it can be picked up with the mouse (the host keeps the drags of its body from it when not).</summary>
    public bool Dragging { get; init; } = true;

    /// <summary>
    /// What it says always in K-Desktop's bubble, although it draws its own (stage 13.7: only for one whose manifest says
    /// <c>"speech": "own"</c>).
    /// </summary>
    [JsonPropertyName("kdesktopBubble")]
    public bool KDesktopBubble { get; init; }

    public const double MinSize = 0.5;
    public const double MaxSize = 2;
    public const double MaxLiveliness = 2;

    // The names in the manifest's «common».
    public const string SizeKey = "size";
    public const string VolumeKey = "volume";
    public const string LivelinessKey = "liveliness";
    public const string DraggingKey = "dragging";

    /// <summary>All of them, in the order the card shows them.</summary>
    public static IReadOnlyList<string> Keys { get; } = [SizeKey, VolumeKey, LivelinessKey, DraggingKey];

    public static AssistantCommon Default { get; } = new();

    /// <summary>All as usual: it is not kept.</summary>
    [JsonIgnore]
    public bool IsDefault => this == Default;

    /// <summary>Within their ranges (a hand-edited config).</summary>
    public AssistantCommon Clamped() => this with
    {
        Size = double.IsFinite(Size) ? Math.Clamp(Size, MinSize, MaxSize) : 1,
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 1,
        Liveliness = double.IsFinite(Liveliness) ? Math.Clamp(Liveliness, 0, MaxLiveliness) : 1,
    };

    /// <summary>The <c>common</c> section: assistant id → its values (only those that are not as usual).</summary>
    public static JsonObject ToJson(IReadOnlyDictionary<string, AssistantCommon> all) =>
        new(all.Where(p => !p.Value.IsDefault).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, JsonSerializer.SerializeToNode(p.Value.Clamped(), Json.Options))));

    /// <summary>Reads the <c>common</c> section (an entry that cannot be read is left out: as usual).</summary>
    public static Dictionary<string, AssistantCommon> FromJson(JsonObject? section)
    {
        var all = new Dictionary<string, AssistantCommon>(StringComparer.Ordinal);
        if (section is null) return all;
        foreach (var (id, node) in section)
        {
            if (node is not JsonObject value) continue;
            try
            {
                if (value.Deserialize<AssistantCommon>(Json.Options) is { } common) all[id] = common.Clamped();
            }
            catch (JsonException ex)
            {
                Log.Warn($"Asistente: no se han podido leer los ajustes comunes de «{id}»: {ex.Message}");
            }
        }
        return all;
    }
}
