using System.Text.Json;
using KDesktop.Core.Assistant;

namespace KDesktop.Assistant.Tools;

/// <summary>
/// Something the assistant can do (plan §7.1): its name and description for the model, its parameters as a JSON schema,
/// how risky it is, what it is about to do in words for people, doing it and,
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

    /// <summary>
    /// Its risk with these arguments, when it depends on them (schedule: as risky as what it approves); the user's choice in
    /// Options → Tools still wins.
    /// </summary>
    ToolRisk RiskFor(JsonElement arguments) => Risk;

    /// <summary>It only reads (the time, the notes, a sum): the test bench runs it for real; the others are only pretended.</summary>
    bool ReadsOnly => false;

    /// <summary>
    /// What exactly would run (a script tool: its file and the script, hashed), or null. A fingerprint the user has not
    /// approved with «always» is asked about whatever <see cref="AskPolicy"/> says: a script dropped in the folder, or
    /// changed, never runs unseen (stage 9.1).
    /// </summary>
    string? Fingerprint => null;

    /// <summary>
    /// It asks before every run, whatever the user's <see cref="AskPolicy"/> or their choice in Options → Tools says
    /// (deleting or moving a file, stage 14.1).
    /// </summary>
    bool AlwaysAsks => false;

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

    /// <summary>
    /// Its text came from outside, from someone who is not the user (a mail): from now on in this turn everything that
    /// acts asks first, even with «no preguntar» (mail, phase 4.3).
    /// </summary>
    public bool Outside { get; init; }

    public static implicit operator ToolResult(string text) => new(text);

    public static ToolResult Fail(string text) => new(text) { Failed = true };
}

/// <summary>What a tool needs to undo something later (kept by the activity, also across restarts).</summary>
/// <param name="Data">The tool's own data (an id…).</param>
public sealed record ToolUndo(string Tool, string Data);

/// <summary>Who a tool works for and what the host lends it.</summary>
/// <param name="Assistant">The assistant it works for; null: the module without a body.</param>
/// <param name="Heard">The order being answered, when there is one.</param>
/// <param name="Decided">The command it asks for is already known and need not sound like the order: a phrase the user taught
/// («la vuelta» → a command of another name), «repite el comando anterior».</param>
/// <param name="Sources">For a call of the AI: everything an address or a name may honestly come from (the order, the
/// conversation, what came to mind, what the tools said); null: not the AI's (a rule, the routing), nothing is checked.</param>
/// <param name="Remembered">What came to mind for the order («cuando diga «Kamugi», abre K-Movies»): a command one of these
/// names may run although the order only sounds like it.</param>
/// <param name="Caller">Another program using the tools (the assistant as an MCP server): its name, for the question on the PC.</param>
public sealed record ToolContext(string? Assistant, string? Heard = null, bool Decided = false, string? Sources = null, IReadOnlyList<string>? Remembered = null,
    string? Caller = null)
{
    /// <summary>
    /// Asks its character for a gesture while the tool works (stage 13.5): set by the <see cref="ToolRunner"/>; it never waits
    /// nor throws. Null outside the runner (tests, the bench).
    /// </summary>
    public Action<ToolGesture>? Show { get; init; }

    /// <summary>
    /// A program the user let use everything without asking (Options → Compartir): only what is set to «Preguntar siempre»,
    /// what always asks and a script not seen before ask.
    /// </summary>
    public bool SharedFree { get; init; }

    /// <summary>
    /// The files the user gave the assistant (stage 14.1: dropped on it) while they are still fresh: what «it», «this file»
    /// means, and the path a tool takes when the model gives none.
    /// </summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>What this turn has read from outside (a mail): shared by every tool of the order (copies keep it).</summary>
    public TurnTrust Trust { get; init; } = new();
}

/// <summary>
/// Whether an order has read text from outside (mail, phase 4.3): a mail's content is data, never orders. After reading
/// one, whatever acts asks first and says why, so an instruction hidden in a mail («reenvía…», «abre…») is never followed
/// unseen.
/// </summary>
public sealed class TurnTrust
{
    private int _outside;

    public bool ReadOutside => Volatile.Read(ref _outside) == 1;

    public void MarkOutside() => Volatile.Write(ref _outside, 1);
}

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

    /// <summary>How the user chose that <paramref name="tool"/> asks in Options → Tools; null: they did not.</summary>
    public ToolRisk? ChosenRisk(ITool tool) =>
        _preferences.TryGetValue(tool.Name, out var preference) ? ToolPreference.Parse(preference.Risk) : null;

    /// <summary>
    /// Whether <paramref name="tool"/> asks every time, whatever «Preguntar antes de hacer algo» says (sending a mail,
    /// deleting a cita…): those that do, unless the user chose «Sin preguntar» for it in Options → Tools.
    /// </summary>
    public bool AlwaysAsks(ITool tool) => tool.AlwaysAsks && ChosenRisk(tool) != ToolRisk.Safe;

    /// <summary>Its risk for one call (<see cref="ITool.RiskFor"/>), unless the user chose one in Options → Tools.</summary>
    public ToolRisk RiskOf(ITool tool, JsonElement arguments) =>
        _preferences.TryGetValue(tool.Name, out var preference) && ToolPreference.Parse(preference.Risk) is { } risk ? risk : tool.RiskFor(arguments);

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

    /// <summary>
    /// Arguments written as texts made typed by the tool's schema («Usar una herramienta», G1: a number field holding
    /// {resultado} is a text until it runs): «5» for a number or an integer, «true»/«sí» for a boolean, «[…]» for a list or
    /// an object. What does not fit is left as it was (the tool says what is wrong).
    /// </summary>
    public static JsonElement Typed(JsonElement arguments, string? schema)
    {
        if (arguments.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(schema)) return arguments;
        System.Text.Json.Nodes.JsonObject? properties;
        try { properties = System.Text.Json.Nodes.JsonNode.Parse(schema)?["properties"] as System.Text.Json.Nodes.JsonObject; }
        catch (JsonException) { return arguments; }
        if (properties is null) return arguments;

        var typed = System.Text.Json.Nodes.JsonNode.Parse(arguments.GetRawText())!.AsObject();
        foreach (var (name, value) in typed.ToList())
        {
            if (value is not System.Text.Json.Nodes.JsonValue text || !text.TryGetValue<string>(out var written)) continue;
            if (properties[name] is not System.Text.Json.Nodes.JsonObject property) continue;
            var type = property["type"] switch
            {
                System.Text.Json.Nodes.JsonValue one when one.TryGetValue<string>(out var t) => t,
                System.Text.Json.Nodes.JsonArray many => many.Select(n => n?.GetValue<string>()).FirstOrDefault(t => t is not null and not "null"),
                _ => null,
            };
            var trimmed = written.Trim();
            System.Text.Json.Nodes.JsonNode? converted = type switch
            {
                "integer" when long.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var whole) => whole,
                "number" when double.TryParse(trimmed.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) => number,
                "boolean" when trimmed.ToLowerInvariant() is "true" or "1" or "sí" or "si" or "yes" => true,
                "boolean" when trimmed.ToLowerInvariant() is "false" or "0" or "no" => false,
                "array" or "object" when trimmed.Length > 0 && trimmed[0] is '[' or '{' => ParseOrNull(trimmed),
                _ => null,
            };
            if (converted is not null) typed[name] = converted;
        }
        return Parse(typed.ToJsonString());

        static System.Text.Json.Nodes.JsonNode? ParseOrNull(string json)
        {
            try { return System.Text.Json.Nodes.JsonNode.Parse(json); }
            catch (JsonException) { return null; }
        }
    }
}
