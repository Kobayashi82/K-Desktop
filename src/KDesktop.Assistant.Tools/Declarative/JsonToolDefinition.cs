using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant;

namespace KDesktop.Assistant.Tools.Declarative;

/// <summary>A JSON tool as written in its file (docs/assistant/esquemas/tool.schema.json, guide in herramientas.md).</summary>
public sealed class JsonToolDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary><c>http</c>, <c>kdesktop</c>, <c>routine</c> or <c>script</c>.</summary>
    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string>? Examples { get; set; }

    public Dictionary<string, JsonParameter>? Parameters { get; set; }

    public List<string>? Required { get; set; }

    public string? Risk { get; set; }

    public bool ReadsOnly { get; set; }

    public string? Intent { get; set; }

    public JsonSay? Say { get; set; }

    public bool Enabled { get; set; } = true;

    public double? Timeout { get; set; }

    public JsonRequest? Request { get; set; }

    public string? Command { get; set; }

    public List<JsonStep>? Steps { get; set; }

    public JsonScript? Script { get; set; }

    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Its risk: what it says, or <see cref="ToolRisk.Safe"/> (a script: <see cref="ToolRisk.AskOnce"/>).</summary>
    public ToolRisk RiskLevel => Risk switch
    {
        "safe" => ToolRisk.Safe,
        "askOnce" => ToolRisk.AskOnce,
        "askAlways" => ToolRisk.AskAlways,
        _ => Type == "script" ? ToolRisk.AskOnce : ToolRisk.Safe,
    };

    /// <summary>The longest it may take.</summary>
    public TimeSpan TimeLimit => TimeSpan.FromSeconds(Math.Clamp(Timeout ?? (Type == "script" ? 30 : Type == "routine" ? 120 : 15), 1, 300));

    /// <summary>The names of the parameters the model must give.</summary>
    public IReadOnlyList<string> RequiredNames =>
        Required ?? [.. (Parameters ?? []).Where(p => p.Value.Default is null).Select(p => p.Key)];

    /// <summary>The JSON schema of its parameters, for the model.</summary>
    public string ParametersSchema()
    {
        var properties = new JsonObject();
        foreach (var (name, parameter) in Parameters ?? [])
        {
            var property = new JsonObject { ["type"] = parameter.Type };
            if (parameter.Description is { Length: > 0 } description) property["description"] = description;
            if (parameter.Enum is { Count: > 0 } values) property["enum"] = new JsonArray([.. values.Select(v => v?.DeepClone())]);
            if (parameter.Minimum is { } min) property["minimum"] = min;
            if (parameter.Maximum is { } max) property["maximum"] = max;
            properties[name] = property;
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        var required = RequiredNames.Where(n => Parameters?.ContainsKey(n) == true).ToList();
        if (required.Count > 0) schema["required"] = new JsonArray([.. required.Select(n => (JsonNode)n)]);
        return schema.ToJsonString();
    }

    /// <summary>
    /// The arguments the model sent, checked and with the defaults: each parameter's value (null when not given and
    /// not required); <paramref name="problem"/> says what is wrong (for the model, in English).
    /// </summary>
    public Dictionary<string, JsonNode?> Resolve(JsonElement arguments, out string? problem)
    {
        problem = null;
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var required = RequiredNames;
        foreach (var (name, parameter) in Parameters ?? [])
        {
            JsonNode? value = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var given)
                && given.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? JsonNode.Parse(given.GetRawText()) : null;
            if (value is JsonValue { } text && text.TryGetValue<string>(out var s) && s.Length == 0 && parameter.Type != "string") value = null;
            value ??= parameter.Default?.DeepClone();
            if (value is null)
            {
                if (required.Contains(name)) problem ??= $"Missing parameter «{name}».";
                values[name] = null;
                continue;
            }
            if (Coerce(value, parameter, out var why) is { } ok) values[name] = ok;
            else problem ??= $"Parameter «{name}»: {why}";
        }
        return values;
    }

    private static JsonNode? Coerce(JsonNode value, JsonParameter parameter, out string why)
    {
        why = string.Empty;
        var raw = value is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : value.ToJsonString();
        JsonNode? result;
        switch (parameter.Type)
        {
            case "number" or "integer":
                if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    why = $"«{raw}» is not a number.";
                    return null;
                }
                if (parameter.Type == "integer") number = Math.Round(number);
                if (number < parameter.Minimum || number > parameter.Maximum)
                {
                    why = $"{number.ToString(CultureInfo.InvariantCulture)} is out of range ({parameter.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "…"} to {parameter.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "…"}).";
                    return null;
                }
                result = parameter.Type == "integer" ? JsonValue.Create((long)number) : JsonValue.Create(number);
                break;
            case "boolean":
                bool? flag = raw.ToLowerInvariant() switch
                {
                    "true" or "yes" or "1" or "on" or "sí" or "si" => true,
                    "false" or "no" or "0" or "off" => false,
                    _ => null,
                };
                if (flag is null)
                {
                    why = $"«{raw}» is not true or false.";
                    return null;
                }
                result = JsonValue.Create(flag.Value);
                break;
            default:
                result = JsonValue.Create(raw);
                break;
        }

        if (parameter.Enum is { Count: > 0 } allowed)
        {
            var text = Text(result);
            var match = allowed.FirstOrDefault(a => string.Equals(Text(a), text, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                why = $"«{text}» is not one of: {string.Join(", ", allowed.Select(Text))}.";
                return null;
            }
            result = match.DeepClone();
        }
        return result;
    }

    /// <summary>A value as text: a string without quotes, numbers in invariant form, true/false.</summary>
    public static string Text(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue<string>(out var s) => s,
        JsonValue value when value.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        JsonValue value when value.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => node.ToJsonString(),
    };
}

public sealed class JsonParameter
{
    /// <summary><c>string</c>, <c>number</c>, <c>integer</c> or <c>boolean</c>.</summary>
    public string Type { get; set; } = "string";

    public string? Description { get; set; }

    public List<JsonNode?>? Enum { get; set; }

    public JsonNode? Default { get; set; }

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }
}

public sealed class JsonSay
{
    public string? Ok { get; set; }

    public string? Error { get; set; }
}

public sealed class JsonRequest
{
    public string Method { get; set; } = "GET";

    public string Url { get; set; } = string.Empty;

    public Dictionary<string, string>? Headers { get; set; }

    public Dictionary<string, string>? Query { get; set; }

    public JsonNode? Body { get; set; }

    public string? BodyText { get; set; }

    public string? ContentType { get; set; }

    /// <summary>The part of a JSON answer given to the model (<c>attributes.temperature</c>, <c>items.0.name</c>).</summary>
    public string? Pick { get; set; }
}

public sealed class JsonStep
{
    public string? Tool { get; set; }

    public JsonObject? Arguments { get; set; }

    public string? Command { get; set; }

    public double? Wait { get; set; }

    public bool ContinueOnError { get; set; }
}

public sealed class JsonScript
{
    public string File { get; set; } = string.Empty;

    public List<string>? Args { get; set; }

    public Dictionary<string, string>? Env { get; set; }
}
