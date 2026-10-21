using System.IO.Compression;
using System.Text.Json;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>
/// The files of Kokoro (hexgrad's Kokoro 82M, Apache 2.0; ONNX export of onnx-community at a pinned commit): the natural
/// voice the assistants speak with. One model for every language (<c>Models\kokoro\model.onnx</c>) and half a megabyte per
/// voice (<c>Models\kokoro\voices\</c>). Spanish turns text into phonemes with rules of our own; English, French, Italian
/// and Portuguese need espeak-ng, which is GPL and so is not part of K-Desktop: it is downloaded apart like a model (the
/// Windows package of espeakng-loader) and unpacked into <c>Models\espeak-ng\</c>. German has no Kokoro voice. K-Desktop
/// downloads them (Options → Assistant → Voice); the assistant process only reads them. The list of files, sizes and hashes
/// is <c>kokoro.json</c>, embedded.
/// </summary>
public static class KokoroModels
{
    /// <summary>Bytes of a voice (510 × 256 floats).</summary>
    public const long VoiceSize = 522_240;

    private sealed record Entry(string Url, long Size, string Sha256);

    private sealed record Language(string? Espeak, IReadOnlyDictionary<string, string> Voices);

    private sealed record Catalog(string Repository, Entry Model, Entry Espeak, IReadOnlyDictionary<string, Language> Languages);

    private static readonly Lazy<Catalog> Data = new(Read);

    public static string Root => Path.Combine(AppPaths.ModelsDir, "kokoro");

    public static string ModelFile => Path.Combine(Root, "model.onnx");

    public static string VoiceFile(string voice) => Path.Combine(Root, "voices", voice + ".bin");

    /// <summary>Where espeak-ng is unpacked (its DLL and <c>espeak-ng-data</c>).</summary>
    public static string EspeakFolder => Path.Combine(AppPaths.ModelsDir, "espeak-ng");

    private static string EspeakPackage => Path.Combine(EspeakFolder, "espeakng_loader.whl");

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
        if (NeedsEspeak(language) && !EspeakInstalled)
            files.Add(new ModelFile(catalog.Espeak.Url, EspeakPackage, catalog.Espeak.Size, catalog.Espeak.Sha256));
        return files;
    }

    /// <summary>Bytes still to download for that language and voice.</summary>
    public static long Missing(string language, string spec) => Files(language, spec).Where(f => !Complete(f)).Sum(f => f.Size);

    /// <summary>Everything that language and voice need is on disk (espeak-ng unpacked, if it needs it).</summary>
    public static bool Installed(string language, string spec) =>
        Supports(language) && Names(spec).Count > 0 && Files(language, spec).All(Complete) && (!NeedsEspeak(language) || EspeakInstalled);

    /// <summary>The model is on disk (without looking at the voices).</summary>
    public static bool ModelInstalled => File.Exists(ModelFile) && new FileInfo(ModelFile).Length == Data.Value.Model.Size;

    /// <summary>The voices of a language that are downloaded.</summary>
    public static IReadOnlyList<string> InstalledVoices(string language) =>
        [.. Voices(language).Where(v => File.Exists(VoiceFile(v)) && new FileInfo(VoiceFile(v)).Length == VoiceSize)];

    /// <summary>espeak-ng is unpacked.</summary>
    public static bool EspeakInstalled =>
        File.Exists(Path.Combine(EspeakFolder, "espeak-ng.dll")) && File.Exists(Path.Combine(EspeakFolder, "espeak-ng-data", "phontab"));

    /// <summary>
    /// After a download: unpacks the package of espeak-ng (its DLL and its data, nothing else) and deletes it. False when
    /// there was nothing to unpack or it failed (the next download gets it again).
    /// </summary>
    public static bool UnpackEspeak()
    {
        if (!File.Exists(EspeakPackage)) return false;
        const string prefix = "espeakng_loader/";
        try
        {
            using (var zip = ZipFile.OpenRead(EspeakPackage))
            {
                var root = Path.GetFullPath(EspeakFolder) + Path.DirectorySeparatorChar;
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.Name.Length == 0) continue;
                    var relative = entry.FullName[prefix.Length..];
                    if (relative != "espeak-ng.dll" && !relative.StartsWith("espeak-ng-data/", StringComparison.Ordinal)) continue;
                    var target = Path.GetFullPath(Path.Combine(EspeakFolder, relative));
                    // Never outside its folder.
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            File.Delete(EspeakPackage);
            return EspeakInstalled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warn($"No se ha podido descomprimir espeak-ng: {ex.Message}");
            return false;
        }
    }

    /// <summary>Size on disk of Kokoro and espeak-ng.</summary>
    public static long InstalledSize() => Size(Root) + Size(EspeakFolder);

    private static long Size(string folder) =>
        Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    /// <summary>Removes Kokoro and espeak-ng (the assistant process must have let them go first).</summary>
    public static void Delete()
    {
        foreach (var folder in new[] { Root, EspeakFolder })
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"No se ha podido borrar {folder}: {ex.Message}");
            }
        }
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
        var espeak = root.GetProperty("espeak");
        var languages = new Dictionary<string, Language>(StringComparer.Ordinal);
        foreach (var language in root.GetProperty("languages").EnumerateObject())
        {
            var voices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var voice in language.Value.GetProperty("voices").EnumerateObject()) voices[voice.Name] = voice.Value.GetString() ?? "";
            var code = language.Value.GetProperty("espeak");
            languages[language.Name] = new Language(code.ValueKind == JsonValueKind.String ? code.GetString() : null, voices);
        }
        return new Catalog(root.GetProperty("repository").GetString()!, EntryOf(model, model.GetProperty("name").GetString()!),
            EntryOf(espeak, espeak.GetProperty("url").GetString()!), languages);
    }
}
