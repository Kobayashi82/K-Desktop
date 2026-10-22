using System.Collections.Concurrent;
using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// Talking with the assistants of the process (stage 3.4): K-Desktop's <c>heard</c> and <c>cancel</c> go to the
/// <see cref="TalkService"/> (fixed rules, then the AI with the persona of the one it was said to); its lines go back as
/// <c>chat</c>, its state as <c>status</c>, and the character shows it (thinking, then Communicate while it «says» it).
/// Without a body (no assistant, or one not on the desktop) only the chat gets it. The tools that reach the AI are
/// chosen by meaning, with K-Desktop's vectors (<see cref="Meaning"/>, stage 3.5).
/// </summary>
internal sealed class ModuleTalk : ITalkOutput
{
    private readonly KDesktopLink _link;
    private readonly EngineLoop _engine;
    private readonly AssistantRuntime _runtime;
    /// <summary>The package folder of each assistant K-Desktop loaded (its persona is read from there).</summary>
    private readonly ConcurrentDictionary<string, string> _folders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Language, AssistantPersona Persona)> _personas = new(StringComparer.Ordinal);

    private readonly ModuleTools _tools;
    private readonly ModulePersonal _personal;

    public ModuleTalk(KDesktopLink link, EngineLoop engine, AssistantRuntime runtime, BrainService brain, ModuleTools tools, ModuleCommands commands, ModuleVoice voice,
        ModulePersonal personal)
    {
        _link = link;
        _engine = engine;
        _runtime = runtime;
        _tools = tools;
        _personal = personal;
        // Its personality or its language changed: its persona is read again for the next order.
        personal.Changed += ids =>
        {
            foreach (var id in ids) _personas.TryRemove(id, out _);
        };
        Voice = voice;
        Voice.ProfileOf = ProfileOf;
        Meaning = new ModuleMeaning(link);
        Chooser = new ToolChooser(Meaning);
        Memory = new TalkMemory(tools.Builtin.Memory, tools.Runner, Meaning);
        Service = new TalkService(who => (brain.Client, brain.ModelFor(who)), tools.Runner, PersonaOf, this, chooser: Chooser, memory: Memory, learn: () => tools.Learn, commands: commands,
            recheck: brain.RecheckAsync, freeBrain: brain.UnloadAsync, loadBrain: () => brain.LoadAsync());
        tools.Builtin.Watches.Fired += (item, detail) => _ = AnnounceAsync(item.Assistant, "watch", item.Kind, "tool.notify_when.title",
            () => Tools.Builtin.WatchStore.Happened(item, detail));
        tools.Builtin.Timers.Rang += timer => _ = AnnounceAsync(timer.Assistant, "timer", null, "tool.set_timer.title", () => TimerText(timer));
    }

    /// <summary>What a timer that rang says (in the language in use).</summary>
    private static string TimerText(Tools.Builtin.TimerItem timer)
    {
        var clock = timer.Due.ToLocalTime().ToString("t", Loc.Instance.Culture);
        return timer.Text.Length > 0 ? Loc.T("tool.set_timer.rangFor", clock, timer.Text) : Loc.T("tool.set_timer.rang", clock);
    }

    public TalkService Service { get; }

    /// <summary>What they answer, said aloud (stage 5.4).</summary>
    public ModuleVoice Voice { get; }

    /// <summary>The vectors of meaning K-Desktop sends (<c>embedded</c>).</summary>
    public ModuleMeaning Meaning { get; }

    public ToolChooser Chooser { get; }

    /// <summary>What comes to mind about the user and what is learned (stage 3.7).</summary>
    public TalkMemory Memory { get; }

    /// <summary>Connected: the example phrases of the tools get their vectors before the first order needs them.</summary>
    public Task WarmAsync() => Chooser.WarmAsync(_tools.Runner.Registry.All, CancellationToken.None);

    /// <summary>K-Desktop is paused: orders are not answered.</summary>
    public bool Paused { get; set; }

    public void OnHeard(HeardMessage heard)
    {
        if (Paused)
        {
            Log.Info("Asistente: orden ignorada (K-Desktop en pausa)");
            return;
        }
        // In its own language (its answer, its phrases, what it did), whatever the others speak in.
        using (Loc.Instance.Use(_personal.LanguageOf(heard.Assistant))) _ = Service.HandleAsync(heard);
    }

    public void OnCancel(CancelMessage cancel)
    {
        Service.Cancel(cancel.Assistant);
        Voice.Stop(cancel.Assistant);
    }

    /// <summary>Who K-Desktop is listening to now (its word, its key, a long click on it).</summary>
    private readonly ConcurrentDictionary<string, bool> _listening = new(StringComparer.Ordinal);

    /// <summary>
    /// K-Desktop is listening for an assistant, or no longer is (stage 5.2): the character shows it. When it ends without
    /// an order, back to rest, unless it is still answering the one before.
    /// </summary>
    public void OnListening(ListeningMessage listening)
    {
        var key = listening.Assistant ?? string.Empty;
        // You are talking to it: its voice steps back meanwhile, and its model and its voice get ready (stage 5.7).
        Voice.Listening(key, listening.Active);
        if (listening.Active)
        {
            if (!Paused)
            {
                using (Loc.Instance.Use(_personal.LanguageOf(listening.Assistant))) Service.Prepare(listening.Assistant);
                Voice.Warm(listening.Assistant);
            }
            _listening[key] = true;
            State(listening.Assistant, "listening");
        }
        else if (_listening.TryRemove(key, out _) && !Service.IsAnswering(listening.Assistant)) State(listening.Assistant, "idle");
    }

    /// <summary>The longest a told watch or a timer waits for the assistant to finish what it is answering.</summary>
    private static readonly TimeSpan AnnounceWait = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Something watched happened (avisame_cuando) or a timer rang: the one who set it says it, in its language, when it
    /// is not answering something else (it would cut the answer). In pause, or after too long, K-Desktop shows it instead.
    /// </summary>
    /// <param name="activity">The line of the activity (<c>watch</c>, <c>timer</c>).</param>
    /// <param name="kind">What was watched, for the log.</param>
    /// <param name="titleKey">The title of the notice when it is shown (no assistant).</param>
    private async Task AnnounceAsync(string? assistant, string activity, string? kind, string titleKey, Func<string> say)
    {
        try
        {
            using var _ = Loc.Instance.Use(_personal.LanguageOf(assistant));
            var text = say();
            // «programa de prueba se ha cerrado.»: the name the user said goes first.
            if (text.Length > 0) text = char.ToUpper(text[0], Loc.Instance.Culture) + text[1..];
            Log.Info(kind is null ? $"Asistente: {activity}: {text}" : $"Asistente: aviso cumplido ({kind}): {text}");
            _tools.Runner.Record(assistant, string.Empty, activity, text, ok: true);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (Service.IsAnswering(assistant) && waited.Elapsed < AnnounceWait) await Task.Delay(500).ConfigureAwait(false);
            if (Paused || Service.IsAnswering(assistant))
            {
                var title = assistant is { } id && AssistantPackages.Find(id) is { } installed ? installed.Manifest.Name : Loc.T(titleKey);
                _link.Send(new NotifyMessage(title, text, assistant));
                return;
            }
            await Service.SayAsync(assistant, text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"Asistente: no se ha podido dar el aviso: {ex.Message}");
        }
    }

    /// <summary>«Asistente: decir»: it says it (not in pause, like the orders).</summary>
    public void OnSay(SayMessage say)
    {
        if (Paused) return;
        using (Loc.Instance.Use(_personal.LanguageOf(say.Assistant))) _ = Service.SayAsync(say.Assistant, say.Text);
    }

    /// <summary>An assistant came onto the desktop from <paramref name="folder"/> (a new version may bring another persona).</summary>
    public void Loaded(string id, string folder)
    {
        _folders[id] = folder;
        _personas.TryRemove(id, out _);
        _profiles.TryRemove(id, out _);
    }

    /// <summary>An assistant left the desktop: what was talked with it is forgotten, and it stops talking.</summary>
    public void Left(string id)
    {
        Service.Forget(id);
        Voice.Stop(id);
    }

    private readonly ConcurrentDictionary<string, VoiceProfile?> _profiles = new(StringComparer.Ordinal);

    /// <summary>The voice profile its package brings (its manifest's <c>voice</c>), read once per load; null: none.</summary>
    private VoiceProfile? ProfileOf(string id) => _profiles.GetOrAdd(id, _ =>
    {
        var folder = _folders.GetValueOrDefault(id) ?? AssistantPackages.Find(id)?.Folder;
        if (folder is null) return null;
        try
        {
            var manifest = AssistantManifest.Parse(File.ReadAllText(Path.Combine(folder, AssistantManifest.FileName)));
            return manifest.Voice is { Length: > 0 } path ? VoiceProfile.Load(Path.Combine(folder, path), id) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ManifestException)
        {
            return null;
        }
    });

    /// <summary>
    /// Its persona in the language it speaks in (read once per language), or the module's; with the personality the user
    /// wrote for it, that one instead of its package's (its fixed phrases stay).
    /// </summary>
    private AssistantPersona PersonaOf(string? id)
    {
        if (id is null)
        {
            // K-Desktop without an assistant: the module's, or the personality written for it.
            var module = AssistantPersona.Module();
            return _personal.Of(null)?.Prompt is { } mine ? module with { Prompt = mine.Trim() } : module;
        }
        var language = _personal.LanguageOf(id);
        if (_personas.TryGetValue(id, out var known) && known.Language == language) return known.Persona;
        var persona = ReadPersona(id, language);
        if (_personal.Of(id)?.Prompt is { } own) persona = persona with { Prompt = own.Trim() };
        _personas[id] = (language, persona);
        return persona;
    }

    private AssistantPersona ReadPersona(string id, string language)
    {
        var folder = _folders.GetValueOrDefault(id) ?? AssistantPackages.Find(id)?.Folder;
        if (folder is null) return AssistantPersona.Module();
        try
        {
            var manifest = AssistantManifest.Parse(File.ReadAllText(Path.Combine(folder, AssistantManifest.FileName)));
            return AssistantPersona.Read(folder, manifest, language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ManifestException)
        {
            Log.Warn($"Asistente: no se ha podido leer el manifiesto de «{id}»: {ex.Message}");
            return AssistantPersona.Module();
        }
    }

    public void Chat(ChatLineMessage line) => _link.Send(line);

    public void State(string? assistant, string state)
    {
        _link.Send(new StatusMessage(state, Assistant: assistant));
        if (state == "thinking") Voice.Warm(assistant);
        if (assistant is null) return;
        var shown = state switch
        {
            "listening" => AssistantState.Listening,
            "thinking" => AssistantState.Thinking,
            "speaking" => AssistantState.Speaking,
            _ => AssistantState.Idle,
        };
        _engine.Post(() => _runtime.SetState(assistant, shown));
    }

    public Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken) =>
        SpeakTurnAsync(assistant, Guid.NewGuid().ToString("N")[..12], text, cancellationToken);

    public void SpeakAhead(string? assistant, string turn, string textSoFar) => Voice.Ahead(assistant, turn, textSoFar);

    public void Preface(string? assistant, string turn, string phrase) => Voice.Preface(assistant, turn, phrase);

    /// <summary>
    /// The bubble with the text (for as long as it takes to read it) and its voice (what was not said ahead). Over when its
    /// voice is (the bubble stays on its own until it has been read: an answer that asks listens for the reply right away,
    /// not 15 s later); without a voice, when the bubble is; or when it is cancelled. Without an assistant only the voice
    /// (K-Desktop shows the text).
    /// </summary>
    public Task SpeakTurnAsync(string? assistant, string turn, string text, CancellationToken cancellationToken)
    {
        if (text.Length == 0) return Task.CompletedTask;
        if (assistant is null) return Voice.SayAsync(null, turn, text, cancellationToken);
        var speaking = Voice.SayAsync(assistant, turn, text, cancellationToken);
        var said = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Post(async () =>
        {
            try { await _runtime.CommunicateAsync(assistant, text, ReadingTime(text), cancellationToken, speaking); }
            catch (Exception ex) { Log.Warn($"Asistente: «{assistant}» no ha podido decirlo: {ex.Message}"); }
            finally { said.TrySetResult(); }
        });
        return speaking.IsCompleted ? said.Task : Task.WhenAny(speaking, said.Task);
    }

    public void Listen(string? assistant, int seconds) => _link.Send(new ListenMessage(seconds, Assistant: assistant));

    public void Dictate(string? assistant) => _link.Send(new DictateMessage(assistant));

    /// <summary>How long a bubble stays at least: enough to read it (while its voice speaks, longer).</summary>
    public static TimeSpan ReadingTime(string text) => TimeSpan.FromSeconds(Math.Clamp(1.5 + text.Length * 0.065, 2.5, 15));
}
