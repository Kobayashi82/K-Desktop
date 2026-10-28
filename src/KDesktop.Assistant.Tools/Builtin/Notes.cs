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

    /// <summary>Puts back a note removed (an undo of delete_note), with its id and date; false when it is there already.</summary>
    public bool Restore(Note note)
    {
        lock (_gate)
        {
            var notes = Notes();
            if (notes.Any(n => n.Id == note.Id)) return false;
            notes.Add(note);
            notes.Sort((a, b) => a.At.CompareTo(b.At));
            Save(notes);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The notes <paramref name="query"/> points at (B1, 5.0.343), for changing or deleting one: the one with that very text;
    /// else the ones with the most of its words (several when they tie: then the user is asked which).
    /// <paramref name="having"/>: only notes with these items in them («quita la leche» is the list with milk).
    /// </summary>
    public IReadOnlyList<Note> Pointed(string? query, IReadOnlyList<string>? having = null)
    {
        List<Note> notes;
        lock (_gate) notes = [.. Notes()];
        if (having is { Count: > 0 }) notes = [.. notes.Where(n => having.Any(h => NoteItems.Parse(n.Text).IndexOf(h) >= 0))];
        var text = query?.Trim() ?? string.Empty;
        if (text.Length == 0) return notes;
        if (notes.FirstOrDefault(n => string.Equals(n.Text.Trim(), text, StringComparison.OrdinalIgnoreCase)) is { } same) return [same];
        var words = FuzzyMatch.Tokens(text).Where(w => w.Length > 2).Distinct().ToArray();
        if (words.Length == 0) return [];
        var scored = notes
            .Select(n => (Note: n, Score: words.Count(w => FuzzyMatch.Tokens(n.Text).Any(t => t.StartsWith(w, StringComparison.Ordinal)))))
            .Where(p => p.Score > 0)
            .ToList();
        if (scored.Count == 0) return [];
        var best = scored.Max(p => p.Score);
        return [.. scored.Where(p => p.Score == best).Select(p => p.Note).OrderByDescending(n => n.At)];
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

/// <summary>What edit_note and delete_note share: reading their arguments and asking which note when several fit.</summary>
internal static class NoteTools
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A list argument, also given as one text («leche, huevos») or a single item.</summary>
    public static List<string> List(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value)) return [];
        IEnumerable<string?> raw = value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()),
            JsonValueKind.String => (value.GetString() ?? string.Empty).Split([',', '\n', ';']),
            _ => [],
        };
        return [.. raw.Select(r => r?.Trim() ?? string.Empty).Where(r => r.Length > 0)];
    }

    /// <summary>Several notes fit: nothing is changed, and the user is asked which one.</summary>
    public static ToolResult Which(IReadOnlyList<Note> notes)
    {
        var shown = notes.Take(5).Select(n => ToolText.Clean(n.Text.ReplaceLineEndings(" "))).ToList();
        return new ToolResult("Several notes fit, so nothing was changed: ask the user which one. Notes: " +
            string.Join("; ", shown.Select((t, i) => $"{i + 1}) {t}")))
        {
            Done = Loc.T("tool.notes.whichDone"),
            Reply = Loc.T("tool.notes.which", string.Join(" · ", shown)),
        };
    }

    public static ToolResult None() =>
        ToolResult.Fail("There is no such note (to start a new one, use save_note).") with { Reply = Loc.T("tool.notes.none"), Done = Loc.T("tool.notes.noneDone") };

    public static ToolUndo Undo(string tool, Note before) => new(tool, JsonSerializer.Serialize(before, Json));

    /// <summary>Puts the note back as it was (changed or removed).</summary>
    public static bool Revert(NoteStore notes, ToolUndo undo)
    {
        if (JsonSerializer.Deserialize<Note>(undo.Data, Json) is not { } before) return false;
        return notes.All.Any(n => n.Id == before.Id) ? notes.Edit(before.Id, before.Text) : notes.Restore(before);
    }
}

/// <summary>«Quita la leche de la lista de la compra», «añade huevos» (B1, 5.0.343).</summary>
public sealed class EditNoteTool(NoteStore notes) : ITool
{
    public string Name => "edit_note";

    public string Description => "Changes one of the user's notes: adds lines or items to it, or takes some out (a shopping list, a to-do list). Never to start a new note (that is save_note).";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"note":{"type":"string","description":"Which note: a few words of it or its title (the shopping list)"},"add":{"type":"array","items":{"type":"string"},"description":"Items or lines to add"},"remove":{"type":"array","items":{"type":"string"},"description":"Items to take out (a word or two of each)"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) =>
        Loc.T("tool.edit_note.intent", ToolText.Clean(arguments.GetString("note") is { Length: > 0 } note ? note
            : string.Join(", ", NoteTools.List(arguments, "add").Concat(NoteTools.List(arguments, "remove")))));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var add = NoteTools.List(arguments, "add");
        var remove = NoteTools.List(arguments, "remove");
        if (add.Count == 0 && remove.Count == 0) return Task.FromResult(ToolResult.Fail("Say what to add to the note or take out of it."));
        var query = arguments.GetString("note");
        // Taking something out: the note that has it (only one note, or one of those named).
        var pointed = notes.Pointed(query, remove.Count > 0 ? remove : null);
        if (pointed.Count == 0 && remove.Count > 0 && notes.Pointed(query) is [var named])
            return Task.FromResult(ToolResult.Fail($"That note does not have {string.Join(", ", remove)}: {named.Text}") with
            {
                Reply = Loc.T("tool.edit_note.missing", ToolText.Clean(string.Join(", ", remove))),
                Done = Loc.T("tool.edit_note.missingDone", ToolText.Clean(named.Text)),
            });
        if (pointed.Count == 0 || (string.IsNullOrWhiteSpace(query) && remove.Count == 0)) return Task.FromResult(NoteTools.None());
        if (pointed.Count > 1) return Task.FromResult(NoteTools.Which(pointed));

        var before = pointed[0];
        var list = NoteItems.Parse(before.Text);
        var missing = new List<string>();
        foreach (var item in remove)
        {
            var at = list.IndexOf(item);
            if (at >= 0) list.Items.RemoveAt(at);
            else missing.Add(item);
        }
        foreach (var item in add)
            if (list.IndexOf(item) < 0) list.Items.Add(item);

        var after = list.Text;
        if (after == before.Text.Trim())
            return Task.FromResult(new ToolResult($"Nothing to change; the note is: {before.Text}") { Done = Loc.T("tool.edit_note.same", ToolText.Clean(before.Text)) });
        var note = ToolText.Clean(after.ReplaceLineEndings(" · "));
        var gone = list.Empty && !list.HasHeader;
        if (gone) notes.Remove(before.Id);
        else notes.Edit(before.Id, after);
        var said = gone ? "Nothing was left in the note, so it was deleted." : "The note now says: " + after;
        if (missing.Count > 0) said += $" (It did not have {string.Join(", ", missing)}.)";
        return Task.FromResult(new ToolResult(said)
        {
            Done = gone ? Loc.T("tool.delete_note.done", ToolText.Clean(before.Text)) : Loc.T("tool.edit_note.done", note),
            Reply = gone ? Loc.T("tool.edit_note.emptied") : Loc.T("tool.edit_note.reply", note),
            Undo = NoteTools.Undo(Name, before),
        });
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(NoteTools.Revert(notes, undo));
}

/// <summary>«Borra la nota del fontanero» (B1, 5.0.343): when several fit, it asks which.</summary>
public sealed class DeleteNoteTool(NoteStore notes) : ITool
{
    public string Name => "delete_note";

    public string Description => "Deletes one of the user's notes, the whole note. When several fit it deletes none and you ask which one. To take only something out of a note, use edit_note.";

    public string ParametersSchemaJson => ToolText.OneString("note", "Which note: a few words of it, or its whole text");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.delete_note.intent", ToolText.Clean(arguments.GetString("note")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.GetString("note");
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(ToolResult.Fail("Say which note to delete."));
        var pointed = notes.Pointed(query);
        if (pointed.Count == 0) return Task.FromResult(NoteTools.None());
        if (pointed.Count > 1) return Task.FromResult(NoteTools.Which(pointed));
        var note = pointed[0];
        notes.Remove(note.Id);
        var text = ToolText.Clean(note.Text.ReplaceLineEndings(" · "));
        return Task.FromResult(new ToolResult("Deleted the note: " + note.Text)
        {
            Done = Loc.T("tool.delete_note.done", text),
            Reply = Loc.T("tool.delete_note.reply", text),
            Undo = NoteTools.Undo(Name, note),
        });
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(NoteTools.Revert(notes, undo));
}
