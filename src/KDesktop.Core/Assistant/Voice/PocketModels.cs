using System.Text.Json;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>
/// The models of Pocket TTS (Kyutai, CC BY 4.0, ONNX export of OpenVoiceOS/phoonnx at a pinned commit): the natural voice
/// the assistants speak with (stage 5.4), one model per language and a few MB per voice. K-Desktop downloads them into
/// <c>Models\pocket-tts\&lt;language&gt;\</c> (Options → Assistant → Voice); the assistant process only reads them. The
/// list of files, sizes and hashes is <c>pocket-tts.json</c>, embedded.
/// </summary>
public static class PocketModels
{
    /// <summary>The voice when none is chosen (the one Kobii's pets speak with).</summary>
    public const string DefaultVoice = "eve";

    private sealed record Entry(string Name, long Size, string Sha256);

    private sealed record Language(string Folder, IReadOnlyList<Entry> Files, IReadOnlyDictionary<string, Entry> Voices);

    private static readonly Lazy<(string Repository, Dictionary<string, Language> Languages)> Catalog = new(Read);

    public static string Root => Path.Combine(AppPaths.ModelsDir, "pocket-tts");

    /// <summary>There is a model for this language («es», «en»…).</summary>
    public static bool Supports(string language) => Catalog.Value.Languages.ContainsKey(language);

    /// <summary>The voices of a language, by name (alphabetical).</summary>
    public static IReadOnlyList<string> Voices(string language) =>
        Catalog.Value.Languages.TryGetValue(language, out var l) ? [.. l.Voices.Keys.Order(StringComparer.Ordinal)] : [];

    public static string Folder(string language) => Path.Combine(Root, Catalog.Value.Languages[language].Folder);

    public static string VoiceFile(string language, string voice) => Path.Combine(Folder(language), "voices", voice + ".safetensors");

    /// <summary>A known voice of that language, or the default one.</summary>
    public static string VoiceOrDefault(string language, string? voice) =>
        voice is { Length: > 0 } && Catalog.Value.Languages.TryGetValue(language, out var l) && l.Voices.ContainsKey(voice) ? voice : DefaultVoice;

    /// <summary>What to download for a language and a voice (the model only once, each voice apart).</summary>
    public static IReadOnlyList<ModelFile> Files(string language, string voice)
    {
        var (repository, languages) = Catalog.Value;
        var l = languages[language];
        var files = l.Files.Select(f => new ModelFile($"{repository}{l.Folder}/{f.Name}", Path.Combine(Folder(language), f.Name), f.Size, f.Sha256)).ToList();
        if (l.Voices.TryGetValue(voice, out var v))
            files.Add(new ModelFile($"{repository}{l.Folder}/voices/{voice}.safetensors", VoiceFile(language, voice), v.Size, v.Sha256));
        return files;
    }

    /// <summary>Bytes still to download for that language and voice.</summary>
    public static long Missing(string language, string voice) =>
        Files(language, voice).Where(f => !Complete(f)).Sum(f => f.Size);

    /// <summary>The model of that language and that voice are on disk, whole.</summary>
    public static bool Installed(string language, string voice) =>
        Supports(language) && Files(language, voice).All(Complete);

    /// <summary>The model of that language is on disk (without looking at the voices).</summary>
    public static bool ModelInstalled(string language) =>
        Supports(language) && Files(language, "").All(Complete);

    /// <summary>The voices downloaded for a language.</summary>
    public static IReadOnlyList<string> InstalledVoices(string language) =>
        Supports(language) ? [.. Voices(language).Where(v => File.Exists(VoiceFile(language, v)))] : [];

    /// <summary>Size on disk of every Pocket TTS file.</summary>
    public static long InstalledSize() =>
        Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    /// <summary>Removes every Pocket TTS file (the assistant process must not be using them: it loads them on use and lets them go).</summary>
    public static void Delete()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se ha podido borrar la voz de los asistentes: {ex.Message}");
        }
    }

    private static bool Complete(ModelFile file) => File.Exists(file.Path) && new FileInfo(file.Path).Length == file.Size;

    private static (string, Dictionary<string, Language>) Read()
    {
        using var stream = typeof(PocketModels).Assembly.GetManifestResourceStream("AssistantVoice/pocket-tts.json")
                           ?? throw new InvalidOperationException("Falta pocket-tts.json");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        static Entry EntryOf(string name, JsonElement e) => new(name, e.GetProperty("size").GetInt64(), e.GetProperty("sha256").GetString() ?? "");
        var languages = new Dictionary<string, Language>(StringComparer.Ordinal);
        foreach (var language in root.GetProperty("languages").EnumerateObject())
        {
            var files = language.Value.GetProperty("files").EnumerateArray().Select(f => EntryOf(f.GetProperty("name").GetString()!, f)).ToList();
            var voices = language.Value.GetProperty("voices").EnumerateObject().ToDictionary(v => v.Name, v => EntryOf(v.Name, v.Value), StringComparer.Ordinal);
            languages[language.Name] = new Language(language.Value.GetProperty("folder").GetString()!, files, voices);
        }
        return (root.GetProperty("repository").GetString()!, languages);
    }
}
