using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Native;
using KDesktop.Views;
using KDesktop.Views.Assistant;

namespace KDesktop;

/// <summary>One line of a conversation with an assistant (Panel → Assistant → chat).</summary>
public sealed partial class ChatItem(string role, string turn, DateTime at) : ObservableObject
{
    /// <summary><c>user</c> or <c>assistant</c>.</summary>
    public string Role { get; } = role;

    public string Turn { get; } = turn;

    public DateTime At { get; } = at;

    public bool IsUser => Role == "user";

    [ObservableProperty] private string _text = string.Empty;

    /// <summary>False while the answer is still being written.</summary>
    [ObservableProperty] private bool _final;

    public override string ToString() => Text;
}

/// <summary>
/// The inputs of the assistants (stage 3.6): the chat of Panel → Assistant, the box to write to one (its menu, a middle
/// click on it, «Asistente: preguntar» without text) and «Asistente: preguntar». All of them send the same
/// <c>heard</c> as the voice; the answer comes back as <c>chat</c> lines, kept here per assistant for this session
/// (K-Desktop's under its id: in the process it is the one without one). Without a figure to say it (K-Desktop, 5.0.112, or
/// an assistant that is not on the desktop), an answer to something not written in the panel's chat comes as K-Desktop's
/// OSD. With nobody active there is no assistant: nothing is sent. Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>Lines kept per conversation.</summary>
    private const int KeptChat = 100;

    private readonly Dictionary<string, List<ChatItem>> _chats = new(StringComparer.Ordinal);

    /// <summary>Where the last order of each conversation came from (<c>panel</c>, <c>bubble</c>, <c>command</c>…).</summary>
    private readonly Dictionary<string, string> _chatSources = new(StringComparer.Ordinal);
    private AssistantWriteWindow? _writeWindow;

    /// <summary>A conversation changed (its key: the assistant's id).</summary>
    public event Action<string>? AssistantChatChanged;

    /// <summary>The lines of the conversation with an assistant, oldest first.</summary>
    public IReadOnlyList<ChatItem> AssistantChat(string id) => ChatOf(id);

    private List<ChatItem> ChatOf(string id)
    {
        if (!_chats.TryGetValue(id, out var chat)) _chats[id] = chat = [];
        return chat;
    }

    /// <summary>A line of a conversation: a new one, or more of the answer being written (same turn).</summary>
    private void OnAssistantChat(ChatLineMessage line)
    {
        var who = FromProcess(line.Assistant);
        var chat = ChatOf(who);
        var item = chat.LastOrDefault(c => c.Turn == line.Turn && c.Role == line.Role);
        if (item is null)
        {
            item = new ChatItem(line.Role, line.Turn, DateTime.Now);
            chat.Add(item);
            if (chat.Count > KeptChat) chat.RemoveRange(0, chat.Count - KeptChat);
        }
        item.Text = line.Text;
        item.Final = line.Final;
        // Said aloud by one on the desktop: heard back through the microphone it is not an order (stage 5.7).
        // What K-Desktop or one that is not there says is said aloud too (5.0.111).
        if (line.Role == "assistant") Voice.OwnVoices.Said(who + "/" + line.Turn, line.Text, DateTime.Now);
        AssistantChatChanged?.Invoke(who);
        if (line is { Role: "assistant", Final: true }) ShowUnseenAnswer(who, line.Text);
    }

    /// <summary>
    /// An answer no figure says (K-Desktop's, or one's that is not on the desktop): K-Desktop shows it, unless it was asked
    /// in the panel's chat, where it is already seen. As long as it takes to read it.
    /// </summary>
    private void ShowUnseenAnswer(string id, string text)
    {
        if (text.Length == 0 || (IsOnDesktop(id) && !AssistantTargeting.IsSystem(id))) return;
        if (_chatSources.GetValueOrDefault(id) == "panel") return;
        var seconds = Math.Clamp(2 + text.Length * 0.06, 4, 15);
        Ui.ShowOsd($"{AssistantName(id)}: {text}", Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(seconds));
    }

    /// <summary>Whether an assistant is working on an answer now (K-Desktop's state is the process's own).</summary>
    public bool IsAssistantThinking(string? id) =>
        id is not null && (AssistantTargeting.IsSystem(id) ? AssistantStatus : AssistantStateOf(id)) is { State: "thinking" or "speaking" or "busy" };

    /// <summary>
    /// Sends an order to an assistant (null: nobody is active), as if said to it: <paramref name="source"/> <c>panel</c>,
    /// <c>bubble</c>, <c>command</c>, <c>voice</c>… False (and an OSD) when nobody can answer now.
    /// </summary>
    public bool SendToAssistant(string? id, string text, string source)
    {
        text = text.Trim();
        if (text.Length == 0) return false;
        if (_assistant is null)
        {
            Ui.ShowOsd(Loc.T("assistantExtra.moduleOff"), Core.Engine.OsdKind.Warning);
            return false;
        }
        if (id is null)
        {
            Log.Info($"Asistente: orden por {source} sin ningún asistente activo");
            Ui.ShowOsd(Loc.T("assistantChat.nobody"), Core.Engine.OsdKind.Warning);
            return false;
        }
        if (!_assistant.IsRunning || !_assistant.Send(new HeardMessage(text, source, ProcessIdOf(id))))
        {
            Ui.ShowOsd(Loc.T("assistantChat.notRunning"), Core.Engine.OsdKind.Warning);
            return false;
        }
        if (IsOnDesktop(id)) AssistantTalkedTo(id);
        _chatSources[id] = source;
        Log.Info($"Asistente: orden por {source} para «{id}»");
        return true;
    }

    /// <summary>Stops what an assistant is answering (the chat's stop button).</summary>
    public void StopAssistant(string id)
    {
        CancelChoreographies(ProcessIdOf(id) ?? string.Empty);
        _assistant?.Send(new CancelMessage("panel", ProcessIdOf(id)));
    }

    /// <summary>Forgets a conversation (the chat's clear button).</summary>
    public void ClearAssistantChat(string id)
    {
        ChatOf(id).Clear();
        AssistantChatChanged?.Invoke(id);
    }

    /// <summary>«Asistente: preguntar»: the order to its assistant; without text, the box to write it. Not while it is hidden.</summary>
    private void AskAssistantFromCommand(string id, string text)
    {
        if (!CanCommandAssistant(id)) return;
        if (text.Trim().Length == 0) ShowAssistantWriteBox(id, null);
        else SendToAssistant(id, text, "command");
    }

    /// <summary>
    /// Whether an action of a command may go to its assistant (5.0.113): the module on, an assistant named that exists and
    /// is on the desktop. Otherwise it is not run, and the OSD says why.
    /// </summary>
    private bool CanCommandAssistant(string id)
    {
        var why = _assistant is null ? Loc.T("assistantExtra.moduleOff")
            : id.Length == 0 ? Loc.T("assistantAction.noAssistant")
            : !IsAssistant(id) ? Loc.T("assistantExtra.notInstalled", id)
            : !IsOnDesktop(id) ? Loc.T(AssistantTargeting.IsSystem(id) ? "assistantState.systemInactive" : "assistantState.notOnDesktop", AssistantName(id))
            : null;
        if (why is null) return true;
        Log.Info($"Asistente: acción para «{id}» que no se ejecuta: {why}");
        Ui.ShowOsd(why, Core.Engine.OsdKind.Warning);
        return false;
    }

    /// <summary>
    /// «Asistente: decir»: its assistant says it, in its bubble and its chat (K-Desktop with the OSD: the chat line that
    /// comes back shows it). Not while it is hidden.
    /// </summary>
    private void SayFromCommand(string id, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || !CanCommandAssistant(id)) return;
        _chatSources[id] = "command";
        if (!_assistant!.IsRunning || !_assistant.Send(new SayMessage(text, ProcessIdOf(id))))
            Ui.ShowOsd(text, Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(Math.Clamp(2 + text.Length * 0.06, 4, 15)));
    }

    /// <summary>
    /// The box to write to an assistant, above <paramref name="at"/> (screen pixels, where the character is) or at the
    /// bottom of the monitor with the mouse.
    /// </summary>
    internal void ShowAssistantWriteBox(string id, UiNative.POINT? at)
    {
        _writeWindow?.Leave();
        var window = new AssistantWriteWindow(Loc.T("assistantWrite.placeholder", AssistantName(id)), AssistantLook.IconOf(id),
            text => SendToAssistant(id, text, "bubble"), at);
        window.Closed += (_, _) =>
        {
            if (_writeWindow == window) _writeWindow = null;
        };
        _writeWindow = window;
        window.Show();
    }

    /// <summary><c>write</c>: a middle click on an assistant opens the box above it.</summary>
    private void OnAssistantWrite(WriteMessage write)
    {
        if (!IsOnDesktop(write.Assistant)) return;
        ShowAssistantWriteBox(write.Assistant, new UiNative.POINT { X = write.X, Y = write.Y });
    }
}
