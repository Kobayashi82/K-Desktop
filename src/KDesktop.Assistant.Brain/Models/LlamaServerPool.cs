using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using KDesktop.Core;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>
/// The assistant process as its own llama-swap (5.0.107): the <c>.gguf</c> of the folders of Options, each opened with its
/// llama-server (llama.cpp's, or a fork such as PrismML's for Bonsai: <see cref="GgufFiles.ServerFor"/>; if it cannot
/// load it, the next one) when an order needs it or the user starts talking —never at start-up—, with its vision
/// projector when there is one. Each answers through its OpenAI API on a port of its own. With
/// <see cref="OneAtATime"/>, loading a model stops the others; without it they stay while they fit in the graphics card.
/// A model unused for <see cref="KeepFor"/> stops; asked, all of them stop. They are in a job object, so none outlives
/// the assistant process.
/// </summary>
public sealed class LlamaServerPool : IChatModel
{
    /// <summary>Longest wait for a model to load (a 27 B from a slow disk takes a while the first time).</summary>
    public static readonly TimeSpan StartPatience = TimeSpan.FromMinutes(3);

    /// <summary>The context it starts with: the instructions, a few tools and three exchanges take 1,500–3,000 tokens.</summary>
    public const int ContextTokens = 16384;

    /// <summary>What a model takes in the graphics card besides its file: the context and llama.cpp's buffers.</summary>
    private const double OverheadBytes = 0.8e9;

    /// <summary>Video memory left for Windows and the rest when several models stay loaded.</summary>
    private const double SpareBytes = 1.0e9;

    private static readonly TimeSpan IdleCheckEvery = TimeSpan.FromSeconds(20);

    /// <summary>The models found are looked for again after this long (Options asks every few seconds).</summary>
    private static readonly TimeSpan ModelsFor = TimeSpan.FromSeconds(30);

    private Dictionary<string, IReadOnlyList<string>> _modelArgs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<double?> _videoGb;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly Timer _idle;
    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The server that could load each model (when the usual one could not).</summary>
    private readonly Dictionary<string, string> _serverOf = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _chosen = [];
    private CancellationTokenSource? _start;
    private string? _loading;
    private bool _disposed;
    private IReadOnlyList<LocalModel>? _models;
    private DateTime _modelsAt;

    /// <param name="servers">The llama-server.exe of Options; the first one is the usual.</param>
    /// <param name="folders">Where the models are looked for.</param>
    /// <param name="modelArgs">Each model's options added to the usual ones (model path → «-c 32768»; a later one wins).</param>
    /// <param name="keepFor">Unused this long, a model stops (null: it stays).</param>
    /// <param name="chosen">The models chosen in Options, listed even when they are not in the folders.</param>
    /// <param name="videoGb">The graphics card's own memory in GB (null: unknown), to know what fits.</param>
    public LlamaServerPool(IReadOnlyList<string> servers, IReadOnlyList<string> folders, IReadOnlyDictionary<string, string>? modelArgs, TimeSpan? keepFor, bool oneAtATime, bool vision,
        IEnumerable<string> chosen, Func<double?> videoGb, HttpClient? http = null)
    {
        _videoGb = videoGb;
        _ownsClient = http is null;
        _http = http ?? new HttpClient();
        Configure(servers, folders, modelArgs, keepFor, oneAtATime, vision, chosen);
        _idle = new Timer(_ => StopIdle(), null, IdleCheckEvery, IdleCheckEvery);
    }

    /// <summary>
    /// Options changed: what comes next follows them, and the models running go on (a server added or an option changed
    /// never unloads Bonsai; a new option applies the next time a model loads).
    /// </summary>
    public void Configure(IReadOnlyList<string> servers, IReadOnlyList<string> folders, IReadOnlyDictionary<string, string>? modelArgs, TimeSpan? keepFor, bool oneAtATime, bool vision,
        IEnumerable<string> chosen)
    {
        lock (_gate)
        {
            Servers = [.. servers.Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0)];
            Folders = folders;
            _modelArgs = (modelArgs ?? new Dictionary<string, string>()).ToDictionary(p => p.Key, p => SplitArgs(p.Value), StringComparer.OrdinalIgnoreCase);
            KeepFor = keepFor;
            OneAtATime = oneAtATime;
            Vision = vision;
            _chosen = [.. chosen.Where(ModelRefs.IsLocal)];
            _models = null;
        }
    }

    public IReadOnlyList<string> Servers { get; private set; } = [];

    public IReadOnlyList<string> Folders { get; private set; } = [];

    public TimeSpan? KeepFor { get; private set; }

    public bool OneAtATime { get; private set; }

    public bool Vision { get; private set; }

    /// <summary>Raised (any thread) when a model starts loading, is loaded or leaves memory.</summary>
    public event Action? Changed;

    /// <summary>The model being loaded now, or null.</summary>
    public string? Loading
    {
        get { lock (_gate) return _loading; }
    }

    /// <summary>The models found the last time (empty before the first look).</summary>
    public IReadOnlyList<LocalModel> Known
    {
        get { lock (_gate) return _models ?? []; }
    }

    /// <summary>The folders are looked at again on the next list (a model was just downloaded).</summary>
    public void Rescan()
    {
        lock (_gate) _models = null;
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ModelInfo>>(() => [.. Find().Select(m => new ModelInfo(m.Path, 0, m.Size,
            Vision && m.Projector is not null ? ["completion", "tools", "vision"] : ["completion", "tools"]))], cancellationToken);

    private IReadOnlyList<LocalModel> Find()
    {
        lock (_gate)
        {
            if (_models is { } known && DateTime.UtcNow - _modelsAt < ModelsFor) return known;
        }
        var models = GgufFiles.Find(Folders, _chosen, Servers.Where(File.Exists).ToList() is { Count: > 0 } there ? there : Servers);
        lock (_gate) (_models, _modelsAt) = (models, DateTime.UtcNow);
        return models;
    }

    public Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<string>>([.. _running.Keys]);
    }

    public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var running = await EnsureAsync(request.Model, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref running.Busy);
        try
        {
            await foreach (var chunk in running.Client.StreamAsync(request with { Model = running.Alias }, cancellationToken).ConfigureAwait(false))
                yield return chunk;
        }
        finally
        {
            Interlocked.Decrement(ref running.Busy);
            running.LastUse = DateTime.UtcNow;
            // It died while answering (out of memory, closed by hand): the next order starts it again.
            if (running.Process.HasExited) Forget(running, "se ha cerrado mientras contestaba");
        }
    }

    public async Task PreloadAsync(string model, CancellationToken cancellationToken)
    {
        var running = await EnsureAsync(model, cancellationToken).ConfigureAwait(false);
        running.LastUse = DateTime.UtcNow;
    }

    /// <summary>Stops that model (also while it loads).</summary>
    public Task UnloadAsync(string model, CancellationToken cancellationToken)
    {
        bool loading;
        lock (_gate) loading = _loading is { } now && ModelRefs.Same(now, model);
        if (loading) CancelStart();
        Stop(model, "pedido");
        return Task.CompletedTask;
    }

    /// <summary>Stops every model (and one loading).</summary>
    public void StopAll(string why)
    {
        CancelStart();
        List<string> models;
        lock (_gate) models = [.. _running.Keys];
        foreach (var model in models) Stop(model, why);
    }

    /// <summary>The server with that model, started if needed (making room first).</summary>
    private async Task<Running> EnsureAsync(string model, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Alive(model) is { } now) return now;
        }

        await _starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancellationTokenSource start;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (Alive(model) is { } now) return now;
                start = _start = new CancellationTokenSource();
                _loading = model;
            }
            var local = Describe(model);
            MakeRoom(local);
            Changed?.Invoke();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, start.Token);
                Running running;
                try
                {
                    running = await StartAnyAsync(local, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Unloaded while it was loading («descarga el cerebro» right after «Lander,…»).
                    throw new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.unloaded"));
                }
                lock (_gate)
                {
                    if (start.IsCancellationRequested || _disposed)
                    {
                        running.Dispose();
                        throw new OperationCanceledException(start.Token);
                    }
                    _running[local.Path] = running;
                }
                return running;
            }
            finally
            {
                lock (_gate)
                {
                    _loading = null;
                    if (_start == start) _start = null;
                }
                start.Dispose();
                Changed?.Invoke();
            }
        }
        finally
        {
            _starting.Release();
        }
    }

    /// <summary>A started server of that model that is still there (under the lock); a dead one is forgotten.</summary>
    private Running? Alive(string model)
    {
        var key = _running.Keys.FirstOrDefault(k => ModelRefs.Same(k, model));
        if (key is null) return null;
        var running = _running[key];
        if (!running.Process.HasExited) return running;
        _running.Remove(key);
        running.Dispose();
        return null;
    }

    /// <summary>What is known of a model: as found in the folders, or the file itself with the usual server.</summary>
    private LocalModel Describe(string model)
    {
        if (!ModelRefs.IsLocal(model)) throw new ChatModelException(ChatModelFailure.ModelNotFound, Loc.T("brain.error.modelUnavailable", model));
        var found = Find().FirstOrDefault(m => ModelRefs.Same(m.Path, model));
        if (found is null)
        {
            Rescan();
            found = Find().FirstOrDefault(m => ModelRefs.Same(m.Path, model));
        }
        if (found is not null) return found;
        if (!File.Exists(model)) throw new ChatModelException(ChatModelFailure.ModelNotFound, Loc.T("brain.error.modelUnavailable", Path.GetFileName(model)));
        return new LocalModel(Path.GetFullPath(model), new FileInfo(model).Length, null, 0, GgufFiles.ServerFor(model, Servers));
    }

    private double Needs(LocalModel model) => model.Size + (Vision ? model.ProjectorSize : 0) + OverheadBytes;

    /// <summary>
    /// Before loading a model: with one at a time, the others stop; otherwise, the least used ones stop until it fits in the
    /// graphics card (Bonsai's 8 GB and a 4 B's 3.5 GB do not fit in 12 GB with room for Windows).
    /// </summary>
    private void MakeRoom(LocalModel next)
    {
        List<Running> running;
        lock (_gate) running = [.. _running.Values.OrderBy(r => r.LastUse)];
        if (running.Count == 0) return;
        if (OneAtATime)
        {
            foreach (var other in running) Stop(other.Model, $"se carga {Path.GetFileNameWithoutExtension(next.Path)} (un modelo a la vez)");
            return;
        }
        if (_videoGb() is not { } gb) return;
        var room = gb * 1e9 - SpareBytes;
        var used = running.Sum(r => r.Needs);
        foreach (var other in running)
        {
            if (used + Needs(next) <= room) break;
            Stop(other.Model, $"no cabe con {Path.GetFileNameWithoutExtension(next.Path)} en la gráfica");
            used -= other.Needs;
        }
    }

    /// <summary>
    /// Starts it with its server; if that one closes while loading it (a format it does not know, as Bonsai's for llama.cpp),
    /// with the next one, and that one is remembered for the model.
    /// </summary>
    private async Task<Running> StartAnyAsync(LocalModel model, CancellationToken cancellationToken)
    {
        var there = Servers.Where(File.Exists).ToList();
        if (there.Count == 0)
            throw new ChatModelException(ChatModelFailure.Unreachable, Servers.Count == 0 ? Loc.T("brain.error.noServer") : Loc.T("brain.error.serverMissing", Servers[0]));
        string? remembered;
        lock (_gate) remembered = _serverOf.GetValueOrDefault(model.Path);
        var order = new[] { remembered, model.Server }.OfType<string>().Where(there.Contains).Concat(there).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ChatModelException? first = null;
        foreach (var server in order)
        {
            try
            {
                var running = await StartAsync(model, server, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(server, model.Server, StringComparison.OrdinalIgnoreCase))
                    lock (_gate) _serverOf[model.Path] = server;
                return running;
            }
            catch (ServerClosedException ex)
            {
                first ??= ex.Error;
                if (order.Count > 1) Log.Info($"Asistente: {Path.GetFileNameWithoutExtension(model.Path)} no carga con {ServerName(server)}; se prueba con otro llama-server");
            }
        }
        throw first!;
    }

    private async Task<Running> StartAsync(LocalModel model, string server, CancellationToken cancellationToken)
    {
        var port = FreePort();
        var alias = Path.GetFileNameWithoutExtension(model.Path);
        var info = new ProcessStartInfo(server)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(server)!,
        };
        foreach (var arg in Arguments(model, port, alias)) info.ArgumentList.Add(arg);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var output = new ServerOutput(alias);
        process.OutputDataReceived += (_, e) => output.Write(e.Data);
        process.ErrorDataReceived += (_, e) => output.Write(e.Data);
        var watch = Stopwatch.StartNew();
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Process.Start");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.serverStart", ex.Message), ex);
        }
        ChildJob.Add(process, "llama-server");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var vision = Vision && model.Projector is not null;
        Log.Info($"Asistente: {ServerName(server)} arrancado (pid {process.Id}, puerto {port}) con {Path.GetFileName(model.Path)}{(vision ? " y visión" : "")}");

        var running = new Running(process, model.Path, server, alias, port, output, Needs(model),
            new OpenAICompatibleChatModel($"http://127.0.0.1:{port}", http: _http) { LogTimings = true });
        try
        {
            await WaitReadyAsync(running, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            running.Dispose();
            throw;
        }
        Log.Info($"Asistente: {alias} cargado en {watch.ElapsedMilliseconds} ms");
        return running;
    }

    /// <summary>
    /// llama-server's options: the model (with its projector), a port of this PC only, a fixed context with an 8-bit
    /// cache, one conversation, the model's own chat template, without thinking, and —unset— the layers in the graphics
    /// card and the experts of a big MoE out of it, as <c>--fit</c> decides by itself; then that model's own of Options.
    /// </summary>
    public IReadOnlyList<string> Arguments(LocalModel model, int port, string alias)
    {
        var args = new List<string> { "-m", model.Path };
        if (Vision && model.Projector is not null) args.AddRange(["--mmproj", model.Projector]);
        args.AddRange(["--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture),
            "-fa", "on", "-c", ContextTokens.ToString(CultureInfo.InvariantCulture), "--cache-type-k", "q8_0", "--cache-type-v", "q8_0",
            "-np", "1", "--jinja", "--alias", alias, "--chat-template-kwargs", "{\"enable_thinking\": false}"]);
        lock (_gate) args.AddRange(_modelArgs.GetValueOrDefault(model.Path) ?? []);
        return args;
    }

    /// <summary>«llama-server (Bonsai-demo)»: the server with the folder that tells them apart, for the log.</summary>
    public static string ServerName(string server)
    {
        var folder = Path.GetDirectoryName(server);
        // «…\Bonsai-demo\bin\cuda\llama-server.exe»: the folders «bin», «cuda»… say nothing.
        while (folder is not null && Path.GetFileName(folder).ToLowerInvariant() is "bin" or "cuda" or "cpu" or "vulkan" or "build" or "release" or "x64")
            folder = Path.GetDirectoryName(folder);
        return folder is null ? "llama-server" : $"llama-server ({Path.GetFileName(folder)})";
    }

    /// <summary>Until <c>/health</c> says ok (503 while it loads); its last lines when it closes or takes too long.</summary>
    private async Task WaitReadyAsync(Running running, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + StartPatience;
        var health = new Uri($"http://127.0.0.1:{running.Port}/health");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (running.Process.HasExited)
            {
                var tail = running.Output.Tail();
                Log.Warn($"Asistente: {ServerName(running.Server)} se ha cerrado al cargar {running.Alias} (código {running.Process.ExitCode}): {tail}");
                throw new ServerClosedException(new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.serverClosed", LastLine(tail))));
            }
            if (DateTime.UtcNow > until) throw new ChatModelException(ChatModelFailure.Timeout, Loc.T("brain.error.serverSlow"));
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await _http.GetAsync(health, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // That look took too long: another one.
            }
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A server that closed while loading: another one may be tried.</summary>
    private sealed class ServerClosedException(ChatModelException error) : Exception(error.Message, error)
    {
        public ChatModelException Error { get; } = error;
    }

    private static string LastLine(string tail)
    {
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("failed", StringComparison.OrdinalIgnoreCase))
            ?? lines.LastOrDefault() ?? string.Empty;
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    private void StopIdle()
    {
        if (KeepFor is not { } keep) return;
        List<Running> idle;
        lock (_gate) idle = [.. _running.Values.Where(r => Volatile.Read(ref r.Busy) == 0 && DateTime.UtcNow - r.LastUse >= keep)];
        foreach (var running in idle) Stop(running.Model, $"sin usar desde hace {keep.TotalMinutes:0} min");
    }

    private void CancelStart()
    {
        lock (_gate) _start?.Cancel();
    }

    /// <summary>Stops the server of a model; <paramref name="why"/> for the log.</summary>
    private void Stop(string model, string why)
    {
        Running? running;
        lock (_gate)
        {
            var key = _running.Keys.FirstOrDefault(k => ModelRefs.Same(k, model));
            if (key is null || !_running.Remove(key, out running)) return;
        }
        running.Dispose();
        Log.Info($"Asistente: {running.Alias} descargado ({why})");
        Changed?.Invoke();
    }

    private void Forget(Running running, string why)
    {
        lock (_gate)
        {
            if (!_running.TryGetValue(running.Model, out var known) || known != running) return;
            _running.Remove(running.Model);
        }
        Log.Warn($"Asistente: {ServerName(running.Server)} {why}: {running.Output.Tail()}");
        running.Dispose();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _idle.Dispose();
        StopAll("el proceso del asistente se cierra");
        if (_ownsClient) _http.Dispose();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Options typed in one line, with quotes for those that have spaces.</summary>
    public static IReadOnlyList<string> SplitArgs(string? line)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return args;
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) args.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }
        if (any) args.Add(current.ToString());
        return args;
    }

    private sealed class Running(Process process, string model, string server, string alias, int port, ServerOutput output, double needs, OpenAICompatibleChatModel client) : IDisposable
    {
        public Process Process { get; } = process;
        public string Model { get; } = model;
        public string Server { get; } = server;
        public string Alias { get; } = alias;
        public int Port { get; } = port;
        public ServerOutput Output { get; } = output;
        public double Needs { get; } = needs;
        public OpenAICompatibleChatModel Client { get; } = client;
        public int Busy;
        public DateTime LastUse { get; set; } = DateTime.UtcNow;

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
            Client.Dispose();
            Process.Dispose();
        }
    }

    /// <summary>
    /// What the servers write, to <c>Logs\llama-server.log</c> (one for all, each line with its model; new each run of the
    /// process), and the last lines of each for an error.
    /// </summary>
    private sealed class ServerOutput(string alias)
    {
        private static readonly object FileGate = new();
        private static StreamWriter? _file;
        private static bool _opened;
        private readonly Queue<string> _last = new();

        public void Write(string? line)
        {
            if (line is null) return;
            lock (_last)
            {
                _last.Enqueue(line);
                while (_last.Count > 25) _last.Dequeue();
            }
            lock (FileGate)
            {
                try
                {
                    if (!_opened)
                    {
                        _opened = true;
                        var path = Path.Combine(AppPaths.LogDir, "llama-server.log");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        _file = new StreamWriter(path, append: false) { AutoFlush = true };
                    }
                    _file?.WriteLine($"[{alias}] {line}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    _file = null;
                }
            }
        }

        public string Tail()
        {
            lock (_last) return string.Join("\n", _last);
        }
    }
}
