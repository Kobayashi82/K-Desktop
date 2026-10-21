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
/// On, an <see cref="AssistantSupervisor"/> runs the assistant process and keeps it alive, and the assistants on the
/// desktop (<c>assistantsOnDesktop</c>, stage 1.11: several at once) are loaded into it each time it connects.
/// </summary>
public partial class App
{
    /// <summary>Time an unloaded assistant has for its exit before its files are touched.</summary>
    private static readonly TimeSpan ExitTime = TimeSpan.FromSeconds(3.5);

    private AssistantSupervisor? _assistant;
    private (string? Language, bool Dark) _assistantLook;
    private bool? _assistantAssociated;

    /// <summary>Answers the process's <c>embed</c> with the voice's EmbeddingGemma (stage 3.5).</summary>
    private AssistantMeaning? _assistantMeaning;

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
                    _assistantStates.Clear();
                    ClearAssistantExtras();
                    ForgetAssistantBrain();
                    ForgetAssistantMemory();
                    AbandonAssistantQuestions();
                    // A process that fell cannot ask to let the keys go.
                    if (_assistant == supervisor) ResetAssistantInput();
                }
                AssistantChanged?.Invoke();
            });
            supervisor.AssistantFailed += id => Dispatcher.BeginInvoke(() => OnAssistantFailed(id));
            supervisor.Connected += () => OnAssistantConnected(supervisor);
            supervisor.Received += message =>
            {
                // Vectors of meaning take a moment and need no window: on the pool.
                if (message is EmbedMessage embed)
                {
                    _assistantMeaning ??= new AssistantMeaning(Voice.Semantic.Embedder);
                    var meaning = _assistantMeaning;
                    _ = Task.Run(() => supervisor.Send(meaning.Answer(embed)));
                    return;
                }
                Dispatcher.BeginInvoke(() => OnAssistantMessage(message));
            };
            _assistant = supervisor;
            _assistantLook = (Loc.Instance.Language, ThemeService.IsDark);
            Log.Info("Asistente: módulo activado");
            supervisor.Start();
            UpdateAssistantInput();
            AssistantChanged?.Invoke();
        }
        else if (!s.AssistantEnabled && _assistant is { } running)
        {
            _assistant = null;
            AssistantStatus = null;
            AssistantConnectedAt = null;
            _assistantStates.Clear();
            ClearAssistantExtras();
            _pendingExtras.Clear();
            ForgetAssistantBrain();
            ForgetAssistantMemory();
            AbandonAssistantQuestions();
            Log.Info("Asistente: módulo desactivado");
            ResetAssistantInput();
            _ = running.DisposeAsync().AsTask();
            AssistantChanged?.Invoke();
        }
        else if (_assistant is { } current && _assistantLook != (Loc.Instance.Language, ThemeService.IsDark))
        {
            // Language and theme reach the assistant in its welcome: a new process takes them.
            _assistantLook = (Loc.Instance.Language, ThemeService.IsDark);
            current.Restart();
        }
        if (_assistant is { IsRunning: true })
        {
            SendAssistantGeneral();
            SendAssistantBrainConfig();
            SendAssistantToolsConfig();
        }
    }

    private WelcomeMessage AssistantWelcome()
    {
        var brain = AssistantBrainConfig();
        _assistantBrainSent = brain.ToJsonString();
        var tools = AssistantToolsConfig();
        _assistantToolsSent = tools.ToJsonString();
        return new(Loc.Instance.Language, ThemeService.IsDark ? "dark" : "light", Config.Settings.Paused,
            new() { ["general"] = AssistantGeneral(), ["brain"] = brain, ["tools"] = tools });
    }

    /// <summary>
    /// A new assistant process said hello (pool thread): what was left to delete goes now that nothing holds it, and
    /// the assistants on the desktop come onto it, in the order they were put there.
    /// </summary>
    private void OnAssistantConnected(AssistantSupervisor supervisor)
    {
        AssistantPackages.CleanUp();
        Dispatcher.BeginInvoke(() =>
        {
            AssistantConnectedAt = DateTime.Now;
            AssistantStatus = null;
            _assistantStates.Clear();
            ClearAssistantExtras();
            ResetAssistantInput();
            foreach (var id in Config.Settings.AssistantsOnDesktop.ToList()) LoadAssistant(supervisor, id, "startup");
        });
    }

    /// <summary>A message from the assistant process (UI thread).</summary>
    private void OnAssistantMessage(PipeMessage message)
    {
        switch (message)
        {
            case LoadedMessage loaded:
                OnAssistantLoaded(loaded);
                break;
            case StatusMessage { Assistant: { } id } status:
                _assistantStates[id] = status;
                AssistantChanged?.Invoke();
                break;
            case StatusMessage status:
                AssistantStatus = status;
                if (status.LastError is { Length: > 0 } error) SetAssistantError(error);
                AssistantChanged?.Invoke();
                break;
            case OpenMessage open:
                ShowMainWindow(w => w.Navigate(open.Page switch
                {
                    "assistant" when open.Assistant is { } id => "assistant/" + id,
                    "assistant" => "assistant",
                    "assistantSettings" when open.Assistant is { } id => "settings/assistants/" + id,
                    "assistantSettings" => "settings/assistants",
                    _ => "settings/assistant",
                }));
                break;
            case NotifyMessage notify:
                Ui.ShowOsd(notify.Title.Length > 0 ? $"{notify.Title}: {notify.Text}" : notify.Text, Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(5));
                break;
            case PanelMessage panel:
                OnAssistantPanel(panel);
                break;
            case KeysMessage keys when IsOnDesktop(keys.Assistant):
                OnAssistantKeys(keys);
                break;
            case WatchWheelMessage watch:
                OnAssistantWatchWheel(watch);
                break;
            case MenuMessage menu:
                OnAssistantMenu(menu);
                break;
            case WriteMessage write:
                OnAssistantWrite(write);
                break;
            case ChatLineMessage line:
                OnAssistantChat(line);
                break;
            case MemoryStateMessage memory:
                OnAssistantMemory(memory);
                break;
            case ExtraStateMessage state:
                OnAssistantExtraState(state);
                break;
            case BrainStateMessage brain:
                OnAssistantBrainState(brain);
                break;
            case AskMessage ask:
                OnAssistantAsk(ask);
                break;
            case LogMessage log:
                OnAssistantLog(log);
                break;
            case HideMessage hide when IsOnDesktop(hide.Assistant):
                HideAssistant(hide.Assistant);
                Ui.ShowOsd(Loc.T("assistant.hidden", AssistantName(hide.Assistant)), Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(5));
                break;
        }
    }

    // ── what Panel → Assistant shows ─────────────────────────────────────

    /// <summary>What each assistant last said of its state (<c>status</c> with its id), until the process ends.</summary>
    private readonly Dictionary<string, StatusMessage> _assistantStates = new(StringComparer.Ordinal);

    /// <summary>The last status of the assistant process itself (null until it sends one).</summary>
    public StatusMessage? AssistantStatus { get; private set; }

    /// <summary>The last status an assistant sent of itself, or null.</summary>
    public StatusMessage? AssistantStateOf(string id) => _assistantStates.GetValueOrDefault(id);

    /// <summary>When the process connected the last time (null while it is not running).</summary>
    public DateTime? AssistantConnectedAt { get; private set; }

    /// <summary>The last thing that went wrong with the module, and when.</summary>
    public (string Text, DateTime At)? AssistantError { get; private set; }

    private void SetAssistantError(string text) => AssistantError = (text, DateTime.Now);

    /// <summary>The name of an installed assistant (its id if it is not installed).</summary>
    public static string AssistantName(string id) => AssistantPackages.Find(id)?.Manifest.Name ?? id;

    // ── who is on the desktop ────────────────────────────────────────────

    /// <summary>The assistants on the desktop, in the order they were put there.</summary>
    public IReadOnlyList<string> AssistantsOnDesktop => Config.Settings.AssistantsOnDesktop;

    public bool IsOnDesktop(string id) => Config.Settings.AssistantsOnDesktop.Contains(id);

    /// <summary>
    /// The assistant for something not addressed to anyone (Options → Assistants decides), or null with nobody on the
    /// desktop. Once per order: «at random» picks again on every call.
    /// </summary>
    public string? PickAssistant()
    {
        var s = Config.Settings;
        return AssistantTargeting.Pick(s.AssistantTarget, s.AssistantTargetId, s.AssistantLastTalkedTo, s.AssistantsOnDesktop);
    }

    /// <summary>The user talked to this assistant (its wake word; later, its talk click and its chat).</summary>
    public void AssistantTalkedTo(string id)
    {
        if (Config.Settings.AssistantLastTalkedTo != id) Config.Settings.AssistantLastTalkedTo = id;
    }

    /// <summary>A wake word was heard (UI thread): if it is an assistant's, that is the one being talked to.</summary>
    private void OnAssistantWakeWord(string wakeWordId)
    {
        if (_assistant is null || AssistantTargeting.ForWakeWord(Config.Settings.AssistantWakeWords, wakeWordId) is not { } id) return;
        if (IsOnDesktop(id)) AssistantTalkedTo(id);
    }

    /// <summary>Gives an assistant its wake word (taken from any other one), or none.</summary>
    public void SetAssistantWakeWord(string assistant, string? wakeWordId)
    {
        Config.Settings.AssistantWakeWords = AssistantTargeting.Assign(Config.Settings.AssistantWakeWords, assistant, wakeWordId);
        Log.Info($"Asistente: palabra de activación de «{assistant}»: {wakeWordId ?? "ninguna"}");
        AssistantChanged?.Invoke();
    }

    /// <summary>Sends <c>load</c> with an installed assistant (UI thread). One no longer installed leaves the desktop.</summary>
    private void LoadAssistant(AssistantSupervisor supervisor, string id, string entrance)
    {
        if (AssistantPackages.Find(id) is not { } installed)
        {
            Log.Warn($"Asistente: «{id}» está en el escritorio pero no instalado");
            Config.Settings.AssistantsOnDesktop.Remove(id);
            AssistantChanged?.Invoke();
            return;
        }
        Log.Info($"Asistente: se carga «{id}» {installed.Version}");
        ForgetAssistantPanel(id);
        ForgetAssistantExtras(id);
        supervisor.Send(new LoadMessage(id, installed.Version, installed.Folder, AssistantPaths.Data(id), entrance));
    }

    /// <summary>Sends <c>unload</c> for an assistant (it leaves with its exit; the others stay).</summary>
    private void UnloadAssistant(AssistantSupervisor supervisor, string id, string reason)
    {
        ReleaseAssistantKeys(id);
        supervisor.Send(new UnloadMessage(id, reason));
        supervisor.MarkUnloaded(id);
    }

    private void OnAssistantLoaded(LoadedMessage loaded)
    {
        if (!loaded.Ok)
        {
            ReleaseAssistantKeys(loaded.Assistant);
            // It is not on the desktop (it would only show «coming out…» for ever); putting it there again retries.
            Config.Settings.AssistantsOnDesktop.Remove(loaded.Assistant);
            var text = Loc.T("assistant.loadFailed", AssistantName(loaded.Assistant), loaded.Error);
            SetAssistantError(text);
            Ui.ShowOsd(text, Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        }
        if (loaded.Ok) SendAssistantExtraKeys();
        SendPendingExtra(loaded);
        AssistantChanged?.Invoke();
    }

    /// <summary>It made the process fall again and again: it leaves the desktop (the others come back without it).</summary>
    private void OnAssistantFailed(string id)
    {
        Config.Settings.AssistantsOnDesktop.Remove(id);
        ReleaseAssistantKeys(id);
        var text = Loc.T("assistant.failed", AssistantName(id));
        SetAssistantError(text);
        Ui.ShowOsd(text, Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        AssistantChanged?.Invoke();
    }

    /// <summary>
    /// The tray's «Assistant» submenu, only while the module is on: each installed assistant, ticked when it is on the
    /// desktop (a click puts it there or hides it), hiding them all, writing to whoever answers (also without assistants:
    /// the module without a body) and the way to the panel.
    /// </summary>
    private void FillAssistantTrayMenu(System.Windows.Controls.MenuItem menu)
    {
        menu.Items.Clear();
        menu.Visibility = _assistant is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        if (_assistant is null) return;
        foreach (var row in Views.AssistantLook.Rows(this))
        {
            var item = new System.Windows.Controls.MenuItem { Header = row.Name, IsCheckable = true, IsChecked = row.IsOnDesktop };
            var id = row.Id;
            item.Click += (_, _) =>
            {
                if (IsOnDesktop(id)) HideAssistant(id);
                else ShowAssistant(id);
            };
            menu.Items.Add(item);
        }
        if (AssistantsOnDesktop.Count > 0)
        {
            var hideAll = new System.Windows.Controls.MenuItem { Header = Loc.T("assistantOptions.hideAll") };
            hideAll.Click += (_, _) => HideAllAssistants();
            menu.Items.Add(hideAll);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new System.Windows.Controls.Separator());
        var who = PickAssistant();
        var write = new System.Windows.Controls.MenuItem { Header = Loc.T("assistantMenu.write", who is null ? "K-Desktop" : AssistantName(who)) };
        write.Click += (_, _) => ShowAssistantWriteBox(who, null);
        menu.Items.Add(write);
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
    /// desktop replaces it there at once; <paramref name="activate"/> puts another one on the desktop. Returns what was
    /// installed. Throws <see cref="PackageException"/>.
    /// </summary>
    public async Task<InstalledAssistant> InstallAssistantAsync(PackageInfo package, bool activate)
    {
        // The old version stays loaded meanwhile: the new one goes to a folder of its own, and the old one is
        // deleted once the process lets it go.
        var installed = await Task.Run(() => AssistantPackages.Install(package));
        Ui.ShowOsd(Loc.T("assistant.installed", installed.Manifest.Name), Core.Engine.OsdKind.Success);
        RefreshExtraNames();
        if (IsOnDesktop(installed.Id))
        {
            if (_assistant is { IsRunning: true } supervisor) LoadAssistant(supervisor, installed.Id, "switched");
        }
        else if (activate) ShowAssistant(installed.Id);
        AssistantChanged?.Invoke();
        return installed;
    }

    /// <summary>Puts an installed assistant on the desktop, next to those already there.</summary>
    public void ShowAssistant(string id)
    {
        if (IsOnDesktop(id) || AssistantPackages.Find(id) is null) return;
        Config.Settings.AssistantsOnDesktop.Add(id);
        Log.Info($"Asistente: «{id}» al escritorio");
        if (_assistant is { IsRunning: true } supervisor) LoadAssistant(supervisor, id, "startup");
        AssistantChanged?.Invoke();
    }

    /// <summary>Takes an assistant off the desktop (the others stay; with none, the module works without a body).</summary>
    public void HideAssistant(string id)
    {
        if (!Config.Settings.AssistantsOnDesktop.Remove(id)) return;
        Log.Info($"Asistente: «{id}» se esconde");
        if (_assistant is { IsRunning: true } supervisor) UnloadAssistant(supervisor, id, "hiding");
        AssistantChanged?.Invoke();
    }

    public void HideAllAssistants()
    {
        foreach (var id in AssistantsOnDesktop.ToList()) HideAssistant(id);
    }

    /// <summary>Removes an assistant (it leaves the desktop first), keeping its data or not.</summary>
    public async Task UninstallAssistantAsync(string id, bool keepData)
    {
        var s = Config.Settings;
        s.AssistantsOnDesktop.Remove(id);
        if (s.AssistantLastTalkedTo == id) s.AssistantLastTalkedTo = null;
        if (s.AssistantTarget == Core.Model.AssistantTarget.Fixed && s.AssistantTargetId == id)
        {
            s.AssistantTarget = Core.Model.AssistantTarget.LastTalkedTo;
            s.AssistantTargetId = null;
        }
        if (s.AssistantWakeWords.ContainsKey(id)) s.AssistantWakeWords = AssistantTargeting.Assign(s.AssistantWakeWords, id, null);
        if (_assistant is { IsRunning: true } supervisor && supervisor.IsLoaded(id))
        {
            UnloadAssistant(supervisor, id, "uninstalling");
            await Task.Delay(ExitTime);
        }
        await Task.Run(() => AssistantPackages.Uninstall(id, keepData));
        RefreshExtraNames();
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
