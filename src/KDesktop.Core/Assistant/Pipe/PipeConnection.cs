using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace KDesktop.Core.Assistant.Pipe;

/// <summary>
/// One end of an established pipe (either side): reads and writes messages as lines and keeps the heartbeat. After
/// <see cref="Heartbeat.Idle"/> without receiving anything it sends <c>ping</c> (and answers the other's with
/// <c>pong</c>); after <see cref="Heartbeat.Dead"/> without anything the connection is over. Writes are queued, so
/// <see cref="Send"/> never blocks the caller.
/// </summary>
public sealed class PipeConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly string _peer;
    private readonly Heartbeat _heartbeat;
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private long _lastReceived = Environment.TickCount64;
    private int _closed;
    private Task? _run;
    private Task? _writer;
    private string? _reason;

    /// <param name="peer">For the log: «asistente» or «K-Desktop».</param>
    public PipeConnection(Stream stream, string peer, Heartbeat? heartbeat = null)
    {
        _stream = stream;
        _peer = peer;
        _heartbeat = heartbeat ?? Heartbeat.Default;
    }

    /// <summary>A message arrived (on a pool thread, one at a time, in order). Ping and pong are handled here.</summary>
    public event Action<PipeMessage>? Received;

    /// <summary>The connection is over (with why). Raised once.</summary>
    public event Action<string>? Closed;

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    /// <summary>Starts reading, writing and the heartbeat.</summary>
    public void Start() => _run ??= RunAsync();

    /// <summary>Queues a message. False once the connection is closed.</summary>
    public bool Send(PipeMessage message) => IsOpen && _outgoing.Writer.TryWrite(PipeJson.Serialize(message));

    /// <summary>Sends what is queued (up to <paramref name="timeout"/>) and closes.</summary>
    public async Task CloseAsync(string reason, TimeSpan timeout)
    {
        _outgoing.Writer.TryComplete();
        if (_run is not null) await Task.WhenAny(_writer ?? Task.CompletedTask, Task.Delay(timeout)).ConfigureAwait(false);
        Close(reason);
    }

    public async ValueTask DisposeAsync()
    {
        Close("cerrada");
        if (_run is not null)
        {
            try { await _run.ConfigureAwait(false); } catch { /* already logged */ }
        }
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        var token = _stop.Token;
        var reader = Task.Run(() => ReadLoopAsync(token), CancellationToken.None);
        _writer = Task.Run(() => WriteLoopAsync(token), CancellationToken.None);
        var heart = Task.Run(() => HeartbeatLoopAsync(token), CancellationToken.None);
        await Task.WhenAny(reader, _writer, heart).ConfigureAwait(false);
        Close(_reason ?? "terminada");
        await Task.WhenAll(reader.ContinueWith(_ => { }, TaskScheduler.Default),
            _writer.ContinueWith(_ => { }, TaskScheduler.Default), heart.ContinueWith(_ => { }, TaskScheduler.Default)).ConfigureAwait(false);
    }

    private void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _reason = reason;
        _outgoing.Writer.TryComplete();
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        try { _stream.Dispose(); } catch { /* already broken */ }
        try { Closed?.Invoke(reason); } catch (Exception ex) { Log.Error("Tubería: error al cerrar", ex); }
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        var line = new MemoryStream();
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0)
                {
                    Close($"{_peer} ha cerrado la tubería");
                    return;
                }
                Volatile.Write(ref _lastReceived, Environment.TickCount64);
                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;
                    line.Write(buffer, start, i - start);
                    start = i + 1;
                    if (!Deliver(line)) return;
                    line.SetLength(0);
                }
                line.Write(buffer, start, read - start);
                if (line.Length > PipeJson.MaxLine)
                {
                    Close($"{_peer} ha mandado una línea de más de 1 MiB");
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Close($"tubería rota ({ex.Message})");
        }
    }

    /// <summary>One whole line. False when the connection must end.</summary>
    private bool Deliver(MemoryStream line)
    {
        if (line.Length > PipeJson.MaxLine)
        {
            Close($"{_peer} ha mandado una línea de más de 1 MiB");
            return false;
        }
        if (line.Length == 0) return true;
        var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
        PipeMessage message;
        try { message = PipeJson.Parse(text); }
        catch (JsonException ex)
        {
            Log.Warn($"Tubería: mensaje no válido de {_peer}: {ex.Message}");
            // Never answer a broken «error» with another one: the two sides would bounce them forever.
            if (!IsError(text)) Send(new ErrorMessage("badRequest", ex.Message));
            return true;
        }
        switch (message)
        {
            case PingMessage:
                Send(new PongMessage());
                return true;
            case PongMessage:
                return true;
            case UnknownMessage unknown:
                Log.Info($"Tubería: «{unknown.Type}» de {_peer} no se conoce en esta versión (se ignora)");
                return true;
        }
        try { Received?.Invoke(message); }
        catch (Exception ex) { Log.Error($"Tubería: error atendiendo «{message.Type}» de {_peer}", ex); }
        return true;
    }

    private static bool IsError(string text)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(text)?["t"]?.GetValue<string>() == "error"; }
        catch { return false; }
    }

    private async Task WriteLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (var line in _outgoing.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await _stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await _stream.FlushAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Close($"tubería rota al escribir ({ex.Message})");
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        var pinged = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_heartbeat.Check, token).ConfigureAwait(false);
                var quiet = TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref _lastReceived));
                if (quiet >= _heartbeat.Dead)
                {
                    Close($"{_peer} no responde ({quiet.TotalSeconds:0} s sin mensajes)");
                    return;
                }
                if (quiet >= _heartbeat.Idle)
                {
                    if (!pinged) Send(new PingMessage());
                    pinged = true;
                }
                else pinged = false;
            }
        }
        catch (OperationCanceledException) { }
    }
}

/// <summary>Times of the heartbeat (protocolo.md: ping after 5 s of silence, dead after 15 s).</summary>
public sealed record Heartbeat(TimeSpan Idle, TimeSpan Dead, TimeSpan Check)
{
    public static Heartbeat Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(1));
}
