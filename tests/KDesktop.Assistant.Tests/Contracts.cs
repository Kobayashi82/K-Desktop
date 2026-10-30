using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace KDesktop.Assistant.Tests;

/// <summary>The contract files: pipe examples (Contracts\Pipe), the example package (Contracts\LanderPackage), the
/// package schemas (KDesktop.Core, Assistant\Packages\Schemas), the JSON tool samples and the protocol doc.</summary>
internal static class Contracts
{
    /// <summary>The root of the repository.</summary>
    public static string Root { get; } = Path.GetFullPath(typeof(Contracts).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepoDir").Value!);

    public static string Examples => Path.Combine(Root, "tests", "KDesktop.Assistant.Tests", "Contracts", "Pipe");

    public static string LanderPackage => Path.Combine(Root, "tests", "KDesktop.Assistant.Tests", "Contracts", "LanderPackage");

    public static string SchemasDir => Path.Combine(Root, "src", "KDesktop.Core", "Assistant", "Packages", "Schemas");

    public static string ToolSamples => Path.Combine(Root, "samples", "Tools");

    public static string Protocol => Path.Combine(Root, "docs", "assistant", "protocolo.md");

    private static readonly Lazy<Dictionary<string, JsonSchema>> Schemas = new(LoadSchemas);

    private static Dictionary<string, JsonSchema> LoadSchemas()
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        var dir = SchemasDir;
        var result = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);

        // common first: the others refer to it.
        foreach (var file in Directory.GetFiles(dir, "*.schema.json").OrderBy(f => Path.GetFileName(f) == "common.schema.json" ? 0 : 1))
        {
            var schema = JsonSchema.FromFile(file, options);
            result[Path.GetFileName(file).Replace(".schema.json", "")] = schema;
        }

        return result;
    }

    /// <summary>Validates <paramref name="json"/> against a schema («assistant», «settings»…): the list of errors (empty = valid).</summary>
    public static IReadOnlyList<string> Validate(string schema, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = Schemas.Value[schema].Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid) return [];
        return [.. (results.Details ?? []).Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))];
    }

    public static IReadOnlyList<string> ValidateFile(string schema, string path) => Validate(schema, File.ReadAllText(path));

    /// <summary>A copy of a JSON file with a change applied, to check that broken files are rejected.</summary>
    public static string Mutate(string path, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        change(node);
        return node.ToJsonString();
    }
}
