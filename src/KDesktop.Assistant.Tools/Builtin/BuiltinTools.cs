using KDesktop.Assistant.Tools.Knowledge;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>The tools that come with the module (plan §6.2, from Kobii), and the stores they keep.</summary>
public sealed class BuiltinTools : IDisposable
{
    /// <param name="folder">Where the module keeps its data (<c>&lt;data&gt;\Assistant</c>).</param>
    /// <param name="probe">What the watches look at; null: the real PC.</param>
    /// <param name="meaning">EmbeddingGemma for the user's documents (stage 19); null: found by their words.</param>
    /// <param name="ocr">The OCR of Windows for their pictures and scanned pages; null: none.</param>
    public BuiltinTools(string folder, TimeProvider? time = null, IWatchProbe? probe = null, ITextMeaning? meaning = null, IScreenReader? ocr = null)
    {
        Knowledge = new KnowledgeBase(Path.Combine(folder, "Knowledge"), meaning, ocr);
        Timers = new TimerStore(Path.Combine(folder, "timers.json"), time);
        Notes = new NoteStore(Path.Combine(folder, "notes.json"));
        Memory = new MemoryStore(Path.Combine(folder, "memory.json"), time);
        Files = new FileSearch(time: time);
        Watches = new WatchStore(Path.Combine(folder, "watches.json"), probe ?? new SystemProbe(), time);
        Rates = new EcbRates(Path.Combine(folder, "rates.json"));
    }

    /// <summary>The ECB's exchange rates, kept between sessions (convert).</summary>
    public EcbRates Rates { get; }

    /// <summary>Where the user is, as written in Options (Assistant module → General), for the weather; empty: Windows' location.</summary>
    public string HomePlace { get; set; } = string.Empty;

    /// <summary>What the assistants know: the user's folders of documents (stage 19).</summary>
    public KnowledgeBase Knowledge { get; }

    public TimerStore Timers { get; }

    public NoteStore Notes { get; }

    /// <summary>What the assistants know about the user (stage 3.7).</summary>
    public MemoryStore Memory { get; }

    /// <summary>Finding files, with each assistant's last list (stage 8.2).</summary>
    public FileSearch Files { get; }

    /// <summary>What to tell the user about when it happens (avisame_cuando).</summary>
    public WatchStore Watches { get; }

    /// <param name="screen">The host's eyes on the desktop (stage 8.3); null: none (tests, the bench).</param>
    /// <param name="vision">The model looking at pictures; null: it cannot.</param>
    /// <param name="browser">The assistants' own browser (stage 8.4); null: pages are only read over HTTP.</param>
    public void AddTo(ToolRegistry registry, IScreen? screen = null, IVision? vision = null, IWebBrowser? browser = null, ITextMeaning? meaning = null)
    {
        registry.Add(new TimeTool(Timers));
        registry.Add(new SetTimerTool(Timers));
        registry.Add(new CancelTimerTool(Timers));
        registry.Add(new SaveNoteTool(Notes, Timers.Time));
        registry.Add(new FindNotesTool(Notes));
        registry.Add(new EditNoteTool(Notes));
        registry.Add(new DeleteNoteTool(Notes));
        registry.Add(new RememberTool(Memory));
        registry.Add(new ForgetTool(Memory));
        registry.Add(new CalculateTool());
        registry.Add(new DateMathTool(Timers.Time));
        registry.Add(new OpenWebTool());
        registry.Add(new SearchWebTool());
        registry.Add(new OpenAppTool());
        registry.Add(new FindFilesTool(Files));
        registry.Add(new OpenFileTool(Files));
        registry.Add(new ReadFileTool(vision, screen is null ? null : new WindowsScreenReader(), meaning));
        registry.Add(new DeleteFileTool());
        registry.Add(new MoveFileTool());
        registry.Add(new LookAtScreenTool(screen, vision, screen is null ? null : new WindowsScreenReader()));
        var reader = new WebReader(browser: browser);
        registry.Add(new ReadWebTool(screen, reader));
        registry.Add(new LookUpWebTool(reader));
        registry.Add(new PcStatusTool());
        registry.Add(new WhatsPlayingTool());
        registry.Add(new ControlMediaTool());
        registry.Add(new NotifyWhenTool(Watches));
        registry.Add(new StopWatchingTool(Watches));
        registry.Add(new WeatherTool(() => HomePlace));
        registry.Add(new ConvertTool(Rates));
        registry.Add(new SearchKnowledgeTool(Knowledge));
    }

    public void Dispose()
    {
        Timers.Dispose();
        Watches.Dispose();
        Rates.Dispose();
        Knowledge.Dispose();
    }
}
