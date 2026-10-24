using System.Diagnostics;
using System.Security.Cryptography;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The showcase's stage for one assistant (phase 16): <c>KDesktop.exe --assistant-showcase</c> as the normal user, with a pipe
/// of its own, while the «Probar» tab of its card is open. It is not the assistant on the desktop: another copy on a desktop of
/// its own, drawn off the screen, that never touches the user's windows; its pictures come through shared memory
/// (<see cref="StageFrameMessage"/>). Used once: a new stage is a new object. Not restarted if it ends.
/// </summary>
public sealed class AssistantStage : IAsyncDisposable
{
    private static readonly TimeSpan ByeTimeout = TimeSpan.FromSeconds(2);

    private readonly AssistantPipeServer _server;
    private readonly Func<string, string, Process?> _start;
    private readonly string _pipe;
    private Process? _process;
    private int _ended;

    /// <param name="package">The installed package's folder.</param>
    /// <param name="welcome">K-Desktop's language, theme and the assistant's settings when it connects.</param>
    /// <param name="start">Starts the process (executable, arguments); tests replace it. Default: as the normal user.</param>
    public AssistantStage(string package, Func<WelcomeMessage> welcome, Func<string, string, Process?>? start = null, string? executable = null)
    {
        Package = package;
        Executable = executable ?? AppPaths.ExecutablePath;
        _start = start ?? StartAsUser;
        _pipe = $"{AssistantPipe.Name}.stage.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";
        _server = new AssistantPipeServer(_pipe, new ClientPolicy(Executable, pid => _process is { } p && SafeId(p) == pid), welcome);
        _server.Received += message => Received?.Invoke(message);
        _server.Disconnected += reason => End(reason);
        _server.Connected += _ => Connected?.Invoke();
    }

    public string Package { get; }

    public string Executable { get; }

    /// <summary>A message from the stage (pool thread, in order).</summary>
    public event Action<PipeMessage>? Received;

    /// <summary>The stage connected (pool thread).</summary>
    public event Action? Connected;

    /// <summary>The stage is gone (with why): it closed, it fell, or it never came (pool thread). Once.</summary>
    public event Action<string>? Ended;

    public bool IsConnected => _server.IsConnected;

    public void Start()
    {
        _server.Start();
        var arguments = $"--assistant-showcase \"{Package.TrimEnd('\\')}\" --pipe {_pipe}" +
                        (AppPaths.InstanceSuffix.Length > 0 ? $" --data \"{AppPaths.DataDir}\"" : "");
        _process = _start(Executable, arguments);
        if (_process is null)
        {
            End("no se ha podido lanzar");
            return;
        }
        Log.Info($"Asistente: escenario de {Path.GetFileName(Package.TrimEnd('\\'))} lanzado (pid {SafeId(_process)})");
        _ = WatchAsync(_process);
    }

    public bool Send(PipeMessage message) => _server.Send(message);

    private async Task WatchAsync(Process process)
    {
        var connected = Task.Run(async () =>
        {
            for (var i = 0; i < 200 && !_server.IsConnected; i++) await Task.Delay(100).ConfigureAwait(false);
        });
        var exited = process.WaitForExitAsync();
        if (await Task.WhenAny(connected, exited).ConfigureAwait(false) == connected && !_server.IsConnected)
        {
            Kill();
            End("no se ha conectado a tiempo");
            return;
        }
        await exited.ConfigureAwait(false);
        End($"su proceso ha terminado (código {SafeExitCode(process)})");
    }

    private void End(string reason)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        Log.Info($"Asistente: escenario cerrado ({reason})");
        try { Ended?.Invoke(reason); }
        catch (Exception ex) { Log.Error("Asistente: error al cerrar el escenario", ex); }
    }

    /// <summary>Closes it (its assistant leaves at once) and waits a moment for its process.</summary>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _ended, 1);
        if (_server.Send(new ByeMessage("exit")) && _process is { } process)
        {
            try
            {
                using var limit = new CancellationTokenSource(ByeTimeout);
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { Kill(); }
            catch (InvalidOperationException) { }
        }
        else Kill();
        await _server.DisposeAsync().ConfigureAwait(false);
        _process?.Dispose();
        _process = null;
    }

    private void Kill()
    {
        try { _process?.Kill(); } catch { /* already gone */ }
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
}
