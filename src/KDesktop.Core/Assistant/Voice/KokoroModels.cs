using System.Text.Json;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>
/// The files of Kokoro (hexgrad's Kokoro 82M, Apache 2.0; ONNX export of onnx-community at a pinned commit): the natural
/// voice the assistants speak with. One model for every language (<c>Models\kokoro\model.onnx</c>) and half a megabyte per
/// voice (<c>Models\kokoro\voices\</c>). Spanish turns text into phonemes with rules of our own; English, French, Italian
/// and Portuguese need espeak-ng (<see cref="EspeakFiles"/>, downloaded apart). German has no Kokoro voice. K-Desktop
/// downloads them (Options → Assistant → Voice); the assistant process only reads them. The list of files, sizes and hashes
/// is <c>kokoro.json</c>, embedded.
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

    /// <summary>Kokoro speaks this language («es», «en»…).</summary>
    public static bool Supports(string language) => Data.Value.Languages.ContainsKey(language);

    /// <summary>The espeak-ng voice of a language («en-us»…), or null when it does not need espeak-ng (Spanish).</summary>
    public static string? EspeakVoice(string language, string voice) =>
        Data.Value.Languages.TryGetValue(language, out var l) && l.Espeak is { } espeak
            ? language == "en" && voice.StartsWith('b') ? "en-gb" : espeak
            : null;

    /// <summary>The language needs espeak-ng.</summary>
    public static bool NeedsEspeak(string language) => Data.Value.Languages.TryGetValue(language, out var l) && l.Espeak is not null;

    /// <summary>The voices of a language (Kokoro's order: women first).</summary>
    public static IReadOnlyList<string> Voices(string language) =>
        Data.Value.Languages.TryGetValue(language, out var l) ? [.. l.Voices.Keys] : [];

    public static string DefaultVoice(string language) => Voices(language) is [var first, ..] ? first : string.Empty;

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
        if (catalog.Languages.TryGetValue(language, out var l))
            foreach (var voice in Names(spec))
                if (l.Voices.TryGetValue(voice, out var hash))
                    files.Add(new ModelFile($"{catalog.Repository}voices/{voice}.bin", VoiceFile(voice), VoiceSize, hash));
        if (NeedsEspeak(language) && !EspeakFiles.Installed) files.Add(EspeakFiles.Download);
        return files;
    }

    /// <summary>Bytes still to download for that language and voice.</summary>
    public static long Missing(string language, string spec) => Files(language, spec).Where(f => !Complete(f)).Sum(f => f.Size);

    /// <summary>Everything that language and voice need is on disk (espeak-ng unpacked, if it needs it).</summary>
    public static bool Installed(string language, string spec) =>
        Supports(language) && Names(spec).Count > 0 && Files(language, spec).All(Complete) && (!NeedsEspeak(language) || EspeakFiles.Installed);

    /// <summary>The model is on disk (without looking at the voices).</summary>
    public static bool ModelInstalled => File.Exists(ModelFile) && new FileInfo(ModelFile).Length == Data.Value.Model.Size;

    /// <summary>The voices of a language that are downloaded.</summary>
    public static IReadOnlyList<string> InstalledVoices(string language) =>
        [.. Voices(language).Where(v => File.Exists(VoiceFile(v)) && new FileInfo(VoiceFile(v)).Length == VoiceSize)];

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
