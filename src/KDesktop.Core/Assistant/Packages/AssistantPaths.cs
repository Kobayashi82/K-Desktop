using System.Text.Json;
using System.Text.Json.Nodes;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// Where assistants live (§11.7): each installed version in <c>Assistants\&lt;id&gt;\&lt;version&gt;</c>, its own data
/// (progress, statistics…) in <c>Assistants\Data\&lt;id&gt;</c>, kept on updates and switches, and the values the user
/// gave its settings in Options in <c>Assistants\Settings\&lt;id&gt;.json</c>.
/// </summary>
public static class AssistantPaths
{
    /// <summary>Folder names under <see cref="Root"/> that are not assistants.</summary>
    public static IReadOnlySet<string> Reserved { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data", "settings", "dev" };

    public static string Root => Path.Combine(AppPaths.DataDir, "Assistants");

    public static string Package(string id, string version) => Path.Combine(Root, id, version);

    public static string Data(string id) => Path.Combine(Root, "Data", id);

    public static string SettingsValues(string id) => Path.Combine(Root, "Settings", id + ".json");
}

/// <summary>The values of an assistant's settings: the defaults of its settings.json under what the user chose.</summary>
public static class AssistantSettingsValues
{
    /// <summary>Key → default of every item of a settings.json (pages → sections → items).</summary>
    public static JsonObject Defaults(string settingsJson)
    {
        var result = new JsonObject();
        if (JsonNode.Parse(settingsJson, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            is not JsonObject root || root["pages"] is not JsonArray pages) return result;
        foreach (var section in pages.OfType<JsonObject>().SelectMany(p => p["sections"] as JsonArray ?? []).OfType<JsonObject>())
            foreach (var item in (section["items"] as JsonArray ?? []).OfType<JsonObject>())
                if (item["key"]?.GetValue<string>() is { } key && item["default"] is { } value) result[key] = value.DeepClone();
        return result;
    }

    /// <summary>
    /// The settings of an installed (or in-development) package: defaults from its settings file, then the user's
    /// saved values for the keys that still exist.
    /// </summary>
    public static JsonObject Read(string packageFolder, AssistantManifest manifest)
    {
        var settingsFile = Path.Combine(packageFolder, manifest.SettingsFile);
        JsonObject values;
        try { values = File.Exists(settingsFile) ? Defaults(File.ReadAllText(settingsFile)) : []; }
        catch (JsonException ex)
        {
            Log.Warn($"Asistente «{manifest.Id}»: {manifest.SettingsFile} no es válido ({ex.Message})");
            values = [];
        }
        var saved = AssistantPaths.SettingsValues(manifest.Id);
        if (!File.Exists(saved)) return values;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(saved)) is JsonObject user)
                foreach (var (key, value) in user)
                    if (values.ContainsKey(key)) values[key] = value?.DeepClone();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Warn($"Asistente «{manifest.Id}»: no se han podido leer sus ajustes guardados ({ex.Message})");
        }
        return values;
    }
}
