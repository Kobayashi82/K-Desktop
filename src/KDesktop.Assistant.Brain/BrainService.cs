using KDesktop.Assistant.Brain.Models;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain;

/// <summary>
/// The brain of the assistant process (stage 3.1; its own llama-swap since 5.0.107), one for all the assistants: the models
/// of this PC and of the other servers of Options → Assistant → Brain (<see cref="BrainHub"/>), the one in use, and what
/// Options asks of it (look again, try a server, free the graphics card, load now). Each change goes out as a
/// <see cref="BrainStateMessage"/>. Thread-safe; the work runs on the thread pool.
/// </summary>
public sealed class BrainService : IDisposable
{
    private readonly object _gate = new();
    private readonly HttpClient _http;
    private readonly Func<double?> _videoMemory;
    private readonly SemaphoreSlim _checking = new(1, 1);
    private BrainConfig _config = BrainConfig.Default;
    private readonly LlamaServerPool _pool;
    private BrainHub? _hub;
    private BrainStateMessage _state = new("checking");
    private int _generation;
    /// <summary>The <see cref="_generation"/> last looked at: looking again at the same sources does not say «checking».</summary>
    private int _checkedGeneration = -1;
    private bool _disposed;

    /// <param name="videoMemory">The graphics card's own memory in GB (null: unknown), to know which models fit.</param>
    /// <param name="network">What answers the HTTP requests (tests put a fake server here).</param>
    public BrainService(Func<double?> videoMemory, HttpMessageHandler? network = null)
    {
        _videoMemory = videoMemory;
        _http = network is null ? new HttpClient() : new HttpClient(network);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _pool = new LlamaServerPool([], [], null, null, true, true, [], videoMemory, _http);
        _pool.Changed += OnServerChanged;
    }

    /// <summary>Raised (any thread) with the new state after every change.</summary>
    public event Action<BrainStateMessage>? Changed;

    public BrainStateMessage State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>All the models as one (null without AI). Do not dispose it.</summary>
    public IChatModel? Client
    {
        get { lock (_gate) return _config.IsOff ? null : _hub; }
    }

    /// <summary>The model to talk with now, or null while the brain is not ready.</summary>
    public string? Model
    {
        get { lock (_gate) return _state.State == "ready" ? _state.Model : null; }
    }

    /// <summary>
    /// The model an assistant talks with (stage 3.2): its own when there is such a model; otherwise the general one. No
    /// assistant is K-Desktop (5.0.112), whose own model goes under its id. Null when neither is there.
    /// </summary>
    public string? ModelFor(string? assistant)
    {
        lock (_gate)
        {
            if (_config.IsOff) return null;
            if (_config.AssistantModels.TryGetValue(assistant ?? AssistantTargeting.KDesktop, out var own)
                && ModelRefs.Find(_state.Models?.Select(m => m.Name) ?? [], own) is { } found)
                return found;
            return _state.State == "ready" ? _state.Model : null;
        }
    }

    /// <summary>
    /// How an assistant thinks harder (stage 12): the model of «Para pensar más» if the servers have it, otherwise its usual
    /// one with its reasoning on; null when it never does (or there is no AI now).
    /// </summary>
    public Talk.Thinker? ThinkerFor(string? assistant)
    {
        lock (_gate)
        {
            if (_config.IsOff || _config.ThinkOff || ModelFor(assistant) is not { } usual) return null;
            var model = _config.ThinkModel.Length > 0 && ModelRefs.Find(_state.Models?.Select(m => m.Name) ?? [], _config.ThinkModel) is { } found ? found : usual;
            return new Talk.Thinker(model, Math.Max(BrainConfig.LeastThinkTokens, _config.ThinkTokens));
        }
    }

    /// <summary>Options changed (or the process got its welcome): new sources if they changed, and a look at them.</summary>
    public void Configure(BrainConfig config)
    {
        BrainHub? old = null;
        lock (_gate)
        {
            if (_disposed) return;
            var sameSources = _hub is not null && config.SourcesKey == _config.SourcesKey
                // A chosen .gguf outside the folders is listed too.
                && ChosenFiles(config).SequenceEqual(ChosenFiles(_config), StringComparer.OrdinalIgnoreCase);
            var sameModel = sameSources && config.Model == _config.Model && config.ThinkModel == _config.ThinkModel;
            _config = config;
            // Only the assistants' own models changed: nothing to look at again.
            if (sameModel) return;
            if (!sameSources)
            {
                old = _hub;
                _pool.Configure(config.ServerList, config.FolderList, config.ModelArgList, config.KeepMinutes > 0 ? TimeSpan.FromMinutes(config.KeepMinutes) : null,
                    config.OneAtATime, config.Vision, config.AssistantModels.Values.Append(config.Model).Append(config.ThinkModel));
                _hub = new BrainHub(config, _pool, _http);
                _generation++;
            }
        }
        old?.Dispose();
        Log.Info($"Asistente: cerebro → {config.ServerList.Count} llama-server ({string.Join(", ", config.ServerList.Select(LlamaServerPool.ServerName))}), " +
                 $"{config.FolderList.Count} carpetas de modelos, {config.RemoteList.Count} servidores más{(config.Model.Length > 0 ? $", modelo {BrainProviders.ModelLabel(config.Model)}" : "")}" +
                 $"{(config.OneAtATime ? "" : ", varios modelos a la vez")}");
        _ = WaitForServersAsync();
    }

    private static IEnumerable<string> ChosenFiles(BrainConfig config) =>
        config.AssistantModels.Values.Append(config.Model).Append(config.ThinkModel).Where(ModelRefs.IsLocal).Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>A server that is not there yet is looked at again this often…</summary>
    private static readonly TimeSpan AwayEvery = TimeSpan.FromSeconds(10);

    /// <summary>…for this long after the options arrive (Ollama opens from the Startup folder, after K-Desktop).</summary>
    private static readonly TimeSpan AwayFor = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Looks at everything, and while another server does not answer, again every <see cref="AwayEvery"/> for
    /// <see cref="AwayFor"/>: K-Desktop starts with Windows before the servers of the Startup folder.
    /// </summary>
    private async Task WaitForServersAsync()
    {
        int generation;
        lock (_gate) generation = _generation;
        await CheckAsync().ConfigureAwait(false);
        var until = DateTime.UtcNow + AwayFor;
        while (State.Remotes?.Any(r => r.State == "unreachable") == true && DateTime.UtcNow < until)
        {
            await Task.Delay(AwayEvery).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || generation != _generation) return;
            }
            var was = State.Remotes?.Count(r => r.State == "ok") ?? 0;
            await CheckAsync().ConfigureAwait(false);
            if ((State.Remotes?.Count(r => r.State == "ok") ?? 0) > was) Log.Info("Asistente: otro servidor de modelos ya contesta");
        }
    }

    /// <summary>Not looked at again before this long since the last look that found nothing.</summary>
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(3);

    private DateTime _lastRecheck = DateTime.MinValue;

    /// <summary>
    /// An order needs the AI and the brain is not ready: one more look now (a server may have opened since), at most
    /// once every <see cref="RecheckAfter"/>. Whether it is ready afterwards.
    /// </summary>
    public async Task<bool> RecheckAsync()
    {
        if (State.State == "ready") return true;
        lock (_gate)
        {
            if (_disposed || _hub is null || _config.IsOff || DateTime.UtcNow - _lastRecheck < RecheckAfter) return false;
            _lastRecheck = DateTime.UtcNow;
        }
        await CheckAsync().ConfigureAwait(false);
        var ready = State.State == "ready";
        Log.Info($"Asistente: cerebro mirado otra vez para una orden: {(ready ? "listo" : State.State)}");
        return ready;
    }

    /// <summary>A request of Options → Assistant → Brain.</summary>
    public Task HandleAsync(BrainMessage message) => message.Op switch
    {
        "check" => CheckAsync(),
        "rescan" => RescanAsync(),
        "test" when message.Remote is { Length: > 0 } remote => TestAsync(remote),
        "unload" when message.Model is { Length: > 0 } model => UnloadOneAsync(model),
        "unload" => UnloadAsync(),
        "load" => LoadAsync(message.Model),
        _ => Task.CompletedTask,
    };

    /// <summary>A llama-server started loading, loaded or stopped: the state says so.</summary>
    private void OnServerChanged() => _ = CheckAsync();

    private Task RescanAsync()
    {
        lock (_gate) _hub?.Local.Rescan();
        return CheckAsync();
    }

    private async Task TestAsync(string remote)
    {
        BrainHub? hub;
        lock (_gate) hub = _hub;
        // Not there yet: the options that bring it are on their way, and they look at it anyway.
        if (hub is null || hub.Config.RemoteList.All(r => r.Id != remote)) return;
        Set(s => s with { Remotes = [.. (s.Remotes ?? hub.RemoteStates).Select(r => r.Id == remote ? new BrainRemoteState(remote, "checking") : r)] });
        await hub.TestAsync(remote, CancellationToken.None).ConfigureAwait(false);
        Log.Info($"Asistente: servidor {remote} probado: {hub.RemoteStates.FirstOrDefault(r => r.Id == remote)?.State}");
        await CheckAsync().ConfigureAwait(false);
    }

    /// <summary>Looks at every source: the models, which one is used and which are loaded.</summary>
    public async Task CheckAsync()
    {
        BrainConfig config;
        BrainHub? hub;
        int generation;
        lock (_gate)
        {
            if (_disposed) return;
            (config, hub, generation) = (_config, _hub, _generation);
        }
        if (hub is null) return;

        await _checking.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _checkedGeneration)) Set(s => s with { State = "checking" });
            var next = await LookAsync(config, hub).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation || _disposed) return;
                _checkedGeneration = generation;
            }
            Set(_ => next);
        }
        finally
        {
            _checking.Release();
        }
    }

    private async Task<BrainStateMessage> LookAsync(BrainConfig config, BrainHub hub)
    {
        var video = _videoMemory();
        IReadOnlyList<ModelInfo> models;
        IReadOnlyList<string> loaded;
        try
        {
            models = await hub.ListModelsAsync(CancellationToken.None).ConfigureAwait(false);
            loaded = await hub.ListLoadedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ChatModelException or IOException or UnauthorizedAccessException)
        {
            return new BrainStateMessage("error", Message: ex.Message, VideoMemoryGb: video, Remotes: hub.RemoteStates);
        }

        var local = hub.Local.Known;
        var list = models.Select(m =>
        {
            var found = local.FirstOrDefault(l => string.Equals(l.Path, m.Name, StringComparison.OrdinalIgnoreCase));
            return new BrainModel(m.Name, Math.Round(ModelPicker.SizeOf(m), 2), m.SizeBytes, m.SupportsTools, m.Capabilities.Contains("vision"),
                found?.Server, ModelRefs.SplitRemote(m.Name)?.Remote);
        }).ToList();
        var servers = config.ServerList.Select(s => new BrainServerState(s, File.Exists(s), local.Count(l => string.Equals(l.Server, s, StringComparison.OrdinalIgnoreCase)))).ToList();
        var empty = new BrainStateMessage("ready", Models: list, Loaded: loaded, Loading: hub.Local.Loading, VideoMemoryGb: video, Servers: servers, Remotes: hub.RemoteStates);

        if (config.IsOff) return empty with { State = "off" };
        if (list.Count == 0) return empty with { State = "noModels" };
        if (config.Model.Length > 0)
        {
            return ModelRefs.Find(list.Select(m => m.Name), config.Model) is { } chosen
                ? empty with { Model = chosen }
                : empty with { State = "modelMissing", Model = config.Model, Message = Loc.T("brain.error.modelMissing", BrainProviders.ModelLabel(config.Model)) };
        }
        return empty with { Model = Automatic(models, local, video).Name, Automatic = true };
    }

    /// <summary>Video memory left for Windows and the rest when a model is chosen by itself.</summary>
    private const double SpareVideoGb = 3;

    /// <summary>
    /// The model taken by itself: of this PC, the biggest that fits in the graphics card with room for the rest —whoever
    /// keeps a big model wants that one, the smartest; none fits: the smallest—; with none here, Kobii's rule over the
    /// other servers' (about 4 B, with tools).
    /// </summary>
    private static ModelInfo Automatic(IReadOnlyList<ModelInfo> models, IReadOnlyList<LocalModel> local, double? videoGb)
    {
        var mine = models.Where(m => ModelRefs.IsLocal(m.Name)).ToList();
        if (mine.Count == 0) return ModelPicker.Choose(models, null)!;
        var room = videoGb is { } gb ? (gb - SpareVideoGb) * 1e9 : double.MaxValue;
        long Needs(ModelInfo m) => m.SizeBytes + (local.FirstOrDefault(l => l.Path == m.Name)?.ProjectorSize ?? 0);
        return mine.Where(m => Needs(m) <= room).OrderByDescending(Needs).FirstOrDefault() ?? mine.OrderBy(Needs).First();
    }

    /// <summary>
    /// Unloads everything loaded (also a model still loading): Options, the action «Asistente: cerebro» and «descarga el
    /// cerebro». Whether something was loaded (or loading).
    /// </summary>
    public async Task<bool> UnloadAsync()
    {
        BrainHub? hub;
        lock (_gate) hub = _hub;
        if (hub is null) return false;
        var loaded = await hub.ListLoadedAsync(CancellationToken.None).ConfigureAwait(false);
        var any = loaded.Count > 0 || hub.Local.Loading is not null;
        hub.Local.StopAll("pedido");
        foreach (var model in loaded.Where(m => !ModelRefs.IsLocal(m)))
        {
            try
            {
                await hub.UnloadAsync(model, CancellationToken.None).ConfigureAwait(false);
                Log.Info($"Asistente: modelo {BrainProviders.ModelLabel(model)} liberado");
            }
            catch (Exception ex) when (ex is ChatModelException or HttpRequestException)
            {
                Log.Warn($"Asistente: no se ha podido liberar {BrainProviders.ModelLabel(model)}: {ex.Message}");
            }
        }
        await CheckAsync().ConfigureAwait(false);
        return any;
    }

    /// <summary>Takes one model out of memory (its «Liberar» in Options).</summary>
    private async Task UnloadOneAsync(string model)
    {
        BrainHub? hub;
        lock (_gate) hub = _hub;
        if (hub is null) return;
        try
        {
            await hub.UnloadAsync(model, CancellationToken.None).ConfigureAwait(false);
            Log.Info($"Asistente: modelo {BrainProviders.ModelLabel(model)} liberado");
        }
        catch (Exception ex) when (ex is ChatModelException or HttpRequestException)
        {
            Log.Warn($"Asistente: no se ha podido liberar {BrainProviders.ModelLabel(model)}: {ex.Message}");
        }
        await CheckAsync().ConfigureAwait(false);
    }

    /// <summary>Loads a model now (the one in use when none is said): Options, the action. The first answer does not wait for it.</summary>
    public async Task LoadAsync(string? model = null)
    {
        BrainHub? hub;
        lock (_gate) hub = _config.IsOff ? null : _hub;
        model = model is { Length: > 0 } wanted ? ModelRefs.Find(State.Models?.Select(m => m.Name) ?? [], wanted) ?? wanted : Model;
        if (hub is null || model is null) return;
        Log.Info($"Asistente: cargando {BrainProviders.ModelLabel(model)} a petición");
        try
        {
            using var limit = new CancellationTokenSource(LlamaServerPool.StartPatience);
            await hub.PreloadAsync(model, limit.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ChatModelException or OperationCanceledException or HttpRequestException)
        {
            Log.Warn($"Asistente: no se ha podido cargar {BrainProviders.ModelLabel(model)}: {ex.Message}");
            Set(s => s with { Message = ex.Message });
            return;
        }
        await CheckAsync().ConfigureAwait(false);
    }

    private void Set(Func<BrainStateMessage, BrainStateMessage> change)
    {
        BrainStateMessage next;
        lock (_gate)
        {
            if (_disposed) return;
            next = change(_state);
            if (Same(next, _state)) return;
            _state = next;
        }
        Changed?.Invoke(next);
    }

    /// <summary>Records with lists compare the lists by reference: the same state is the same JSON.</summary>
    private static bool Same(BrainStateMessage a, BrainStateMessage b) =>
        a == b || PipeJson.Serialize(a) == PipeJson.Serialize(b);

    public void Dispose()
    {
        BrainHub? hub;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            hub = _hub;
            _hub = null;
        }
        hub?.Dispose();
        _pool.Dispose();
        _http.Dispose();
    }
}
