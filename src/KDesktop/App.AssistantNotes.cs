using KDesktop.Core.Assistant.Pipe;

namespace KDesktop;

/// <summary>
/// The notes the assistants take for the user («apunta que…»), from K-Desktop's side: the assistant process keeps them;
/// Options → Assistant → Notes asks for them (<c>notes</c>) and shows what comes back (<c>notesState</c>). Everything here
/// runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>The notes as the process last sent them (null until it does, or with the process stopped).</summary>
    public IReadOnlyList<NoteEntry>? AssistantNotes { get; private set; }

    /// <summary>Raised (UI thread) when the notes change or the process goes away.</summary>
    public event Action? AssistantNotesChanged;

    /// <summary>Asks the process for the notes (Options → Notes opened). False when it is not running.</summary>
    public bool RequestAssistantNotes() => SendNotes(new NotesMessage("list"));

    public bool AddAssistantNote(string text) => SendNotes(new NotesMessage("add", Text: text));

    public bool EditAssistantNote(string id, string text) => SendNotes(new NotesMessage("edit", id, text));

    public bool DeleteAssistantNote(string id) => SendNotes(new NotesMessage("delete", id));

    public bool ClearAssistantNotes() => SendNotes(new NotesMessage("clear"));

    private bool SendNotes(NotesMessage message) => _assistant is { IsRunning: true } supervisor && supervisor.Send(message);

    private void OnAssistantNotes(NotesStateMessage state)
    {
        AssistantNotes = state.Items;
        AssistantNotesChanged?.Invoke();
    }

    /// <summary>The process went away: what it sent no longer holds.</summary>
    private void ForgetAssistantNotes()
    {
        if (AssistantNotes is null) return;
        AssistantNotes = null;
        AssistantNotesChanged?.Invoke();
    }
}
