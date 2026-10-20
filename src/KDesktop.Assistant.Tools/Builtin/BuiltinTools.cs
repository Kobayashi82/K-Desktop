namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>The tools that come with the module (plan §6.2, from Kobii), and the stores they keep.</summary>
public sealed class BuiltinTools : IDisposable
{
    /// <param name="folder">Where the module keeps its data (<c>&lt;data&gt;\Assistant</c>).</param>
    public BuiltinTools(string folder, TimeProvider? time = null)
    {
        Timers = new TimerStore(Path.Combine(folder, "timers.json"), time);
        Notes = new NoteStore(Path.Combine(folder, "notes.json"));
    }

    public TimerStore Timers { get; }

    public NoteStore Notes { get; }

    public void AddTo(ToolRegistry registry)
    {
        registry.Add(new TimeTool(Timers));
        registry.Add(new SetTimerTool(Timers));
        registry.Add(new CancelTimerTool(Timers));
        registry.Add(new SaveNoteTool(Notes, Timers.Time));
        registry.Add(new FindNotesTool(Notes));
        registry.Add(new CalculateTool());
        registry.Add(new OpenWebTool());
        registry.Add(new SearchWebTool());
        registry.Add(new OpenAppTool());
    }

    public void Dispose() => Timers.Dispose();
}
