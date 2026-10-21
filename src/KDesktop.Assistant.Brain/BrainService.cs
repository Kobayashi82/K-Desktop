using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Setup;
using KDesktop.Core;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain;

/// <summary>
/// The brain of the assistant process (stage 3.1), one for all the assistants: the server chosen in Options → Assistant →
/// Brain, the model in use, and what Options asks of it (look again, look for servers, install Ollama, download a model,
/// free the graphics card). Each change goes out as a <see cref="BrainStateMessage"/>. Thread-safe; the work runs on the
/// thread pool.
/// </summary>
public sealed class BrainService : IDisposable
{
    /// <summary>Waiting for a just-opened Ollama to answer.</summary>
    private static readonly TimeSpan StartPatience = TimeSpan.FromSeconds(25);

    /// <summary>Download progress is sent at most this often.</summary>
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly HttpClient _http;
    private readonly Func<double?> _videoMemory;
    private readonly SemaphoreSlim _checking = new(1, 1);
    private BrainConfig _config = BrainConfig.Default;
    private IChatModel? _client;
    private BrainStateMessage _state;
    private CancellationTokenSource? _job;
    private int _generation;
    /// <summary>The <see cref="_generation"/> last looked at: looking again at the same server does not say «checking».</summary>
    private int _checkedGeneration = -1;
    private bool _disposed;

    /// <param name="videoMemory">The graphics card's own memory in GB (null: unknown), to recommend a model.</param>
    /// <param name="network">What answers the HTTP requests (tests put a fake server here).</param>
    public BrainService(Func<double?> videoMemory, HttpMessageHandler? network = null)
    {
        _videoMemory = videoMemory;
        _http = network is null ? new HttpClient() : new HttpClient(network);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _state = Empty(_config, "checking");
    }

    /// <summary>Raised (any thread) with the new state after every change.</summary>
    public event Action<BrainStateMessage>? Changed;

    public BrainStateMessage State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>The client of the server in use (null without AI). Do not dispose it.</summary>
    public IChatModel? Client
    {
        get { lock (_gate) return _client; }
    }

    /// <summary>The model to talk with now, or null while the brain is not ready.</summary>
    public string? Model
    {
        get { lock (_gate) return _state.State == "ready" ? _state.Model : null; }
    }

    /// <summary>
    /// The model an assistant talks with (stage 3.2): its own when it has one and the server has it; otherwise the general
    /// one (also with no assistant: the module without a body). Null when neither is there.
    /// </summary>
    public string? ModelFor(string? assistant)
    {
        lock (_gate)
        {
            if (assistant is not null && _config.AssistantModels.TryGetValue(assistant, out var own)
                && _state.Models?.FirstOrDefault(m => BrainProviders.SameModel(m.Name, own)) is { } found)
                return found.Name;
            return _state.State == "ready" ? _state.Model : null;
        }
    }

    /// <summary>Options changed (or the process got its welcome): a new client, and a look at it.</summary>
    public void Configure(BrainConfig config)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var sameServer = config.Provider == _config.Provider && BrainScout.SameEndpoint(config.Address, _config.Address) && config.ApiKey == _config.ApiKey;
            var sameModel = sameServer && config.Model == _config.Model && _client is not null;
            _config = config;
            // Only the assistants' own models changed: nothing to look at again.
            if (sameModel) return;
            if (!sameServer || _client is null)
            {
                _client?.Dispose();
                _client = ChatModels.Create(config, _http);
                _generation++;
            }
        }
        Log.Info($"Asistente: cerebro → {config.Provider.DisplayName()} {(config.Provider == BrainProvider.None ? "" : config.Address)}{(config.Model.Length > 0 ? $", modelo {config.Model}" : "")}");
        _ = WaitForServerAsync();
    }

    /// <summary>A server that is not there yet is looked at again this often…</summary>
    private static readonly TimeSpan AwayEvery = TimeSpan.FromSeconds(10);

    /// <summary>…for this long after the options arrive (Ollama opens from the Startup folder, after K-Desktop).</summary>
    private static readonly TimeSpan AwayFor = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Looks at the server, and while it does not answer, again every <see cref="AwayEvery"/> for <see cref="AwayFor"/>:
    /// K-Desktop starts with Windows before the Ollama of the Startup folder, and the first look found nothing.
    /// </summary>
    private async Task WaitForServerAsync()
    {
        int generation;
        lock (_gate) generation = _generation;
        await CheckAsync(scanIfMissing: true).ConfigureAwait(false);
        var until = DateTime.UtcNow + AwayFor;
        while (State.State == "unreachable" && DateTime.UtcNow < until)
        {
            await Task.Delay(AwayEvery).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || generation != _generation) return;
            }
            await CheckAsync(scanIfMissing: false).ConfigureAwait(false);
            if (State.State == "ready") Log.Info($"Asistente: el cerebro ya contesta ({State.Model})");
        }
    }

    /// <summary>Not looked at again before this long since the last look that found nothing.</summary>
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(3);

    private DateTime _lastRecheck = DateTime.MinValue;

    /// <summary>
    /// An order needs the AI and the brain is not ready: one more look now (the server may have opened since), at most
    /// once every <see cref="RecheckAfter"/>. Whether it is ready afterwards.
    /// </summary>
    public async Task<bool> RecheckAsync()
    {
        if (State.State == "ready") return true;
        lock (_gate)
        {
            if (_disposed || _client is null || DateTime.UtcNow - _lastRecheck < RecheckAfter) return false;
            _lastRecheck = DateTime.UtcNow;
        }
        await CheckAsync(scanIfMissing: false).ConfigureAwait(false);
        var ready = State.State == "ready";
        Log.Info($"Asistente: cerebro mirado otra vez para una orden: {(ready ? "listo" : State.State)}");
        return ready;
    }

    /// <summary>A request of Options → Assistant → Brain.</summary>
    public Task HandleAsync(BrainMessage message) => message.Op switch
    {
        "check" => CheckAsync(scanIfMissing: false),
        "scan" => ScanAsync(),
        "installOllama" => InstallOllamaAsync(),
        "startOllama" => StartOllamaAsync(),
        "downloadPage" => Task.Run(OllamaSetup.OpenDownloadPage),
        "pull" when message.Model is { Length: > 0 } model => PullAsync(model),
        "cancel" => Cancel(),
        "unload" => UnloadAsync(),
        _ => Task.CompletedTask,
    };

    /// <summary>
    /// Looks at the server: its models, which one is used and which are loaded. With <paramref name="scanIfMissing"/>, a
    /// server that is not there makes it look in the usual places (to offer another one it found).
    /// </summary>
    public async Task CheckAsync(bool scanIfMissing)
    {
        BrainConfig config;
        IChatModel? client;
        int generation;
        lock (_gate)
        {
            if (_disposed) return;
            (config, client, generation) = (_config, _client, _generation);
        }
        if (client is null)
        {
            Set(_ => Empty(config, "off"));
            return;
        }

        await _checking.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref _checkedGeneration)) Set(s => s with { State = "checking" });
            var next = await LookAsync(config, client).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation || _disposed) return;
                _checkedGeneration = generation;
            }
            Set(s => next with { Job = s.Job, Found = next.State == "ready" ? null : s.Found });
            if (scanIfMissing && next.State == "unreachable") await ScanAsync().ConfigureAwait(false);
        }
        finally
        {
            _checking.Release();
        }
    }

    private async Task<BrainStateMessage> LookAsync(BrainConfig config, IChatModel client)
    {
        var local = BrainProviders.PlaceOf(config.Address) == EndpointPlace.ThisPc && config.Provider == BrainProvider.Ollama;
        var ollama = local ? new BrainOllama(OllamaSetup.IsInstalled, OllamaSetup.CanInstall) : null;
        var video = _videoMemory();
        try
        {
            var models = await client.ListModelsAsync(CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<string> loaded;
            try { loaded = await client.ListLoadedAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (ChatModelException) { loaded = []; }

            var chat = ModelPicker.Chat(models).ToList();
            var list = chat.Select(m => new BrainModel(m.Name, Math.Round(ModelPicker.SizeOf(m), 2), m.SizeBytes, m.SupportsTools)).ToList();
            var suggested = config.Provider == BrainProvider.Ollama ? ModelAdvice.Recommend(video) : null;
            var empty = Empty(config, "ready") with { Models = list, Loaded = loaded, Ollama = ollama, VideoMemoryGb = video };
            if (chat.Count == 0) return empty with { State = "noModels", Suggested = suggested };
            if (config.Model.Length > 0)
            {
                return ModelPicker.Find(chat, config.Model) is { } chosen
                    ? empty with { Model = chosen.Name }
                    : empty with { State = "modelMissing", Model = config.Model, Message = Loc.T("brain.error.modelMissing", config.Model) };
            }
            var picked = ModelPicker.Choose(chat, null)!;
            return empty with { Model = picked.Name, Automatic = true, Suggested = picked.SupportsTools ? null : suggested };
        }
        catch (ChatModelException ex)
        {
            var state = ex.Failure is ChatModelFailure.Unreachable or ChatModelFailure.Timeout ? "unreachable" : "error";
            return Empty(config, state) with { Message = ex.Message, Ollama = ollama, VideoMemoryGb = video, Suggested = local ? ModelAdvice.Recommend(video) : null };
        }
    }

    /// <summary>Looks for servers in the usual places of this PC (not the one in use).</summary>
    public async Task ScanAsync()
    {
        BrainConfig config;
        lock (_gate) config = _config;
        var found = await BrainScout.ScanAsync(config, c => ChatModels.Create(c, _http), CancellationToken.None).ConfigureAwait(false);
        if (found.Count > 0) Log.Info($"Asistente: cerebros encontrados: {string.Join(", ", found.Select(f => $"{f.Provider} {f.Endpoint} ({f.Models})"))}");
        Set(s => s with { Found = found });
    }

    private async Task InstallOllamaAsync()
    {
        if (OllamaSetup.IsInstalled)
        {
            await StartOllamaAsync().ConfigureAwait(false);
            return;
        }
        if (!OllamaSetup.CanInstall)
        {
            OllamaSetup.OpenDownloadPage();
            return;
        }
        var token = BeginJob(new BrainJob("install", "running"));
        if (token is null) return;
        Log.Info("Asistente: instalando Ollama con winget");
        var error = await OllamaSetup.InstallAsync(token.Value).ConfigureAwait(false);
        if (token.Value.IsCancellationRequested)
        {
            EndJob(new BrainJob("install", "cancelled"));
            return;
        }
        EndJob(error is null ? new BrainJob("install", "done") : new BrainJob("install", "failed", Error: error));
        if (error is null) await StartOllamaAsync().ConfigureAwait(false);
    }

    /// <summary>Opens Ollama (if it is not answering) and waits for it to answer.</summary>
    private async Task StartOllamaAsync()
    {
        await CheckAsync(scanIfMissing: false).ConfigureAwait(false);
        if (State.State != "unreachable" || !OllamaSetup.IsInstalled) return;
        if (!OllamaSetup.Start()) return;
        var until = DateTime.UtcNow + StartPatience;
        while (DateTime.UtcNow < until && !_disposed)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            await CheckAsync(scanIfMissing: false).ConfigureAwait(false);
            if (State.State != "unreachable") return;
        }
    }

    private async Task PullAsync(string model)
    {
        BrainConfig config;
        lock (_gate) config = _config;
        if (config.Provider != BrainProvider.Ollama) return;
        var token = BeginJob(new BrainJob("pull", "running", model));
        if (token is null) return;

        Log.Info($"Asistente: descargando el modelo {model}");
        var expected = (long)((model == ModelAdvice.Standard.Name ? ModelAdvice.Standard.DownloadGb : model == ModelAdvice.Light.Name ? ModelAdvice.Light.DownloadGb : 0) * 1e9);
        var last = DateTime.MinValue;
        var progress = new Progress<PullProgress>(p =>
        {
            var now = DateTime.UtcNow;
            if (now - last < ProgressEvery) return;
            last = now;
            Set(s => s.Job is { State: "running", Kind: "pull" } ? s with { Job = s.Job with { Completed = p.Completed, Total = p.Total } } : s);
        });
        try
        {
            await new OllamaPull(_http, config.Address).PullAsync(model, progress, expected, token.Value).ConfigureAwait(false);
            Log.Info($"Asistente: modelo {model} descargado");
            EndJob(new BrainJob("pull", "done", model));
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Asistente: descarga de {model} cancelada");
            EndJob(new BrainJob("pull", "cancelled", model));
        }
        catch (ChatModelException ex)
        {
            Log.Warn($"Asistente: no se ha podido descargar {model}: {ex.Message}");
            EndJob(new BrainJob("pull", "failed", model, Error: ex.Message));
        }
        await CheckAsync(scanIfMissing: false).ConfigureAwait(false);
    }

    private Task Cancel()
    {
        lock (_gate) _job?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>Unloads what is in memory (or the model in use, when the server does not say what is loaded).</summary>
    private async Task UnloadAsync()
    {
        var (client, state) = (Client, State);
        if (client is null) return;
        var models = state.Loaded is { Count: > 0 } loaded ? loaded : state.Model is { } model ? [model] : [];
        foreach (var name in models)
        {
            try
            {
                await client.UnloadAsync(name, CancellationToken.None).ConfigureAwait(false);
                Log.Info($"Asistente: modelo {name} liberado");
            }
            catch (ChatModelException ex)
            {
                Log.Warn($"Asistente: no se ha podido liberar {name}: {ex.Message}");
            }
        }
        await CheckAsync(scanIfMissing: false).ConfigureAwait(false);
    }

    /// <summary>Starts a job (one at a time); null when another one is running.</summary>
    private CancellationToken? BeginJob(BrainJob job)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed || _job is not null) return null;
            _job = cts = new CancellationTokenSource();
        }
        Set(s => s with { Job = job });
        return cts.Token;
    }

    private void EndJob(BrainJob job)
    {
        lock (_gate)
        {
            _job?.Dispose();
            _job = null;
        }
        Set(s => s with { Job = job });
    }

    private static BrainStateMessage Empty(BrainConfig config, string state) =>
        new(state, config.Provider.Key(), config.Provider == BrainProvider.None ? string.Empty : config.Address);

    private void Set(Func<BrainStateMessage, BrainStateMessage> change)
    {
        BrainStateMessage next;
        lock (_gate)
        {
            if (_disposed) return;
            next = change(_state);
            if (next == _state) return;
            _state = next;
        }
        Changed?.Invoke(next);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _job?.Cancel();
            _client?.Dispose();
            _client = null;
        }
        _http.Dispose();
    }
}
