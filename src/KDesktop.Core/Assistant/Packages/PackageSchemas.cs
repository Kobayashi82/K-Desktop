using System.Text.Json;
using Json.Schema;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// The JSON schemas of a package (Schemas next to this file, built into the assembly): assistant, settings, panel,
/// persona and voice. Only data is checked here; nothing of the package runs.
/// </summary>
public static class PackageSchemas
{
    private const string Prefix = "AssistantSchemas/";

    private static readonly Lazy<Dictionary<string, JsonSchema>> Schemas = new(Load);

    private static Dictionary<string, JsonSchema> Load()
    {
        var assembly = typeof(PackageSchemas).Assembly;
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        var result = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);
        // common first: the others refer to it.
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                     .OrderBy(n => n.EndsWith("/common.schema.json", StringComparison.Ordinal) ? 0 : 1))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            result[name[Prefix.Length..].Replace(".schema.json", "", StringComparison.Ordinal)] = JsonSchema.FromText(reader.ReadToEnd(), options);
        }
        return result;
    }

    /// <summary>
    /// Checks <paramref name="json"/> against a schema («assistant», «settings», «panel», «persona», «voice»): the
    /// errors as «/where: what» (empty = valid). Text that is not JSON is one error.
    /// </summary>
    public static IReadOnlyList<string> Validate(string schema, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { return [ex.Message]; }
        using (doc)
        {
            var results = Schemas.Value[schema].Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (results.IsValid) return [];
            List<string> errors = [.. (results.Details ?? []).Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{(d.InstanceLocation.ToString() is { Length: > 0 } at ? at : "/")}: {e.Value}"))
                .Distinct()];
            // Invalid without a message (a «oneOf» none of whose choices fit): where it fails, at least.
            if (errors.Count == 0)
                errors = [.. (results.Details ?? []).Where(d => !d.IsValid).Select(d => d.InstanceLocation.ToString()).Where(at => at.Length > 0)
                    .Distinct().Select(at => $"{at}: no es válido").DefaultIfEmpty("/: no es válido")];
            return errors;
        }
    }
}
