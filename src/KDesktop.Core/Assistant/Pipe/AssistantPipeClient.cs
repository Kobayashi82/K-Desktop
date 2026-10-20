using System.IO.Pipes;

namespace KDesktop.Core.Assistant.Pipe;

/// <summary>How a connection attempt ended.</summary>
public enum ConnectOutcome
{
    Welcomed,
    /// <summary>The pipe is not there (K-Desktop is not running or the module is off) or did not answer in time.</summary>
    Unavailable,
    /// <summary>K-Desktop said no (<see cref="AssistantPipeClient.RejectReason"/>).</summary>
    Rejected,
}

/// <summary>
/// The assistant process's end of the pipe: connects, says <c>hello</c> and waits for <c>welcome</c> (or
/// <c>reject</c>); then hands over every message. One connection per instance.
/// </summary>
public sealed class AssistantPipeClient : IAsyncDisposable
{
    private static readonly TimeSpan WelcomeTimeout = TimeSpan.FromSeconds(5);

    private readonly string _name;
    private readonly Heartbeat? _heartbeat;
    private PipeConnection? _connection;

    public AssistantPipeClient(string name, Heartbeat? heartbeat = null)
    {
        _name = name;
        _heartbeat = heartbeat;
    }

    public WelcomeMessage? Welcome { get; private set; }

    public string? RejectReason { get; private set; }

    public bool IsConnected => _connection is { IsOpen: true } && Welcome is not null;

    /// <summary>A message after the welcome. Pool thread, in order.</summary>
    public event Action<PipeMessage>? Received;

    /// <summary>The connection ended after the welcome (with why).</summary>
    public event Action<string>? Disconnected;

    /// <summary>Connects and greets. Waits up to <paramref name="timeout"/> for the pipe to exist.</summary>
    public async Task<ConnectOutcome> ConnectAsync(HelloMessage hello, TimeSpan timeout, CancellationToken token)
    {
        if (_connection is not null) throw new InvalidOperationException("Ya conectado");
        var stream = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await stream.ConnectAsync((int)timeout.TotalMilliseconds, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return ConnectOutcome.Unavailable;
        }

        var connection = new PipeConnection(stream, "K-Desktop", _heartbeat);
        var answer = new TaskCompletionSource<PipeMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var welcomed = false;
        connection.Received += message =>
        {
            if (!welcomed)
            {
                // Here, not after the await: K-Desktop may send the next message right behind the welcome.
                if (message is WelcomeMessage w)
                {
                    Welcome = w;
                    _connection = connection;
                    welcomed = true;
                }
                answer.TrySetResult(message);
                return;
            }
            Received?.Invoke(message);
        };
        connection.Closed += reason =>
        {
            answer.TrySetResult(null);
            if (welcomed) Disconnected?.Invoke(reason);
        };
        connection.Start();
        connection.Send(hello);

        PipeMessage? first;
        try { first = await answer.Task.WaitAsync(WelcomeTimeout, token).ConfigureAwait(false); }
        catch (TimeoutException) { first = null; }

        switch (first)
        {
            case WelcomeMessage:
                if (!connection.IsOpen) return ConnectOutcome.Unavailable;
                return ConnectOutcome.Welcomed;
            case RejectMessage reject:
                RejectReason = reject.Reason;
                await connection.DisposeAsync().ConfigureAwait(false);
                return ConnectOutcome.Rejected;
            default:
                // Closed without a word: K-Desktop turned this process away (it checks who connects before talking).
                RejectReason = "client";
                await connection.DisposeAsync().ConfigureAwait(false);
                return first is null ? ConnectOutcome.Rejected : ConnectOutcome.Unavailable;
        }
    }

    public bool Send(PipeMessage message) => _connection is { } c && c.Send(message);

    public async Task CloseAsync(string reason)
    {
        if (_connection is { } c) await c.CloseAsync(reason, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is { } c) await c.DisposeAsync().ConfigureAwait(false);
        _connection = null;
    }
}
