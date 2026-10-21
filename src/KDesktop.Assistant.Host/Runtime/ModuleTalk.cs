using System.Collections.Concurrent;
using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
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

    public ModuleTalk(KDesktopLink link, EngineLoop engine, AssistantRuntime runtime, BrainService brain, ModuleTools tools)
    {
        _link = link;
        _engine = engine;
        _runtime = runtime;
        _tools = tools;
        Meaning = new ModuleMeaning(link);
        Chooser = new ToolChooser(Meaning);
        Service = new TalkService(who => (brain.Client, brain.ModelFor(who)), tools.Runner, PersonaOf, this, chooser: Chooser);
    }

    public TalkService Service { get; }

    /// <summary>The vectors of meaning K-Desktop sends (<c>embedded</c>).</summary>
    public ModuleMeaning Meaning { get; }

    public ToolChooser Chooser { get; }

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
        _ = Service.HandleAsync(heard);
    }

    public void OnCancel(CancelMessage cancel) => Service.Cancel(cancel.Assistant);

    /// <summary>An assistant came onto the desktop from <paramref name="folder"/> (a new version may bring another persona).</summary>
    public void Loaded(string id, string folder)
    {
        _folders[id] = folder;
        _personas.TryRemove(id, out _);
    }

    /// <summary>An assistant left the desktop: what was talked with it is forgotten.</summary>
    public void Left(string id) => Service.Forget(id);

    /// <summary>Its persona in the user's language (read once per language), or the module's.</summary>
    private AssistantPersona PersonaOf(string? id)
    {
        if (id is null) return AssistantPersona.Module();
        var language = Loc.Instance.Language;
        if (_personas.TryGetValue(id, out var known) && known.Language == language) return known.Persona;
        var persona = ReadPersona(id, language);
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
        if (assistant is null) return;
        var shown = state switch
        {
            "thinking" => AssistantState.Thinking,
            "speaking" => AssistantState.Speaking,
            _ => AssistantState.Idle,
        };
        _engine.Post(() => _runtime.SetState(assistant, shown));
    }

    public Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken)
    {
        if (assistant is null || text.Length == 0) return Task.CompletedTask;
        var said = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine.Post(async () =>
        {
            try { await _runtime.CommunicateAsync(assistant, text, ReadingTime(text), cancellationToken); }
            catch (Exception ex) { Log.Warn($"Asistente: «{assistant}» no ha podido decirlo: {ex.Message}"); }
            finally { said.TrySetResult(); }
        });
        return said.Task;
    }

    /// <summary>How long a bubble stays: enough to read it (no voice yet; stage 5 says it aloud).</summary>
    public static TimeSpan ReadingTime(string text) => TimeSpan.FromSeconds(Math.Clamp(1.5 + text.Length * 0.065, 2.5, 15));
}
