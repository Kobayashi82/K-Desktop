using System.IO.Pipes;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Mcp;
using KDesktop.Core;
using KDesktop.Core.Assistant.Sharing;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The tools for other programs (phase 12, <see cref="McpShare"/>): while <c>config</c> <c>tools</c> <c>share</c> says so,
/// the pipe is open for the bridges (<c>KDesktop.exe --mcp</c>); each one says the key first and then is served by
/// <see cref="SharedTools"/>. Switched off, the pipe closes and the connected programs are let go.
/// </summary>
internal sealed class ModuleShare : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _running;
    private string _keyHash = string.Empty;
    /// <summary>Without the key: any program of this user gets in.</summary>
    private bool _open;

    public ModuleShare(ToolRunner runner)
    {
        Tools = new SharedTools(runner);
        runner.Registry.Changed += Tools.NotifyChanged;
    }

    public SharedTools Tools { get; }

    /// <summary>Sharing is on.</summary>
    public bool On { get; private set; }

    /// <summary>The section <c>share</c>: <c>enabled</c>, <c>key</c> (its hash), <c>open</c> (no key needed) and <c>scope</c>.</summary>
    public void Configure(JsonObject? share)
    {
        var enabled = share?["enabled"] is JsonValue e && e.TryGetValue<bool>(out var on) && on;
        var key = share?["key"] is JsonValue k && k.TryGetValue<string>(out var hash) ? hash : string.Empty;
        var open = share?["open"] is JsonValue o && o.TryGetValue<bool>(out var free) && free;
        var scope = share?["scope"] is JsonValue s && s.TryGetValue<string>(out var value) && value is McpShare.ScopeAll or McpShare.ScopeFree ? value : McpShare.ScopeRead;
        var scopeChanged = scope != Tools.Scope;
        Tools.Scope = scope;
        lock (_gate)
        {
            var keyChanged = key != _keyHash;
            _keyHash = key;
            _open = open;
            var wanted = enabled && (open || key.Length > 0);
            if (wanted && (_running is null || keyChanged))
            {
                Stop();
                _running = new CancellationTokenSource();
                var token = _running.Token;
                _ = Task.Run(() => ListenAsync(token));
                Log.Info($"Asistente: herramientas compartidas por MCP ({scope}{(open ? ", sin clave" : string.Empty)})");
            }
            else if (!wanted && _running is not null)
            {
                Stop();
                Log.Info("Asistente: herramientas ya no compartidas");
            }
            On = wanted;
        }
        if (scopeChanged) Tools.NotifyChanged();
    }

    private void Stop()
    {
        _running?.Cancel();
        _running?.Dispose();
        _running = null;
    }

    private async Task ListenAsync(CancellationToken token)
    {
        var first = true;
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = McpShare.CreatePipe(McpShare.PipeName, first);
                first = false;
                failures = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Warn($"Asistente: no se pudo abrir la tubería de las herramientas compartidas ({ex.Message})");
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures++, 5))), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                continue;
            }
            // The next one may connect while this one is served.
            _ = ServeAsync(pipe, token);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                var key = await McpShare.ReadHelloAsync(pipe, token).ConfigureAwait(false);
                string hash;
                bool open;
                lock (_gate) (hash, open) = (_keyHash, _open);
                // Without the key, the hello line is still said (with any key, or none).
                if (key is null || !open && !McpShare.Matches(key, hash))
                {
                    Log.Warn("Asistente: un programa ha querido usar las herramientas con una clave que no vale");
                    return;
                }
                await Tools.ServeAsync(pipe, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            catch (Exception ex)
            {
                Log.Error("Asistente: error sirviendo las herramientas compartidas", ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) Stop();
    }
}
