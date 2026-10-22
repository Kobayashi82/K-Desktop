using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using ModelContextProtocol.Client;

namespace KDesktop.Assistant.Tools.Mcp;

/// <summary>
/// The MCP servers of <c>Assistant\mcp.json</c> (stage 9.2): each server's tools go in the registry as the group
/// <c>mcp:&lt;id&gt;</c>, named <c>mcp_&lt;servidor&gt;_&lt;herramienta&gt;</c> (letters, digits and _: OpenAI-compatible
/// servers refuse dots). What each server has is kept in <c>Assistant\mcp-cache.json</c>, so choosing the 3–5 tools for
/// an order never starts a server: only calling one does. A server never listed (new, or changed) is started once in
/// the background to learn its tools, and stopped again. Saving <c>mcp.json</c> reloads them. Thread-safe.
/// </summary>
public sealed partial class McpServers : IDisposable
{
    public const string GroupPrefix = "mcp:";

    /// <summary>The longest name a model takes for a tool.</summary>
    private const int MaxName = 64;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _configFile;
    private readonly string _cacheFile;
    private readonly string _logFolder;
    private readonly ToolRegistry _registry;
    private readonly ToolSecrets? _secrets;
    private readonly HttpClient _http;
    private readonly Dictionary<string, McpServer> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _learning = new(1, 1);
    private Dictionary<string, CachedServer> _cache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _started;

    /// <param name="configFile"><c>Assistant\mcp.json</c>.</param>
    /// <param name="logFolder">Where each server writes its <c>mcp-&lt;id&gt;.log</c>.</param>
    public McpServers(string configFile, string cacheFile, string logFolder, ToolRegistry registry, ToolSecrets? secrets, HttpClient? http = null)
    {
        _configFile = configFile;
        _cacheFile = cacheFile;
        _logFolder = logFolder;
        _registry = registry;
        _secrets = secrets;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>What is wrong with <c>mcp.json</c> (empty: nothing).</summary>
    public IReadOnlyList<string> Errors { get; private set; } = [];

    /// <summary>A server started, stopped or failed, its tools changed, or <c>mcp.json</c> was loaded again.</summary>
    public event Action? Changed;

    /// <summary>The servers, in the order of <c>mcp.json</c>.</summary>
    public IReadOnlyList<McpServer> Servers
    {
        get
        {
            lock (_gate) return [.. _servers.Values];
        }
    }

    /// <summary>The tools of <paramref name="server"/> it knows (empty: not listed yet).</summary>
    public IReadOnlyList<McpToolInfo> ToolsOf(string server)
    {
        lock (_gate) return _cache.TryGetValue(server, out var cached) ? cached.Tools : [];
    }

    /// <summary>Loads them and watches <c>mcp.json</c> (once; again does nothing).</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }
        _cache = ReadCache();
        Reload();
        var folder = Path.GetDirectoryName(_configFile)!;
        try
        {
            Directory.CreateDirectory(folder);
            _timer = new Timer(_ => Reload());
            _watcher = new FileSystemWatcher(folder, Path.GetFileName(_configFile))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => _timer.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (_, _) => _timer.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"Asistente: no se puede vigilar {_configFile}: {ex.Message}");
        }
    }

    /// <summary>Reads <c>mcp.json</c> again: new servers come in, changed ones start over, removed ones stop.</summary>
    public void Reload()
    {
        var entries = McpConfig.Read(_configFile, out var errors);
        Errors = errors;
        if (errors.Count > 0) Log.Warn($"Asistente: {Path.GetFileName(_configFile)} tiene errores: {errors[0]}");
        List<McpServer> dispose = [];
        List<McpServer> stop = [];
        List<McpServer> toLearn = [];
        lock (_gate)
        {
            var wanted = entries.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var (id, server) in _servers.ToList())
                if (!wanted.TryGetValue(id, out var entry) || entry.Key != server.Entry.Key)
                {
                    dispose.Add(server);
                    _servers.Remove(id);
                }
            var ordered = new Dictionary<string, McpServer>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (_servers.TryGetValue(entry.Id, out var kept)) kept.Entry = entry;
                else
                {
                    kept = new McpServer(entry, _secrets, _http, _logFolder);
                    kept.Changed += _ => Changed?.Invoke();
                    kept.Listed += OnListed;
                }
                ordered[entry.Id] = kept;
                if (entry.Disabled) stop.Add(kept);
                else if (!_cache.TryGetValue(entry.Id, out var cached) || cached.Key != entry.Key) toLearn.Add(kept);
            }
            _servers.Clear();
            foreach (var (id, server) in ordered) _servers[id] = server;
            Register();
        }
        foreach (var server in stop) _ = server.StopAsync();
        foreach (var server in dispose) _ = server.DisposeAsync().AsTask();
        Log.Info($"Asistente: servidores MCP: {entries.Count}" + (entries.Count > 0
            ? " (" + string.Join(", ", entries.Select(e => e.Id + (e.Disabled ? " apagado" : $" {ToolsOf(e.Id).Count}"))) + ")" : string.Empty));
        Changed?.Invoke();
        foreach (var server in toLearn) _ = LearnAsync(server);
    }

    /// <summary>A server never listed: started once to learn its tools, and stopped again if nothing else used it.</summary>
    private async Task LearnAsync(McpServer server)
    {
        await _learning.WaitAsync().ConfigureAwait(false);
        try
        {
            if (server.Entry.Disabled || server.State == McpRunState.Running) return;
            Log.Info($"Asistente: servidor MCP {server.Id}: arranca para saber sus herramientas");
            await server.ListAsync(CancellationToken.None).ConfigureAwait(false);
            await server.StopIfUnusedAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is McpServerException or ObjectDisposedException) { }
        finally
        {
            _learning.Release();
        }
    }

    /// <summary>Starts <paramref name="id"/> (or checks it answers) and says what went wrong, or null (Options → Test).</summary>
    public async Task<string?> TestAsync(string id, CancellationToken cancellationToken)
    {
        if (Find(id) is not { } server) return Loc.T("tools.mcp.unknown", id);
        try
        {
            await server.ListAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (McpServerException ex) { return ex.Message; }
        catch (ModelContextProtocol.McpException ex) { return Mask(ex.Message); }
    }

    public Task StopAsync(string id) => Find(id)?.StopAsync() ?? Task.CompletedTask;

    internal async Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(string id, string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        if (Find(id) is not { } server || server.Entry.Disabled) throw new McpServerException(Loc.T("tools.mcp.unknown", id));
        return await server.CallAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
    }

    internal string Mask(string text) => _secrets?.Mask(text) ?? text;

    private McpServer? Find(string id)
    {
        lock (_gate) return _servers.GetValueOrDefault(id);
    }

    private void OnListed(McpServer server, IList<McpClientTool> tools)
    {
        var infos = new List<McpToolInfo>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            var protocol = tool.ProtocolTool;
            var name = NameFor(server.Id, protocol.Name);
            // Two that end up alike once cleaned or cut: the second gets a number.
            var unique = name;
            for (var i = 2; !names.Add(unique); i++) unique = name[..Math.Min(MaxName - 3, name.Length)] + "_" + i;
            name = unique;
            var description = (protocol.Description ?? protocol.Title ?? protocol.Name).Trim();
            if (description.Length > 700) description = description[..700].TrimEnd() + "…";
            infos.Add(new McpToolInfo(name, protocol.Name, description, McpTool.SchemaOf(protocol.InputSchema), protocol.Annotations?.ReadOnlyHint == true));
        }
        lock (_gate)
        {
            _cache[server.Id] = new CachedServer(server.Entry.Key, infos, DateTimeOffset.Now);
            Register();
        }
        WriteCache();
        Changed?.Invoke();
    }

    /// <summary>Puts each enabled server's known tools in the registry (and takes away the rest).</summary>
    private void Register()
    {
        foreach (var group in _registry.Groups.Where(g => g.StartsWith(GroupPrefix, StringComparison.Ordinal)).ToList())
            if (!_servers.TryGetValue(group[GroupPrefix.Length..], out var server) || server.Entry.Disabled)
                _registry.SetGroup(group, []);
        foreach (var server in _servers.Values)
        {
            var tools = !server.Entry.Disabled && _cache.TryGetValue(server.Id, out var cached) && cached.Key == server.Entry.Key
                ? cached.Tools.Select(t => (ITool)new McpTool(this, server.Id, t)).ToList()
                : [];
            _registry.SetGroup(GroupPrefix + server.Id, tools);
        }
    }

    /// <summary><c>mcp_&lt;servidor&gt;_&lt;herramienta&gt;</c>, only lower-case letters, digits and _, 64 at most.</summary>
    public static string NameFor(string server, string tool)
    {
        var name = "mcp_" + Clean(server) + "_" + Clean(tool);
        return name.Length <= MaxName ? name : name[..MaxName].TrimEnd('_');

        static string Clean(string text) => Underscores().Replace(NotName().Replace(text.ToLowerInvariant(), "_"), "_").Trim('_');
    }

    private Dictionary<string, CachedServer> ReadCache()
    {
        try
        {
            if (File.Exists(_cacheFile))
                return new Dictionary<string, CachedServer>(
                    JsonSerializer.Deserialize<Dictionary<string, CachedServer>>(File.ReadAllText(_cacheFile), Json) ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido leer {_cacheFile}: {ex.Message}");
        }
        return new Dictionary<string, CachedServer>(StringComparer.OrdinalIgnoreCase);
    }

    private void WriteCache()
    {
        string json;
        lock (_gate) json = JsonSerializer.Serialize(_cache, Json);
        try { File.WriteAllText(_cacheFile, json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar {_cacheFile}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer?.Dispose();
        foreach (var server in Servers)
        {
            try { server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4)); }
            catch (AggregateException) { }
        }
    }

    /// <param name="Key">The <see cref="McpServerEntry.Key"/> it had when listed.</param>
    private sealed record CachedServer(string Key, IReadOnlyList<McpToolInfo> Tools, DateTimeOffset At);

    [GeneratedRegex("[^a-z0-9_]")]
    private static partial Regex NotName();

    [GeneratedRegex("_{2,}")]
    private static partial Regex Underscores();
}
