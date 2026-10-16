using System.Text;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

/// <summary>A command, or a built-in order (by its closest phrase), and how close in meaning the order is to it.</summary>
public sealed record SemanticHit(Command? Command, MediaOrderKind? Media, string Anchor, double Score)
{
    /// <summary>Two commands with the same name, or two phrases of one order, are the same thing.</summary>
    public string Key => Media is { } kind ? "#" + kind : Command!.Name.Trim().ToLowerInvariant();
}

/// <summary>How close in meaning an order is to each command and built-in order, and to the things K-Desktop does not do.</summary>
/// <param name="Ranked">Most alike first, one per <see cref="SemanticHit.Key"/>.</param>
/// <param name="None">The closest of the «not mine» phrases (<c>voice.none</c>): «apaga la tele» is not "Apagar Equipo".</param>
public sealed record SemanticScores(IReadOnlyList<SemanticHit> Ranked, double None, string NonePhrase);

/// <summary>
/// Second layer of <see cref="IntentMatcher"/>: when the words alone name no command, what the order means. The order
/// and every command name become vectors (<see cref="TextEmbedder"/>) and the closest name wins if it is close enough
/// and clearly ahead — of the other commands and of a list of everyday requests that are not K-Desktop's (lights,
/// the TV, alarms, the weather…, in the language files), which is what keeps «apaga la tele» away from "Apagar Equipo".
/// No synonyms per command: «borra el directorio» finds "Eliminar carpeta" by itself, in any language. Besides its
/// name, a command is also compared with what its actions say they do when that is telling («Apagar el equipo»,
/// «Papelera de reciclaje», «Cerrar AIMP»…; not paths or addresses). The built-in
/// orders compete too, by the phrases of the interface language and English (two words or more: a lone word makes a
/// poor vector): «cancela» always, the media ones when they are on; the volume to a number stays with the words.
/// The names' vectors are kept on disk (<see cref="CacheFile"/>), so only new names cost anything.
/// </summary>
public sealed class SemanticMatcher : IDisposable
{
    /// <summary>
    /// Measured on the user's real commands with orders said in other words (and everyday requests that must not run
    /// anything): accept from here when clearly ahead.
    /// </summary>
    public const double Accept = 0.62;

    /// <summary>
    /// A built-in order needs more: its many short phrases pull everything about sound or music towards them («abre
    /// spotify» → put music on). Right ones measured from 0.81, wrong ones up to 0.79.
    /// </summary>
    public const double AcceptOrder = 0.80;

    /// <summary>
    /// What turns off, restarts or logs off the PC needs to be said clearly: «apaga el ordenador / el pc / el portátil»
    /// measure 0.91–0.98 against «apagar el ordenador», other devices («la consola», «el monitor», «la impresora») up
    /// to 0.875. It still runs at once; it only has to be closer.
    /// </summary>
    public const double AcceptPower = 0.90;

    /// <summary>Lead over the next command (or the closest «not mine» phrase).</summary>
    public const double Margin = 0.02;

    private const string CacheMagic = "KDSEM1";

    private readonly object _gate = new();
    private Dictionary<string, float[]>? _cache;
    private bool _dirty;

    public TextEmbedder Embedder { get; } = new();

    public static bool Available => TextEmbedder.Available;

    /// <summary>The names' vectors (model and prompt in the header: another one starts afresh).</summary>
    public static string CacheFile => Path.Combine(VoiceModels.EmbeddingDir, "anchors.bin");

    public TimeSpan IdleUnload
    {
        get => Embedder.IdleUnload;
        set => Embedder.IdleUnload = value;
    }

    /// <summary>A «not mine» phrase this close to one of the user's commands is left out (their own «Apagar la luz»).</summary>
    private const double OwnedPhrase = 0.88;

    /// <summary>Loads the model and gets the names ready in the background (when the user starts speaking).</summary>
    public void Preload(IReadOnlyCollection<Command> commands, bool media)
    {
        if (!Available) return;
        Task.Run(() =>
        {
            try { Vectors(Texts(commands, media)); }
            catch (Exception ex) { Log.Error("No se pudo preparar el modelo de significado", ex); }
        });
    }

    /// <summary>How close <paramref name="text"/> is to each command (and built-in order); null without the model.</summary>
    /// <param name="media">The media orders are on (Settings › Multimedia); «cancela» always counts.</param>
    public SemanticScores? Score(string text, IReadOnlyCollection<Command> commands, bool media)
    {
        if (!Available || text.Trim().Length == 0) return null;
        var texts = Texts(commands, media);
        var vectors = Vectors(texts);
        var query = Embedder.Embed([text])[0];
        var hits = commands
            .Where(c => Name(c).Length > 0)
            .Select(c => CommandAnchors(c).Select(a => new SemanticHit(c, null, a, TextEmbedder.Similarity(query, vectors[a]))).MaxBy(h => h.Score)!)
            .Concat(texts.Orders.Select(o => new SemanticHit(null, o.Kind, o.Phrase, TextEmbedder.Similarity(query, vectors[o.Phrase]))))
            .OrderByDescending(h => h.Score)
            .DistinctBy(h => h.Key)
            .ToList();
        // «Not mine» phrases that are really one of the user's commands do not count.
        var names = texts.Names.Select(n => vectors[n]).ToList();
        var (best, phrase) = (-1.0, "");
        foreach (var n in texts.None)
        {
            var s = TextEmbedder.Similarity(query, vectors[n]);
            if (s <= best || names.Any(v => TextEmbedder.Similarity(v, vectors[n]) >= OwnedPhrase)) continue;
            (best, phrase) = (s, n);
        }
        return new SemanticScores(hits, best, phrase);
    }

    private static string Name(Command c) => c.Name.Trim();

    /// <summary>The command turns off, restarts, logs off or suspends the PC (<see cref="AcceptPower"/>).</summary>
    public static bool IsPower(Command c) => c.Actions.Any(a => a is PowerAction { Enabled: true, Mode: not PowerMode.Lock });

    /// <summary>The name, and what the first actions do when their description says it (not a path or a URL).</summary>
    private static IEnumerable<string> CommandAnchors(Command c) =>
        c.Actions.Where(a => a.Enabled).Take(3).Select(Describe).OfType<string>().Prepend(Name(c)).Distinct();

    private static string? Describe(CommandAction action) => action switch
    {
        // «Apagar el equipo» would fit any device («equipo» is any appliance): the voice says what is turned off.
        PowerAction p => Loc.T($"voice.power.{p.Mode}"),
        OpenFolderAction { Special: not null } f => f.Summary,
        VolumeAction or TopMostAction or HiddenFilesAction or AudioOutputAction or AppControlAction => action.Summary,
        _ => null,
    } is { } text && text.Trim().Length > 0 ? text.Trim() : null;

    private sealed record Anchors(List<string> Names, List<(MediaOrderKind Kind, string Phrase)> Orders, List<string> None)
    {
        public IEnumerable<string> All => Names.Concat(Orders.Select(o => o.Phrase)).Concat(None);
    }

    /// <summary>
    /// The command names, the built-in orders' phrases (interface language and English) and the «not mine» phrases of
    /// every installed language.
    /// </summary>
    private static Anchors Texts(IReadOnlyCollection<Command> commands, bool media)
    {
        var names = commands.Where(c => Name(c).Length > 0).SelectMany(CommandAnchors).Distinct().ToList();
        var orders = new[] { Loc.Instance.Language, Loc.Fallback }.Distinct()
            .SelectMany(language => MediaOrders.Sentences(language))
            .Where(p => p.Key == MediaOrderKind.Cancel || media && p.Key != MediaOrderKind.VolumeSet)
            .SelectMany(p => p.Value.Where(phrase => TextSimilarity.Words(phrase).Length >= 2).Select(phrase => (p.Key, phrase)))
            .Distinct()
            .ToList();
        var none = Loc.Available()
            .SelectMany(l => Loc.Strings(l.Code, "voice.none").Where(p => p.Key == "voice.none").Select(p => p.Value))
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(p => p.Length > 0)
            .Distinct()
            .ToList();
        return new Anchors(names, orders, none);
    }

    /// <summary>The vector of each text, from the cache or computed now (and saved).</summary>
    private Dictionary<string, float[]> Vectors(Anchors texts)
    {
        lock (_gate)
        {
            _cache ??= Load();
            var missing = texts.All.Where(t => !_cache.ContainsKey(t)).Distinct().ToList();
            if (missing.Count > 0)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var vectors = Embedder.Embed(missing);
                for (var i = 0; i < missing.Count; i++) _cache[missing[i]] = vectors[i];
                _dirty = true;
                Log.Info($"Significado: {missing.Count} nombre(s) o frase(s) nuevas preparadas en {watch.ElapsedMilliseconds} ms");
            }
            if (_dirty) Save(texts.All.ToHashSet());
            return _cache;
        }
    }

    private static string Header => $"{CacheMagic}|{ModelDownloader.Files.First(f => f.Path == VoiceModels.EmbeddingWeights).Sha256}|{TextEmbedder.Prompt}";

    private static Dictionary<string, float[]> Load()
    {
        var cache = new Dictionary<string, float[]>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(CacheFile)) return cache;
            using var reader = new BinaryReader(File.OpenRead(CacheFile), Encoding.UTF8);
            if (reader.ReadString() != Header) return cache;
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var text = reader.ReadString();
                var vector = new float[TextEmbedder.Size];
                for (var j = 0; j < vector.Length; j++) vector[j] = reader.ReadSingle();
                cache[text] = vector;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer la caché del modelo de significado: {ex.Message}");
            cache.Clear();
        }
        return cache;
    }

    /// <summary>Writes the cache keeping only what is in use (renamed or deleted commands leave it).</summary>
    private void Save(HashSet<string> keep)
    {
        try
        {
            foreach (var old in _cache!.Keys.Where(k => !keep.Contains(k)).ToList()) _cache.Remove(old);
            var temp = CacheFile + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temp), Encoding.UTF8))
            {
                writer.Write(Header);
                writer.Write(_cache.Count);
                foreach (var (text, vector) in _cache)
                {
                    writer.Write(text);
                    foreach (var x in vector) writer.Write(x);
                }
            }
            File.Move(temp, CacheFile, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo guardar la caché del modelo de significado: {ex.Message}");
        }
    }

    public void Dispose() => Embedder.Dispose();
}
