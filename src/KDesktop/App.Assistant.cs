using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Storage;
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
            supervisor.StateChanged += state => Dispatcher.BeginInvoke(() =>
            {
                if (state != AssistantProcessState.Running)
                {
                    AssistantConnectedAt = null;
                    AssistantStatus = null;
                }
                AssistantChanged?.Invoke();
            });
            supervisor.AssistantFailed += id => Dispatcher.BeginInvoke(() => OnAssistantFailed(id));
            supervisor.Connected += () => OnAssistantConnected(supervisor);
            supervisor.Received += message => Dispatcher.BeginInvoke(() => OnAssistantMessage(message));
            _assistant = supervisor;
            _assistantLook = (Loc.Instance.Language, ThemeService.IsDark);
            Log.Info("Asistente: módulo activado");
            supervisor.Start();
            AssistantChanged?.Invoke();
        }
        else if (!s.AssistantEnabled && _assistant is { } running)
        {
            _assistant = null;
            AssistantStatus = null;
            AssistantConnectedAt = null;
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
        Dispatcher.BeginInvoke(() =>
        {
            AssistantConnectedAt = DateTime.Now;
            AssistantStatus = null;
        });
        AssistantPackages.CleanUp();
        LoadActiveAssistant(supervisor, "startup");
    }

    /// <summary>A message from the assistant process (UI thread).</summary>
    private void OnAssistantMessage(PipeMessage message)
    {
        switch (message)
        {
            case LoadedMessage loaded:
                OnAssistantLoaded(loaded);
                break;
            case StatusMessage status:
                AssistantStatus = status;
                if (status.LastError is { Length: > 0 } error) SetAssistantError(error);
                AssistantChanged?.Invoke();
                break;
            case OpenMessage open:
                ShowMainWindow(w => w.Navigate(open.Page switch
                {
                    "assistant" => "assistant",
                    "assistantSettings" => "settings/assistants",
                    _ => "settings/assistant",
                }));
                break;
            case NotifyMessage notify:
                Ui.ShowOsd(notify.Title.Length > 0 ? $"{notify.Title}: {notify.Text}" : notify.Text, Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(5));
                break;
            case HideMessage when Config.Settings.AssistantActive is { } id:
                var name = AssistantPackages.Find(id)?.Manifest.Name ?? id;
                ActivateAssistant(null);
                Ui.ShowOsd(Loc.T("assistant.hidden", name), Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(5));
                break;
        }
    }

    // ── what Panel → Assistant shows ─────────────────────────────────────

    /// <summary>The last status the assistant process sent (null until it sends one).</summary>
    public StatusMessage? AssistantStatus { get; private set; }

    /// <summary>When the process connected the last time (null while it is not running).</summary>
    public DateTime? AssistantConnectedAt { get; private set; }

    /// <summary>The last thing that went wrong with the module, and when.</summary>
    public (string Text, DateTime At)? AssistantError { get; private set; }

    private void SetAssistantError(string text) => AssistantError = (text, DateTime.Now);

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
        {
            var text = Loc.T("assistant.loadFailed", loaded.Assistant, loaded.Error);
            SetAssistantError(text);
            Ui.ShowOsd(text, Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        }
        AssistantChanged?.Invoke();
    }

    private void OnAssistantFailed(string id)
    {
        if (Config.Settings.AssistantActive == id) Config.Settings.AssistantActive = null;
        var text = Loc.T("assistant.failed", id);
        SetAssistantError(text);
        Ui.ShowOsd(text, Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        AssistantChanged?.Invoke();
    }

    /// <summary>
    /// The tray's «Assistant» submenu, only while the module is on: which one is on the desktop (or none), and the
    /// way to its panel.
    /// </summary>
    private void FillAssistantTrayMenu(System.Windows.Controls.MenuItem menu)
    {
        menu.Items.Clear();
        menu.Visibility = _assistant is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        if (_assistant is null) return;
        var active = Config.Settings.AssistantActive;
        void Add(string? id, string name)
        {
            var item = new System.Windows.Controls.MenuItem { Header = name, IsCheckable = true, IsChecked = id == active };
            item.Click += (_, _) => ActivateAssistant(id);
            menu.Items.Add(item);
        }
        foreach (var row in Views.AssistantLook.Rows(active)) Add(row.Id, row.Name);
        Add(null, Loc.T("assistantOptions.none"));
        menu.Items.Add(new System.Windows.Controls.Separator());
        var panel = new System.Windows.Controls.MenuItem { Header = Loc.T("assistantMenu.panel") };
        panel.Click += (_, _) => ShowMainWindow(w => w.Navigate("assistant"));
        menu.Items.Add(panel);
    }

    /// <summary>Closes the assistant process and starts a new one (Panel → Assistant).</summary>
    public void RestartAssistantProcess()
    {
        if (_assistant is not { } supervisor) return;
        Log.Info("Asistente: reinicio pedido desde el panel");
        supervisor.Restart();
    }

    /// <summary>Shows the card of a package (<c>--install-assistant</c>, «Add assistant», a file dropped on the window).</summary>
    public void OpenAssistantPackage(string file) => AssistantPackageDialog.Open(this, _main, file);

    /// <summary>
    /// After restoring a backup: its assistants that are not installed here. Their data and settings came back with
    /// it, their packages did not; those that say where they are published are downloaded and shown with their card.
    /// </summary>
    public async Task OfferMissingAssistantsAsync(IReadOnlyList<DataArchive.ArchivedAssistant> archived)
    {
        var missing = archived.Where(a => AssistantPackages.Find(a.Id) is null).ToList();
        if (missing.Count == 0) return;
        var downloadable = missing.Where(a => a.Updates is { Length: > 0 }).ToList();
        var list = string.Join(Environment.NewLine, missing.Select(a => $"• {a.Name} {a.Version}"));
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "K-Desktop",
            Content = Loc.T(downloadable.Count > 0 ? "assistant.restoreMissing" : "assistant.restoreMissingManual", list),
            CloseButtonText = Loc.T(downloadable.Count > 0 ? "common.cancel" : "common.close"),
        };
        if (downloadable.Count > 0) box.PrimaryButtonText = Loc.T("assistant.restoreDownload", downloadable.Count);
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

        foreach (var assistant in downloadable)
        {
            Ui.ShowOsd(Loc.T("assistant.downloading", assistant.Name), Core.Engine.OsdKind.Info);
            try
            {
                var file = await AssistantPackages.DownloadAsync(assistant.Updates!, assistant.Name);
                AssistantPackageDialog.Open(this, _main, file, downloaded: true);
            }
            catch (PackageException ex)
            {
                Ui.ShowOsd(ex.Message, Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
            }
        }
    }

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
