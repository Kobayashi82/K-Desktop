namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>The tools that come with the module (plan §6.2, from Kobii), and the stores they keep.</summary>
public sealed class BuiltinTools : IDisposable
{
    /// <param name="folder">Where the module keeps its data (<c>&lt;data&gt;\Assistant</c>).</param>
    public BuiltinTools(string folder, TimeProvider? time = null)
    {
        Timers = new TimerStore(Path.Combine(folder, "timers.json"), time);
        Notes = new NoteStore(Path.Combine(folder, "notes.json"));
        Memory = new MemoryStore(Path.Combine(folder, "memory.json"), time);
        Files = new FileSearch(time: time);
    }

    public TimerStore Timers { get; }

    public NoteStore Notes { get; }

    /// <summary>What the assistants know about the user (stage 3.7).</summary>
    public MemoryStore Memory { get; }

    /// <summary>Finding files, with each assistant's last list (stage 8.2).</summary>
    public FileSearch Files { get; }

    /// <param name="screen">The host's eyes on the desktop (stage 8.3); null: none (tests, the bench).</param>
    /// <param name="vision">The model looking at pictures; null: it cannot.</param>
    /// <param name="browser">The assistants' own browser (stage 8.4); null: pages are only read over HTTP.</param>
    public void AddTo(ToolRegistry registry, IScreen? screen = null, IVision? vision = null, IWebBrowser? browser = null)
    {
        registry.Add(new TimeTool(Timers));
        registry.Add(new SetTimerTool(Timers));
        registry.Add(new CancelTimerTool(Timers));
        registry.Add(new SaveNoteTool(Notes, Timers.Time));
        registry.Add(new FindNotesTool(Notes));
        registry.Add(new RememberTool(Memory));
        registry.Add(new ForgetTool(Memory));
        registry.Add(new CalculateTool());
        registry.Add(new DateMathTool(Timers.Time));
        registry.Add(new OpenWebTool());
        registry.Add(new SearchWebTool());
        registry.Add(new OpenAppTool());
        registry.Add(new FindFilesTool(Files));
        registry.Add(new OpenFileTool(Files));
        registry.Add(new LookAtScreenTool(screen, vision));
        var reader = new WebReader(browser: browser);
        registry.Add(new ReadWebTool(screen, reader));
        registry.Add(new LookUpWebTool(reader));
    }

    public void Dispose() => Timers.Dispose();
}
