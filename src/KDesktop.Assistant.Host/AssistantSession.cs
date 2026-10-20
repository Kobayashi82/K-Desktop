using System.Windows.Threading;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Core;

namespace KDesktop.Assistant.Host;

/// <summary>
/// Everything one run of the assistant process owns: the UI thread (menus, the text box), the engine thread and,
/// as the stages add them, the pipe to K-Desktop, the overlay, the world and the loaded assistant.
/// </summary>
internal sealed class AssistantSession(AssistantOptions options, Dispatcher ui) : IDisposable
{
    private int _shuttingDown;

    public AssistantOptions Options { get; } = options;

    public Dispatcher Ui { get; } = ui;

    public EngineLoop Engine { get; } = new();

    public int ExitCode { get; private set; }

    public void Start()
    {
        Engine.Started += () => Log.Info("Asistente: motor en marcha");
        Engine.Tick = _ => 60; // nothing to do yet: the next stages give it work
        Engine.Start();
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
        Engine.Dispose();
    }
}
