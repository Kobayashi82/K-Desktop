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

    /// <summary>Whether this run of the module already had a process: a new one (restarted, back from a fall) reloads the assistants.</summary>
    private bool _assistantConnectedBefore;
    private (string? Language, bool Dark) _assistantLook;
    private bool? _assistantAssociated;

    /// <summary>Answers the process's <c>embed</c> with the voice's EmbeddingGemma (stage 3.5).</summary>
    private AssistantMeaning? _assistantMeaning;

    /// <summary>The supervisor while the module is on (null when off).</summary>
    public AssistantSupervisor? Assistant => _assistant;

    /// <summary>Raised (UI thread) when the module is turned on or off, its process changes state, or the assistants change.</summary>
    public event Action? AssistantChanged;

    /// <summary>The voice of an answer for the phone that asked (<see cref="SpeechAudioMessage"/>, <see cref="SpeechEndMessage"/>; any thread).</summary>
    internal event Action<PipeMessage>? AssistantSpeech;

    /// <summary>
    /// Where the answers to the orders from the phone sound, by assistant (<see cref="HeardMessage.Speak"/>): set by the web
    /// app just before it sends the order (written or said).
    /// </summary>
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> PhoneSpeak = new(StringComparer.Ordinal);

    /// <summary>Starts or stops the module after a settings change (and at start-up).</summary>
    private void ApplyAssistant()
    {
        var s = Config.Settings;
        // The user's own Piper voices (looked at only when a voice is needed, and again when the folders change).
        Core.Assistant.Voice.PiperModels.UseFolders(s.AssistantPiperFolders);
        Core.Assistant.Voice.KokoroModels.UseFolders(s.AssistantKokoroFolders);
        if (_assistantAssociated != s.AssistantEnabled)
        {
            _assistantAssociated = s.AssistantEnabled;
            var on = s.AssistantEnabled;
            var typeName = Loc.T("assistant.fileType");
            _ = Task.Run(() => PackageAssociation.Apply(on, typeName));
        }

        // K-Desktop is an assistant too (5.0.112): active the first time the module is on (also for those who had it on).
        if (s.AssistantEnabled && !s.AssistantKDesktopAdded)
        {
            s.AssistantKDesktopAdded = true;
            if (!s.AssistantsOnDesktop.Contains(AssistantTargeting.KDesktop)) s.AssistantsOnDesktop.Add(AssistantTargeting.KDesktop);
            Log.Info("Asistente: K-Desktop, activo por primera vez");
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
                    ForgetAssistantNotes();
                    ForgetAssistantKnowledge();
                    ForgetAssistantTools();
                    ForgetAssistantBench();
                    ForgetAssistantVoice();
                    AbandonAssistantQuestions();
                    AbandonChoreographies();
                    ForgetReplies();
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
                if (OnAssistantCommandRequest(supervisor, message)) return;
                // The voice of an answer for the phone: straight to the web server, without the UI thread.
                if (message is SpeechAudioMessage or SpeechEndMessage)
                {
                    AssistantSpeech?.Invoke(message);
                    return;
                }
                Dispatcher.BeginInvoke(() => OnAssistantMessage(message));
            };
            _assistant = supervisor;
            _assistantConnectedBefore = false;
            _assistantLook = (AssistantLanguage, ThemeService.IsDark);
            Log.Info("Asistente: módulo activado");
            supervisor.Start();
            UpdateAssistantInput();
            AssistantChanged?.Invoke();
            // Switched on after K-Desktop already checked its own updates: the assistants' now, not in 6 hours.
            if (Updates.LastCheck is not null && AssistantUpdatesChecked is null) _ = CheckAssistantUpdatesAsync(manual: false);
            else RaiseAssistantUpdates();
        }
        else if (!s.AssistantEnabled && _assistant is { } running)
        {
            _assistant = null;
            AssistantStatus = null;
            AssistantConnectedAt = null;
            _assistantStates.Clear();
            ClearAssistantExtras();
            _pendingExtras.Clear();
            _pendingOrders.Clear();
            ForgetReplies();
            ForgetAssistantBrain();
            ForgetAssistantMemory();
            ForgetAssistantNotes();
            ForgetAssistantKnowledge();
            ForgetAssistantTools();
            ForgetAssistantBench();
            ForgetAssistantVoice();
            AbandonAssistantQuestions();
            AbandonChoreographies();
            Log.Info("Asistente: módulo desactivado");
            ResetAssistantInput();
            _ = running.DisposeAsync().AsTask();
            AssistantChanged?.Invoke();
            RaiseAssistantUpdates();
        }
        else if (_assistant is { } current && _assistantLook != (AssistantLanguage, ThemeService.IsDark))
        {
            // Language and theme reach the assistant in its welcome: a new process takes them.
            _assistantLook = (AssistantLanguage, ThemeService.IsDark);
            current.Restart();
        }
        if (_assistant is { IsRunning: true })
        {
            SendAssistantGeneral();
            SendAssistantBrainConfig();
            SendAssistantToolsConfig();
            SendAssistantVoiceConfig();
            SendAssistantPersonalConfig();
            SendAssistantCommonConfig();
        }
    }

    /// <summary>
    /// The language the assistants speak in: the one of Options → Assistant → General, or K-Desktop's. The assistant
    /// process runs in it (its answers, its voice, the texts of the assistants), whatever the user speaks in.
    /// </summary>
    public string AssistantLanguage =>
        Config.Settings.AssistantLanguage is { Length: > 0 } chosen && Loc.Available().Any(l => l.Code == chosen) ? chosen : Loc.Instance.Language;

    private WelcomeMessage AssistantWelcome()
    {
        var brain = AssistantBrainConfig();
        _assistantBrainSent = brain.ToJsonString();
        var tools = AssistantToolsConfig();
        _assistantToolsSent = tools.ToJsonString();
        var voice = AssistantVoiceConfig();
        _assistantVoiceSent = voice.ToJsonString();
        return new(AssistantLanguage, ThemeService.IsDark ? "dark" : "light", Config.Settings.Paused,
            new() { ["general"] = AssistantGeneral(), ["brain"] = brain, ["tools"] = tools, ["voice"] = voice, ["personal"] = AssistantPersonalWelcome(), ["common"] = AssistantCommonWelcome() });
    }

    /// <summary>
    /// A new assistant process said hello (pool thread): what was left to delete goes now that nothing holds it, and
    /// the assistants on the desktop come onto it, in the order they were put there: with their entrance the first time
    /// (K-Desktop started, the module switched on), reappearing where they were after that (stage 13.1).
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
            var entrance = _assistantConnectedBefore ? LoadMessage.Reloaded : LoadMessage.Startup;
            _assistantConnectedBefore = true;
            foreach (var id in Config.Settings.AssistantsOnDesktop.ToList()) LoadAssistant(supervisor, id, entrance);
            SendPendingOrders(AssistantTargeting.KDesktop);
            // Which voice speaks: it was forgotten when the process went (a new language restarts it); else Options said
            // «not running» until the voice was chosen again.
            AskAssistantVoice("state");
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
                    "assistantVoice" => "settings/speech",
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
            case DroppedMessage dropped:
                OnAssistantDropped(dropped);
                break;
            case TalkMessage talk:
                OnAssistantTalk(talk);
                break;
            case ListenMessage listen:
                OnAssistantListen(listen);
                break;
            case DictateMessage dictate:
                OnAssistantDictate(dictate);
                break;
            case RepliedMessage replied:
                OnAssistantReplied(replied);
                break;
            case ChatLineMessage line:
                OnAssistantChat(line);
                break;
            case MemoryStateMessage memory:
                OnAssistantMemory(memory);
                break;
            case NotesStateMessage notes:
                OnAssistantNotes(notes);
                break;
            case KnowledgeStateMessage knowledge:
                OnAssistantKnowledge(knowledge);
                break;
            case KnowledgeFilesMessage files:
                AssistantKnowledgeFiles?.Invoke(files);
                break;
            case KnowledgeFoundMessage found:
                AssistantKnowledgeFound?.Invoke(found);
                break;
            case ToolsStateMessage tools:
                OnAssistantTools(tools);
                break;
            case ToolTestMessage test:
                OnAssistantToolTest(test);
                break;
            case BenchStateMessage bench:
                OnAssistantBench(bench);
                break;
            case ExtraStateMessage state:
                OnAssistantExtraState(state);
                break;
            case BrainStateMessage brain:
                OnAssistantBrainState(brain);
                break;
            case VoiceStateMessage voiceState:
                OnAssistantVoiceState(voiceState);
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

    /// <summary>The name of an assistant (K-Desktop for the one of the system; its id if it is not installed).</summary>
    public static string AssistantName(string? id) =>
        id is null or "" || AssistantTargeting.IsSystem(id) ? "K-Desktop" : AssistantPackages.Find(id)?.Manifest.Name ?? id;

    /// <summary>An assistant that exists: K-Desktop, or an installed one.</summary>
    public static bool IsAssistant(string id) => AssistantTargeting.IsSystem(id) || AssistantPackages.Find(id) is not null;

    /// <summary>The id the assistant process knows it by: K-Desktop is the one without an id (5.0.112).</summary>
    internal static string? ProcessIdOf(string? id) => AssistantTargeting.IsSystem(id) ? null : id;

    /// <summary>The assistant of a message of the process: without one (null or empty), K-Desktop.</summary>
    internal static string FromProcess(string? id) => id is { Length: > 0 } ? id : AssistantTargeting.KDesktop;

    // ── who is on the desktop ────────────────────────────────────────────

    /// <summary>The assistants on the desktop, in the order they were put there.</summary>
    public IReadOnlyList<string> AssistantsOnDesktop => Config.Settings.AssistantsOnDesktop;

    public bool IsOnDesktop(string id) => Config.Settings.AssistantsOnDesktop.Contains(id);

    /// <summary>
    /// The one on the desktop K-Desktop turns to by itself (showing the commands you run, «Probar»): the last one talked to,
    /// else the first one put there; null with nobody. What the user says or writes always names its assistant.
    /// </summary>
    public string? PickAssistant() => AssistantTargeting.Pick(Config.Settings.AssistantLastTalkedTo, Config.Settings.AssistantsOnDesktop);

    /// <summary>The key to talk to an assistant (5.0.113), or empty.</summary>
    public string TalkHotkeyOf(string id) => Config.Settings.AssistantTalkHotkeys.GetValueOrDefault(id) ?? string.Empty;

    /// <summary>
    /// Gives an assistant its key to talk (empty: none). Refused, with who has it, when another assistant, a command for
    /// every program or K-Desktop itself already answers it.
    /// </summary>
    public IReadOnlyList<string> SetTalkHotkey(string id, string hotkey)
    {
        var key = Core.Input.Hotkey.Normalize(hotkey);
        if (key.Length > 0 && Config.HotkeyUsers(key, exceptAssistant: id) is { Count: > 0 } users) return users;
        var keys = new Dictionary<string, string>(Config.Settings.AssistantTalkHotkeys, StringComparer.Ordinal);
        if (key.Length == 0) keys.Remove(id);
        else keys[id] = key;
        Config.Settings.AssistantTalkHotkeys = keys;
        Log.Info($"Asistente: tecla para hablar con «{id}»: {(key.Length > 0 ? key : "ninguna")}");
        AssistantChanged?.Invoke();
        return [];
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
        // A word has one owner (5.0.109): given to an assistant, it no longer calls K-Desktop's commands.
        if (wakeWordId is not null && Config.Settings.KDesktopWakeWords?.Contains(wakeWordId) == true)
            Config.Settings.KDesktopWakeWords = [.. Config.Settings.KDesktopWakeWords.Where(id => id != wakeWordId)];
        Log.Info($"Asistente: palabra de activación de «{assistant}»: {wakeWordId ?? "ninguna"}");
        AssistantChanged?.Invoke();
    }

    /// <summary>
    /// Sends <c>load</c> with an installed assistant (UI thread). One no longer installed leaves the desktop; K-Desktop is
    /// never loaded (it is the process's own, without a package).
    /// </summary>
    private void LoadAssistant(AssistantSupervisor supervisor, string id, string entrance)
    {
        if (AssistantTargeting.IsSystem(id)) return;
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
        LoadingInLanguage(id);
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
        SendPendingOrders(loaded.Assistant, loaded.Ok);
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
    /// The tray's «Assistant» submenu, only while the module is on: each assistant (K-Desktop first), ticked when it is
    /// active (a click puts it there or hides it), hiding them all and the way to the panel.
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
        // The models out of the graphics card (before a game, say); only when there is something in it
        var freeBrain = new System.Windows.Controls.MenuItem { Header = Loc.T("assistantMenu.freeBrain"), IsEnabled = AssistantBrainInMemory };
        freeBrain.Click += (_, _) => FreeAssistantBrain();
        menu.Items.Add(freeBrain);
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
            if (_assistant is { IsRunning: true } supervisor) LoadAssistant(supervisor, installed.Id, LoadMessage.Reloaded);
        }
        else if (activate) ShowAssistant(installed.Id);
        AssistantChanged?.Invoke();
        FollowInstalledAssistants();
        FollowCatalog();
        return installed;
    }

    /// <summary>Puts an assistant on the desktop (K-Desktop: makes it active), next to those already there.</summary>
    public void ShowAssistant(string id)
    {
        if (IsOnDesktop(id) || !IsAssistant(id)) return;
        Config.Settings.AssistantsOnDesktop.Add(id);
        Log.Info($"Asistente: «{id}» al escritorio");
        if (_assistant is { IsRunning: true } supervisor) LoadAssistant(supervisor, id, LoadMessage.Summoned);
        AssistantChanged?.Invoke();
        // Without a word to call it it is only talked to by writing: said once per run.
        if (_assistant is not null && !HasWakeWord(id, AssistantName(id)) && Core.Voice.VoiceModels.AllInstalled && _wakeTipShown.Add(id))
            Ui.ShowOsd(Loc.T("assistant.wakeTip", AssistantName(id)), Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(6));
    }

    /// <summary>The assistants already told how to call them by voice in this run.</summary>
    private readonly HashSet<string> _wakeTipShown = new(StringComparer.Ordinal);

    /// <summary>Its word is about to be recorded (its card): no tip about it.</summary>
    public void SkipWakeTip(string id) => _wakeTipShown.Add(id);

    /// <summary>Takes an assistant off the desktop (the others stay; with none, there is no assistant).</summary>
    public void HideAssistant(string id)
    {
        if (!Config.Settings.AssistantsOnDesktop.Remove(id)) return;
        Log.Info($"Asistente: «{id}» se esconde");
        CloseReadingSheets(id);
        // K-Desktop has nothing loaded: it only stops what it was saying.
        if (AssistantTargeting.IsSystem(id)) StopAssistant(id);
        else if (_assistant is { IsRunning: true } supervisor) UnloadAssistant(supervisor, id, UnloadMessage.Dismissed);
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
        if (s.AssistantTalkHotkeys.ContainsKey(id)) s.AssistantTalkHotkeys = s.AssistantTalkHotkeys.Where(p => p.Key != id).ToDictionary(p => p.Key, p => p.Value);
        if (s.AssistantWakeWords.ContainsKey(id)) s.AssistantWakeWords = AssistantTargeting.Assign(s.AssistantWakeWords, id, null);
        if (_assistant is { IsRunning: true } supervisor && supervisor.IsLoaded(id))
        {
            UnloadAssistant(supervisor, id, UnloadMessage.Uninstalling);
            await Task.Delay(ExitTime);
        }
        await Task.Run(() => AssistantPackages.Uninstall(id, keepData));
        RefreshExtraNames();
        AssistantChanged?.Invoke();
        FollowInstalledAssistants();
        FollowCatalog();
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
