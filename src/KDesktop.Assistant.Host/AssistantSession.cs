using System.Text.Json;
using System.Windows.Threading;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host;

/// <summary>
/// Everything one run of the assistant process owns: the UI thread (menus, the text box), the engine thread with
/// the overlay and the loaded assistant, and the link with K-Desktop.
/// </summary>
internal sealed class AssistantSession : IDisposable
{
    private int _shuttingDown;

    public AssistantSession(AssistantOptions options, Dispatcher ui)
    {
        Options = options;
        Ui = ui;
        // Launched by K-Desktop: its pipe. In development: this data folder's, if K-Desktop is running.
        var pipe = options.Pipe ?? (options.DevFolder is not null ? AssistantPipe.Name : null);
        if (pipe is not null) Link = new KDesktopLink(pipe, optional: options.Pipe is null);
        Runtime = new AssistantRuntime(Engine, Link);
    }

    public AssistantOptions Options { get; }

    public Dispatcher Ui { get; }

    public EngineLoop Engine { get; } = new();

    public KDesktopLink? Link { get; }

    public AssistantRuntime Runtime { get; }

    public int ExitCode { get; private set; }

    public void Start()
    {
        Engine.Started += OnEngineStarted;
        Engine.Stopping += Runtime.Dispose;
        Engine.Tick = Runtime.Tick;
        Engine.Schedule = Runtime.NextTick;
        Engine.Start();

        if (Link is { } link)
        {
            link.Connected += welcome =>
            {
                link.Send(new StatusMessage("idle"));
                Engine.Post(() => Runtime.SetPaused(welcome.Paused));
            };
            link.Received += message => Engine.Post(() => OnMessage(message));
            link.Ended += reason => Shutdown(reason);
            link.Start();
        }
    }

    private void OnEngineStarted()
    {
        Runtime.Start();
        Log.Info("Asistente: motor en marcha");
        if (Options.Probe is { } probe)
            _ = Runtime.LoadAsync(new ProbeAssistant(probe), "probe", AppContext.BaseDirectory, Path.GetTempPath(), default(JsonElement), EntranceKind.Startup);
    }

    /// <summary>A message from K-Desktop, on the engine thread.</summary>
    private void OnMessage(PipeMessage message)
    {
        switch (message)
        {
            case PauseMessage pause:
                Log.Info(pause.Paused ? "Asistente: K-Desktop en pausa" : "Asistente: K-Desktop reanudado");
                Runtime.SetPaused(pause.Paused);
                break;
            case ErrorMessage error:
                Log.Warn($"Asistente: K-Desktop responde con un error ({error.Code}: {error.Message})");
                break;
        }
    }

    /// <summary>
    /// Closes the process: the assistant leaves (its exit, up to 3 s), the engine stops and the UI thread ends. Any
    /// thread but the engine's; only the first call counts.
    /// </summary>
    public void Shutdown(string reason, int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
        Log.Info($"Asistente: cerrando ({reason})");
        ExitCode = exitCode;
        var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.Post(async () =>
        {
            try { await Runtime.UnloadAsync(ExitKind.Shutdown); }
            finally { left.TrySetResult(); }
        });
        left.Task.Wait(TimeSpan.FromSeconds(3.5));
        Engine.Stop(TimeSpan.FromSeconds(3));
        Ui.BeginInvokeShutdown(DispatcherPriority.Normal);
    }

    public void Dispose()
    {
        Shutdown("fin");
        if (Link is { } link)
        {
            // Not on the UI thread's context: it has already stopped.
            try { Task.Run(async () => await link.DisposeAsync()).Wait(TimeSpan.FromSeconds(2)); } catch { /* closing anyway */ }
        }
        Engine.Dispose();
    }
}
