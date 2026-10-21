using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>
/// Tool calls a model wrote as text instead of as calls: the Qwen «coder» format
/// (<c>&lt;tool_call&gt;&lt;function=name&gt;&lt;parameter=x&gt;value&lt;/parameter&gt;&lt;/function&gt;&lt;/tool_call&gt;</c>),
/// which Bonsai sometimes leaves in its answer, even half closed
/// (<c>&lt;function=find_command&gt; &lt;parameter=query&gt; desactivar notificaciones</c>).
/// </summary>
public static partial class TextToolCalls
{
    /// <summary>Whether an answer begins like one (it is held back instead of being shown).</summary>
    public static bool Starts(string text)
    {
        var start = text.TrimStart();
        return start.StartsWith("<tool_call", StringComparison.OrdinalIgnoreCase) || start.StartsWith("<function=", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The calls written in the text (none: an empty list).</summary>
    public static IReadOnlyList<ToolCall> Parse(string text)
    {
        if (!text.Contains("<function=", StringComparison.OrdinalIgnoreCase)) return [];
        var calls = new List<ToolCall>();
        foreach (Match function in FunctionPattern().Matches(text))
        {
            var arguments = new JsonObject();
            foreach (Match parameter in ParameterPattern().Matches(function.Groups["body"].Value))
                arguments[parameter.Groups["name"].Value] = Value(parameter.Groups["value"].Value.Trim());
            calls.Add(new ToolCall(function.Groups["name"].Value, JsonSerializer.SerializeToElement(arguments), "text_" + calls.Count));
        }
        return calls;
    }

    /// <summary>A number, true/false or JSON as such; anything else as text.</summary>
    private static JsonNode? Value(string value)
    {
        if (NumberPattern().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number % 1 == 0 && Math.Abs(number) < 1e15 ? JsonValue.Create((long)number) : JsonValue.Create(number);
        if (value is "true" or "false") return JsonValue.Create(value == "true");
        if (value.StartsWith('{') || value.StartsWith('['))
        {
            try { return JsonNode.Parse(value); }
            catch (JsonException) { }
        }
        return JsonValue.Create(value);
    }

    [GeneratedRegex(@"<function=(?<name>[\w.\-]+)>(?<body>.*?)(?:</function>|(?=<function=)|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FunctionPattern();

    [GeneratedRegex(@"<parameter=(?<name>[\w.\-]+)>(?<value>.*?)(?:</parameter>|(?=<parameter=)|(?=</function>)|(?=</tool_call>)|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParameterPattern();

    [GeneratedRegex(@"^-?\d+(?:\.\d+)?$")]
    private static partial Regex NumberPattern();
}
