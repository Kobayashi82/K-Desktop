using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using KDesktop.Core.Native;
using static KDesktop.Core.Assistant.Pipe.PipeSecurityNative;

namespace KDesktop.Core.Assistant.Pipe;

/// <summary>Name and version of the pipe (protocolo.md, «Transporte»).</summary>
public static class AssistantPipe
{
    /// <summary>Version of the protocol this build speaks (2: several assistants at once, each message says whose).</summary>
    public const int Protocol = 2;

    /// <summary>
    /// <c>KDesktop.Assistant.&lt;session&gt;.&lt;folder&gt;</c>: the Windows session and the first 8 hex characters of
    /// the SHA-256 of the data folder, so a test instance with <c>--data</c> never meets the everyday one.
    /// </summary>
    public static string NameFor(string dataDir, int sessionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).TrimEnd('\\').ToLowerInvariant()));
        return $"KDesktop.Assistant.{sessionId}.{Convert.ToHexStringLower(hash)[..8]}";
    }

    /// <summary>The pipe of this data folder and session.</summary>
    public static string Name => NameFor(AppPaths.DataDir, Process.GetCurrentProcess().SessionId);
}

/// <summary>Who may connect: always the user and session of the server; and this executable and process id.</summary>
/// <param name="ExecutablePath">Full path the client must run from (K-Desktop's own exe).</param>
/// <param name="AcceptsProcess">Whether this process id is expected (the one K-Desktop launched, or any with <c>--assistant-dev</c>).</param>
public sealed record ClientPolicy(string ExecutablePath, Func<int, bool> AcceptsProcess);

/// <summary>
/// K-Desktop's end of the pipe (§3.3 of the plan). The pipe is only open to the user (and SYSTEM), with a medium
/// integrity label so the non-elevated assistant can write to it, rejects remote clients and is always the first
/// instance (nobody can create it before and pose as K-Desktop). One client at a time: the next instance is only
/// created when the current connection ends. Each client is checked (executable, user, session, process id) and
/// must say <c>hello</c> with a protocol version this build understands.
/// </summary>
public sealed class AssistantPipeServer : IAsyncDisposable
{
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);

    private readonly string _name;
    private readonly ClientPolicy _policy;
    private readonly Func<WelcomeMessage> _welcome;
    private readonly Heartbeat? _heartbeat;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private volatile PipeConnection? _current;

    public AssistantPipeServer(string name, ClientPolicy policy, Func<WelcomeMessage> welcome, Heartbeat? heartbeat = null)
    {
        _name = name;
        _policy = policy;
        _welcome = welcome;
        _heartbeat = heartbeat;
    }

    /// <summary>A client was accepted and welcomed (with its hello). Pool thread.</summary>
    public event Action<HelloMessage>? Connected;

    /// <summary>A message from the connected client, after the handshake. Pool thread, in order.</summary>
    public event Action<PipeMessage>? Received;

    /// <summary>The welcomed client is gone (with why).</summary>
    public event Action<string>? Disconnected;

    /// <summary>A client was turned away (with why), for the log and the tests.</summary>
    public event Action<string>? Rejected;

    public bool IsConnected => _current is { IsOpen: true };

    public void Start() => _loop ??= Task.Run(() => AcceptLoopAsync(_stop.Token));

    /// <summary>Sends to the connected client. False if there is none.</summary>
    public bool Send(PipeMessage message) => _current is { } c && c.Send(message);

    /// <summary>Says goodbye (sends what is queued) and drops the current client.</summary>
    public async Task DisconnectAsync(string reason)
    {
        if (_current is { } c) await c.CloseAsync(reason, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_current is { } c) await c.DisposeAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { /* logged */ }
        }
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream stream;
            try
            {
                stream = CreatePipe(_name);
                failures = 0;
            }
            catch (Exception ex)
            {
                // Someone else holds the name (a stale instance, or a squatter): try again a bit later.
                Log.Warn($"Tubería del asistente: no se pudo crear ({ex.Message})");
                await DelayAsync(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures++, 5))), token).ConfigureAwait(false);
                continue;
            }

            try
            {
                await stream.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (IOException)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            if (CheckClient(stream) is { } refusal)
            {
                Reject(refusal);
                await stream.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            await ServeAsync(stream, token).ConfigureAwait(false);
        }
    }

    /// <summary>Runs one connection until it ends.</summary>
    private async Task ServeAsync(NamedPipeServerStream stream, CancellationToken token)
    {
        var connection = new PipeConnection(stream, "el asistente", _heartbeat);
        var hello = new TaskCompletionSource<HelloMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var welcomed = false;
        connection.Received += message =>
        {
            if (!welcomed)
            {
                hello.TrySetResult(message as HelloMessage);
                return;
            }
            Received?.Invoke(message);
        };
        connection.Closed += reason =>
        {
            hello.TrySetResult(null);
            ended.TrySetResult(reason);
        };
        connection.Start();

        await using (connection.ConfigureAwait(false))
        {
            var first = await hello.Task.WaitAsync(HelloTimeout, token).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null,
                TaskScheduler.Default).ConfigureAwait(false);
            if (first is null)
            {
                Reject("no ha dicho «hello»");
                return;
            }
            if (first.Protocol != AssistantPipe.Protocol)
            {
                connection.Send(new RejectMessage("protocol"));
                await connection.CloseAsync("protocolo distinto", TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                Reject($"protocolo {first.Protocol} (este K-Desktop habla el {AssistantPipe.Protocol})");
                return;
            }

            // Before the welcome goes: the client may answer it at once.
            welcomed = true;
            _current = connection;
            connection.Send(_welcome());
            Log.Info($"Tubería del asistente: conectado (pid {first.Pid}, SDK {first.Sdk})");
            try { Connected?.Invoke(first); } catch (Exception ex) { Log.Error("Tubería del asistente: error al conectar", ex); }

            string reason;
            try { reason = await ended.Task.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { reason = "K-Desktop cierra la tubería"; }
            _current = null;
            Log.Info($"Tubería del asistente: desconectado ({reason})");
            try { Disconnected?.Invoke(reason); } catch (Exception ex) { Log.Error("Tubería del asistente: error al desconectar", ex); }
        }
    }

    private void Reject(string why)
    {
        Log.Warn($"Tubería del asistente: cliente rechazado ({why})");
        try { Rejected?.Invoke(why); } catch { /* only for tests and the log */ }
    }

    /// <summary>Null when the client is acceptable; else why not.</summary>
    private string? CheckClient(NamedPipeServerStream stream)
    {
        if (!GetNamedPipeClientProcessId(stream.SafePipeHandle, out var pid)) return "no se sabe qué proceso es";
        var path = Win32.GetProcessPath(pid);
        if (path is null || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(_policy.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            return $"pid {pid}: otro ejecutable ({path ?? "desconocido"})";
        if (!ProcessIdToSessionId(pid, out var session) || session != (uint)Process.GetCurrentProcess().SessionId)
            return $"pid {pid}: otra sesión";
        if (UserOf(pid) is not { } user || user != WindowsIdentity.GetCurrent().User)
            return $"pid {pid}: otro usuario";
        if (!_policy.AcceptsProcess((int)pid)) return $"pid {pid}: no es el proceso que se lanzó";
        return null;
    }

    private static SecurityIdentifier? UserOf(uint pid)
    {
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!Win32.OpenProcessToken(process, Win32.TOKEN_QUERY, out var token)) return null;
            try
            {
                using var identity = new WindowsIdentity(token);
                return identity.User;
            }
            finally { Win32.CloseHandle(token); }
        }
        catch { return null; }
        finally { Win32.CloseHandle(process); }
    }

    private static NamedPipeServerStream CreatePipe(string name)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Sin usuario");
        // Full access for SYSTEM and the user only; mandatory label medium + no-write-up.
        var sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;{user})S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, SDDL_REVISION_1, out var descriptor, out _))
            throw new IOException("SDDL: " + Marshal.GetLastPInvokeErrorMessage());
        try
        {
            var attributes = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = descriptor };
            var handle = CreateNamedPipe(@"\\.\pipe\" + name,
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
                PIPE_TYPE_BYTE | PIPE_REJECT_REMOTE_CLIENTS, 1, 64 * 1024, 64 * 1024, 0, in attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeErrorMessage();
                handle.Dispose();
                throw new IOException(error);
            }
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally { LocalFree(descriptor); }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try { await Task.Delay(delay, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
