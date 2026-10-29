using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Host;

/// <summary>
/// Everything one run of the assistant process owns: the UI thread (menus, the text box), the engine thread with
/// the overlay and the assistants on the desktop, and the link with K-Desktop.
/// </summary>
internal sealed class AssistantSession : IDisposable
{
    private int _shuttingDown;
    private DevLoader? _dev;
    /// <summary>K-Desktop's loads and unloads, one after another (engine thread): an unload then a load of the same one keep their order.</summary>
    private Task _changes = Task.CompletedTask;

    public AssistantSession(AssistantOptions options, Dispatcher ui)
    {
        Options = options;
        Ui = ui;
        // Launched by K-Desktop: its pipe. In development: this data folder's, if K-Desktop is running.
        var pipe = options.Pipe ?? (options.DevFolder is not null ? AssistantPipe.Name : null);
        if (pipe is not null) Link = new KDesktopLink(pipe, optional: options.Pipe is null);
        Runtime = new AssistantRuntime(Engine, Link);
        // The brain answers Options → Assistant → Brain: only with K-Desktop there.
        if (Link is not null) Brain = new BrainService(World.VideoMemory.Gigabytes);
        // How things went, shown with a reaction that knows where it comes from (stage 13.3).
        var reactions = Link is null ? null : new ModuleReactions(Engine, Runtime, new ReactionLog(Path.Combine(AppPaths.DataDir, "Assistant", "reactions.json")));
        var choreography = Link is null ? null : new ModuleChoreography(Link, Engine, Runtime) { Reactions = reactions };
        Choreography = choreography;
        if (Link is not null) Commands = new ModuleCommands(Link) { Choreography = choreography, HasBody = Runtime.HasBody };
        var sight = new ModuleSight(Runtime.WindowServices);
        if (Link is not null) Tools = new ModuleTools(Link, Commands!, sight, new ModelVision(Brain!), new ModelWriter(Brain!),
            new ModuleBrowser(Path.Combine(AppPaths.DataDir, "Assistant", "Browser")));
        // What its tools do is seen too (stage 13.5): a beacon by the clock, waiting for what opens, scanning what it reads…
        if (choreography is not null)
        {
            choreography.Front = sight.UserWindow;
            Tools!.Runner.Showing += choreography.Show;
        }
        Runtime.LanguageOf = Personal.LanguageOf;
        if (Link is not null) Voice = new ModuleVoice(Engine, Runtime, Personal);
        if (Link is not null) Talk = new ModuleTalk(Link, Engine, Runtime, Brain!, Tools!, Commands!, Voice!, Personal, reactions!) { Choreography = choreography };
        if (Link is not null) Bench = new ModuleBench(Link, Brain!, Tools!, Talk!.Chooser, Commands!);
        if (Link is not null) Games = new ModuleGames(Brain!, Talk!.Service);
        // Reading the user's documents waits for games, pauses and answers (stage 19).
        if (Link is not null) Tools!.Builtin.Knowledge.WaitReason = new KnowledgePace(Talk!).WaitReason;
    }

    /// <summary>A game on the whole screen frees the graphics card (stage 12; null without K-Desktop).</summary>
    public ModuleGames? Games { get; }

    /// <summary>The user's K-Desktop commands (null without K-Desktop).</summary>
    public ModuleCommands? Commands { get; }

    /// <summary>Options → Brain → Test the model (null without K-Desktop).</summary>
    public ModuleBench? Bench { get; }

    /// <summary>What each assistant has of its own: language, personality, voice (5.0.105).</summary>
    public ModulePersonal Personal { get; } = new();

    /// <summary>Their voice (null without K-Desktop).</summary>
    public ModuleVoice? Voice { get; }

    /// <summary>Their gestures for orders, tools and notices (null without K-Desktop).</summary>
    public ModuleChoreography? Choreography { get; }

    /// <summary>Talking with the assistants: orders, answers and the chat (null without K-Desktop).</summary>
    public ModuleTalk? Talk { get; }

    /// <summary>The tools of all the assistants of the process (null without K-Desktop).</summary>
    public ModuleTools? Tools { get; }

    /// <summary>The AI of all the assistants of the process (null without K-Desktop).</summary>
    public BrainService? Brain { get; }

    public AssistantOptions Options { get; }

    public Dispatcher Ui { get; }

    public EngineLoop Engine { get; } = new();

    public KDesktopLink? Link { get; }

    public AssistantRuntime Runtime { get; }

    public int ExitCode { get; private set; }

    public void Start()
    {
        Engine.Started += OnEngineStarted;
        Engine.Stopping += () =>
        {
            _dev?.Dispose();
            Runtime.Dispose();
        };
        Engine.Tick = Runtime.Tick;
        Engine.Schedule = Runtime.NextTick;
        Runtime.MenuRequested += request => Ui.BeginInvoke(() => ShowMenu(request));
        Engine.Start();

        if (Link is { } link)
        {
            if (Brain is { } brain) brain.Changed += state => link.Send(state);
            link.Connected += welcome =>
            {
                link.Send(new StatusMessage("idle"));
                Engine.Post(() => Runtime.SetPaused(welcome.Paused));
                if (Talk is { } talk) talk.Paused = welcome.Paused;
                if (welcome.Config?["general"] is JsonObject general) ApplyGeneral(general);
                Brain?.Configure(BrainConfig.FromJson(welcome.Config?["brain"] as JsonObject));
                Tools?.Configure(welcome.Config?["tools"] as JsonObject);
                Personal.Configure(welcome.Config?["personal"] as JsonObject);
                var common = welcome.Config?["common"] as JsonObject;
                Engine.Post(() => Runtime.ApplyCommon(common));
                Voice?.Configure(welcome.Config?["voice"] as JsonObject);
                Tools?.Start();
                if (Talk is { } ready) _ = ready.WarmAsync();
            };
            // The brain's requests do not wait for the engine.
            link.Received += message =>
            {
                switch (message)
                {
                    case BrainMessage request when Brain is { } brain:
                        _ = brain.HandleAsync(request);
                        return;
                    case ConfigMessage { Section: "brain" } config when Brain is { } brain:
                        brain.Configure(BrainConfig.FromJson(config.Value as JsonObject));
                        return;
                    case ConfigMessage { Section: "personal" } config:
                        Personal.Configure(config.Value as JsonObject);
                        return;
                    case ConfigMessage { Section: "voice" } config when Voice is { } voice:
                        voice.Configure(config.Value as JsonObject);
                        link.Send(voice.State());
                        return;
                    case VoiceMessage { Op: "unload" } when Voice is { } voice:
                        voice.Speech.Release();
                        return;
                    case VoiceMessage { Op: "state" } when Voice is { } voice:
                        link.Send(voice.State());
                        return;
                    case VoiceMessage { Op: "test", Text: { Length: > 0 } text } test when Voice is { } voice:
                        // Said by whoever K-Desktop chose, with its bubble; with nobody, only the voice (who is there: engine thread).
                        Engine.Post(() =>
                        {
                            VoiceProfile? edited = null;
                            try { edited = test.Profile is { } json ? VoiceProfile.Parse(json, json["id"]?.GetValue<string>() ?? "editor") : null; }
                            catch (JsonException ex) { Log.Warn($"Asistente: perfil de voz para probar no válido: {ex.Message}"); }
                            if (test.Assistant is { } who && Runtime.IsLoaded(who) && Talk is { } speaker && test.Engine is null)
                                voice.Test(text, test.Mood, (turn, said, stop) => speaker.SpeakTurnAsync(who, turn, said, stop), edited);
                            else voice.Test(text, test.Mood, (turn, said, stop) => voice.SayAsync(string.Empty, turn, said, stop), edited, test.Engine);
                        });
                        return;
                    case VoiceMessage { Op: "stop" } when Voice is { } voice:
                        voice.StopTest();
                        return;
                    case ConfigMessage { Section: "tools" } config when Tools is { } tools:
                        tools.Configure(config.Value as JsonObject);
                        return;
                    case AnswerMessage answer when Tools is { } tools:
                        tools.OnAnswer(answer);
                        return;
                    case UndoMessage undo when Tools is { } tools:
                        _ = tools.UndoAsync(undo);
                        return;
                    case MemoryMessage memory when Tools is { } tools:
                        tools.OnMemory(memory);
                        return;
                    case NotesMessage notes when Tools is { } tools:
                        tools.OnNotes(notes);
                        return;
                    case KnowledgeMessage knowledge when Tools is { } tools:
                        _ = tools.OnKnowledgeAsync(knowledge);
                        return;
                    case ToolsMessage request when Tools is { } tools:
                        _ = tools.OnToolsAsync(request);
                        return;
                    case BenchMessage bench when Bench is { } module:
                        module.OnBench(bench);
                        return;
                    case MatchedMessage matched when Commands is { } commands:
                        commands.OnMatched(matched);
                        return;
                    case StepMessage step when Commands?.Choreography is { } choreography:
                        choreography.OnStep(step);
                        return;
                    case ObservedMessage observed when Commands?.Choreography is { } choreography:
                        choreography.OnObserved(observed);
                        return;
                    case StepResultMessage result when Commands?.Choreography is { } choreography:
                        choreography.OnResult(result);
                        return;
                    case TextDoneMessage done when Commands is { } commands:
                        commands.OnTextDone(done);
                        return;
                    case EndMessage end when Commands is { } commands:
                        commands.OnEnd(end);
                        return;
                    case DescribedMessage described when Commands is { } commands:
                        commands.OnDescribed(described);
                        return;
                    case PhraseDoneMessage phraseDone when Commands is { } commands:
                        commands.OnPhraseDone(phraseDone);
                        return;
                    case ScheduleDoneMessage scheduleDone when Commands is { } commands:
                        commands.OnScheduleDone(scheduleDone);
                        return;
                    case LogFoundMessage logFound when Commands is { } commands:
                        commands.OnLogFound(logFound);
                        return;
                    case UsageDoneMessage usageDone when Commands is { } commands:
                        commands.OnUsageDone(usageDone);
                        return;
                    case DiagnosedMessage diagnosed when Commands is { } commands:
                        commands.OnDiagnosed(diagnosed);
                        return;
                    case PhoneSentMessage phoneSent when Commands is { } commands:
                        commands.OnPhoneSent(phoneSent);
                        return;
                    case MadeEventMessage madeEvent when Commands is { } commands:
                        commands.OnMadeEvent(madeEvent);
                        return;
                    case HeardMessage heard when Talk is { } talk:
                        talk.OnHeard(heard);
                        return;
                    case ListeningMessage listening when Talk is { } talk:
                        talk.OnListening(listening);
                        return;
                    case CancelMessage cancel when Talk is { } talk:
                        talk.OnCancel(cancel);
                        return;
                    case SayMessage say when Talk is { } talk:
                        talk.OnSay(say);
                        return;
                    case EmbeddedMessage embedded when Talk is { } talk:
                        talk.Meaning.OnEmbedded(embedded);
                        return;
                    case ErrorMessage error when Talk is { } talk && talk.Meaning.OnError(error):
                        return;
                    case PauseMessage pause when Talk is { } talk:
                        talk.Paused = pause.Paused;
                        break;
                    case UnloadMessage unload when Talk is { } talk:
                        talk.Left(unload.Assistant);
                        break;
                }
                Engine.Post(() => OnMessage(message));
            };
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
        else if (Options.DevFolder is { } dev)
        {
            _dev = new DevLoader(dev, Runtime, Engine.Post);
            _dev.Start();
        }
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
            case LoadMessage load when _dev is not null || Options.Probe is not null:
                Log.Info($"Asistente: se ignora «load» de «{load.Assistant}» (modo desarrollo o sonda)");
                break;
            case LoadMessage load:
                Then(() => LoadAsync(load));
                break;
            case UnloadMessage unload:
                Then(() => Runtime.UnloadAsync(unload.Assistant, ExitOf(unload.Reason)));
                break;
            case ConfigMessage { Section: "assistantSettings", Assistant: { } id, Value: { } value }:
                Runtime.ApplySettings(id, JsonSerializer.SerializeToElement(value));
                break;
            case ConfigMessage { Section: "general", Value: JsonObject general }:
                ApplyGeneral(general);
                break;
            case ConfigMessage { Section: "common", Value: JsonObject common }:
                Runtime.ApplyCommon(common);
                break;
            case MenuClosedMessage closed:
                Runtime.MenuClosed(closed.Assistant);
                break;
            case ExtraKeysMessage keys:
                Runtime.SetExtraKeys(keys.Assistant, keys.Keys);
                break;
            case ExtraMessage extra:
                _ = Runtime.RunExtraAsync(extra.Assistant, extra.Action);
                break;
            case KeyMessage key:
                Runtime.OnKey(key.Assistant, key.Key, key.Down);
                break;
            case WheelMessage wheel:
                Runtime.OnWheel(wheel);
                break;
            case FastScrollMessage scroll:
                Runtime.OnFastScroll(scroll);
                break;
        }
    }

    /// <summary>The module's settings of Options → Assistant that the process uses (the sound).</summary>
    private void ApplyGeneral(JsonObject general)
    {
        var sound = general["sound"] is JsonValue on && on.TryGetValue<bool>(out var b) ? b : true;
        var volume = general["volume"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 1;
        Runtime.Audio.Apply(sound, volume);
        if (Choreography is not null && general["manoeuvres"] is JsonValue m && m.TryGetValue<string>(out var speed)) Choreography.Speed = speed;
    }

    /// <summary>Runs a load or an unload after the ones before it (engine thread).</summary>
    private void Then(Func<Task> change) => _changes = After(_changes, change);

    private static async Task After(Task previous, Func<Task> change)
    {
        try { await previous.ConfigureAwait(true); }
        catch { /* logged where it failed */ }
        await change().ConfigureAwait(true);
    }

    /// <summary>
    /// The right-click menu, on the UI thread. Connected, K-Desktop draws it like the rest of its menus (stage 2.3) and does
    /// what is chosen; it may take the foreground for that. Otherwise (or in development, where K-Desktop does not know
    /// the assistant), Windows' own menu here, and what is chosen goes to K-Desktop or to the assistant.
    /// </summary>
    private void ShowMenu(MenuRequest request)
    {
        if (Volatile.Read(ref _shuttingDown) != 0) return;
        var linked = Link is { IsConnected: true };
        if (linked && _dev is null && Options.Probe is null)
        {
            Interop.Native.AllowSetForegroundWindow(Interop.Native.ASFW_ANY);
            Link!.Send(new MenuMessage(request.Id, (int)Math.Round(request.At.X), (int)Math.Round(request.At.Y)));
            return;
        }
        var (choice, extra) = AssistantMenu.Show(request, Link?.Theme == "dark", linked);
        Engine.Post(() => Runtime.MenuClosed(request.Id));
        switch (choice)
        {
            case MenuChoice.Panel:
                Link?.Send(new OpenMessage("assistant", request.Id));
                break;
            case MenuChoice.Settings:
                Link?.Send(new OpenMessage("assistantSettings", request.Id));
                break;
            case MenuChoice.Hide:
                Link?.Send(new HideMessage(request.Id));
                break;
            case MenuChoice.Extra when extra is not null:
                Engine.Post(() => _ = Runtime.RunExtraAsync(request.Id, extra));
                break;
        }
    }

    /// <summary>K-Desktop's «load»: an assistant it wants on the desktop (next to the others), answered with «loaded».</summary>
    private async Task LoadAsync(LoadMessage load)
    {
        try
        {
            var id = await Runtime.LoadPackageAsync(load.Path, load.Data, EntranceOf(load.Entrance));
            Talk?.Loaded(id, load.Path);
            Link?.Send(new LoadedMessage(id, true));
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: no se ha podido cargar «{load.Assistant}»", ex);
            Link?.Send(new LoadedMessage(load.Assistant, false, ex.Message));
        }
    }

    /// <summary>Why an assistant comes, from a <c>load</c> (stage 13.1).</summary>
    internal static EntranceKind EntranceOf(string entrance) => entrance switch
    {
        LoadMessage.Summoned => EntranceKind.Summoned,
        LoadMessage.Reloaded => EntranceKind.Reloaded,
        _ => EntranceKind.Startup,
    };

    /// <summary>Why an assistant leaves, from an <c>unload</c> (stage 13.1).</summary>
    internal static ExitKind ExitOf(string reason) => reason switch
    {
        UnloadMessage.Dismissed => ExitKind.Dismissed,
        UnloadMessage.Uninstalling => ExitKind.Uninstalling,
        UnloadMessage.Reloading => ExitKind.Reloading,
        _ => ExitKind.Shutdown,
    };

    /// <summary>Why the assistants leave when the process closes: as reloading if K-Desktop starts another one at once.</summary>
    internal static ExitKind ExitOnClose(string reason) =>
        reason == KDesktopLink.ByePrefix + ByeMessage.Restart ? ExitKind.Reloading : ExitKind.Shutdown;

    /// <summary>
    /// Closes the process: the assistants leave (their exits at once, up to 3 s), the engine stops and the UI thread
    /// ends. Any thread but the engine's; only the first call counts. K-Desktop's «bye» to start another process at once
    /// (language, theme, «Restart») lets them leave as reloading: they come back with no show.
    /// </summary>
    public void Shutdown(string reason, int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
        Log.Info($"Asistente: cerrando ({reason})");
        ExitCode = exitCode;
        var why = ExitOnClose(reason);
        var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.Post(async () =>
        {
            try { await Runtime.UnloadAllAsync(why); }
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
        Voice?.Dispose();
        Tools?.Dispose();
        Brain?.Dispose();
        Engine.Dispose();
    }
}
