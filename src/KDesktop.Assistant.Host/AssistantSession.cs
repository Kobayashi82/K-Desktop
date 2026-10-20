using System.Windows.Threading;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host;

/// <summary>
/// Everything one run of the assistant process owns: the UI thread (menus, the text box), the engine thread, the
/// link with K-Desktop and, as the stages add them, the overlay, the world and the loaded assistant.
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
    }

    public AssistantOptions Options { get; }

    public Dispatcher Ui { get; }

    public EngineLoop Engine { get; } = new();

    public KDesktopLink? Link { get; }

    public int ExitCode { get; private set; }

    public void Start()
    {
        Engine.Started += () => Log.Info("Asistente: motor en marcha");
        Engine.Tick = _ => 60; // nothing to do yet: the next stages give it work
        Engine.Start();

        if (Link is { } link)
        {
            link.Connected += _ => link.Send(new StatusMessage("idle"));
            link.Received += message => Engine.Post(() => OnMessage(message));
            link.Ended += reason => Shutdown(reason);
            link.Start();
        }
    }

    /// <summary>A message from K-Desktop, on the engine thread.</summary>
    private static void OnMessage(PipeMessage message)
    {
        switch (message)
        {
            case PauseMessage pause:
                Log.Info(pause.Paused ? "Asistente: K-Desktop en pausa" : "Asistente: K-Desktop reanudado");
                break;
            case ErrorMessage error:
                Log.Warn($"Asistente: K-Desktop responde con un error ({error.Code}: {error.Message})");
                break;
        }
    }

    /// <summary>Closes the process: stops the engine and ends the UI thread. Any thread; only the first call counts.</summary>
    public void Shutdown(string reason, int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
        Log.Info($"Asistente: cerrando ({reason})");
        ExitCode = exitCode;
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
