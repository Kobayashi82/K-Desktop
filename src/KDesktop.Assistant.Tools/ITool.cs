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

    /// <summary>It only reads (the time, the notes, a sum): the test bench runs it for real; the others are only pretended.</summary>
    bool ReadsOnly => false;

    /// <summary>
    /// What exactly would run (a script tool: its file and the script, hashed), or null. A fingerprint the user has not
    /// approved with «always» is asked about whatever <see cref="AskPolicy"/> says: a script dropped in the folder, or
    /// changed, never runs unseen (stage 9.1).
    /// </summary>
    string? Fingerprint => null;

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

    /// <summary>
    /// What the assistant says when it was asked without the AI (a fixed rule, stage 3.4) or the AI says nothing, in the
    /// user's language («Te aviso a las 17:42.»); null: the assistant's «ok».
    /// </summary>
    public string? Reply { get; init; }

    /// <summary>How to undo it, or null when it cannot be undone.</summary>
    public ToolUndo? Undo { get; init; }

    /// <summary>It did something (a command ran) even if it cannot be undone: its reply says exactly what.</summary>
    public bool Acted { get; init; }

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
/// <param name="Heard">The order being answered, when there is one.</param>
public sealed record ToolContext(string? Assistant, string? Heard = null);

/// <summary>
/// The tools that exist, in groups: the built-in ones (<see cref="Builtin"/>), the JSON ones (stage 9.1) and each MCP
/// server's (9.2), which come and go while the process runs. What the user switched off in Options → Tools is not
/// found nor listed (<see cref="Everything"/> and <see cref="FindAny"/> still see it), and their choice of risk wins over
/// the tool's (<see cref="RiskOf"/>). Readers get a snapshot; thread-safe.
/// </summary>
public sealed class ToolRegistry
{
    /// <summary>The group of the tools added with <see cref="Add"/>.</summary>
    public const string Builtin = "builtin";

    private readonly object _gate = new();
    private readonly List<(string Group, List<ITool> Tools)> _groups = [];
    private volatile Snapshot _snapshot = new([], [], new Dictionary<string, (ITool, string)>(StringComparer.Ordinal));
    private IReadOnlyDictionary<string, ToolPreference> _preferences = new Dictionary<string, ToolPreference>();

    /// <summary>A group changed (JSON tools reloaded, an MCP server's tools listed).</summary>
    public event Action? Changed;

    public void Add(ITool tool)
    {
        lock (_gate)
        {
            var tools = GroupList(Builtin);
            tools.RemoveAll(t => t.Name == tool.Name);
            tools.Add(tool);
            Rebuild();
        }
    }

    /// <summary>
    /// Puts <paramref name="tools"/> as the whole of <paramref name="group"/> (an empty list removes it). A name already
    /// taken by another group is left out: the first one keeps it.
    /// </summary>
    public void SetGroup(string group, IEnumerable<ITool> tools)
    {
        lock (_gate)
        {
            var list = GroupList(group);
            list.Clear();
            list.AddRange(tools);
            if (list.Count == 0) _groups.RemoveAll(g => g.Group == group);
            Rebuild();
        }
        Changed?.Invoke();
    }

    /// <summary>The tool called <paramref name="name"/>, unless there is none or the user switched it off.</summary>
    public ITool? Find(string name) => FindAny(name) is { } tool && IsEnabled(tool.Name) ? tool : null;

    /// <summary>The tool called <paramref name="name"/>, also when switched off (Options).</summary>
    public ITool? FindAny(string name) => _snapshot.ByName.TryGetValue(name, out var found) ? found.Tool : null;

    /// <summary>The user's choices for each tool (Options → Tools): switched off, or another risk.</summary>
    public void SetPreferences(IReadOnlyDictionary<string, ToolPreference> preferences)
    {
        lock (_gate)
        {
            _preferences = preferences;
            Rebuild();
        }
        Changed?.Invoke();
    }

    public bool IsEnabled(string name) => !_preferences.TryGetValue(name, out var preference) || preference.Enabled;

    /// <summary>How <paramref name="tool"/> asks: the user's choice, or its own.</summary>
    public ToolRisk RiskOf(ITool tool) =>
        _preferences.TryGetValue(tool.Name, out var preference) && ToolPreference.Parse(preference.Risk) is { } risk ? risk : tool.Risk;

    /// <summary>The group of <paramref name="name"/>, or null when there is no such tool.</summary>
    public string? GroupOf(string name) => _snapshot.ByName.TryGetValue(name, out var found) ? found.Group : null;

    /// <summary>The groups there are now.</summary>
    public IReadOnlyList<string> Groups
    {
        get
        {
            lock (_gate) return [.. _groups.Select(g => g.Group)];
        }
    }

    /// <summary>All of them that are switched on: the built-in ones first, then each group in the order it came.</summary>
    public IReadOnlyCollection<ITool> All => _snapshot.All;

    /// <summary>All of them, also those switched off.</summary>
    public IReadOnlyCollection<ITool> Everything => _snapshot.Everything;

    private List<ITool> GroupList(string group)
    {
        foreach (var (name, tools) in _groups)
            if (name == group) return tools;
        var created = new List<ITool>();
        if (group == Builtin) _groups.Insert(0, (group, created));
        else _groups.Add((group, created));
        return created;
    }

    private void Rebuild()
    {
        var byName = new Dictionary<string, (ITool, string)>(StringComparer.Ordinal);
        var all = new List<ITool>();
        foreach (var (group, tools) in _groups)
            foreach (var tool in tools)
                if (byName.TryAdd(tool.Name, (tool, group))) all.Add(tool);
        _snapshot = new Snapshot([.. all.Where(t => IsEnabled(t.Name))], all, byName);
    }

    private sealed record Snapshot(IReadOnlyCollection<ITool> All, IReadOnlyCollection<ITool> Everything, Dictionary<string, (ITool Tool, string Group)> ByName);
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
