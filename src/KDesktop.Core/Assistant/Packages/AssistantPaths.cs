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
    /// <summary>
    /// Folder names under <see cref="Root"/> that are not assistants, and the id of K-Desktop itself (5.0.112: an assistant
    /// of the system, no package can be it).
    /// </summary>
    public static IReadOnlySet<string> Reserved { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data", "settings", "dev", AssistantTargeting.KDesktop };

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
        foreach (var (key, value) in ReadSaved(manifest.Id))
            if (values.ContainsKey(key)) values[key] = value?.DeepClone();
        return values;
    }

    /// <summary>What the user changed (key → value; only what differs from the defaults), or nothing.</summary>
    public static JsonObject ReadSaved(string id)
    {
        var saved = AssistantPaths.SettingsValues(id);
        if (!File.Exists(saved)) return [];
        try { return JsonNode.Parse(File.ReadAllText(saved)) as JsonObject ?? []; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente «{id}»: no se han podido leer sus ajustes guardados ({ex.Message})");
            return [];
        }
    }

    /// <summary>
    /// Saves what the user changed (only what differs from the defaults, so a new default of an update reaches whoever
    /// never touched it); nothing left deletes the file. Throws IOException.
    /// </summary>
    public static void WriteSaved(string id, JsonObject saved)
    {
        var file = AssistantPaths.SettingsValues(id);
        if (saved.Count == 0)
        {
            File.Delete(file);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, saved.ToJsonString(Indented));
        File.Move(tmp, file, overwrite: true);
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
