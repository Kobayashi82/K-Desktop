using System.IO;
using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop;

/// <summary>
/// Options → Tools from K-Desktop's side (stage 9.3): what the assistant process says it has (<c>toolsState</c>), testing
/// a tool or a server (<c>tools</c> → <c>toolTest</c>), the user's choices for each tool (<c>assistantToolSettings</c>, in
/// the <c>tools</c> section of <c>config</c>), and the files the process watches, which K-Desktop writes: the MCP
/// servers (<c>Assistant\mcp.json</c>, also imported from another program's) and the secrets (<c>Assistant\secrets.json</c>,
/// encrypted). Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    private readonly Dictionary<string, ToolTestMessage> _toolTests = new(StringComparer.Ordinal);
    private ToolSecrets? _toolSecrets;

    /// <summary>What the process last said it has (null until it does, or with the process stopped).</summary>
    public ToolsStateMessage? AssistantTools { get; private set; }

    /// <summary>Raised (UI thread) when the tools change, a test ends or the process goes away.</summary>
    public event Action? AssistantToolsChanged;

    /// <summary>The last test of each tool or server (by name), this session.</summary>
    public IReadOnlyDictionary<string, ToolTestMessage> AssistantToolTests => _toolTests;

    /// <summary>The tests sent and not answered yet.</summary>
    public HashSet<string> AssistantToolsTesting { get; } = new(StringComparer.Ordinal);

    public static string AssistantFolder => Path.Combine(AppPaths.DataDir, "Assistant");

    /// <summary>Where the JSON tools are.</summary>
    public static string ToolsFolder => Path.Combine(AssistantFolder, "Tools");

    public static string McpFile => Path.Combine(AssistantFolder, "mcp.json");

    public ToolSecrets ToolSecrets => _toolSecrets ??= new ToolSecrets(Path.Combine(AssistantFolder, "secrets.json"));

    public bool RequestAssistantTools() => SendTools(new ToolsMessage("list"));

    /// <summary>Runs a tool as an assistant would (<paramref name="arguments"/> as JSON); the answer comes as <c>toolTest</c>.</summary>
    public bool TestAssistantTool(string name, string? arguments)
    {
        if (!SendTools(new ToolsMessage("test", name, arguments))) return false;
        AssistantToolsTesting.Add(name);
        AssistantToolsChanged?.Invoke();
        return true;
    }

    /// <summary>Starts an MCP server to see that it works (the answer comes as <c>toolTest</c>).</summary>
    public bool StartMcpServer(string id)
    {
        if (!SendTools(new ToolsMessage("start", id))) return false;
        AssistantToolsTesting.Add(id);
        AssistantToolsChanged?.Invoke();
        return true;
    }

    public bool StopMcpServer(string id) => SendTools(new ToolsMessage("stop", id));

    private bool SendTools(ToolsMessage message) => _assistant is { IsRunning: true } supervisor && supervisor.Send(message);

    private void OnAssistantTools(ToolsStateMessage state)
    {
        AssistantTools = state;
        AssistantToolsChanged?.Invoke();
    }

    private void OnAssistantToolTest(ToolTestMessage test)
    {
        _toolTests[test.Name] = test;
        AssistantToolsTesting.Remove(test.Name);
        AssistantToolsChanged?.Invoke();
    }

    /// <summary>The process went away: what it sent no longer holds.</summary>
    private void ForgetAssistantTools()
    {
        if (AssistantTools is null && AssistantToolsTesting.Count == 0) return;
        AssistantTools = null;
        AssistantToolsTesting.Clear();
        AssistantToolsChanged?.Invoke();
    }

    // ── the user's choices for each tool ─────────────────────────────────

    public ToolPreference PreferenceOf(string tool) =>
        Config.Settings.AssistantToolSettings.GetValueOrDefault(tool) ?? new ToolPreference();

    /// <summary>Switches a tool on or off for every assistant.</summary>
    public void SetToolEnabled(string tool, bool enabled) => SetPreference(tool, PreferenceOf(tool) with { Enabled = enabled });

    /// <summary>How a tool asks (<c>safe</c>, <c>askOnce</c>, <c>askAlways</c>; null: as it says).</summary>
    public void SetToolRisk(string tool, string? risk) => SetPreference(tool, PreferenceOf(tool) with { Risk = risk });

    private void SetPreference(string tool, ToolPreference preference)
    {
        var all = new Dictionary<string, ToolPreference>(Config.Settings.AssistantToolSettings, StringComparer.Ordinal);
        if (preference == new ToolPreference()) all.Remove(tool);
        else all[tool] = preference;
        Config.Settings.AssistantToolSettings = all;
    }

    // ── MCP servers (Assistant\mcp.json) ─────────────────────────────────

    /// <summary>The servers of <c>mcp.json</c>, and what is wrong with it.</summary>
    public static IReadOnlyList<McpServerEntry> ReadMcpServers(out IReadOnlyList<string> errors) => McpConfig.Read(McpFile, out errors);

    /// <summary>
    /// Saves a server (new, or <paramref name="replacing"/> the one with that id, kept in its place) and the secrets its
    /// fields hold (name → value; empty: left as it was). The process sees the file change and starts over with it.
    /// </summary>
    public void SaveMcpServer(McpServerEntry entry, string? replacing, IReadOnlyDictionary<string, string>? secrets = null)
    {
        foreach (var (name, value) in secrets ?? new Dictionary<string, string>())
            if (value.Length > 0) ToolSecrets.Set(name, value);
        var servers = ReadMcpServers(out _).ToList();
        var at = servers.FindIndex(s => string.Equals(s.Id, replacing ?? entry.Id, StringComparison.OrdinalIgnoreCase));
        // Renamed onto another server's name: that one is replaced (the editor asked first).
        servers = [.. servers.Where((s, i) => i == at || !string.Equals(s.Id, entry.Id, StringComparison.OrdinalIgnoreCase))];
        at = servers.FindIndex(s => string.Equals(s.Id, replacing ?? entry.Id, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) servers[at] = entry;
        else servers.Add(entry);
        WriteMcp(servers);
        Log.Info($"Asistente: servidor MCP {entry.Id} guardado ({entry.Summary})");
    }

    /// <summary>Switches a server on or off (its tools come and go with it).</summary>
    public static void SetMcpServerDisabled(string id, bool disabled)
    {
        var servers = ReadMcpServers(out _).ToList();
        var at = servers.FindIndex(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return;
        var raw = servers[at].Raw.DeepClone().AsObject();
        if (disabled) raw["disabled"] = true;
        else raw.Remove("disabled");
        servers[at] = servers[at] with { Raw = raw };
        WriteMcp(servers);
    }

    /// <summary>Removes a server, and the secrets that were only its own (<c>&lt;id&gt;.…</c>).</summary>
    public void RemoveMcpServer(string id)
    {
        var servers = ReadMcpServers(out _).ToList();
        if (servers.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) == 0) return;
        WriteMcp(servers);
        foreach (var name in ToolSecrets.Names.Where(n => n.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase)).ToList())
            ToolSecrets.Set(name, null);
        Log.Info($"Asistente: servidor MCP {id} quitado");
    }

    /// <summary>Brings in servers of another program's file (Options → Tools → Import), each with its secrets encrypted.</summary>
    public int ImportMcpServers(IEnumerable<McpImportCandidate> chosen)
    {
        var servers = ReadMcpServers(out _).ToList();
        var count = 0;
        foreach (var candidate in chosen)
        {
            foreach (var (name, value) in candidate.Secrets) ToolSecrets.Set(name, value);
            servers.RemoveAll(s => string.Equals(s.Id, candidate.Entry.Id, StringComparison.OrdinalIgnoreCase));
            servers.Add(candidate.Entry);
            count++;
        }
        if (count > 0) WriteMcp(servers);
        Log.Info($"Asistente: {count} servidor(es) MCP importado(s)");
        return count;
    }

    private static void WriteMcp(IEnumerable<McpServerEntry> servers)
    {
        try { McpConfig.Write(McpFile, servers); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar {McpFile}: {ex.Message}");
        }
    }

    // ── secrets ──────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps a secret (empty: removes it). The MCP servers that use it and are running stop, so the next call starts them
    /// with the new value; the JSON tools read it again by themselves.
    /// </summary>
    public void SetToolSecret(string name, string? value)
    {
        ToolSecrets.Set(name, value);
        foreach (var server in AssistantTools?.Servers ?? [])
            if (server.State == "running" && server.Secrets.Contains(name, StringComparer.OrdinalIgnoreCase)) StopMcpServer(server.Id);
        AssistantToolsChanged?.Invoke();
    }

    /// <summary>Every secret: those kept and those some tool or server names, with who uses each.</summary>
    public IReadOnlyList<(string Name, bool Kept, IReadOnlyList<string> UsedBy)> ToolSecretList()
    {
        var users = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string who)
        {
            if (!users.TryGetValue(name, out var list)) users[name] = list = [];
            if (!list.Contains(who)) list.Add(who);
        }
        foreach (var file in AssistantTools?.Files ?? [])
            foreach (var name in file.Secrets) Add(name, file.Name ?? Path.GetFileName(file.File));
        foreach (var server in ReadMcpServers(out _))
            foreach (var name in server.SecretNames) Add(name, server.Id);
        var kept = ToolSecrets.Names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in kept) users.TryAdd(name, []);
        return [.. users.OrderBy(u => u.Key, StringComparer.OrdinalIgnoreCase)
            .Select(u => (u.Key, kept.Contains(u.Key) && ToolSecrets.Get(u.Key) is not null, (IReadOnlyList<string>)u.Value))];
    }

    // ── JSON tool files ──────────────────────────────────────────────────

    /// <summary>Opens the folder of the JSON tools (made if it is not there), as the normal user.</summary>
    public static void OpenToolsFolder()
    {
        Directory.CreateDirectory(ToolsFolder);
        ProcessLauncher.Launch(new LaunchSpec(ToolsFolder), asAdmin: false);
    }

    /// <summary>Opens a tool's file with the program the user has for it.</summary>
    public static void OpenToolFile(string file) => ProcessLauncher.Launch(new LaunchSpec(file), asAdmin: false);

    /// <summary>A new JSON tool of <paramref name="type"/> from its template, with a free name; opened to be filled in.</summary>
    public static string NewToolFromTemplate(string type)
    {
        Directory.CreateDirectory(ToolsFolder);
        var stem = "nueva_" + type;
        var name = stem;
        for (var i = 2; File.Exists(Path.Combine(ToolsFolder, name + ".json")); i++) name = stem + "_" + i;
        var file = Path.Combine(ToolsFolder, name + ".json");
        File.WriteAllText(file, ToolTemplates.For(type, name));
        if (type == "script")
        {
            var script = Path.Combine(ToolsFolder, name + ".ps1");
            if (!File.Exists(script)) File.WriteAllText(script, ToolTemplates.Script, new System.Text.UTF8Encoding(true));
        }
        OpenToolFile(file);
        return file;
    }
}

/// <summary>The starting point of a new JSON tool of each type (guide in docs/assistant/herramientas.md).</summary>
internal static class ToolTemplates
{
    private const string Schema = "https://raw.githubusercontent.com/Kobayashi82/K-Desktop/main/docs/assistant/esquemas/tool.schema.json";

    public static string For(string type, string name) => (type switch
    {
        "kdesktop" => """
            {
              "$schema": "SCHEMA",
              "name": "NAME",
              "type": "kdesktop",
              "description": "What it does, for the model (one sentence).",
              "examples": ["una frase que la pida", "otra frase"],
              "command": "El nombre exacto de uno de tus comandos",
              "say": { "ok": "Hecho." }
            }
            """,
        "routine" => """
            {
              "$schema": "SCHEMA",
              "name": "NAME",
              "type": "routine",
              "description": "What it does, for the model (one sentence).",
              "examples": ["una frase que la pida"],
              "steps": [
                { "command": "El nombre de uno de tus comandos" },
                { "wait": 1 },
                { "tool": "get_time" }
              ],
              "say": { "ok": "Hecho.", "error": "Algo no ha salido." }
            }
            """,
        "script" => """
            {
              "$schema": "SCHEMA",
              "name": "NAME",
              "type": "script",
              "description": "What it does, for the model (one sentence).",
              "examples": ["una frase que la pida"],
              "parameters": { "texto": { "type": "string", "description": "What the script gets" } },
              "script": { "file": "NAME.ps1", "args": ["{{texto}}"] },
              "readsOnly": true
            }
            """,
        _ => """
            {
              "$schema": "SCHEMA",
              "name": "NAME",
              "type": "http",
              "description": "What it does, for the model (one sentence).",
              "examples": ["una frase que la pida"],
              "parameters": { "estado": { "type": "string", "enum": ["on", "off"] } },
              "request": {
                "method": "POST",
                "url": "http://homeassistant.local:8123/api/services/light/turn_{{estado}}",
                "headers": { "Authorization": "Bearer {{secret:ha_token}}" },
                "body": { "entity_id": "light.salon" }
              },
              "say": { "ok": "Hecho: {{estado}}.", "error": "No responde." }
            }
            """,
    }).Replace("SCHEMA", Schema, StringComparison.Ordinal).Replace("NAME", name, StringComparison.Ordinal);

    public const string Script = """
        param([string]$Texto)
        # Lo que escribas aquí es lo que recibe el asistente.
        Write-Output "Recibido: $Texto"
        """;
}
