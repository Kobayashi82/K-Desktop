using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Services;
using KDesktop.Views.Dialogs;

namespace KDesktop;

/// <summary>
/// The assistant module from K-Desktop's side. Off (the default) there is nothing: no pipe, no process, no thread.
/// On, an <see cref="AssistantSupervisor"/> runs the assistant process and keeps it alive, and the active assistant
/// (<c>assistantActive</c>) is loaded into it each time it connects.
/// </summary>
public partial class App
{
    /// <summary>Time an unloaded assistant has for its exit before its files are touched.</summary>
    private static readonly TimeSpan ExitTime = TimeSpan.FromSeconds(3.5);

    private AssistantSupervisor? _assistant;
    private (string? Language, bool Dark) _assistantLook;
    private bool? _assistantAssociated;

    /// <summary>The supervisor while the module is on (null when off).</summary>
    public AssistantSupervisor? Assistant => _assistant;

    /// <summary>Raised (UI thread) when the module is turned on or off, its process changes state, or the assistants change.</summary>
    public event Action? AssistantChanged;

    /// <summary>Starts or stops the module after a settings change (and at start-up).</summary>
    private void ApplyAssistant()
    {
        var s = Config.Settings;
        if (_assistantAssociated != s.AssistantEnabled)
        {
            _assistantAssociated = s.AssistantEnabled;
            var on = s.AssistantEnabled;
            var typeName = Loc.T("assistant.fileType");
            _ = Task.Run(() => PackageAssociation.Apply(on, typeName));
        }

        if (s.AssistantEnabled && _assistant is null)
        {
            var supervisor = new AssistantSupervisor(AssistantWelcome);
            supervisor.StateChanged += _ => Dispatcher.BeginInvoke(() => AssistantChanged?.Invoke());
            supervisor.AssistantFailed += id => Dispatcher.BeginInvoke(() => OnAssistantFailed(id));
            supervisor.Connected += () => OnAssistantConnected(supervisor);
            supervisor.Received += message =>
            {
                if (message is LoadedMessage loaded) Dispatcher.BeginInvoke(() => OnAssistantLoaded(loaded));
            };
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

    /// <summary>
    /// A new assistant process said hello (pool thread): what was left to delete goes now that nothing holds it, and
    /// the active assistant comes onto the desktop.
    /// </summary>
    private void OnAssistantConnected(AssistantSupervisor supervisor)
    {
        AssistantPackages.CleanUp();
        LoadActiveAssistant(supervisor, "startup");
    }

    /// <summary>Sends <c>load</c> with the active assistant, if there is one installed. Any thread.</summary>
    private void LoadActiveAssistant(AssistantSupervisor supervisor, string entrance)
    {
        if (Config.Settings.AssistantActive is not { } id) return;
        if (AssistantPackages.Find(id) is not { } installed)
        {
            Log.Warn($"Asistente: «{id}» está elegido pero no instalado");
            Dispatcher.BeginInvoke(() =>
            {
                if (Config.Settings.AssistantActive == id) Config.Settings.AssistantActive = null;
                AssistantChanged?.Invoke();
            });
            return;
        }
        Log.Info($"Asistente: se carga «{id}» {installed.Version}");
        supervisor.Send(new LoadMessage(id, installed.Version, installed.Folder, AssistantPaths.Data(id), entrance));
    }

    private void OnAssistantLoaded(LoadedMessage loaded)
    {
        if (!loaded.Ok)
            Ui.ShowOsd(Loc.T("assistant.loadFailed", loaded.Assistant, loaded.Error), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        AssistantChanged?.Invoke();
    }

    private void OnAssistantFailed(string id)
    {
        if (Config.Settings.AssistantActive == id) Config.Settings.AssistantActive = null;
        Ui.ShowOsd(Loc.T("assistant.failed", id), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        AssistantChanged?.Invoke();
    }

    /// <summary>Shows the card of a package (<c>--install-assistant</c>, «Add assistant», a file dropped on the window).</summary>
    public void OpenAssistantPackage(string file) => AssistantPackageDialog.Open(this, _main, file);

    /// <summary>
    /// Installs a package checked with <see cref="AssistantPackages.Inspect"/>. A new version of the assistant on the
    /// desktop replaces it there at once; <paramref name="activate"/> puts another one on the desktop. Throws
    /// <see cref="PackageException"/>.
    /// </summary>
    public async Task InstallAssistantAsync(PackageInfo package, bool activate)
    {
        // The old version stays loaded meanwhile: the new one goes to a folder of its own, and the old one is
        // deleted once the process lets it go.
        var installed = await Task.Run(() => AssistantPackages.Install(package));
        Ui.ShowOsd(Loc.T("assistant.installed", installed.Manifest.Name), Core.Engine.OsdKind.Success);
        if (Config.Settings.AssistantActive == installed.Id)
        {
            if (_assistant is { IsRunning: true } supervisor) LoadActiveAssistant(supervisor, "switched");
        }
        else if (activate) ActivateAssistant(installed.Id);
        AssistantChanged?.Invoke();
    }

    /// <summary>Puts an installed assistant on the desktop (null: none, the module works without a body).</summary>
    public void ActivateAssistant(string? id)
    {
        var previous = Config.Settings.AssistantActive;
        if (previous == id) return;
        Config.Settings.AssistantActive = id;
        Log.Info($"Asistente: activo «{id ?? "ninguno"}»");
        if (_assistant is { IsRunning: true } supervisor)
        {
            if (id is null)
            {
                supervisor.Send(new UnloadMessage("hiding"));
                supervisor.LoadedAssistant = null;
            }
            else LoadActiveAssistant(supervisor, previous is null ? "startup" : "switched");
        }
        AssistantChanged?.Invoke();
    }

    /// <summary>Removes an assistant (it leaves the desktop first), keeping its data or not.</summary>
    public async Task UninstallAssistantAsync(string id, bool keepData)
    {
        if (Config.Settings.AssistantActive == id) Config.Settings.AssistantActive = null;
        if (_assistant is { IsRunning: true } supervisor && supervisor.LoadedAssistant == id)
        {
            supervisor.Send(new UnloadMessage("uninstalling"));
            supervisor.LoadedAssistant = null;
            await Task.Delay(ExitTime);
        }
        await Task.Run(() => AssistantPackages.Uninstall(id, keepData));
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
