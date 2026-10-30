using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

public sealed partial class CommandEngine
{
    /// <summary>The longest a command waits for an assistant's answer (a big model that loads and uses tools takes a while).</summary>
    public static readonly TimeSpan AnswerWait = TimeSpan.FromMinutes(3);

    /// <summary>
    /// «Asistente: decir» (D4, 5.0.372: «Leer lo seleccionado» with {seleccion}, whole however long). With the selection and
    /// nothing selected, the OSD says so instead of saying nothing.
    /// </summary>
    private void AssistantSay(AssistantSayAction a)
    {
        var text = TextVariables.Expand(a.Text);
        if (text.Trim().Length == 0 && TextVariables.UsesSelection(a.Text))
        {
            _ui.ShowOsd(Loc.T("osd.clipboardNoSelection"), OsdKind.Warning, look: OsdLook.Of("Megaphone24"));
            return;
        }
        _ui.AssistantSay(a.Assistant, text, a.CatchEye, a.BringOut);
    }

    /// <summary>
    /// «Asistente: preguntar» (D1, 5.0.370). «Decirla»: the order goes and the command goes on, as always. Otherwise the
    /// command waits for the answer (Esc stops it, and the assistant too), leaves it in {resultado} and pastes it over the
    /// selection, copies it or shows it on the assistant's sheet. No answer: the rest of the command is not run.
    /// </summary>
    private async Task AssistantAskAsync(AssistantAskAction a, ExecutionContext ctx)
    {
        var text = TextVariables.Expand(a.Text);
        if (a.Answer == AssistantAnswer.Say)
        {
            _ui.AskAssistant(a.Assistant, text, a.BringOut);
            return;
        }

        RepliedMessage? reply;
        var cts = BeginCancellable(ctx.Token);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        limit.CancelAfter(AnswerWait);
        try
        {
            reply = await _ui.AskAssistantForAsync(a.Assistant, text, a.BringOut, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            Log.Warn($"'{ctx.Command.Name}': el asistente «{a.Assistant}» no ha contestado en {AnswerWait.TotalMinutes:0} minutos");
            _ui.ShowOsd(Loc.T("osd.assistantNoAnswer"), OsdKind.Warning);
            throw new OperationCanceledException(); // the rest of the command needs the answer
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ctx.Token.IsCancellationRequested)
        {
            CountdownCancelled();
            throw;
        }
        finally { EndCancellable(cts); }

        // Not done (the OSD said why) or no answer: what comes after needed it.
        if (reply is not { Ok: true })
        {
            if (reply is { Text.Length: > 0 } failed) _ui.ShowOsd(failed.Text, OsdKind.Warning);
            throw new OperationCanceledException();
        }

        // A long answer: its whole text, not the summary it would have said.
        var answer = reply.Written is { Length: > 0 } written ? written : reply.Text;
        Gave(a, answer);
        switch (a.Answer)
        {
            case AssistantAnswer.Paste:
                await PasteAnswerAsync(answer, ctx).ConfigureAwait(false);
                break;
            case AssistantAnswer.Copy:
                if (_ui.SetClipboardText(answer)) _ui.ShowOsd(Loc.T("osd.assistantAnswerCopied"), OsdKind.Success, look: OsdLook.Of("Copy24"));
                else Log.Warn("Asistente: preguntar: no se pudo copiar la respuesta (otro programa tiene el portapapeles abierto)");
                break;
            case AssistantAnswer.Sheet:
                _ui.ShowAnswerSheet(a.Assistant, answer);
                break;
        }
    }

    /// <summary>
    /// «Usar una herramienta» (G1): the tool runs in the assistants' process, with the variables put inside the texts of its
    /// arguments; what it gives back goes to {resultado}. Not done or failed (the OSD says why): the rest of the command is
    /// not run, it needed it. Run by itself (an event, at startup…), what would ask is not done. Esc stops waiting.
    /// </summary>
    private async Task AssistantToolAsync(AssistantToolAction a, ExecutionContext ctx)
    {
        if (a.Tool.Length == 0) return;
        var arguments = ExpandArguments(a.Arguments);
        var unattended = ctx.Source is TriggerSource.Event or TriggerSource.Startup or TriggerSource.Exit or TriggerSource.Remote
            or TriggerSource.Web or TriggerSource.Mcp or TriggerSource.CommandLine or TriggerSource.Telegram;

        Assistant.Pipe.ToolTestMessage? done;
        var cts = BeginCancellable(ctx.Token);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        limit.CancelAfter(AnswerWait);
        try
        {
            done = await _ui.UseAssistantToolAsync(a.Tool, arguments, ctx.Command.Name, unattended, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            Log.Warn($"'{ctx.Command.Name}': la herramienta {a.Tool} no ha contestado en {AnswerWait.TotalMinutes:0} minutos");
            _ui.ShowOsd(Loc.T("osd.toolNoAnswer"), OsdKind.Warning);
            throw new OperationCanceledException();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ctx.Token.IsCancellationRequested)
        {
            CountdownCancelled();
            throw;
        }
        finally { EndCancellable(cts); }

        if (done is null) throw new OperationCanceledException(); // the OSD said why
        if (!done.Ok)
        {
            Log.Warn($"'{ctx.Command.Name}': la herramienta {a.Tool} no lo hizo: {done.Text}");
            _ui.ShowOsd(done.Text.Length > 0 ? done.Text : Loc.T("osd.toolFailed"), OsdKind.Warning, look: OsdLook.Of("Wrench24"));
            throw new OperationCanceledException();
        }
        Log.Info($"'{ctx.Command.Name}': la herramienta {a.Tool} dio {done.Text.Length} letras en {done.Ms} ms");
        Gave(a, done.Text);
    }

    /// <summary>
    /// The arguments with the variables put inside their texts (only there, so a {seleccion} with quotes stays good JSON);
    /// not JSON (written by hand and broken): as they are, the tool says what is wrong.
    /// </summary>
    internal static string ExpandArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return "{}";
        System.Text.Json.Nodes.JsonNode? root;
        try { root = System.Text.Json.Nodes.JsonNode.Parse(arguments); }
        catch (System.Text.Json.JsonException) { return arguments; }
        if (root is null) return "{}";
        if (Text(root) is { } alone) return System.Text.Json.Nodes.JsonValue.Create(alone).ToJsonString();
        Walk(root);
        return root.ToJsonString();

        static string? Text(System.Text.Json.Nodes.JsonNode? node) =>
            node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text) ? TextVariables.Expand(text) : null;

        static void Walk(System.Text.Json.Nodes.JsonNode node)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj:
                    foreach (var (name, value) in obj.ToList())
                    {
                        if (Text(value) is { } text) obj[name] = text;
                        else if (value is not null) Walk(value);
                    }
                    break;
                case System.Text.Json.Nodes.JsonArray list:
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (Text(list[i]) is { } text) list[i] = text;
                        else if (list[i] is { } item) Walk(item);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The answer over the selection, in the window the command was run over, like transform_text: only there (another one
    /// in front now: left copied, and the OSD says so), and the clipboard put back as it was.
    /// </summary>
    private async Task PasteAnswerAsync(string answer, ExecutionContext ctx)
    {
        var target = ctx.Foreground;
        // Still on K-Desktop's own (the menu, the box to write it): back to the window it was opened over.
        if (target.Handle != IntPtr.Zero && ForegroundWindow.Capture().ProcessId == (uint)Environment.ProcessId)
        {
            WindowTools.ForceForeground(target.Handle);
            await Task.Delay(60, ctx.Token).ConfigureAwait(false);
        }
        var put = new TextMessage(string.Empty, string.Empty, "put", Text: answer, Mode: "paste", Hwnd: (long)target.Handle);
        var done = await AssistantText.HandleAsync(put, new SystemTextDesk(), ctx.Token).ConfigureAwait(false);
        if (done is { Ok: true, From: "copied" }) _ui.ShowOsd(Loc.T("osd.assistantAnswerMoved"), OsdKind.Info, look: OsdLook.Of("Copy24"));
        else if (!done.Ok) Log.Warn($"Asistente: preguntar: no se pudo pegar la respuesta ({done.Reason})");
    }
}
