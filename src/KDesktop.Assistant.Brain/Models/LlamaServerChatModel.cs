using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using KDesktop.Core;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>
/// llama-server (llama.cpp, or a fork with its own formats such as PrismML's for Bonsai) started by the assistant process
/// itself: it loads a <c>.gguf</c> when an order needs it (or when the user starts talking), answers through its OpenAI
/// API, and stops —freeing the graphics card— when asked (Options, the action, «descarga el cerebro») or after
/// <see cref="KeepFor"/> without use. One model at a time: asking for another one restarts it with that one. It is in a
/// job object, so it never outlives the assistant process.
/// </summary>
public sealed class LlamaServerChatModel : IChatModel
{
    /// <summary>Longest wait for the model to load (a 27 B from a slow disk takes a while the first time).</summary>
    public static readonly TimeSpan StartPatience = TimeSpan.FromMinutes(3);

    /// <summary>The context it starts with: the instructions, a few tools and three exchanges take 1,500–3,000 tokens.</summary>
    public const int ContextTokens = 16384;

    private static readonly TimeSpan IdleCheckEvery = TimeSpan.FromSeconds(20);

    private readonly string _server;
    private readonly IReadOnlyList<string> _extraArgs;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _starting = new(1, 1);
    private readonly Timer _idle;
    private Running? _running;
    private CancellationTokenSource? _start;
    private string? _loading;
    private int _busy;
    private DateTime _lastUse = DateTime.UtcNow;
    private bool _disposed;
    private IReadOnlyList<ModelInfo>? _models;
    private DateTime _modelsAt;

    /// <summary>The models found are looked for again after this long (Options asks every few seconds).</summary>
    private static readonly TimeSpan ModelsFor = TimeSpan.FromSeconds(30);

    /// <param name="server">The llama-server.exe.</param>
    /// <param name="extraArgs">Options added to the usual ones (a later one wins: «-c 32768»).</param>
    /// <param name="keepFor">Unused this long, it stops (null: it stays).</param>
    /// <param name="chosen">The model chosen in Options, listed even when it is not next to the server.</param>
    public LlamaServerChatModel(string server, string extraArgs, TimeSpan? keepFor, string? chosen = null, HttpClient? http = null)
    {
        _server = server.Trim().Trim('"');
        _extraArgs = SplitArgs(extraArgs);
        KeepFor = keepFor;
        Chosen = string.IsNullOrWhiteSpace(chosen) ? null : chosen.Trim().Trim('"');
        _ownsClient = http is null;
        _http = http ?? new HttpClient();
        _idle = new Timer(_ => StopIfIdle(), null, IdleCheckEvery, IdleCheckEvery);
    }

    public TimeSpan? KeepFor { get; }

    public string? Chosen { get; }

    /// <summary>Raised (any thread) when a model starts loading, is loaded or leaves memory.</summary>
    public event Action? Changed;

    /// <summary>The model being loaded now, or null.</summary>
    public string? Loading
    {
        get { lock (_gate) return _loading; }
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_server))
            throw new ChatModelException(ChatModelFailure.Unreachable, _server.Length == 0 ? Loc.T("brain.error.noServer") : Loc.T("brain.error.serverMissing", _server));
        lock (_gate)
        {
            if (_models is { } known && DateTime.UtcNow - _modelsAt < ModelsFor) return Task.FromResult(known);
        }
        return Task.Run<IReadOnlyList<ModelInfo>>(() =>
        {
            IReadOnlyList<ModelInfo> models = [.. FindModels(_server, Chosen).Select(f => new ModelInfo(f.FullName, 0, f.Length, ["completion", "tools"]))];
            lock (_gate) (_models, _modelsAt) = (models, DateTime.UtcNow);
            return models;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<string>>(_running is { } running ? [running.Model] : []);
    }

    public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var running = await EnsureAsync(request.Model, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _busy);
        try
        {
            await foreach (var chunk in running.Client.StreamAsync(request with { Model = running.Alias }, cancellationToken).ConfigureAwait(false))
                yield return chunk;
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
            lock (_gate) _lastUse = DateTime.UtcNow;
            // It died while answering (out of memory, closed by hand): the next order starts it again.
            if (running.Process.HasExited) Forget(running, "se ha cerrado mientras contestaba");
        }
    }

    public async Task PreloadAsync(string model, CancellationToken cancellationToken)
    {
        await EnsureAsync(model, cancellationToken).ConfigureAwait(false);
        lock (_gate) _lastUse = DateTime.UtcNow;
    }

    public Task UnloadAsync(string model, CancellationToken cancellationToken)
    {
        Stop("pedido");
        return Task.CompletedTask;
    }

    /// <summary>The server with that model, started if needed (another model running is stopped first).</summary>
    private async Task<Running> EnsureAsync(string model, CancellationToken cancellationToken)
    {
        if (!model.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && Chosen is { } chosen) model = chosen;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_running is { } now && SamePath(now.Model, model) && !now.Process.HasExited) return now;
        }

        await _starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancellationTokenSource start;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_running is { } now && SamePath(now.Model, model) && !now.Process.HasExited) return now;
                start = _start = new CancellationTokenSource();
                _loading = model;
            }
            Stop(null, keepStart: true);
            Changed?.Invoke();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, start.Token);
                Running running;
                try
                {
                    running = await StartAsync(model, linked.Token).ConfigureAwait(false);
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
                    _running = running;
                    _lastUse = DateTime.UtcNow;
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

    private async Task<Running> StartAsync(string model, CancellationToken cancellationToken)
    {
        if (!File.Exists(_server)) throw new ChatModelException(ChatModelFailure.Unreachable, Loc.T("brain.error.serverMissing", _server));
        if (!File.Exists(model)) throw new ChatModelException(ChatModelFailure.ModelNotFound, Loc.T("brain.error.modelUnavailable", Path.GetFileName(model)));

        var port = FreePort();
        var alias = Path.GetFileNameWithoutExtension(model);
        var info = new ProcessStartInfo(_server)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_server)!,
        };
        string[] usual = ["-m", model, "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ngl", "99", "-fa", "on", "-c", ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture), "-np", "1", "--jinja",
            "--alias", alias, "--chat-template-kwargs", "{\"enable_thinking\": false}"];
        foreach (var arg in usual.Concat(_extraArgs)) info.ArgumentList.Add(arg);

        var output = new ServerOutput(Path.Combine(AppPaths.LogDir, "llama-server.log"));
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
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
            output.Dispose();
            throw new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.serverStart", ex.Message), ex);
        }
        ChildJob.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Log.Info($"Asistente: llama-server arrancado (pid {process.Id}, puerto {port}) con {Path.GetFileName(model)}");

        var running = new Running(process, model, alias, port, output, new OpenAICompatibleChatModel($"http://127.0.0.1:{port}", http: _http) { LogTimings = true });
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
                Log.Warn($"Asistente: llama-server se ha cerrado al cargar (código {running.Process.ExitCode}): {tail}");
                throw new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.serverClosed", LastLine(tail)));
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

    private static string LastLine(string tail)
    {
        var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("failed", StringComparison.OrdinalIgnoreCase))
            ?? lines.LastOrDefault() ?? string.Empty;
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    private void StopIfIdle()
    {
        lock (_gate)
        {
            if (_running is null || KeepFor is not { } keep || Volatile.Read(ref _busy) > 0 || DateTime.UtcNow - _lastUse < keep) return;
        }
        Stop($"sin usar desde hace {KeepFor.Value.TotalMinutes:0} min");
    }

    /// <summary>Stops the server (and a start going on, unless <paramref name="keepStart"/>); <paramref name="why"/> for the log.</summary>
    private void Stop(string? why, bool keepStart = false)
    {
        Running? running;
        lock (_gate)
        {
            running = _running;
            _running = null;
            if (!keepStart) _start?.Cancel();
        }
        if (running is null) return;
        running.Dispose();
        if (why is not null) Log.Info($"Asistente: {running.Alias} descargado ({why})");
        if (!keepStart) Changed?.Invoke();
    }

    private void Forget(Running running, string why)
    {
        lock (_gate)
        {
            if (_running != running) return;
            _running = null;
        }
        Log.Warn($"Asistente: llama-server {why}: {running.Output.Tail()}");
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
        Stop("el proceso del asistente se cierra");
        if (_ownsClient) _http.Dispose();
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// The <c>.gguf</c> models near the server (its folder and the two above it, a few levels down: a demo keeps them in
    /// <c>models\…</c> next to <c>bin\cuda</c>) and the chosen one; without the vision projectors, drafters, extra parts
    /// of a split model and the like.
    /// </summary>
    public static IReadOnlyList<FileInfo> FindModels(string server, string? chosen)
    {
        var found = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        if (chosen is not null && File.Exists(chosen)) found[Path.GetFullPath(chosen)] = new FileInfo(chosen);
        var folder = Path.GetDirectoryName(Path.GetFullPath(server));
        for (var up = 0; up < 3 && folder is not null; up++, folder = Path.GetDirectoryName(folder))
        {
            // Never a whole drive or the user's folder (a server in C:\llama would look through all of C:).
            if (string.Equals(Path.GetPathRoot(folder)?.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
            if (string.Equals(folder, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)) break;
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true };
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.gguf", options))
                {
                    if (found.Count >= 60) break;
                    if (IsModel(Path.GetFileName(file))) found.TryAdd(Path.GetFullPath(file), new FileInfo(file));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read.
            }
        }
        return [.. found.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static readonly string[] NotModels = ["mmproj", "dspark", "dflash", "kv-bias", "draft", "embed", "vocab", "reranker"];

    public static bool IsModel(string fileName) =>
        !NotModels.Any(n => fileName.Contains(n, StringComparison.OrdinalIgnoreCase))
        && !System.Text.RegularExpressions.Regex.IsMatch(fileName, @"-0000[2-9]-of-\d+\.gguf$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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

    private sealed record Running(Process Process, string Model, string Alias, int Port, ServerOutput Output, OpenAICompatibleChatModel Client) : IDisposable
    {
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
            Output.Dispose();
        }
    }

    /// <summary>What the server writes, to <c>Logs\llama-server.log</c> (new each start), and its last lines for an error.</summary>
    private sealed class ServerOutput : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<string> _last = new();
        private StreamWriter? _file;

        public ServerOutput(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                _file = new StreamWriter(path, append: false) { AutoFlush = true };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _file = null;
            }
        }

        public void Write(string? line)
        {
            if (line is null) return;
            lock (_gate)
            {
                _last.Enqueue(line);
                while (_last.Count > 25) _last.Dequeue();
                try { _file?.WriteLine(line); }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException) { _file = null; }
            }
        }

        public string Tail()
        {
            lock (_gate) return string.Join("\n", _last);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _file?.Dispose();
                _file = null;
            }
        }
    }
}
