using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Native;
using KDesktop.Views;
using KDesktop.Views.Assistant;

namespace KDesktop;

/// <summary>
/// Long answers (stage 14.2): the whole text an assistant writes comes with its chat lines (<see cref="ChatLineMessage.Written"/>),
/// and K-Desktop shows it on a reading sheet next to it while its voice says the summary. One sheet per assistant: its next
/// long answer replaces the text. Not when it was asked in the panel's chat (which shows it whole) or from the phone (the
/// phone shows it). UI thread.
/// </summary>
public partial class App
{
    private readonly Dictionary<string, ReadingSheetWindow> _sheets = new(StringComparer.Ordinal);

    /// <summary>A chat line with a long text: on its sheet (made, or the one it has).</summary>
    private void OnLongAnswer(string id, ChatLineMessage line)
    {
        if (line.Written is not { Length: > 0 } text) return;
        if (_sheets.TryGetValue(id, out var open) && open.Turn == line.Turn)
        {
            open.Show(line.Turn, text, line.Final, At(line));
            return;
        }
        if (_chatSources.GetValueOrDefault(id) is "panel" or "phone" or ReplySource) return;
        ShowReadingSheet(id, line.Turn, text, line.Final, At(line));
    }

    private static UiNative.POINT? At(ChatLineMessage line) => line is { X: { } x, Y: { } y } ? new UiNative.POINT { X = x, Y = y } : null;

    /// <summary>The sheet of an assistant with that text (the chat's «Leer entero» too).</summary>
    internal void ShowReadingSheet(string id, string turn, string markdown, bool final = true, UiNative.POINT? at = null)
    {
        if (!_sheets.TryGetValue(id, out var sheet))
        {
            sheet = new ReadingSheetWindow(AssistantName(id), AssistantLook.IconOf(id),
                note => SaveAssistantNote(note),
                text => ReadAloud(id, text),
                () => StopAssistant(id),
                () => IsAssistantThinking(id),
                () => ShowChatWindow(id));
            var made = sheet;
            sheet.Closed += (_, _) =>
            {
                if (_sheets.TryGetValue(id, out var current) && current == made) _sheets.Remove(id);
            };
            _sheets[id] = sheet;
            Log.Info($"Asistente: hoja de lectura de «{id}» ({markdown.Length} letras)");
        }
        sheet.Show(turn, markdown, final, at);
    }

    /// <summary>«Guardar como nota»: the text goes to the assistants' notes (Options → Assistant → Notes).</summary>
    private void SaveAssistantNote(string text)
    {
        if (_assistant is not { IsRunning: true } || !_assistant.Send(new NotesMessage("add", Text: text)))
            Ui.ShowOsd(Loc.T("readingSheet.noteFailed"), Core.Engine.OsdKind.Warning);
    }

    /// <summary>«Léemela»: the assistant reads it aloud, only with its voice.</summary>
    private void ReadAloud(string id, string text)
    {
        if (_assistant is { IsRunning: true }) _assistant.Send(new SayMessage(text, ProcessIdOf(id), Aloud: true));
    }

    /// <summary>The sheets go with the module (or when their assistant leaves).</summary>
    private void CloseReadingSheets(string? id = null)
    {
        foreach (var (key, sheet) in _sheets.ToList())
            if (id is null || key == id) sheet.Leave();
    }
}
