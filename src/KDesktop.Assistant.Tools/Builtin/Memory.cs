using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// What the assistants know about the user (stage 3.7, from Kobii's memory without diary or age): lasting facts, likes
/// and events, shared by all of them, in a file. A new one that says nearly the same as one already known replaces its
/// text; past <see cref="Capacity"/> the least valuable goes (importance, freshness, use; what the user asked to
/// remember or wrote weighs more). Options → Assistant → Memory shows, edits and deletes them. Thread-safe.
/// </summary>
public sealed partial class MemoryStore(string file, TimeProvider? time = null)
{
    public const int Capacity = 250;

    public const int MaxLength = 200;

    /// <summary>Share of meaningful words from which two memories are the same one.</summary>
    private const double SameWords = 0.6;

    /// <summary>Share of the shorter one's words found in the other: one says the same as the other, and more.</summary>
    private const double Contained = 0.75;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private List<MemoryEntry>? _items;

    /// <summary>Something was remembered, changed or forgotten (any thread).</summary>
    public event Action? Changed;

    /// <summary>Everything remembered, newest first.</summary>
    public IReadOnlyList<MemoryEntry> All
    {
        get { lock (_gate) return [.. Items().OrderByDescending(m => m.CreatedAt)]; }
    }

    /// <summary>
    /// Remembers something: <c>IsNew</c> when it was not known; one nearly the same takes the new text (<c>Previous</c>:
    /// the text it had). Null when the text is too short.
    /// </summary>
    /// <param name="kind"><c>fact</c>, <c>like</c> or <c>event</c>.</param>
    /// <param name="source"><c>said</c>, <c>learned</c> or <c>added</c>.</param>
    public (MemoryEntry Entry, bool IsNew, string? Previous)? Remember(string text, string kind, double importance, string source)
    {
        text = Clean(text);
        if (text.Length < 3) return null;
        (MemoryEntry, bool, string?) result;
        lock (_gate)
        {
            var items = Items();
            var words = Keywords(text);
            var same = items.Select(m => (Item: m, Similarity: Similarity(words, Keywords(m.Text))))
                .Where(p => p.Similarity >= SameWords).OrderByDescending(p => p.Similarity).Select(p => p.Item).FirstOrDefault();
            // Already said by a fuller one («Le gusta el café solo» of «Le gusta el café solo y salir a correr»): known.
            if (same is null && items.FirstOrDefault(m => Keywords(m.Text) is var known && known.Count > words.Count && Inside(words, known)) is { } fuller)
            {
                var kept = fuller with { Importance = Math.Max(fuller.Importance, importance) };
                items[items.IndexOf(fuller)] = kept;
                Save(items);
                return (kept, false, null);
            }
            // A fuller one of what was known takes its place.
            same ??= items.FirstOrDefault(m => Keywords(m.Text) is var known && known.Count >= 2 && known.Count < words.Count && Inside(known, words));
            if (same is not null)
            {
                var updated = same with
                {
                    Text = text,
                    Importance = Math.Max(same.Importance, importance),
                    // What the user said or wrote stays theirs.
                    Source = same.Source == "learned" ? source : same.Source,
                };
                items[items.IndexOf(same)] = updated;
                result = (updated, false, string.Equals(same.Text, text, StringComparison.OrdinalIgnoreCase) ? null : same.Text);
            }
            else
            {
                var entry = new MemoryEntry("m-" + Guid.NewGuid().ToString("N")[..10], text, kind is "like" or "event" ? kind : "fact",
                    Math.Clamp(importance, 0, 1), _time.GetLocalNow(), source);
                items.Add(entry);
                var now = _time.GetLocalNow();
                while (items.Count > Capacity) items.Remove(items.MinBy(m => Value(m, now))!);
                result = (entry, true, null);
            }
            Save(items);
        }
        Changed?.Invoke();
        return result;
    }

    /// <summary>Changes the text of one (Options, or undoing an update).</summary>
    public bool Edit(string id, string text)
    {
        text = Clean(text);
        if (text.Length < 3) return false;
        lock (_gate)
        {
            var items = Items();
            var index = items.FindIndex(m => m.Id == id);
            if (index < 0) return false;
            items[index] = items[index] with { Text = text };
            Save(items);
        }
        Changed?.Invoke();
        return true;
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var items = Items();
            if (items.RemoveAll(m => m.Id == id) == 0) return false;
            Save(items);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The ones <paramref name="text"/> names («olvida lo de la vuelta»): those that share the most of its words, at least
    /// half of them. Several only when they share as many.
    /// </summary>
    public IReadOnlyList<MemoryEntry> Find(string text)
    {
        var words = Keywords(text);
        if (words.Count == 0) return [];
        lock (_gate)
        {
            var scored = Items().Select(m => (Item: m, Shared: Keywords(m.Text).Count(words.Contains))).Where(p => p.Shared * 2 >= words.Count && p.Shared > 0).ToList();
            if (scored.Count == 0) return [];
            var most = scored.Max(p => p.Shared);
            return [.. scored.Where(p => p.Shared == most).Select(p => p.Item)];
        }
    }

    /// <summary>
    /// The ones with a phrase of the user's in quotes that the order says («Cuando diga «la vuelta», quiere que se abra…»
    /// and «vamos a hacer la vuelta»): the user taught it that phrase, so it wins over a command that sounds like it.
    /// </summary>
    public IReadOnlyList<MemoryEntry> Triggered(string said)
    {
        var words = TextSimilarity.Words(said);
        if (words.Length == 0) return [];
        lock (_gate)
        {
            return [.. Items().Where(m => Quoted().Matches(m.Text).Any(q => Contains(words, TextSimilarity.Words(q.Groups[1].Value))))];
        }

        // Word by word as written, nearly (a slip in a long word) or as it sounds: speech recognition writes a name its own
        // way each time («Kamugi», «Kamuji»).
        static bool Contains(string[] words, string[] phrase)
        {
            if (phrase.Length == 0 || phrase.Length > words.Length) return false;
            for (var i = 0; i + phrase.Length <= words.Length; i++)
            {
                var all = true;
                for (var k = 0; k < phrase.Length && all; k++) all = Alike(words[i + k], phrase[k]);
                if (all) return true;
            }
            return false;
        }

        static bool Alike(string said, string written) =>
            said == written
            || written.Length >= 5 && TextSimilarity.WordSimilarity(said, written) >= 0.85
            || written.Length >= 5 && said.Length >= 4 && PhoneticKey.Similarity(PhoneticKey.Encode(said), PhoneticKey.Encode(written)) >= 0.8;
    }

    [System.Text.RegularExpressions.GeneratedRegex("[«\"“„]([^«»\"“”„]{2,60})[»\"”“]")]
    private static partial System.Text.RegularExpressions.Regex Quoted();

    /// <summary>Puts back one that was forgotten (undoing «olvida…»).</summary>
    public bool Restore(MemoryEntry entry)
    {
        lock (_gate)
        {
            var items = Items();
            if (items.Any(m => m.Id == entry.Id)) return false;
            items.Add(entry);
            Save(items);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>The <paramref name="count"/> worth the most (what the user asked to remember first), to tell them what is known.</summary>
    public IReadOnlyList<MemoryEntry> Best(int count)
    {
        lock (_gate)
        {
            var now = _time.GetLocalNow();
            return [.. Items().OrderByDescending(m => Value(m, now)).Take(count)];
        }
    }

    public int Count
    {
        get { lock (_gate) return Items().Count; }
    }

    public void Clear()
    {
        lock (_gate)
        {
            var items = Items();
            if (items.Count == 0) return;
            items.Clear();
            Save(items);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// What comes to mind for an order, at most <paramref name="count"/>: those that share words with it or stand out by
    /// their meaning (<paramref name="meaning"/>: how alike each text is to the order, NaN when unknown), and the most
    /// important facts (a name is always useful). Marks them as recalled.
    /// </summary>
    public IReadOnlyList<MemoryEntry> Recall(string query, int count, Func<string, double>? meaning = null)
    {
        List<MemoryEntry> chosen;
        lock (_gate)
        {
            var items = Items();
            if (items.Count == 0 || count <= 0) return [];
            var now = _time.GetLocalNow();
            var words = Keywords(query);
            var boost = MeaningBoost(items, meaning);
            var relevant = items
                .Select(m => (Item: m, Overlap: words.Count == 0 ? 0 : Keywords(m.Text).Count(words.Contains), Boost: boost.GetValueOrDefault(m.Id)))
                .Where(p => p.Overlap > 0 || p.Boost > 0)
                .OrderByDescending(p => p.Overlap * 1.5 + p.Boost + Value(p.Item, now))
                .Select(p => p.Item);
            var core = items.Where(m => m.Kind == "fact" && m.Importance >= 0.8).OrderByDescending(m => Value(m, now)).Take(2);
            chosen = [.. relevant.Concat(core).Distinct().Take(count)];
            foreach (var item in chosen)
                items[items.IndexOf(item)] = item with { LastRecalled = now, RecallCount = item.RecallCount + 1 };
            if (chosen.Count > 0) Save(items);
        }
        return chosen;
    }

    /// <summary>The texts of all (to get their meaning).</summary>
    public IReadOnlyList<string> Texts
    {
        get { lock (_gate) return [.. Items().Select(m => m.Text)]; }
    }

    /// <summary>
    /// How much each memory rises for its meaning: only what stands out over the rest (z-score above the mean), as models
    /// give similarities on their own scales — what really has to do with the order, not everything a little.
    /// </summary>
    private static Dictionary<string, double> MeaningBoost(List<MemoryEntry> items, Func<string, double>? meaning)
    {
        var boost = new Dictionary<string, double>(StringComparer.Ordinal);
        if (meaning is null || items.Count < 2) return boost;
        var scores = items.Select(m => (m.Id, Value: meaning(m.Text))).Where(p => !double.IsNaN(p.Value)).ToList();
        if (scores.Count < 2) return boost;
        var mean = scores.Average(p => p.Value);
        var spread = Math.Sqrt(scores.Average(p => (p.Value - mean) * (p.Value - mean)));
        if (spread < 1e-6) return boost;
        foreach (var (id, value) in scores)
        {
            var z = (value - mean) / spread;
            // Clearly about it, even among few memories.
            if (z > 1 || value >= 0.75) boost[id] = Math.Min(3, Math.Max(0, z - 0.5) * 1.6 + (value >= 0.75 ? 1 : 0));
        }
        return boost;
    }

    /// <summary>How much it is worth keeping: importance, freshness and use; what the user said or wrote, more.</summary>
    private static double Value(MemoryEntry item, DateTimeOffset now)
    {
        var days = Math.Max(0, (now - (item.LastRecalled ?? item.CreatedAt)).TotalDays);
        var theirs = item.Source is "said" or "added" ? 0.5 : 0;
        var fact = item.Kind == "fact" ? 0.2 : 0;
        return item.Importance + 0.4 * Math.Exp(-days / 30) + 0.05 * Math.Min(item.RecallCount, 6) + theirs + fact;
    }

    private static string Clean(string text)
    {
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.').Trim();
        if (text.Length > 0 && char.IsLower(text[0])) text = char.ToUpper(text[0], Loc.Instance.Culture) + text[1..];
        return text.Length <= MaxLength ? text : text[..MaxLength].TrimEnd() + "…";
    }

    /// <summary>
    /// The meaningful words, folded and cut to their stem («cafés» and «café» are the same). Numbers always count: «Tiene
    /// 44 años» is the same as «Kobayashi tiene 44 años».
    /// </summary>
    public static HashSet<string> Keywords(string text) =>
        [.. TextSimilarity.Words(text).Where(w => (w.Length > 2 || w.All(char.IsDigit)) && !StopWords.Contains(w)).Select(w => w.Length > 6 && !w.All(char.IsDigit) ? w[..6] : w)];

    /// <summary>Whether most of <paramref name="part"/> (two words or more) is in <paramref name="whole"/>.</summary>
    private static bool Inside(HashSet<string> part, HashSet<string> whole) => part.Count >= 2 && part.Count(whole.Contains) >= Contained * part.Count;

    /// <summary>Jaccard of two sets of words.</summary>
    public static double Similarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var shared = a.Count(b.Contains);
        return shared / (double)(a.Count + b.Count - shared);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "que", "los", "las", "una", "uno", "unos", "unas", "del", "por", "para", "con", "sin", "sus", "mas", "pero", "como",
        "esta", "este", "esto", "eso", "esa", "ese", "son", "fue", "era", "muy", "mucho", "mucha", "tiene", "tengo", "tienes",
        "hay", "ser", "estar", "usuario", "usuaria", "the", "and", "you", "your", "with", "this", "that", "have", "has", "are",
        "was", "were", "for", "but", "not", "what", "who", "they", "them", "their", "from", "about", "user", "les", "des",
        "est", "une", "der", "die", "das", "und", "ist", "ein", "eine", "che", "per", "non", "uma", "com", "nao",
    };

    private List<MemoryEntry> Items()
    {
        if (_items is not null) return _items;
        try
        {
            _items = File.Exists(file) ? JsonSerializer.Deserialize<List<MemoryEntry>>(File.ReadAllText(file), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido leer la memoria: {ex.Message}");
            _items = [];
        }
        return _items;
    }

    private void Save(List<MemoryEntry> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(items, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar la memoria: {ex.Message}");
        }
    }
}

/// <summary>
/// «Recuerda que mi jefe se llama Luis»: something lasting about the user, for later conversations (the AI also learns
/// them by itself after talking: <c>MemoryLearner</c>, which uses this tool with the route <c>memory</c>).
/// </summary>
public sealed class RememberTool(MemoryStore memory) : ITool
{
    public string Name => "remember";

    public string Description => "Remembers a lasting fact about the user (name, family, work, likes, important dates) for later conversations. Use it when they ask you to remember something about them, not for to-dos (that is save_note), and never for what they think of you or of what you can do.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"fact":{"type":"string","description":"The fact, short, in the user's language and in the third person (Their boss is called Luis)"},"kind":{"type":"string","enum":["fact","like","event"]},"importance":{"type":"number","description":"0 to 1"}},"required":["fact"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.remember.intent", ToolText.Clean(arguments.GetString("fact")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var fact = arguments.GetString("fact") ?? string.Empty;
        var kind = arguments.GetString("kind") ?? "fact";
        var importance = arguments.GetNumber("importance") ?? 0.6;
        var source = arguments.GetString("source") is "learned" ? "learned" : "said";
        if (memory.Remember(fact, kind, importance, source) is not { } stored) return Task.FromResult(ToolResult.Fail("The fact is empty."));
        var (entry, isNew, previous) = stored;
        var known = !isNew && previous is null;
        return Task.FromResult(new ToolResult(known ? "Already known." : "Remembered.")
        {
            Done = Loc.T(known ? "tool.remember.known" : "tool.remember.done", ToolText.Clean(entry.Text)),
            Reply = Loc.T(known ? "tool.remember.replyKnown" : "tool.remember.reply", ToolText.Clean(entry.Text)),
            Undo = isNew ? new ToolUndo(Name, entry.Id) : previous is not null ? new ToolUndo(Name, entry.Id + "\n" + previous) : null,
        });
    }

    /// <summary>A new one is forgotten; an updated one gets its old text back.</summary>
    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        var parts = undo.Data.Split('\n', 2);
        return Task.FromResult(parts.Length == 2 ? memory.Edit(parts[0], parts[1]) : memory.Delete(parts[0]));
    }
}

/// <summary>«Olvida que tengo 44 años», «olvida lo de la vuelta»: forgets what it names (undoing it puts it back).</summary>
public sealed class ForgetTool(MemoryStore memory) : ITool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "forget";

    public string Description => "Forgets something remembered about the user, when they ask you to forget it or say it is no longer true.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"fact":{"type":"string","description":"What to forget, in a few words (their age, the thing about the trip)"}},"required":["fact"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.forget.intent", ToolText.Clean(arguments.GetString("fact")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var found = memory.Find(arguments.GetString("fact") ?? string.Empty);
        if (found.Count == 0)
            return Task.FromResult(ToolResult.Fail("Nothing remembered about that.") with { Reply = Loc.T("tool.forget.none"), Done = Loc.T("tool.forget.noneDone") });
        foreach (var entry in found) memory.Delete(entry.Id);
        var text = ToolText.Clean(string.Join(" · ", found.Select(m => m.Text)));
        return Task.FromResult(new ToolResult("Forgotten: " + string.Join("; ", found.Select(m => m.Text)))
        {
            Done = Loc.T("tool.forget.done", text),
            Reply = Loc.T("tool.forget.reply", text),
            Undo = new ToolUndo(Name, JsonSerializer.Serialize(found, Json)),
        });
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        var entries = JsonSerializer.Deserialize<List<MemoryEntry>>(undo.Data, Json) ?? [];
        var restored = false;
        foreach (var entry in entries) restored |= memory.Restore(entry);
        return Task.FromResult(restored);
    }
}
