using System.Diagnostics;
using System.Runtime.CompilerServices;
using KDesktop.Core;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>
/// All the models of the brain as one (5.0.107): the <c>.gguf</c> of this PC (<see cref="LlamaServerPool"/>) and those of
/// the other servers of Options, each named by its <see cref="ModelRefs">reference</see> —so two servers with a model of
/// the same name never get mixed up— and each request sent where its model is. With one model at a time, switching
/// models also unloads what the other servers of this PC have loaded.
/// </summary>
public sealed class BrainHub : IChatModel
{
    /// <summary>Longest wait for another server to list its models.</summary>
    public static readonly TimeSpan ListPatience = TimeSpan.FromSeconds(4);

    private readonly object _gate = new();
    private readonly Dictionary<string, Remote> _remotes;
    private readonly Dictionary<string, BrainRemoteState> _states = new(StringComparer.Ordinal);
    private string? _lastModel;

    /// <param name="local">The llama-servers of this PC, already following <paramref name="config"/>; it outlives the hub (its models go on when Options change).</param>
    public BrainHub(BrainConfig config, LlamaServerPool local, HttpClient http)
    {
        Config = config;
        Local = local;
        _remotes = config.RemoteList
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToDictionary(r => r.Id, r => new Remote(r, Create(r, config.KeepMinutes, http)), StringComparer.Ordinal);
    }

    public BrainConfig Config { get; }

    public LlamaServerPool Local { get; }

    /// <summary>How each other server answered the last time it was asked.</summary>
    public IReadOnlyList<BrainRemoteState> RemoteStates
    {
        get
        {
            lock (_gate) return [.. Config.RemoteList.Select(r => _states.GetValueOrDefault(r.Id) ?? new BrainRemoteState(r.Id, "checking"))];
        }
    }

    private static IChatModel Create(BrainRemote remote, int keepMinutes, HttpClient http) => remote.Kind switch
    {
        RemoteKind.Ollama => new OllamaChatModel(remote.Address, http) { KeepAlive = keepMinutes > 0 ? $"{keepMinutes}m" : "-1" },
        RemoteKind.LlamaSwap => new OpenAICompatibleChatModel(remote.Address, remote.ApiKey, http) { UnloadPath = "unload", RunningPath = "running" },
        _ => new OpenAICompatibleChatModel(remote.Address, remote.ApiKey, http) { DetectLlamaSwap = true },
    };

    /// <summary>Whether that server says what it has loaded and unloads (Ollama, and an OpenAI one that turned out to be a llama-swap).</summary>
    private static bool CanUnload(Remote remote) => remote.Client switch
    {
        OllamaChatModel => true,
        OpenAICompatibleChatModel openai => openai.CanUnload,
        _ => false,
    };

    /// <summary>The local models, then those of each other server that answers (a server that does not answer lists none).</summary>
    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        var local = Local.ListModelsAsync(cancellationToken);
        var remote = _remotes.Values.Select(r => ListAsync(r, cancellationToken)).ToList();
        var all = new List<ModelInfo>(await local.ConfigureAwait(false));
        foreach (var list in await Task.WhenAll(remote).ConfigureAwait(false)) all.AddRange(list);
        return all;
    }

    /// <summary>Asks one other server now (Options → «Probar»).</summary>
    public async Task TestAsync(string remoteId, CancellationToken cancellationToken)
    {
        if (_remotes.GetValueOrDefault(remoteId) is { } remote) await ListAsync(remote, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ModelInfo>> ListAsync(Remote remote, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ListPatience);
            var models = await remote.Client.ListModelsAsync(timeout.Token).ConfigureAwait(false);
            var chat = models.Where(m => !m.IsEmbeddingOnly).Select(m => m with { Name = ModelRefs.Remote(remote.Settings.Id, m.Name) }).ToList();
            SetState(new BrainRemoteState(remote.Settings.Id, "ok", chat.Count, watch.ElapsedMilliseconds));
            return chat;
        }
        catch (ChatModelException ex)
        {
            var state = ex.Failure is ChatModelFailure.Unreachable or ChatModelFailure.Timeout ? "unreachable" : "error";
            SetState(new BrainRemoteState(remote.Settings.Id, state, 0, watch.ElapsedMilliseconds, ex.Message));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetState(new BrainRemoteState(remote.Settings.Id, "unreachable", 0, watch.ElapsedMilliseconds, Loc.T("brain.error.timeout")));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        {
            SetState(new BrainRemoteState(remote.Settings.Id, "error", 0, watch.ElapsedMilliseconds, ex.Message));
        }
        return [];
    }

    private void SetState(BrainRemoteState state)
    {
        lock (_gate) _states[state.Id] = state;
    }

    /// <summary>What is loaded: the local ones and what the other servers that say it have.</summary>
    public async Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken)
    {
        var loaded = new List<string>(await Local.ListLoadedAsync(cancellationToken).ConfigureAwait(false));
        foreach (var remote in _remotes.Values.Where(r => CanUnload(r) && LastStateOk(r)))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ListPatience);
                loaded.AddRange((await remote.Client.ListLoadedAsync(timeout.Token).ConfigureAwait(false)).Select(m => ModelRefs.Remote(remote.Settings.Id, m)));
            }
            catch (Exception ex) when (ex is ChatModelException or OperationCanceledException or HttpRequestException)
            {
                // It does not say now.
            }
        }
        return loaded;
    }

    public async Task<bool?> IsLoadedAsync(string model, CancellationToken cancellationToken)
    {
        if (ModelRefs.IsLocal(model)) return await Local.IsLoadedAsync(model, cancellationToken).ConfigureAwait(false);
        if (ModelRefs.SplitRemote(model) is not { } named || _remotes.GetValueOrDefault(named.Remote) is not { } remote || !LastStateOk(remote)) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ListPatience);
            return await remote.Client.IsLoadedAsync(named.Model, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ChatModelException or OperationCanceledException or HttpRequestException)
        {
            return null;
        }
    }

    private bool LastStateOk(Remote remote)
    {
        lock (_gate) return _states.GetValueOrDefault(remote.Settings.Id) is { State: "ok" };
    }

    public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (client, model) = Route(request.Model);
        await SwitchingAsync(request.Model, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in client.StreamAsync(request with { Model = model }, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    public async Task<IReadOnlyDictionary<char, double>?> LettersAsync(string model, IReadOnlyList<ChatMessage> messages, string letters, CancellationToken cancellationToken)
    {
        var (client, name) = Route(model);
        await SwitchingAsync(model, cancellationToken).ConfigureAwait(false);
        return await client.LettersAsync(name, messages, letters, cancellationToken).ConfigureAwait(false);
    }

    public async Task PreloadAsync(string model, CancellationToken cancellationToken)
    {
        var (client, name) = Route(model);
        await SwitchingAsync(model, cancellationToken).ConfigureAwait(false);
        await client.PreloadAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public Task UnloadAsync(string model, CancellationToken cancellationToken)
    {
        var (client, name) = Route(model);
        return client.UnloadAsync(name, cancellationToken);
    }

    /// <summary>Who has a model and its name there.</summary>
    private (IChatModel Client, string Model) Route(string model)
    {
        if (ModelRefs.IsLocal(model)) return (Local, model);
        if (ModelRefs.SplitRemote(model) is { } remote && _remotes.GetValueOrDefault(remote.Remote) is { } server) return (server.Client, remote.Model);
        throw new ChatModelException(ChatModelFailure.ModelNotFound, Loc.T("brain.error.modelUnavailable", ModelRefs.Label(model)));
    }

    /// <summary>
    /// One model at a time, across servers: before talking with another model than the last one, what the other servers of
    /// this PC have loaded leaves the graphics card (llama-server's own make room by themselves).
    /// </summary>
    private async Task SwitchingAsync(string model, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_lastModel is { } last && ModelRefs.Same(last, model)) return;
            _lastModel = model;
        }
        if (!Config.OneAtATime) return;
        if (!ModelRefs.IsLocal(model)) Local.StopAll("se usa un modelo de otro servidor (un modelo a la vez)");
        foreach (var remote in _remotes.Values.Where(r => CanUnload(r) && BrainProviders.PlaceOf(r.Settings.Address) == EndpointPlace.ThisPc))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                foreach (var loaded in await remote.Client.ListLoadedAsync(timeout.Token).ConfigureAwait(false))
                {
                    var reference = ModelRefs.Remote(remote.Settings.Id, loaded);
                    if (ModelRefs.Same(reference, model)) continue;
                    await remote.Client.UnloadAsync(loaded, timeout.Token).ConfigureAwait(false);
                    Log.Info($"Asistente: {loaded} liberado en {remote.Settings.Name} (un modelo a la vez)");
                }
            }
            catch (Exception ex) when (ex is ChatModelException or OperationCanceledException or HttpRequestException)
            {
                // That server does not answer now: nothing of it is loaded either.
            }
        }
    }

    /// <summary>The clients of the other servers (the llama-servers of this PC are not the hub's).</summary>
    public void Dispose()
    {
        foreach (var remote in _remotes.Values) remote.Client.Dispose();
    }

    private sealed record Remote(BrainRemote Settings, IChatModel Client);
}
