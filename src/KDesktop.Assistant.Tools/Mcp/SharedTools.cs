using System.Collections.Concurrent;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant.Sharing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SdkServer = ModelContextProtocol.Server.McpServer;

namespace KDesktop.Assistant.Tools.Mcp;

/// <summary>A program using the tools now (Options → Tools → «Compartir»).</summary>
public sealed record SharedClient(string Name, DateTimeOffset Since, int Calls);

/// <summary>
/// The assistants' tools as an MCP server for other programs (phase 12; see <see cref="McpShare"/>). The built-in ones and
/// the user's JSON tools, switched on, never those of the user's own MCP servers (the program can talk to them itself); with
/// <see cref="McpShare.ScopeRead"/> only those that only read. Each call goes through the <see cref="ToolRunner"/> with the
/// route <see cref="McpShare.Route"/>: what does not only read asks on the PC first, whatever the user's policy, and
/// everything is in the activity under the program's name. One per process; each connection is served on its own.
/// </summary>
public sealed class SharedTools(ToolRunner runner)
{
    private readonly ConcurrentDictionary<int, SharedClient> _clients = new();
    private readonly ConcurrentDictionary<int, SdkServer> _servers = new();
    private int _next;

    /// <summary><see cref="McpShare.ScopeRead"/>, <see cref="McpShare.ScopeAll"/> or <see cref="McpShare.ScopeFree"/>.</summary>
    public string Scope { get; set; } = McpShare.ScopeRead;

    /// <summary>Who is connected changed, or a call was made.</summary>
    public event Action? Changed;

    public IReadOnlyList<SharedClient> Clients => [.. _clients.Values.OrderBy(c => c.Since)];

    /// <summary>The tools a program may see now.</summary>
    public IReadOnlyList<ITool> Shared =>
        [.. runner.Registry.All.Where(t => runner.Registry.GroupOf(t.Name) is not { } g || !g.StartsWith(McpServers.GroupPrefix, StringComparison.Ordinal))
            .Where(t => Scope is McpShare.ScopeAll or McpShare.ScopeFree || t.ReadsOnly)];

    /// <summary>The tools changed (or what is shared): the connected programs are told to list them again.</summary>
    public void NotifyChanged()
    {
        foreach (var server in _servers.Values)
            _ = server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, CancellationToken.None)
                .ContinueWith(_ => { }, TaskScheduler.Default);
    }

    /// <summary>Serves one program on <paramref name="stream"/> (already past the key) until it goes.</summary>
    public async Task ServeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _next);
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "k-desktop", Title = "K-Desktop", Version = AppPaths.FullVersion },
            ServerInstructions = "Tools of K-Desktop on the user's Windows PC: the time, timers, notes, calculations, files, the screen, the web, "
                + "the PC's state, the music playing and the user's own K-Desktop commands. Actions that change something are confirmed by the user on the PC.",
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [.. Shared.Select(Describe)] }),
                CallToolHandler = (request, token) => CallAsync(id, request.Server.ClientInfo, request.Params, token),
            },
        };
        var transport = new StreamServerTransport(stream, stream, "k-desktop");
        await using var server = SdkServer.Create(transport, options);
        _servers[id] = server;
        _clients[id] = new SharedClient("?", DateTimeOffset.Now, 0);
        Changed?.Invoke();
        try
        {
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            _servers.TryRemove(id, out _);
            if (_clients.TryRemove(id, out var gone)) Log.Info($"Asistente: «{gone.Name}» deja de usar las herramientas ({gone.Calls} llamadas)");
            Changed?.Invoke();
        }
    }

    private static Tool Describe(ITool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = ToolArguments.Parse(tool.ParametersSchemaJson),
        Annotations = new ToolAnnotations { ReadOnlyHint = tool.ReadsOnly },
    };

    private async ValueTask<CallToolResult> CallAsync(int id, Implementation? client, CallToolRequestParams? call, CancellationToken cancellationToken)
    {
        var name = client?.Title ?? client?.Name ?? "?";
        if (_clients.TryGetValue(id, out var known))
        {
            if (known.Calls == 0) Log.Info($"Asistente: «{name}» usa las herramientas ({Scope})");
            _clients[id] = known with { Name = name, Calls = known.Calls + 1 };
        }
        Changed?.Invoke();

        var tool = call?.Name ?? string.Empty;
        if (!Shared.Any(t => t.Name == tool))
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = $"No shared tool called «{tool}»." }] };

        var arguments = JsonSerializer.SerializeToElement(call!.Arguments ?? new Dictionary<string, JsonElement>());
        var outcome = await runner.RunAsync(tool, arguments, new ToolContext(null, Caller: name) { SharedFree = Scope == McpShare.ScopeFree }, name, McpShare.Route, cancellationToken).ConfigureAwait(false);
        var text = outcome.Result?.Text ?? outcome.Entry.Did;
        return new CallToolResult
        {
            IsError = outcome.Result is null or { Failed: true },
            Content = [new TextContentBlock { Text = text }],
        };
    }
}
