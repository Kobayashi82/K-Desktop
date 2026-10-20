using System.Diagnostics;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

public enum AssistantProcessState
{
    /// <summary>Not started, or stopped.</summary>
    Off,
    /// <summary>Launched, waiting for its hello.</summary>
    Starting,
    /// <summary>Connected and welcomed.</summary>
    Running,
    /// <summary>It ended unexpectedly: waiting before starting it again.</summary>
    Restarting,
}

/// <summary>
/// Runs the assistant process for K-Desktop (stage 1.3): opens the pipe, launches <c>KDesktop.exe --assistant</c> as
/// the normal user, and keeps it alive. If it ends or stops answering, it is started again after a growing wait
/// (1, 2, 4… up to 60 s); three falls in a row with the same assistant the last one loaded, each within two minutes of
/// the previous one, take that assistant off the desktop (<see cref="AssistantFailed"/>): with several, the newest is
/// the likeliest culprit, and if the falls go on the next one is blamed. Only exists while the module is on, and is
/// used once: turning the module on again makes a new one.
/// </summary>
public sealed class AssistantSupervisor : IAsyncDisposable
{
    /// <summary>A launched process that has not said hello by then is stuck: it is ended and started again.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ByeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FallWindow = TimeSpan.FromMinutes(2);

    private readonly string _pipe;
    private readonly Func<WelcomeMessage> _welcome;
    private readonly Func<string, string, Process?> _start;
    private readonly AssistantPipeServer _server;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _loop;
    private Process? _process;
    private TaskCompletionSource _lost = NewSignal();
    private TaskCompletionSource _connected = NewSignal();
    private bool _expectedExit;
    private int _falls;
    private DateTime _lastFall = DateTime.MinValue;
    private string? _fallsWith;
    private int _failures;
    /// <summary>The assistants loaded in the process now, in the order they were loaded.</summary>
    private readonly List<string> _loaded = [];

    /// <param name="welcome">K-Desktop's language, theme, pause and options when the process connects.</param>
    /// <param name="start">Starts the process (executable, arguments); tests replace it. Default: as the normal user.</param>
    /// <param name="pipe">Pipe name (default: the one of this data folder).</param>
    /// <param name="heartbeat">Heartbeat times (tests).</param>
    public AssistantSupervisor(Func<WelcomeMessage> welcome, Func<string, string, Process?>? start = null, string? pipe = null,
        Heartbeat? heartbeat = null, string? executable = null)
    {
        _welcome = welcome;
        _start = start ?? StartAsUser;
        _pipe = pipe ?? AssistantPipe.Name;
        Executable = executable ?? AppPaths.ExecutablePath;
        _server = new AssistantPipeServer(_pipe, new ClientPolicy(Executable, IsOurs), welcome, heartbeat);
        _server.Connected += _ =>
        {
            lock (_gate) _connected.TrySetResult();
            SetState(AssistantProcessState.Running);
        };
        _server.Disconnected += _ =>
        {
            lock (_gate) _lost.TrySetResult();
        };
        _server.Received += OnReceived;
    }

    public string Executable { get; }

    public AssistantProcessState State { get; private set; } = AssistantProcessState.Off;

    /// <summary>Process id of the assistant process now (0 if none).</summary>
    public int ProcessId { get { lock (_gate) return _process is { } p ? SafeId(p) : 0; } }

    /// <summary>
    /// The assistants the process has loaded, in the order they were loaded (their «loaded» came back ok). Whoever
    /// sends <c>unload</c> calls <see cref="MarkUnloaded"/>; a new process starts with none.
    /// </summary>
    public IReadOnlyList<string> LoadedAssistants { get { lock (_gate) return [.. _loaded]; } }

    public bool IsLoaded(string id)
    {
        lock (_gate) return _loaded.Contains(id);
    }

    /// <summary>It is loaded (a «loaded» that went well; tests). A new version keeps its place.</summary>
    public void MarkLoaded(string id)
    {
        lock (_gate)
            if (!_loaded.Contains(id)) _loaded.Add(id);
    }

    /// <summary>It is no longer loaded («unload» sent, or its «loaded» failed).</summary>
    public void MarkUnloaded(string id)
    {
        lock (_gate) _loaded.Remove(id);
    }

    /// <summary>When the state changes (any thread).</summary>
    public event Action<AssistantProcessState>? StateChanged;

    /// <summary>The process connected (again): send it what it needs (load…). Pool thread.</summary>
    public event Action? Connected;

    /// <summary>A message from the assistant process. Pool thread, in order.</summary>
    public event Action<PipeMessage>? Received;

    /// <summary>This assistant made the process fall three times in a row and was unloaded: tell the user.</summary>
    public event Action<string>? AssistantFailed;

    public bool IsRunning => State == AssistantProcessState.Running;

    public void Start()
    {
        if (_loop is not null) return;
        _server.Start();
        _loop = Task.Run(() => LoopAsync(_stop.Token));
    }

    public bool Send(PipeMessage message) => _server.Send(message);

    /// <summary>Asks the process to close (with its exit) and stops watching it.</summary>
    public async Task StopAsync(string reason = "disable")
    {
        if (_loop is null) return;
        // First the loop, so the exit that follows is not taken for a fall and nothing is started again.
        _stop.Cancel();
        lock (_gate) _expectedExit = true;
        if (_server.Send(new ByeMessage(reason))) await WaitForExitOrKillAsync(ByeTimeout).ConfigureAwait(false);
        else Kill(); // not connected (yet): nobody to say goodbye to
        try { await _loop.ConfigureAwait(false); } catch { /* logged */ }
        _loop = null;
        lock (_gate)
        {
            _process?.Dispose();
            _process = null;
        }
        await _server.DisposeAsync().ConfigureAwait(false);
        SetState(AssistantProcessState.Off);
    }

    /// <summary>Closes the process and starts a new one at once (an assistant was switched in development…).</summary>
    public void Restart()
    {
        lock (_gate) _expectedExit = true;
        if (!_server.Send(new ByeMessage("restart"))) Kill();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("exit").ConfigureAwait(false);
        _stop.Dispose();
    }

    private bool IsOurs(int pid)
    {
        lock (_gate) return _process is { } p && SafeId(p) == pid;
    }

    private void OnReceived(PipeMessage message)
    {
        if (message is LoadedMessage loaded)
        {
            if (loaded.Ok) MarkLoaded(loaded.Assistant);
            else MarkUnloaded(loaded.Assistant);
        }
        Received?.Invoke(message);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Process? process;
            lock (_gate)
            {
                _connected = NewSignal();
                _lost = NewSignal();
                _expectedExit = false;
            }
            SetState(AssistantProcessState.Starting);
            var arguments = $"--assistant --pipe {_pipe}" + (AppPaths.InstanceSuffix.Length > 0 ? $" --data \"{AppPaths.DataDir}\"" : "");
            process = _start(Executable, arguments);
            if (process is null)
            {
                Log.Warn("Asistente: no se pudo lanzar su proceso");
                if (!await RetryAfterAsync(Backoff(++_failures), token).ConfigureAwait(false)) return;
                continue;
            }
            lock (_gate) _process = process;
            var startedAt = DateTime.UtcNow;
            Log.Info($"Asistente: proceso lanzado (pid {SafeId(process)})");

            var exited = process.WaitForExitAsync(CancellationToken.None);
            var connected = _connected.Task;
            var first = await Task.WhenAny(connected, exited, Task.Delay(ConnectTimeout, token)).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;
            if (first == connected)
            {
                _failures = 0;
                lock (_gate) _loaded.Clear(); // a new process: what the last one had loaded is gone
                try { Connected?.Invoke(); } catch (Exception ex) { Log.Error("Asistente: error al conectar", ex); }
                var lost = _lost.Task;
                if (await Task.WhenAny(exited, lost).ConfigureAwait(false) == lost && !exited.IsCompleted)
                {
                    // The link died with the process still there (hung, or it lost the pipe): give it a moment, then end it.
                    if (!Volatile.Read(ref _expectedExit)) Log.Warn("Asistente: se ha perdido la conexión con su proceso");
                    await WaitForExitOrKillAsync(ByeTimeout).ConfigureAwait(false);
                }
            }
            else if (first != exited)
            {
                Log.Warn("Asistente: su proceso no se ha conectado a tiempo; se cierra");
                Kill();
            }

            await exited.ConfigureAwait(false);
            var code = SafeExitCode(process);
            lock (_gate) _process = null;
            process.Dispose();
            if (token.IsCancellationRequested) return;

            bool expected;
            lock (_gate) expected = _expectedExit;
            if (expected)
            {
                Log.Info("Asistente: proceso cerrado; se vuelve a lanzar");
                continue;
            }

            Log.Warn($"Asistente: su proceso ha terminado de forma inesperada (código {code}, tras {(DateTime.UtcNow - startedAt).TotalSeconds:0} s)");
            CountFall();
            SetState(AssistantProcessState.Restarting);
            // A run that lasted a while starts the waits from the beginning.
            if (DateTime.UtcNow - startedAt > FallWindow) _failures = 0;
            if (!await RetryAfterAsync(Backoff(++_failures), token).ConfigureAwait(false)) return;
        }
    }

    /// <summary>
    /// Three falls in a row with the same assistant the last one loaded, each within two minutes of the last: unload it.
    /// </summary>
    private void CountFall()
    {
        var now = DateTime.UtcNow;
        string? with;
        lock (_gate) with = _loaded.Count > 0 ? _loaded[^1] : null;
        _falls = with is not null && with == _fallsWith && now - _lastFall <= FallWindow ? _falls + 1 : 1;
        _fallsWith = with;
        _lastFall = now;
        if (with is null || _falls < 3) return;
        Log.Warn($"Asistente: «{with}» ha hecho caer el proceso 3 veces seguidas; se desactiva");
        _falls = 0;
        _fallsWith = null;
        MarkUnloaded(with);
        try { AssistantFailed?.Invoke(with); } catch (Exception ex) { Log.Error("Asistente: error al desactivarlo", ex); }
    }

    private static TimeSpan Backoff(int failures) => TimeSpan.FromSeconds(Math.Min(60, 1 << Math.Clamp(failures - 1, 0, 6)));

    private static async Task<bool> RetryAfterAsync(TimeSpan wait, CancellationToken token)
    {
        Log.Info($"Asistente: nuevo intento en {wait.TotalSeconds:0} s");
        try
        {
            await Task.Delay(wait, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }

    private async Task WaitForExitOrKillAsync(TimeSpan timeout)
    {
        Process? process;
        lock (_gate) process = _process;
        if (process is null) return;
        try
        {
            using var limit = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Warn("Asistente: su proceso no se ha cerrado a tiempo; se termina");
            Kill();
        }
        catch (InvalidOperationException) { /* already gone */ }
    }

    private void Kill()
    {
        lock (_gate)
        {
            try { _process?.Kill(); } catch { /* already gone */ }
        }
    }

    private void SetState(AssistantProcessState state)
    {
        if (State == state) return;
        State = state;
        try { StateChanged?.Invoke(state); } catch (Exception ex) { Log.Error("Asistente: error al cambiar de estado", ex); }
    }

    private static Process? StartAsUser(string executable, string arguments) =>
        ProcessLauncher.IsElevated
            ? ProcessLauncher.StartAsUser(executable, arguments)
            : Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false });

    private static int SafeId(Process p)
    {
        try { return p.Id; } catch { return 0; }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
