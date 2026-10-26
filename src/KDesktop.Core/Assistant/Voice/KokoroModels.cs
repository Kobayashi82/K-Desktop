using System.Text.Json;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>
/// The files of Kokoro (hexgrad's Kokoro 82M, Apache 2.0; ONNX export of onnx-community at a pinned commit): the natural
/// voice the assistants speak with. One model for every language (<c>Models\kokoro\model.onnx</c>) and half a megabyte per
/// voice (<c>Models\kokoro\voices\</c>). Spanish turns text into phonemes with rules of our own; English, French, Italian
/// and Portuguese need espeak-ng (<see cref="EspeakFiles"/>, downloaded apart). German has no Kokoro voice. K-Desktop
/// downloads them (Options → Assistant → Voice); the assistant process only reads them. The list of files, sizes and hashes
/// is <c>kokoro.json</c>, embedded. Since 5.0.282 the user's own folders count too (<see cref="UseFolders"/>): Kokoro's
/// <c>.pt</c> (PyTorch's zip, the pack inside) or <c>.bin</c>, named as Kokoro names them (<c>af_ellen</c>: its language
/// by the first letter); one of the catalog that is there needs no download, and none is listed twice.
/// </summary>
public static class KokoroModels
{
    /// <summary>Bytes of a voice (510 × 256 floats).</summary>
    public const long VoiceSize = 522_240;

    private sealed record Entry(string Url, long Size, string Sha256);

    private sealed record Language(string? Espeak, IReadOnlyDictionary<string, string> Voices);

    private sealed record Catalog(string Repository, Entry Model, IReadOnlyDictionary<string, Language> Languages);

    private static readonly Lazy<Catalog> Data = new(Read);

    public static string Root => Path.Combine(AppPaths.ModelsDir, "kokoro");

    public static string ModelFile => Path.Combine(Root, "model.onnx");

    public static string VoiceFile(string voice) => Path.Combine(Root, "voices", voice + ".bin");

    /// <summary>The file a voice is read from: the downloaded one, else the user's, else where it would be downloaded.</summary>
    public static string PackFile(string voice) =>
        IsComplete(VoiceFile(voice)) ? VoiceFile(voice) : Found.TryGetValue(voice, out var own) ? own : VoiceFile(voice);

    // ── the user's folders ───────────────────────────────────────────────

    private static readonly object FoldersGate = new();
    private static IReadOnlyList<string> _folders = [];
    private static Lazy<IReadOnlyDictionary<string, string>> _local = new(() => new Dictionary<string, string>());

    /// <summary>Kokoro's languages by the first letter of a voice's name (a/b: American and British English…).</summary>
    private static readonly Dictionary<char, string> LanguageOfLetter = new() { ['a'] = "en", ['b'] = "en", ['e'] = "es", ['f'] = "fr", ['i'] = "it", ['p'] = "pt" };

    /// <summary>The folders with the user's voices (Options → Voz → Voces → Kokoro): looked at again when they change.</summary>
    public static void UseFolders(IEnumerable<string> folders)
    {
        var list = folders.Where(f => f.Trim().Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        lock (FoldersGate)
        {
            if (list.SequenceEqual(_folders, StringComparer.OrdinalIgnoreCase)) return;
            _folders = list;
            _local = new(() => Scan(list));
        }
    }

    /// <summary>Looks in the user's folders again (a voice added there).</summary>
    public static void Rescan()
    {
        lock (FoldersGate)
        {
            var list = _folders;
            _local = new(() => Scan(list));
        }
    }

    /// <summary>The user's voices: name → file (the first of each name wins).</summary>
    private static IReadOnlyDictionary<string, string> Found
    {
        get
        {
            Lazy<IReadOnlyDictionary<string, string>> local;
            lock (FoldersGate) local = _local;
            return local.Value;
        }
    }

    /// <summary>How many of the user's voices a folder has.</summary>
    public static int CountIn(string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Found.Values.Count(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The voice is read from one of the user's folders (it is not downloaded).</summary>
    public static bool IsInFolders(string voice) => !IsComplete(VoiceFile(voice)) && Found.ContainsKey(voice);

    /// <summary>The language of a voice by its name («ef_dora» → «es»), or null when it is not Kokoro's way or not one it speaks.</summary>
    public static string? LanguageOf(string voice) =>
        voice.Length > 3 && voice[2] == '_' && voice[1] is 'f' or 'm' && LanguageOfLetter.TryGetValue(voice[0], out var language) ? language : null;

    /// <summary>Every <c>.pt</c> or <c>.bin</c> with a voice pack inside, in the folders and the ones in them.</summary>
    private static Dictionary<string, string> Scan(IReadOnlyList<string> folders)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*.*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".pt", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bin", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                if (LanguageOf(name) is null || found.ContainsKey(name) || !IsPack(file)) continue;
                found[name] = file;
            }
        }
        if (found.Count > 0) Log.Info($"Kokoro: {found.Count} voz/voces en las carpetas del usuario");
        return found;
    }

    /// <summary>A voice pack: a .bin of its size, or a .pt with one inside.</summary>
    private static bool IsPack(string file)
    {
        try { return ReadPack(file).Length == VoiceSize; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn($"Kokoro: {file} no es una voz: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The bytes of a voice pack (510 × 256 floats): a .bin as it is; a .pt is PyTorch's zip, whose <c>data/0</c> is the
    /// same floats (little-endian).
    /// </summary>
    public static byte[] ReadPack(string file)
    {
        if (!Path.GetExtension(file).Equals(".pt", StringComparison.OrdinalIgnoreCase)) return File.ReadAllBytes(file);
        using var zip = System.IO.Compression.ZipFile.OpenRead(file);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/data/0", StringComparison.Ordinal) && e.Length == VoiceSize)
                    ?? throw new InvalidDataException("sin la voz dentro");
        using var stream = entry.Open();
        var bytes = new byte[VoiceSize];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Kokoro speaks this language («es», «en»…).</summary>
    public static bool Supports(string language) => Data.Value.Languages.ContainsKey(language);

    /// <summary>The espeak-ng voice of a language («en-us»…), or null when it does not need espeak-ng (Spanish).</summary>
    public static string? EspeakVoice(string language, string voice) =>
        Data.Value.Languages.TryGetValue(language, out var l) && l.Espeak is { } espeak
            ? language == "en" && voice.StartsWith('b') ? "en-gb" : espeak
            : null;

    /// <summary>The language needs espeak-ng.</summary>
    public static bool NeedsEspeak(string language) => Data.Value.Languages.TryGetValue(language, out var l) && l.Espeak is not null;

    /// <summary>
    /// The voices of a language: Kokoro's (its order: women first) and then the user's that are not those (women first, by
    /// name). Each one once.
    /// </summary>
    public static IReadOnlyList<string> Voices(string language)
    {
        if (!Data.Value.Languages.TryGetValue(language, out var l)) return [];
        var own = Found.Keys.Where(v => LanguageOf(v) == language && !l.Voices.ContainsKey(v))
            .OrderBy(v => IsFemale(v) ? 0 : 1).ThenBy(v => v, StringComparer.Ordinal);
        return [.. l.Voices.Keys, .. own];
    }

    /// <summary>Kokoro's first voice of the language (never one of the user's: they come and go).</summary>
    public static string DefaultVoice(string language) =>
        Data.Value.Languages.TryGetValue(language, out var l) && l.Voices.Keys.FirstOrDefault() is { } first ? first : string.Empty;

    /// <summary>A voice or mix («ef_dora», «ef_dora:0.7,em_alex:0.3») whose voices are all of the language, or its default one.</summary>
    public static string VoiceOrDefault(string language, string? spec)
    {
        var names = Names(spec);
        var known = Voices(language);
        return names.Count > 0 && names.All(known.Contains) ? spec!.Trim() : DefaultVoice(language);
    }

    /// <summary>The voices named in a spec, without their weights.</summary>
    public static IReadOnlyList<string> Names(string? spec) => [.. Parts(spec).Select(p => p.Name).Distinct(StringComparer.Ordinal)];

    /// <summary>The voices of a spec («ef_dora:0.7,em_alex:0.3») and their weights (no weight = 1; nothing or 0 is left out).</summary>
    public static IReadOnlyList<(string Name, double Weight)> Parts(string? spec)
    {
        var parts = new List<(string, double)>();
        foreach (var part in (spec ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':', StringComparison.Ordinal);
            var name = (colon < 0 ? part : part[..colon]).Trim();
            var weight = colon < 0 ? 1
                : double.TryParse(part[(colon + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : 1;
            if (name.Length > 0 && weight > 0) parts.Add((name, weight));
        }
        return parts;
    }

    /// <summary>The spec of a voice mixed with another one: <paramref name="share"/> (0–1) of the second.</summary>
    public static string Mix(string first, string? second, double share) =>
        second is null || second == first || share <= 0
            ? first
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{first}:{1 - share:0.##},{second}:{share:0.##}");

    /// <summary>«ef_dora» → «Dora», «bm_george» → «George».</summary>
    public static string DisplayName(string voice)
    {
        var name = voice.Length > 3 && voice[2] == '_' ? voice[3..] : voice;
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }

    /// <summary>The voice is a woman's (Kokoro's names: second letter f or m).</summary>
    public static bool IsFemale(string voice) => voice.Length > 1 && voice[1] == 'f';

    /// <summary>For English: the voice speaks British English.</summary>
    public static bool IsBritish(string voice) => voice.StartsWith('b');

    /// <summary>What to download for a language and a voice or mix: the model, its voices and, if the language needs it, espeak-ng.</summary>
    public static IReadOnlyList<ModelFile> Files(string language, string spec)
    {
        var catalog = Data.Value;
        var files = new List<ModelFile> { new(catalog.Repository + catalog.Model.Url, ModelFile, catalog.Model.Size, catalog.Model.Sha256) };
        // The user's voices (also one of the catalog that is in their folders) are not downloaded.
        if (catalog.Languages.TryGetValue(language, out var l))
            foreach (var voice in Names(spec))
                if (l.Voices.TryGetValue(voice, out var hash) && !IsInFolders(voice))
                    files.Add(new ModelFile($"{catalog.Repository}voices/{voice}.bin", VoiceFile(voice), VoiceSize, hash));
        if (NeedsEspeak(language) && !EspeakFiles.Installed) files.Add(EspeakFiles.Download);
        return files;
    }

    /// <summary>Bytes still to download for that language and voice.</summary>
    public static long Missing(string language, string spec) => Files(language, spec).Where(f => !Complete(f)).Sum(f => f.Size);

    /// <summary>Everything that language and voice need is on disk (espeak-ng unpacked, if it needs it).</summary>
    public static bool Installed(string language, string spec) =>
        Supports(language) && Names(spec).Count > 0 && Names(spec).All(v => IsComplete(VoiceFile(v)) || IsInFolders(v))
        && Files(language, spec).All(Complete) && (!NeedsEspeak(language) || EspeakFiles.Installed);

    /// <summary>The model is on disk (without looking at the voices).</summary>
    public static bool ModelInstalled => File.Exists(ModelFile) && new FileInfo(ModelFile).Length == Data.Value.Model.Size;

    /// <summary>The voices of a language that are downloaded.</summary>
    public static IReadOnlyList<string> InstalledVoices(string language) =>
        [.. Voices(language).Where(v => IsComplete(VoiceFile(v)) || Found.ContainsKey(v))];

    /// <summary>A voice on disk needs espeak-ng (one of a language other than Spanish).</summary>
    public static bool NeedsEspeakOnDisk() =>
        Data.Value.Languages.Where(l => l.Value.Espeak is not null).Any(l => InstalledVoices(l.Key).Count > 0);

    /// <summary>Size on disk of Kokoro (espeak-ng apart: it is shared with Piper).</summary>
    public static long InstalledSize() =>
        Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    /// <summary>Removes Kokoro, and espeak-ng if Piper does not need it (the assistant process must have let them go first).</summary>
    public static void Delete()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se ha podido borrar {Root}: {ex.Message}");
        }
        EspeakFiles.DeleteIfUnused();
    }

    /// <summary>The files of Pocket TTS, the voice until 5.0.93, are no longer used: they go (some 125–350 MB).</summary>
    public static void DeleteOldPocket()
    {
        var old = Path.Combine(AppPaths.ModelsDir, "pocket-tts");
        try
        {
            if (!Directory.Exists(old)) return;
            Directory.Delete(old, recursive: true);
            Log.Info("Borrados los modelos de Pocket TTS (los asistentes hablan ahora con Kokoro)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se han podido borrar los modelos de Pocket TTS: {ex.Message}");
        }
    }

    private static bool Complete(ModelFile file) => File.Exists(file.Path) && new FileInfo(file.Path).Length == file.Size;

    /// <summary>A downloaded voice of its size.</summary>
    private static bool IsComplete(string path) => File.Exists(path) && new FileInfo(path).Length == VoiceSize;

    private static Catalog Read()
    {
        using var stream = typeof(KokoroModels).Assembly.GetManifestResourceStream("AssistantVoice/kokoro.json")
                           ?? throw new InvalidOperationException("Falta kokoro.json");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        static Entry EntryOf(JsonElement e, string url) => new(url, e.GetProperty("size").GetInt64(), e.GetProperty("sha256").GetString() ?? "");
        var model = root.GetProperty("model");
        var languages = new Dictionary<string, Language>(StringComparer.Ordinal);
        foreach (var language in root.GetProperty("languages").EnumerateObject())
        {
            var voices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var voice in language.Value.GetProperty("voices").EnumerateObject()) voices[voice.Name] = voice.Value.GetString() ?? "";
            var code = language.Value.GetProperty("espeak");
            languages[language.Name] = new Language(code.ValueKind == JsonValueKind.String ? code.GetString() : null, voices);
        }
        return new Catalog(root.GetProperty("repository").GetString()!, EntryOf(model, model.GetProperty("name").GetString()!), languages);
    }
}
