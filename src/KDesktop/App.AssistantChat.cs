using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core;
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
/// <c>heard</c> as the voice; the answer comes back as <c>chat</c> lines, kept here per assistant for this session (and
/// for the module without a body under the empty key). Without a body to say it (the module without one, stage 3.8, or an
/// assistant that is not on the desktop), an answer to something not written in the panel's chat comes as K-Desktop's
/// OSD. Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>Lines kept per conversation.</summary>
    private const int KeptChat = 100;

    private readonly Dictionary<string, List<ChatItem>> _chats = new(StringComparer.Ordinal);

    /// <summary>Where the last order of each conversation came from (<c>panel</c>, <c>bubble</c>, <c>command</c>…).</summary>
    private readonly Dictionary<string, string> _chatSources = new(StringComparer.Ordinal);
    private AssistantWriteWindow? _writeWindow;

    /// <summary>A conversation changed (its key: the assistant's id, or empty without a body).</summary>
    public event Action<string>? AssistantChatChanged;

    /// <summary>The lines of the conversation with an assistant (null: the module without a body), oldest first.</summary>
    public IReadOnlyList<ChatItem> AssistantChat(string? id) => ChatOf(id);

    private List<ChatItem> ChatOf(string? id)
    {
        var key = id ?? string.Empty;
        if (!_chats.TryGetValue(key, out var chat)) _chats[key] = chat = [];
        return chat;
    }

    /// <summary>A line of a conversation: a new one, or more of the answer being written (same turn).</summary>
    private void OnAssistantChat(ChatLineMessage line)
    {
        var chat = ChatOf(line.Assistant);
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
        if (line is { Role: "assistant", Assistant: { } speaker } && IsOnDesktop(speaker)) Voice.OwnVoices.Said(speaker + "/" + line.Turn, line.Text, DateTime.Now);
        AssistantChatChanged?.Invoke(line.Assistant ?? string.Empty);
        if (line is { Role: "assistant", Final: true }) ShowBodilessAnswer(line);
    }

    /// <summary>
    /// An answer nobody on the desktop says (no body, or one that is not there): K-Desktop shows it, unless it was asked in
    /// the panel's chat, where it is already seen. As long as it takes to read it.
    /// </summary>
    private void ShowBodilessAnswer(ChatLineMessage line)
    {
        if (line.Text.Length == 0 || (line.Assistant is { } id && IsOnDesktop(id))) return;
        if (_chatSources.GetValueOrDefault(line.Assistant ?? string.Empty) == "panel") return;
        var name = line.Assistant is { } who ? AssistantName(who) : "K-Desktop";
        var seconds = Math.Clamp(2 + line.Text.Length * 0.06, 4, 15);
        Ui.ShowOsd($"{name}: {line.Text}", Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(seconds));
    }

    /// <summary>Whether an assistant (null: the module without a body) is working on an answer now.</summary>
    public bool IsAssistantThinking(string? id) =>
        (id is null ? AssistantStatus : AssistantStateOf(id)) is { State: "thinking" or "speaking" or "busy" };

    /// <summary>
    /// Sends an order to an assistant (null: the module without a body), as if said to it: <paramref name="source"/>
    /// <c>panel</c>, <c>bubble</c> or <c>command</c>. False (and an OSD) when nobody can answer now.
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
        if (!_assistant.IsRunning || !_assistant.Send(new HeardMessage(text, source, id)))
        {
            Ui.ShowOsd(Loc.T("assistantChat.notRunning"), Core.Engine.OsdKind.Warning);
            return false;
        }
        if (id is not null && IsOnDesktop(id)) AssistantTalkedTo(id);
        _chatSources[id ?? string.Empty] = source;
        Log.Info($"Asistente: orden por {source} para «{id ?? "(sin cuerpo)"}»");
        return true;
    }

    /// <summary>Stops what an assistant is answering (the chat's stop button).</summary>
    public void StopAssistant(string? id)
    {
        CancelChoreographies(id ?? string.Empty);
        _assistant?.Send(new CancelMessage("panel", id));
    }

    /// <summary>Forgets a conversation (the chat's clear button).</summary>
    public void ClearAssistantChat(string? id)
    {
        ChatOf(id).Clear();
        AssistantChatChanged?.Invoke(id ?? string.Empty);
    }

    /// <summary>«Asistente: preguntar»: the order to an assistant (empty: the default one); without text, the box to write it.</summary>
    private void AskAssistantFromCommand(string id, string text)
    {
        if (_assistant is null)
        {
            Ui.ShowOsd(Loc.T("assistantExtra.moduleOff"), Core.Engine.OsdKind.Warning);
            return;
        }
        var who = id.Length > 0 ? id : PickAssistant();
        if (who is not null && AssistantPackages.Find(who) is null)
        {
            Ui.ShowOsd(Loc.T("assistantExtra.notInstalled", who), Core.Engine.OsdKind.Warning);
            return;
        }
        if (text.Trim().Length == 0) ShowAssistantWriteBox(who, null);
        else SendToAssistant(who, text, "command");
    }

    /// <summary>
    /// «Asistente: decir»: the assistant (empty: the default one) says it, in its bubble and its chat; without anybody to
    /// say it, K-Desktop's OSD (the chat line that comes back shows it).
    /// </summary>
    private void SayFromCommand(string id, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (_assistant is null)
        {
            Ui.ShowOsd(Loc.T("assistantExtra.moduleOff"), Core.Engine.OsdKind.Warning);
            return;
        }
        var who = id.Length > 0 ? id : PickAssistant();
        _chatSources[who ?? string.Empty] = "command";
        if (!_assistant.IsRunning || !_assistant.Send(new SayMessage(text, who)))
            Ui.ShowOsd(text, Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(Math.Clamp(2 + text.Length * 0.06, 4, 15)));
    }

    /// <summary>
    /// The box to write to an assistant (null: the module without a body), above <paramref name="at"/> (screen pixels,
    /// where the character is) or at the bottom of the monitor with the mouse.
    /// </summary>
    internal void ShowAssistantWriteBox(string? id, UiNative.POINT? at)
    {
        _writeWindow?.Leave();
        var installed = id is null ? null : AssistantPackages.Find(id);
        var name = installed?.Manifest.Name ?? "K-Desktop";
        var window = new AssistantWriteWindow(Loc.T("assistantWrite.placeholder", name), installed is null ? null : AssistantLook.Icon(installed),
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
