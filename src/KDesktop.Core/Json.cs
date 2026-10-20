using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KDesktop.Core;

public static class Json
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipComputedActionProperties } },
    };

    /// <summary>
    /// [JsonIgnore] on an abstract property is not inherited by its overrides, so the computed Kind and Summary of
    /// every action ended up in config.json (the summary even in the current language). Drop them here, once.
    /// </summary>
    private static void SkipComputedActionProperties(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        if (typeof(Model.CommandAction).IsAssignableFrom(info.Type))
        {
            for (var i = info.Properties.Count - 1; i >= 0; i--)
                if (info.Properties[i].Name is "kind" or "summary") info.Properties.RemoveAt(i);
        }
        else if (typeof(Model.EventTrigger).IsAssignableFrom(info.Type))
        {
            for (var i = info.Properties.Count - 1; i >= 0; i--)
                if (info.Properties[i].Name is "kind" or "summary" or "hasEnd" or "defaultCooldown") info.Properties.RemoveAt(i);
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Deep copy through a JSON round-trip (uses the runtime type for polymorphic objects).</summary>
    public static T Clone<T>(T value) where T : class
    {
        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options)!;
    }
}
