using KDesktop.Core.Assistant.Pipe;

namespace KDesktop;

/// <summary>
/// What the assistants know about the user (stage 3.7), from K-Desktop's side: the assistant process keeps it; Options →
/// Assistant → Memory asks for it (<c>memory</c>) and shows what comes back (<c>memoryState</c>). Everything here runs
/// on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>The memory as the process last sent it (null until it does, or with the process stopped).</summary>
    public IReadOnlyList<MemoryEntry>? AssistantMemory { get; private set; }

    /// <summary>Raised (UI thread) when the memory changes or the process goes away.</summary>
    public event Action? AssistantMemoryChanged;

    /// <summary>Asks the process for the memory (Options → Memory opened). False when it is not running.</summary>
    public bool RequestAssistantMemory() => SendMemory(new MemoryMessage("list"));

    public bool AddAssistantMemory(string text) => SendMemory(new MemoryMessage("add", Text: text));

    public bool EditAssistantMemory(string id, string text) => SendMemory(new MemoryMessage("edit", id, text));

    public bool DeleteAssistantMemory(string id) => SendMemory(new MemoryMessage("delete", id));

    public bool ClearAssistantMemory() => SendMemory(new MemoryMessage("clear"));

    private bool SendMemory(MemoryMessage message) => _assistant is { IsRunning: true } supervisor && supervisor.Send(message);

    private void OnAssistantMemory(MemoryStateMessage state)
    {
        AssistantMemory = state.Items;
        AssistantMemoryChanged?.Invoke();
    }

    /// <summary>The process went away: what it sent no longer holds.</summary>
    private void ForgetAssistantMemory()
    {
        if (AssistantMemory is null) return;
        AssistantMemory = null;
        AssistantMemoryChanged?.Invoke();
    }
}
