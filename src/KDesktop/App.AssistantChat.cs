using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;
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

    /// <summary>A long answer's whole text in Markdown (stage 14.2; null: none): <see cref="Text"/> is its summary.</summary>
    [ObservableProperty] private string? _written;

    /// <summary>False while the answer is still being written.</summary>
    [ObservableProperty] private bool _final;

    public override string ToString() => Text;
}

/// <summary>
/// The inputs of the assistants (stage 3.6): the chat of Panel → Assistant, the box to write to one (its menu, a middle
/// click on it, «Asistente: preguntar» without text) and «Asistente: preguntar». All of them send the same
/// <c>heard</c> as the voice; the answer comes back as <c>chat</c> lines, kept here per assistant (K-Desktop's under its
/// id: in the process it is the one without one) and in <c>Assistant\chats.json</c>, so they are there after a restart. Without a figure to say it (K-Desktop, 5.0.112, or
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
        LoadChats();
        if (!_chats.TryGetValue(id, out var chat)) _chats[id] = chat = [];
        return chat;
    }

    private static string ChatsFile => Path.Combine(AppPaths.DataDir, "Assistant", "chats.json");

    private static readonly JsonSerializerOptions ChatsJson = new(JsonSerializerDefaults.Web);

    private bool _chatsLoaded;

    private sealed record SavedChatLine(string Role, string Turn, DateTime At, string Text, string? Written);

    /// <summary>The conversations of before (once, the first time one is asked for).</summary>
    private void LoadChats()
    {
        if (_chatsLoaded) return;
        _chatsLoaded = true;
        try
        {
            if (!File.Exists(ChatsFile)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, List<SavedChatLine>>>(File.ReadAllText(ChatsFile), ChatsJson) ?? [];
            foreach (var (id, lines) in saved)
                _chats[id] = [.. lines.TakeLast(KeptChat).Select(l => new ChatItem(l.Role, l.Turn, l.At) { Text = l.Text, Written = l.Written, Final = true })];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido leer las conversaciones: {ex.Message}");
        }
    }

    /// <summary>Written when a line is complete (not while an answer is being written): small, and not often.</summary>
    private void SaveChats()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ChatsFile)!);
            var all = _chats.Where(c => c.Value.Count > 0).ToDictionary(c => c.Key,
                c => c.Value.Where(l => l.Final || l.IsUser).Select(l => new SavedChatLine(l.Role, l.Turn, l.At, l.Text, l.Written)).ToList());
            File.WriteAllText(ChatsFile, JsonSerializer.Serialize(all, ChatsJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido guardar las conversaciones: {ex.Message}");
        }
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
        item.Written = line.Written;
        item.Final = line.Final;
        if (line.Role == "assistant") OnLongAnswer(who, line);
        // Said aloud by one on the desktop: heard back through the microphone it is not an order (stage 5.7).
        // What K-Desktop or one that is not there says is said aloud too (5.0.111).
        if (line.Role == "assistant") Voice.OwnVoices.Said(who + "/" + line.Turn, line.Text, DateTime.Now);
        AssistantChatChanged?.Invoke(who);
        if (line.Final || line.Role == "user") SaveChats();
        if (line is { Role: "assistant", Final: true }) ShowUnseenAnswer(who, line.Text);
    }

    /// <summary>
    /// An answer no figure says (K-Desktop's, or one's that is not on the desktop): K-Desktop shows it, unless it was asked
    /// in the panel's chat, where it is already seen. As long as it takes to read it.
    /// </summary>
    private void ShowUnseenAnswer(string id, string text)
    {
        if (text.Length == 0 || (IsOnDesktop(id) && !AssistantTargeting.IsSystem(id))) return;
        // In the panel's chat it is already seen; asked from the phone, the phone shows it.
        if (_chatSources.GetValueOrDefault(id) is "panel" or "phone") return;
        var seconds = Math.Clamp(2 + text.Length * 0.06, 4, 15);
        Ui.ShowOsd($"{AssistantName(id)}: {text}", Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(seconds));
    }

    /// <summary>Whether an assistant is working on an answer now (K-Desktop's state is the process's own).</summary>
    public bool IsAssistantThinking(string? id) =>
        id is not null && (AssistantTargeting.IsSystem(id) ? AssistantStatus : AssistantStateOf(id)) is { State: "thinking" or "thinkingHard" or "loadingBrain" or "noBrain" or "speaking" or "busy" or "awaitingConfirmation" };

    /// <summary>
    /// Sends an order to an assistant (null: nobody is active), as if said to it: <paramref name="source"/> <c>panel</c>,
    /// <c>bubble</c>, <c>command</c>, <c>voice</c>… False (and an OSD) when nobody can answer now.
    /// </summary>
    /// <param name="files">Files given with it (stage 14.1: dropped on the assistant); <paramref name="act"/>: what to do with them without the AI.</param>
    /// <param name="permit">An order an assistant programmed (source <c>scheduled</c>): what was approved for it then.</param>
    public bool SendToAssistant(string? id, string text, string source, IReadOnlyList<string>? files = null, string? act = null, ScheduledPermit? permit = null)
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
        // The process starting (K-Desktop just started, the module just switched on, it fell and comes back) or the
        // assistant still coming out: the order waits for it instead of being lost («Asistente: preguntar» at startup).
        if (_assistant.State is AssistantProcessState.Starting or AssistantProcessState.Restarting
            || (_assistant.IsRunning && AssistantConnectedAt is { } connected && DateTime.Now - connected < PendingOrderTime
                && !AssistantTargeting.IsSystem(id) && IsOnDesktop(id) && !IsAssistantLoaded(id)))
        {
            _pendingOrders.RemoveAll(o => DateTime.UtcNow - o.At > PendingOrderTime);
            _pendingOrders.Add(new PendingOrder(id, text, source, DateTime.UtcNow, files, act, permit));
            Log.Info($"Asistente: orden por {source} para «{id}» en espera de que esté listo");
            return true;
        }
        var heard = new HeardMessage(text, source, ProcessIdOf(id), files is { Count: > 0 } ? files : null, act,
            permit?.Key, permit is null ? null : [.. permit.Tools.Order(StringComparer.Ordinal)]);
        if (!_assistant.IsRunning || !_assistant.Send(heard))
        {
            Ui.ShowOsd(Loc.T("assistantChat.notRunning"), Core.Engine.OsdKind.Warning);
            return false;
        }
        if (IsOnDesktop(id)) AssistantTalkedTo(id);
        _chatSources[id] = source;
        Log.Info($"Asistente: orden por {source} para «{id}»");
        return true;
    }

    /// <summary>How long an order waits for its assistant (the process starts in ~1 s; an assistant comes out in a few).</summary>
    private static readonly TimeSpan PendingOrderTime = TimeSpan.FromSeconds(30);

    private sealed record PendingOrder(string Id, string Text, string Source, DateTime At, IReadOnlyList<string>? Files = null, string? Act = null,
        ScheduledPermit? Permit = null);

    private readonly List<PendingOrder> _pendingOrders = [];

    /// <summary>
    /// The orders that waited for <paramref name="id"/> go now, in order: K-Desktop's when the process connects, an
    /// assistant's when it says it is loaded (<paramref name="ok"/> false: it did not come, they are dropped).
    /// </summary>
    private void SendPendingOrders(string id, bool ok = true)
    {
        var waiting = _pendingOrders.Where(o => o.Id == id).ToList();
        if (waiting.Count == 0) return;
        _pendingOrders.RemoveAll(o => o.Id == id);
        foreach (var order in waiting)
        {
            if (!ok || DateTime.UtcNow - order.At > PendingOrderTime)
            {
                Log.Info($"Asistente: la orden por {order.Source} para «{id}» no se manda ({(ok ? "llegó tarde" : "no ha salido")})");
                continue;
            }
            SendToAssistant(id, order.Text, order.Source, order.Files, order.Act, order.Permit);
        }
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
        SaveChats();
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
    /// comes back shows it), going towards the mouse first if <paramref name="catchEye"/>. Not while it is hidden.
    /// </summary>
    private void SayFromCommand(string id, string text, bool catchEye)
    {
        text = text.Trim();
        if (text.Length == 0 || !CanCommandAssistant(id)) return;
        _chatSources[id] = "command";
        if (!_assistant!.IsRunning || !_assistant.Send(new SayMessage(text, ProcessIdOf(id), CatchEye: catchEye)))
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

    private AssistantDropWindow? _dropWindow;

    /// <summary>How long the microphone waits for what to do with dropped files.</summary>
    private static readonly TimeSpan DropListen = TimeSpan.FromSeconds(8);

    /// <summary>
    /// <c>dropped</c>: files dropped on an assistant (stage 13.8). It has caught them; K-Desktop asks what to do with them
    /// (stage 14.1): buttons by the kind of file, the box to write it and, with the voice models, the microphone a few
    /// seconds. The choice goes to it as an order with the files.
    /// </summary>
    private void OnAssistantDropped(DroppedMessage dropped)
    {
        if (!IsOnDesktop(dropped.Assistant) || dropped.Files.Count == 0) return;
        Log.Info($"Asistente: «{dropped.Assistant}» ha recibido {dropped.Files.Count} archivo(s): {string.Join(", ", dropped.Files.Select(System.IO.Path.GetFileName))}");
        var id = dropped.Assistant;
        var files = dropped.Files;
        _dropWindow?.Leave();
        var canSend = AssistantTools?.Tools.Any(t => t.Enabled && SendsThings(t.Name)) == true;
        var window = new AssistantDropWindow(AssistantName(id), AssistantLook.IconOf(id), files, new UiNative.POINT { X = dropped.X, Y = dropped.Y }, canSend,
            (text, act) => SendToAssistant(id, text, "bubble", files, act));
        window.Closed += (_, _) =>
        {
            if (_dropWindow == window) _dropWindow = null;
        };
        _dropWindow = window;
        window.Show();
        ListenForDrop(window, id);
    }

    /// <summary>A tool that sends things somewhere (Telegram, mail…): «Enviar» is offered for dropped files.</summary>
    private static bool SendsThings(string tool) => SendWords.Any(w => tool.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] SendWords = ["send", "telegram", "mail", "whatsapp", "share", "upload"];

    /// <summary>While «¿Qué hago con…?» is open, what is said (without the wake word) is the answer.</summary>
    private async void ListenForDrop(AssistantDropWindow window, string id)
    {
        if (!Voice.Ready) return;
        window.ShowListening();
        var to = new VoiceAddressee(AssistantName(id), (text, _) =>
        {
            Log.Info($"Asistente: «{text}» para los archivos de «{id}» (por voz)");
            _chatSources[id] = "voice";
            Dispatcher.Invoke(() => window.Choose(text));
            return true;
        });
        try { await Voice.ListenForAsync(to, DropListen, window.Over.Token); }
        catch (ObjectDisposedException) { }
    }
}
