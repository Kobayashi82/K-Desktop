using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KDesktop.Core.Assistant;

namespace KDesktop.Assistant.Tools.Declarative;

/// <summary>A secret a tool needs that is not kept (or cannot be read on this PC).</summary>
public sealed class MissingSecretException(string name) : Exception($"Falta el secreto «{name}»")
{
    public string Name => name;
}

/// <summary>
/// The <c>{{…}}</c> of a JSON tool: <c>{{parámetro}}</c>, <c>{{secret:nombre}}</c>, and once it ran <c>{{result}}</c>,
/// <c>{{result.campo}}</c> and <c>{{status}}</c>. A secret goes only where the request leaves (with <c>withSecrets</c>);
/// for people (the intent, what is said) it shows as <see cref="ToolSecrets.Hidden"/>.
/// </summary>
public sealed partial class JsonTemplate(IReadOnlyDictionary<string, JsonNode?> arguments, ToolSecrets? secrets, bool withSecrets)
{
    private const string SecretPrefix = "secret:";

    /// <summary>The answer it got (JSON or text), once it ran.</summary>
    public JsonNode? Result { get; set; }

    /// <summary>What <c>{{result}}</c> is: the picked part of the answer, as text.</summary>
    public string? ResultText { get; set; }

    public int? Status { get; set; }

    /// <summary>Fills every <c>{{…}}</c> of <paramref name="text"/>; <paramref name="encode"/> each value (for a URL).</summary>
    public string Fill(string text, Func<string, string>? encode = null) =>
        Placeholder().Replace(text, m =>
        {
            var value = Value(m.Groups[1].Value);
            return encode is null ? value : encode(value);
        });

    /// <summary>
    /// A JSON body with its <c>{{…}}</c> filled: a text that is only <c>"{{parámetro}}"</c> becomes the parameter with its
    /// type (a number stays a number); any other text is filled as text.
    /// </summary>
    public JsonNode? Fill(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Fill(p.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Fill)]),
        JsonValue value when value.TryGetValue<string>(out var text) =>
            IsWhole(text, out var key) && arguments.TryGetValue(key, out var typed) ? typed?.DeepClone() : JsonValue.Create(Fill(text)),
        _ => node.DeepClone(),
    };

    /// <summary>Whether <paramref name="text"/> is exactly one <c>{{…}}</c>, and which.</summary>
    public static bool IsWhole(string text, out string key)
    {
        var match = Placeholder().Match(text);
        key = match.Success && match.Length == text.Length ? match.Groups[1].Value : string.Empty;
        return key.Length > 0;
    }

    /// <summary>The secrets a text names (to say which are missing before running).</summary>
    public static IEnumerable<string> SecretsIn(string? text) =>
        text is null ? [] : Placeholder().Matches(text).Select(m => m.Groups[1].Value)
            .Where(k => k.StartsWith(SecretPrefix, StringComparison.Ordinal)).Select(k => k[SecretPrefix.Length..]);

    private string Value(string key)
    {
        if (key.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            if (!withSecrets) return ToolSecrets.Hidden;
            var name = key[SecretPrefix.Length..];
            return secrets?.Get(name) ?? throw new MissingSecretException(name);
        }
        if (key == "status") return Status?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        if (key == "result") return ResultText ?? string.Empty;
        if (key.StartsWith("result.", StringComparison.Ordinal)) return JsonToolDefinition.Text(Pick(Result, key["result.".Length..]));
        return arguments.TryGetValue(key, out var value) ? JsonToolDefinition.Text(value) : string.Empty;
    }

    /// <summary>The part of <paramref name="node"/> at <paramref name="path"/> (<c>a.b.0.c</c>), or null.</summary>
    public static JsonNode? Pick(JsonNode? node, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return node;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            node = node switch
            {
                JsonObject obj => obj.TryGetPropertyValue(part, out var child) ? child
                    : obj.FirstOrDefault(p => string.Equals(p.Key, part, StringComparison.OrdinalIgnoreCase)).Value,
                JsonArray array when int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) =>
                    i >= 0 && i < array.Count ? array[i] : i < 0 && -i <= array.Count ? array[array.Count + i] : null,
                _ => null,
            };
            if (node is null) return null;
        }
        return node;
    }

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex Placeholder();
}
