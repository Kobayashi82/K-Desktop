using System.Text.Json;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Declarative;

/// <summary>How a tool file is.</summary>
/// <param name="Name">Its tool's name, when it could be read.</param>
/// <param name="Errors">What is wrong with it (empty: it works, or is switched off).</param>
public sealed record JsonToolFile(string File, string? Name, string? Type, bool Enabled, IReadOnlyList<string> Errors);

/// <summary>
/// The JSON tools (stage 9.1): every <c>*.json</c> of <c>&lt;data&gt;\Assistant\Tools</c> is a tool, checked against
/// the schema (tool.schema.json) and put in the registry as the group <see cref="Group"/>. Saving, adding or removing a
/// file there (or a script) loads them all again a moment later; a file that breaks is told about (<see cref="Broken"/>).
/// </summary>
public sealed class JsonTools : IDisposable
{
    public const string Group = "json";

    /// <summary>How long it waits after the last change in the folder (an editor saves in several writes).</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private static readonly string[] Watched = [".json", ".ps1", ".bat", ".cmd", ".py", ".js", ".exe"];

    private readonly object _gate = new();
    private readonly ToolRegistry _registry;
    private readonly HttpClient? _ownHttp;
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private IReadOnlyList<JsonToolFile> _files = [];
    private bool _started;

    /// <param name="folder">The folder of the tool files.</param>
    /// <param name="commands">K-Desktop's commands (kdesktop tools, routines); null: those fail.</param>
    /// <param name="http">For the http tools; null: its own.</param>
    public JsonTools(string folder, ToolRegistry registry, ToolSecrets? secrets, ICommandBridge? commands, HttpClient? http = null)
    {
        Folder = folder;
        _registry = registry;
        if (http is null)
        {
            _ownHttp = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }) { Timeout = Timeout.InfiniteTimeSpan };
            _ownHttp.DefaultRequestHeaders.UserAgent.ParseAdd("K-Desktop/5");
        }
        Environment = new JsonToolEnvironment(folder, secrets, commands, http ?? _ownHttp!);
    }

    public string Folder { get; }

    public JsonToolEnvironment Environment { get; }

    /// <summary>Set once the runner exists: the routines run their steps with it.</summary>
    public ToolRunner? Runner
    {
        get => Environment.Runner;
        set => Environment.Runner = value;
    }

    /// <summary>Every file of the folder, as it was last loaded.</summary>
    public IReadOnlyList<JsonToolFile> Files => _files;

    /// <summary>A file changed and now has errors (not at the first load: those are only written in the log).</summary>
    public event Action<JsonToolFile>? Broken;

    /// <summary>A load finished (the tools in the registry are the new ones).</summary>
    public event Action? Loaded;

    /// <summary>Loads them and watches the folder (once; again does nothing).</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }
        try { Directory.CreateDirectory(Folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido crear {Folder}: {ex.Message}");
            return;
        }
        Reload(quiet: true);
        _timer = new Timer(_ => Reload(quiet: false));
        _watcher = new FileSystemWatcher(Folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!Watched.Contains(Path.GetExtension(e.FullPath), StringComparer.OrdinalIgnoreCase)
            && !(e is RenamedEventArgs renamed && Watched.Contains(Path.GetExtension(renamed.OldFullPath), StringComparer.OrdinalIgnoreCase))) return;
        _timer?.Change(Settle, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Reads every file again and puts the tools that work in the registry.</summary>
    /// <param name="quiet">Without <see cref="Broken"/> (the first load).</param>
    public void Reload(bool quiet = false)
    {
        List<JsonToolFile> files;
        lock (_gate)
        {
            var tools = new List<JsonTool>();
            files = [];
            string[] paths;
            try { paths = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json") : []; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Asistente: no se puede leer {Folder}: {ex.Message}");
                paths = [];
            }
            foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
                files.Add(Load(path, tools));
            _registry.SetGroup(Group, tools);

            var before = _files.ToDictionary(f => f.File, StringComparer.OrdinalIgnoreCase);
            _files = files;
            Log.Info($"Asistente: herramientas JSON: {tools.Count}" + (tools.Count > 0 ? $" ({string.Join(", ", tools.Select(t => t.Name))})" : string.Empty)
                + string.Concat(files.Where(f => f.Errors.Count > 0).Select(f => $" · {Path.GetFileName(f.File)}: {f.Errors[0]}")));
            if (!quiet)
                foreach (var file in files.Where(f => f.Errors.Count > 0))
                    if (!before.TryGetValue(file.File, out var was) || !was.Errors.SequenceEqual(file.Errors)) Broken?.Invoke(file);
        }
        Loaded?.Invoke();
    }

    private JsonToolFile Load(string path, List<JsonTool> tools)
    {
        string text;
        try { text = ReadShared(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JsonToolFile(path, null, null, false, [ex.Message]);
        }

        var errors = PackageSchemas.Validate("tool", text);
        if (errors.Count > 0) return new JsonToolFile(path, null, null, false, errors);
        JsonToolDefinition definition;
        try { definition = JsonSerializer.Deserialize<JsonToolDefinition>(text, JsonToolDefinition.Options)!; }
        catch (JsonException ex) { return new JsonToolFile(path, null, null, false, [ex.Message]); }

        var problems = new List<string>();
        var name = definition.Name;
        if (_registry.GroupOf(name) is { } group && group != Group) problems.Add(Loc.T("tools.json.taken", name));
        else if (tools.FirstOrDefault(t => t.Name == name) is { } twin) problems.Add(Loc.T("tools.json.duplicate", name, Path.GetFileName(twin.File)));
        foreach (var required in definition.Required ?? [])
            if (definition.Parameters?.ContainsKey(required) != true) problems.Add(Loc.T("tools.json.noParameter", required));
        if (definition.Type == "routine" && definition.Steps!.Any(s => s.Tool == name)) problems.Add(Loc.T("tools.json.selfStep"));

        var tool = problems.Count == 0 ? JsonTool.Create(definition, path, Environment) : null;
        if (tool is ScriptJsonTool script && !File.Exists(script.ScriptPath)) problems.Add(Loc.T("tools.json.noScript", script.ScriptPath));
        if (problems.Count == 0 && definition.Enabled) tools.Add(tool!);
        return new JsonToolFile(path, name, definition.Type, definition.Enabled, problems);
    }

    /// <summary>The file's text even while an editor holds it open (a few tries: it may be mid-save).</summary>
    private static string ReadShared(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer?.Dispose();
        _ownHttp?.Dispose();
    }
}
