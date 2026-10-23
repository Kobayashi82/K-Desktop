using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

public sealed record Note(string Id, string Text, DateTimeOffset At);

/// <summary>The notes the assistants take for the user (shared by all of them), kept in a file. Thread-safe.</summary>
public sealed class NoteStore(string file)
{
    public const int MaxNotes = 500;

    public const int MaxLength = 500;

    /// <summary>A note the user keeps by hand (Options → Notes, a long answer's «Guardar como nota»): a whole text.</summary>
    public const int MaxKept = 20000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private List<Note>? _notes;

    /// <summary>A note was taken, changed or removed (any thread): Options → Notes shows it.</summary>
    public event Action? Changed;

    public IReadOnlyList<Note> All
    {
        get { lock (_gate) return [.. Notes()]; }
    }

    /// <summary>Takes a note; the same text again is not repeated (<c>IsNew</c> false). <paramref name="most"/>: its longest.</summary>
    public (Note Note, bool IsNew) Add(string text, DateTimeOffset at, int most = MaxLength)
    {
        text = text.Trim();
        if (text.Length > most) text = text[..most].TrimEnd() + "…";
        Note note;
        lock (_gate)
        {
            var notes = Notes();
            if (notes.FirstOrDefault(n => string.Equals(n.Text, text, StringComparison.OrdinalIgnoreCase)) is { } same) return (same, false);
            note = new Note(Guid.NewGuid().ToString("N")[..10], text, at);
            notes.Add(note);
            if (notes.Count > MaxNotes) notes.RemoveRange(0, notes.Count - MaxNotes);
            Save(notes);
        }
        Changed?.Invoke();
        return (note, true);
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            var notes = Notes();
            if (notes.RemoveAll(n => n.Id == id) == 0) return false;
            Save(notes);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Changes the text of one (Options → Notes).</summary>
    public bool Edit(string id, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return false;
        if (text.Length > MaxKept) text = text[..MaxKept].TrimEnd() + "…";
        lock (_gate)
        {
            var notes = Notes();
            var index = notes.FindIndex(n => n.Id == id);
            if (index < 0) return false;
            notes[index] = notes[index] with { Text = text };
            Save(notes);
        }
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            var notes = Notes();
            if (notes.Count == 0) return;
            notes.Clear();
            Save(notes);
        }
        Changed?.Invoke();
    }

    /// <summary>The notes with any of these words (no accents, any case), best first; nothing asked: the newest.</summary>
    public IReadOnlyList<Note> Find(string? query, int max)
    {
        var words = FuzzyMatch.Tokens(query ?? string.Empty).Where(w => w.Length > 2).ToArray();
        lock (_gate)
        {
            var notes = Notes();
            if (words.Length == 0) return [.. notes.OrderByDescending(n => n.At).Take(max)];
            return [.. notes
                .Select(n => (Note: n, Score: words.Count(w => FuzzyMatch.Tokens(n.Text).Any(t => t.StartsWith(w, StringComparison.Ordinal)))))
                .Where(p => p.Score > 0)
                .OrderByDescending(p => p.Score).ThenByDescending(p => p.Note.At)
                .Take(max)
                .Select(p => p.Note)];
        }
    }

    private List<Note> Notes()
    {
        if (_notes is not null) return _notes;
        try
        {
            _notes = File.Exists(file) ? JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(file), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido leer las notas: {ex.Message}");
            _notes = [];
        }
        return _notes;
    }

    private void Save(List<Note> notes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(notes, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido guardar las notas: {ex.Message}");
        }
    }
}

/// <summary>«Apunta que tengo que comprar pilas».</summary>
public sealed class SaveNoteTool(NoteStore notes, TimeProvider? time = null) : ITool
{
    public string Name => "save_note";

    public string Description => "Writes a note for the user, as they said it, to read it later. Only when they ask you to note or write something down; never for your own thoughts or about this conversation.";

    public string ParametersSchemaJson => ToolText.OneString("note", "The note, in the user's words");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.save_note.intent", ToolText.Clean(arguments.GetString("note")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var text = arguments.GetString("note")?.Trim();
        if (string.IsNullOrEmpty(text)) return Task.FromResult(ToolResult.Fail("The note is empty."));
        var (note, isNew) = notes.Add(text, (time ?? TimeProvider.System).GetLocalNow());
        return Task.FromResult(new ToolResult(isNew ? "Noted." : "That note was already there.")
        {
            Done = Loc.T(isNew ? "tool.save_note.done" : "tool.save_note.known", ToolText.Clean(note.Text)),
            Reply = Loc.T(isNew ? "tool.save_note.reply" : "tool.save_note.replyKnown", ToolText.Clean(note.Text)),
            Undo = isNew ? new ToolUndo(Name, note.Id) : null,
        });
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(notes.Remove(undo.Data));
}

/// <summary>«¿Qué tenía que comprar?», «lee mis notas».</summary>
public sealed class FindNotesTool(NoteStore notes) : ITool
{
    /// <summary>Notes given to the model at most.</summary>
    public const int MaxGiven = 12;

    public string Name => "find_notes";

    public string Description => "Reads the user's notes (those about something, or the newest ones). Use it whenever they ask what they noted, had to do or had to buy: never answer that from memory.";

    public string ParametersSchemaJson => ToolText.Optional(("search", "string", "Words to look for (empty: the newest notes)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => ToolText.Clean(arguments.GetString("search")) is { Length: > 0 } query
        ? Loc.T("tool.find_notes.intentFor", query)
        : Loc.T("tool.find_notes.intent");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.GetString("search");
        var done = ToolText.Clean(query) is { Length: > 0 } words ? Loc.T("tool.find_notes.doneFor", words) : Loc.T("tool.find_notes.done");
        var found = notes.Find(query, MaxGiven);
        var text = found.Count == 0 ? "There are no such notes."
            : "Notes: " + string.Join("; ", found.Select(n => $"({n.At.ToLocalTime():yyyy-MM-dd}) {n.Text}"));
        return Task.FromResult(new ToolResult(text) { Done = done });
    }
}
