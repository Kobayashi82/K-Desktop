using System.Text.Json;
using KDesktop.Core.Assistant;

namespace KDesktop.Assistant.Tools;

/// <summary>
/// Something the assistant can do (plan §7.1): its name and description for the model, its parameters as a JSON schema,
/// how risky it is, the gesture it asks of the assistant, what it is about to do in words for people, doing it and,
/// when it can, undoing it.
/// </summary>
public interface ITool
{
    /// <summary>Its name for the model (snake_case, English: models follow it better in any language).</summary>
    string Name { get; }

    /// <summary>For the model, one sentence: what it does and when to use it.</summary>
    string Description { get; }

    /// <summary>JSON schema of its parameters.</summary>
    string ParametersSchemaJson { get; }

    /// <summary>Phrases that ask for it, in the user's language (to choose it by meaning, stage 3.5).</summary>
    IReadOnlyList<string> Examples { get; }

    ToolRisk Risk { get; }

    /// <summary>The gesture it asks of the assistant (a <c>GestureKind</c> name, «communicate»…), or null.</summary>
    string? Gesture => null;

    /// <summary>What it is about to do with these arguments, for people («poner un temporizador de 10 min»): to ask and for the activity.</summary>
    string Intent(JsonElement arguments);

    Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken);

    /// <summary>Undoes what <see cref="ToolResult.Undo"/> describes; false when it could not (already gone…).</summary>
    Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>What a tool gives back: the text for the model, what it did in words for the activity, and how to undo it.</summary>
/// <param name="Text">The result for the model.</param>
public sealed record ToolResult(string Text)
{
    /// <summary>What it did, for the activity («Temporizador para las 17:42»); null: its intent.</summary>
    public string? Done { get; init; }

    /// <summary>How to undo it, or null when it cannot be undone.</summary>
    public ToolUndo? Undo { get; init; }

    /// <summary>It could not do it (something was missing…): the activity shows it failed.</summary>
    public bool Failed { get; init; }

    public static implicit operator ToolResult(string text) => new(text);

    public static ToolResult Fail(string text) => new(text) { Failed = true };
}

/// <summary>What a tool needs to undo something later (kept by the activity, also across restarts).</summary>
/// <param name="Data">The tool's own data (an id…).</param>
public sealed record ToolUndo(string Tool, string Data);

/// <summary>Who a tool works for and what the host lends it.</summary>
/// <param name="Assistant">The assistant it works for; null: the module without a body.</param>
public sealed record ToolContext(string? Assistant);

/// <summary>The tools that exist (built in now; JSON and MCP ones later, stage 9).</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public void Add(ITool tool) => _tools[tool.Name] = tool;

    public ITool? Find(string name) => _tools.GetValueOrDefault(name);

    public IReadOnlyCollection<ITool> All => _tools.Values;
}

/// <summary>Reading the arguments the model sends (it may send numbers as text or text as numbers).</summary>
public static class ToolArguments
{
    public static string? GetString(this JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.ToString(),
            }
            : null;

    public static double? GetNumber(this JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Number ? value.GetDouble()
            : value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString()?.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n
            : null
            : null;

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
