using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Pipe;

/// <summary>
/// The assistant process's link with K-Desktop. Started by K-Desktop (<c>--pipe</c>), it connects (retrying while
/// K-Desktop gets the pipe ready) and lives as long as the link: a <c>bye</c> or a broken pipe ends the process.
/// With <c>--assistant-dev</c> it is optional: it keeps trying and reconnects when K-Desktop comes back.
/// </summary>
internal sealed class KDesktopLink : IAsyncDisposable
{
    /// <summary>How long a launched process waits for K-Desktop's pipe before giving up.</summary>
    private static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DevRetry = TimeSpan.FromSeconds(2);

    private readonly string _pipe;
    private readonly bool _optional;
    private readonly CancellationTokenSource _stop = new();
    private AssistantPipeClient? _client;
    private Task? _run;

    /// <param name="optional">Development: K-Desktop may not be running; keep trying and never end the process.</param>
    public KDesktopLink(string pipe, bool optional)
    {
        _pipe = pipe;
        _optional = optional;
    }

    public bool IsConnected => _client is { IsConnected: true };

    /// <summary>K-Desktop's interface language and theme («light» / «dark»), from its welcome.</summary>
    public string Language { get; private set; } = Loc.Instance.Language;

    public string Theme { get; private set; } = "light";

    public bool Paused { get; private set; }

    /// <summary>Welcomed (also after a reconnection in development). Pool thread.</summary>
    public event Action<WelcomeMessage>? Connected;

    /// <summary>A message from K-Desktop. Pool thread, in order.</summary>
    public event Action<PipeMessage>? Received;

    /// <summary>The process should end (with why): bye, link lost, or K-Desktop never answered.</summary>
    public event Action<string>? Ended;

    public void Start() => _run ??= RunAsync(_stop.Token);

    public bool Send(PipeMessage message) => _client is { } c && c.Send(message);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_client is { } c) await c.DisposeAsync().ConfigureAwait(false);
        if (_run is not null)
        {
            try { await _run.ConfigureAwait(false); } catch { /* logged */ }
        }
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var deadline = DateTime.UtcNow + StartupWindow;
        var everConnected = false;
        while (!token.IsCancellationRequested)
        {
            var client = new AssistantPipeClient(_pipe);
            var lost = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Received += OnReceived;
            client.Disconnected += reason => lost.TrySetResult(reason);
            var hello = new HelloMessage(AssistantPipe.Protocol, SdkVersion, Environment.ProcessId);
            var outcome = await client.ConnectAsync(hello, TimeSpan.FromSeconds(1), token).ConfigureAwait(false);

            if (outcome == ConnectOutcome.Welcomed)
            {
                _client = client;
                everConnected = true;
                OnWelcome(client.Welcome!);
                var reason = await lost.Task.WaitAsync(token).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : "cerrando",
                    TaskScheduler.Default).ConfigureAwait(false);
                _client = null;
                await client.DisposeAsync().ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                Log.Info($"Asistente: sin conexión con K-Desktop ({reason})");
                if (!_optional)
                {
                    Ended?.Invoke("K-Desktop ya no está");
                    return;
                }
                continue;
            }

            await client.DisposeAsync().ConfigureAwait(false);
            if (outcome == ConnectOutcome.Rejected)
            {
                Log.Warn($"Asistente: K-Desktop ha rechazado la conexión ({client.RejectReason})");
                // Launched: nothing to do without K-Desktop. In development: go on alone, without asking again.
                if (!_optional) Ended?.Invoke("rechazado por K-Desktop");
                return;
            }
            else if (!_optional && !everConnected && DateTime.UtcNow > deadline)
            {
                Log.Warn("Asistente: K-Desktop no ha abierto la tubería a tiempo");
                Ended?.Invoke("sin K-Desktop");
                return;
            }

            try { await Task.Delay(_optional ? DevRetry : TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static string SdkVersion =>
        typeof(Sdk.IAssistant).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "1.0";

    private void OnWelcome(WelcomeMessage welcome)
    {
        Theme = welcome.Theme;
        Paused = welcome.Paused;
        if (!string.Equals(welcome.Lang, Loc.Instance.Language, StringComparison.OrdinalIgnoreCase)) Loc.Instance.Load(welcome.Lang);
        Language = Loc.Instance.Language;
        Log.Info($"Asistente: conectado con K-Desktop (idioma {Language}, tema {Theme}{(Paused ? ", en pausa" : "")})");
        Connected?.Invoke(welcome);
    }

    private void OnReceived(PipeMessage message)
    {
        switch (message)
        {
            case ByeMessage bye:
                Log.Info($"Asistente: K-Desktop pide cerrar ({bye.Reason})");
                Ended?.Invoke("bye: " + bye.Reason);
                return;
            case PauseMessage pause:
                Paused = pause.Paused;
                break;
        }
        Received?.Invoke(message);
    }
}
