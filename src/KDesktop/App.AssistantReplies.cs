using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop;

/// <summary>
/// «Asistente: preguntar» with something to do with the answer (D1, 5.0.370): the order goes with a <c>reply</c> id, the
/// assistant answers quietly (no voice nor bubble; its chat has it) and the answer comes back as <c>replied</c>, for the
/// command to paste, copy, show or keep. While it thinks, the OSD says so. UI thread.
/// </summary>
public partial class App
{
    /// <summary>The source kept for a conversation whose last order is answered quietly: no sheet nor OSD of its own.</summary>
    private const string ReplySource = "reply";

    private readonly Dictionary<string, TaskCompletionSource<RepliedMessage?>> _replies = new(StringComparer.Ordinal);

    /// <summary>
    /// The order, answered quietly, and its answer; null when it is not done (the OSD says why), the box to write it was
    /// closed or the process went. Cancelling stops the assistant.
    /// </summary>
    private async Task<RepliedMessage?> AskForAnswerFromCommand(string id, string text, bool bringOut, CancellationToken token)
    {
        if (!CanCommandAssistant(id, bringOut)) return null;
        var reply = Guid.NewGuid().ToString("N")[..12];
        var answered = new TaskCompletionSource<RepliedMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _replies[reply] = answered;
        IDisposable? thinking = null;
        void Send(string order)
        {
            if (!BringingOut(id, order, "command", reply) && !SendToAssistant(id, order, "command", reply: reply))
            {
                EndReply(reply, null);
                return;
            }
            thinking = Ui.ShowPersistentOsd(Loc.T("osd.assistantThinking", AssistantName(id)), icon: "ChatBubblesQuestion24");
        }
        var sent = false;
        if (text.Trim().Length == 0)
            ShowAssistantWriteBox(id, null, order => { sent = true; Send(order); }, () => { if (!sent) EndReply(reply, null); });
        else Send(text);

        using var stop = token.Register(() => Dispatcher.BeginInvoke(() =>
        {
            if (!answered.Task.IsCompleted) StopAssistant(id);
        }));
        try
        {
            return await answered.Task.WaitAsync(token);
        }
        finally
        {
            _replies.Remove(reply);
            thinking?.Dispose();
        }
    }

    /// <summary>
    /// An order (or, with <paramref name="say"/>, something to say) from a command for one that is still coming out (hidden and brought out now, or not loaded yet): it waits
    /// for it, and goes as soon as it is loaded. False: it is there (or it has no body), send it now.
    /// </summary>
    private bool BringingOut(string id, string text, string source, string? reply = null, bool? say = null)
    {
        if (AssistantTargeting.IsSystem(id))
        {
            if (!IsOnDesktop(id)) ShowAssistant(id);
            return false;
        }
        if (IsOnDesktop(id) && IsAssistantLoaded(id)) return false;
        _pendingOrders.Add(new PendingOrder(id, text, source, DateTime.UtcNow, Reply: reply, Say: say));
        Log.Info($"Asistente: orden por {source} para «{id}» en espera de que salga");
        if (!IsOnDesktop(id)) ShowAssistant(id);
        return true;
    }

    /// <summary><c>replied</c>: the answer the command is waiting for.</summary>
    private void OnAssistantReplied(RepliedMessage replied)
    {
        Log.Info($"Asistente: respuesta para un comando de «{FromProcess(replied.Assistant)}» ({(replied.Ok ? $"{replied.Text.Length} letras" : "sin respuesta")})");
        EndReply(replied.Id, replied);
    }

    private void EndReply(string reply, RepliedMessage? answer)
    {
        if (_replies.Remove(reply, out var waiting)) waiting.TrySetResult(answer);
    }

    /// <summary>The process went (or the module was switched off): nobody answers what was asked (what still waits to go, goes later).</summary>
    private void ForgetReplies()
    {
        var unsent = _pendingOrders.Select(o => o.Reply).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var reply in _replies.Keys.Where(r => !unsent.Contains(r)).ToList()) EndReply(reply, null);
    }

    /// <summary>«Mostrarla en una hoja»: the answer on its assistant's reading sheet.</summary>
    private void ShowAnswerSheetFromCommand(string id, string markdown) =>
        ShowReadingSheet(id, Guid.NewGuid().ToString("N")[..12], markdown);
}
