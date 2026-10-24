namespace KDesktop.Assistant.Sdk;

/// <summary>What the module is busy with, besides its state: what a character may show it doing.</summary>
/// <remarks>New ones may come in later versions: show the ones it does not know as <see cref="Other"/>.</remarks>
public enum AssistantActivity
{
    /// <summary>Nothing in particular (or nothing to tell).</summary>
    None,
    /// <summary>Something not listed here: a tool of the user or of an MCP server (<see cref="StateDetail.Tool"/>).</summary>
    Other,
    /// <summary>Running, finding or explaining one of the user's K-Desktop commands, or an event.</summary>
    Command,
    /// <summary>Opening, closing or controlling a program or a window, or ending a process.</summary>
    Apps,
    /// <summary>Searching the internet.</summary>
    SearchingWeb,
    /// <summary>Reading a web page.</summary>
    ReadingWeb,
    /// <summary>Opening a web page in the user's browser.</summary>
    OpeningWeb,
    /// <summary>Looking for files, or opening one.</summary>
    Files,
    /// <summary>Writing down a note, or looking for one.</summary>
    Notes,
    /// <summary>Remembering or forgetting something about the user.</summary>
    Memory,
    /// <summary>Looking at the weather.</summary>
    Weather,
    /// <summary>What is playing, or controlling the player.</summary>
    Music,
    /// <summary>Setting or cancelling a timer.</summary>
    Timer,
    /// <summary>Setting a watch («avísame cuando…»), or stopping one.</summary>
    Watching,
    /// <summary>K-Desktop writes what the user says where the cursor is (with <see cref="AssistantState.Listening"/>).</summary>
    Dictation,
    /// <summary>Looking at the screen (reading its text or seeing it).</summary>
    Screen,
    /// <summary>Rewriting a text: translating, correcting, summing up…</summary>
    Writing,
    /// <summary>Doing sums, converting units or money, working out dates.</summary>
    Calculating,
    /// <summary>Looking at the PC (processor, memory, disks…) or freeing its brain.</summary>
    System,
}

/// <summary>
/// The detail of a state (<see cref="IAssistant.SetState(AssistantState, StateDetail)"/>).
/// </summary>
/// <param name="Activity">What it is busy with.</param>
/// <param name="Tool">The tool running, when there is one («look_up_web», «mcp_calendar_list_events»…).</param>
/// <param name="Text">
/// A few words saying it in the language the assistant speaks («Buscando en internet»): for an assistant that writes it
/// itself. Null when there is nothing to say.
/// </param>
public sealed record StateDetail(AssistantActivity Activity, string? Tool = null, string? Text = null)
{
    /// <summary>No detail.</summary>
    public static StateDetail None { get; } = new(AssistantActivity.None);
}

public static class AssistantStates
{
    /// <summary>Whether the module is at work on an order (thinking in any way, loading its brain, running something, asking).</summary>
    public static bool IsWorking(AssistantState state) => state is AssistantState.Thinking or AssistantState.ThinkingHard
        or AssistantState.LoadingBrain or AssistantState.Busy or AssistantState.AwaitingConfirmation;
}
