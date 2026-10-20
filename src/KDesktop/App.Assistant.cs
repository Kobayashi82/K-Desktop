using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Services;

namespace KDesktop;

/// <summary>
/// The assistant module from K-Desktop's side. Off (the default) there is nothing: no pipe, no process, no thread.
/// On, an <see cref="AssistantSupervisor"/> runs the assistant process and keeps it alive.
/// </summary>
public partial class App
{
    private AssistantSupervisor? _assistant;
    private (string? Language, bool Dark) _assistantLook;

    /// <summary>The supervisor while the module is on (null when off).</summary>
    public AssistantSupervisor? Assistant => _assistant;

    /// <summary>Raised (UI thread) when the module is turned on or off, or its process changes state.</summary>
    public event Action? AssistantChanged;

    /// <summary>Starts or stops the module after a settings change (and at start-up).</summary>
    private void ApplyAssistant()
    {
        var s = Config.Settings;
        if (s.AssistantEnabled && _assistant is null)
        {
            var supervisor = new AssistantSupervisor(AssistantWelcome);
            supervisor.StateChanged += _ => Dispatcher.BeginInvoke(() => AssistantChanged?.Invoke());
            supervisor.AssistantFailed += id => Dispatcher.BeginInvoke(() => OnAssistantFailed(id));
            _assistant = supervisor;
            _assistantLook = (Loc.Instance.Language, ThemeService.IsDark);
            Log.Info("Asistente: módulo activado");
            supervisor.Start();
            AssistantChanged?.Invoke();
        }
        else if (!s.AssistantEnabled && _assistant is { } running)
        {
            _assistant = null;
            Log.Info("Asistente: módulo desactivado");
            _ = running.DisposeAsync().AsTask();
            AssistantChanged?.Invoke();
        }
        else if (_assistant is { } current && _assistantLook != (Loc.Instance.Language, ThemeService.IsDark))
        {
            // Language and theme reach the assistant in its welcome: a new process takes them.
            _assistantLook = (Loc.Instance.Language, ThemeService.IsDark);
            current.Restart();
        }
    }

    private WelcomeMessage AssistantWelcome() =>
        new(Loc.Instance.Language, ThemeService.IsDark ? "dark" : "light", Config.Settings.Paused);

    private void OnAssistantFailed(string id)
    {
        if (Config.Settings.AssistantActive == id) Config.Settings.AssistantActive = null;
        Ui.ShowOsd(Loc.T("assistant.failed", id), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        AssistantChanged?.Invoke();
    }

    /// <summary>Closes the assistant process with its goodbye (on exit).</summary>
    private void StopAssistant()
    {
        if (_assistant is not { } running) return;
        _assistant = null;
        try { Task.Run(() => running.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(4)); }
        catch (Exception ex) { Log.Error("Asistente: error al cerrarlo", ex); }
    }
}
