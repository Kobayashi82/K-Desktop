using System.Text.Json;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>A Piper voice of the catalog.</summary>
/// <param name="Key">Its name in rhasspy/piper-voices («es_ES-davefx-medium»).</param>
/// <param name="Language">Its language («es»).</param>
/// <param name="Region">Its country («ES», «MX»…).</param>
/// <param name="Quality"><c>x_low</c>, <c>low</c>, <c>medium</c> or <c>high</c>.</param>
/// <param name="SampleRate">Hz of its audio.</param>
/// <param name="Espeak">The espeak-ng voice it was trained with («es», «en-us»…).</param>
/// <param name="Size">Bytes of its model.</param>
/// <param name="Speakers">The speakers of a voice with several (empty: one).</param>
public sealed record PiperVoice(string Key, string Language, string Region, string Quality, int SampleRate, string Espeak, long Size,
    IReadOnlyList<string> Speakers)
{
    /// <summary>«es_ES-davefx-medium» → «Davefx», «en_GB-jenny_dioco-medium» → «Jenny Dioco».</summary>
    public string Name
    {
        get
        {
            var parts = Key.Split('-');
            var name = parts.Length >= 3 ? string.Join('-', parts[1..^1]) : Key;
            return string.Join(' ', name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        }
    }
}

/// <summary>
/// The voices of Piper (rhasspy's VITS voices, the code MIT; each voice with the license of its recordings, in its
/// <c>MODEL_CARD</c>): a second natural voice, faster than Kokoro and with voices in every language of K-Desktop (German
/// too). Each voice is a model of its own (20–115 MB) with its <c>.onnx.json</c>, in <c>Models\piper\</c>; all of them
/// turn text into phonemes with espeak-ng (<see cref="EspeakFiles"/>). K-Desktop downloads them (Options → Assistant →
/// Voice); the assistant process only reads them. The catalog (pinned commit, sizes and hashes) is <c>piper.json</c>,
/// embedded. A voice is chosen by its key, and one with several speakers as «key#speaker».
/// </summary>
public static class PiperModels
{
    private sealed record Entry(PiperVoice Voice, string Path, string Sha256, long ConfigSize, string ConfigSha256);

    private sealed record Catalog(string Repository, IReadOnlyDictionary<string, string> Defaults, IReadOnlyDictionary<string, Entry> Voices);

    private static readonly Lazy<Catalog> Data = new(Read);

    public static string Root => Path.Combine(AppPaths.ModelsDir, "piper");

    public static string ModelFile(string key) => Path.Combine(Root, key + ".onnx");

    public static string ConfigFile(string key) => ModelFile(key) + ".json";

    /// <summary>Piper has voices for this language («es», «de»…).</summary>
    public static bool Supports(string language) => Data.Value.Defaults.ContainsKey(language);

    /// <summary>
    /// The voices of a language: those of the country of its default voice first (Spain, the US, Brazil…), then by country,
    /// name and quality (from the smallest).
    /// </summary>
    public static IReadOnlyList<PiperVoice> Voices(string language)
    {
        var home = Find(DefaultVoice(language))?.Region;
        return
        [
            .. Data.Value.Voices.Values.Select(e => e.Voice).Where(v => v.Language == language)
                .OrderBy(v => v.Region == home ? 0 : 1).ThenBy(v => v.Region, StringComparer.Ordinal).ThenBy(v => v.Name, StringComparer.Ordinal)
                .ThenBy(v => Array.IndexOf(Qualities, v.Quality)),
        ];
    }

    /// <summary>The qualities of the voices, from the smallest.</summary>
    private static readonly string[] Qualities = ["x_low", "low", "medium", "high"];

    /// <summary>A voice of the catalog by its key (the speaker of a spec is ignored), or null.</summary>
    public static PiperVoice? Find(string? spec) => Data.Value.Voices.GetValueOrDefault(KeyOf(spec))?.Voice;

    public static string DefaultVoice(string language) => Data.Value.Defaults.GetValueOrDefault(language) ?? string.Empty;

    /// <summary>«es_ES-sharvard-medium#F» → «es_ES-sharvard-medium».</summary>
    public static string KeyOf(string? spec)
    {
        var text = (spec ?? string.Empty).Trim();
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? text : text[..hash];
    }

    /// <summary>The speaker of a spec («F»), or null (the voice's first).</summary>
    public static string? SpeakerOf(string? spec)
    {
        var text = (spec ?? string.Empty).Trim();
        var hash = text.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 || hash == text.Length - 1 ? null : text[(hash + 1)..];
    }

    /// <summary>The spec of a voice and, if it has several, one of its speakers.</summary>
    public static string Spec(string key, string? speaker) => speaker is { Length: > 0 } ? key + "#" + speaker : key;

    /// <summary>A voice (and speaker) of the language, or its default one: a spec of another language or unknown is not used.</summary>
    public static string VoiceOrDefault(string language, string? spec)
    {
        if (Find(spec) is not { } voice || voice.Language != language) return DefaultVoice(language);
        var speaker = SpeakerOf(spec);
        return Spec(voice.Key, speaker is not null && voice.Speakers.Contains(speaker) ? speaker : null);
    }

    /// <summary>What to download for a voice: its model, its settings and, if it is not there yet, espeak-ng.</summary>
    public static IReadOnlyList<ModelFile> Files(string spec)
    {
        var catalog = Data.Value;
        if (!catalog.Voices.TryGetValue(KeyOf(spec), out var entry)) return [];
        var key = entry.Voice.Key;
        // The key may carry letters that are not plain in a URL («pt_PT-tugão-medium»).
        var url = catalog.Repository + string.Join('/', (entry.Path + key).Split('/').Select(Uri.EscapeDataString));
        List<ModelFile> files =
        [
            new(url + ".onnx", ModelFile(key), entry.Voice.Size, entry.Sha256),
            new(url + ".onnx.json", ConfigFile(key), entry.ConfigSize, entry.ConfigSha256),
        ];
        if (!EspeakFiles.Installed) files.Add(EspeakFiles.Download);
        return files;
    }

    /// <summary>Bytes still to download for a voice.</summary>
    public static long Missing(string spec) => Files(spec).Where(f => !Complete(f)).Sum(f => f.Size);

    /// <summary>The voice (of that language) and espeak-ng are on disk.</summary>
    public static bool Installed(string language, string spec) =>
        Find(spec) is { } voice && voice.Language == language && IsDownloaded(voice.Key) && EspeakFiles.Installed;

    /// <summary>The model and settings of a voice are on disk (without looking at espeak-ng).</summary>
    public static bool IsDownloaded(string key) =>
        Data.Value.Voices.TryGetValue(key, out var entry) && Complete(ModelFile(key), entry.Voice.Size) && Complete(ConfigFile(key), entry.ConfigSize);

    /// <summary>The voices of a language that are downloaded.</summary>
    public static IReadOnlyList<string> InstalledVoices(string language) => [.. Voices(language).Where(v => IsDownloaded(v.Key)).Select(v => v.Key)];

    /// <summary>The voices of every language that are downloaded.</summary>
    public static IReadOnlyList<string> AllInstalled => [.. Data.Value.Voices.Keys.Where(IsDownloaded)];

    /// <summary>Some voice of any language is on disk (Piper is in use: choosing another one downloads it at once).</summary>
    public static bool AnyInstalled => Data.Value.Voices.Keys.Any(IsDownloaded);

    /// <summary>Size on disk of the Piper voices (espeak-ng apart: it is shared with Kokoro).</summary>
    public static long InstalledSize() =>
        Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    /// <summary>
    /// Removes the Piper voices but <paramref name="keep"/> (none: all of them, and espeak-ng if Kokoro does not need it).
    /// The assistant process must have let them go first.
    /// </summary>
    public static void Delete(IReadOnlyCollection<string>? keep = null)
    {
        try
        {
            if (keep is not { Count: > 0 })
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            else if (Directory.Exists(Root))
            {
                foreach (var file in Directory.EnumerateFiles(Root))
                {
                    var name = Path.GetFileName(file);
                    var key = name.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase) ? name[..^".onnx.json".Length]
                        : name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ? name[..^".onnx".Length] : name;
                    if (!keep.Contains(key)) File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se han podido borrar las voces de Piper: {ex.Message}");
        }
        EspeakFiles.DeleteIfUnused();
    }

    private static bool Complete(ModelFile file) => Complete(file.Path, file.Size);

    private static bool Complete(string path, long size) => File.Exists(path) && new FileInfo(path).Length == size;

    private static Catalog Read()
    {
        using var stream = typeof(PiperModels).Assembly.GetManifestResourceStream("AssistantVoice/piper.json")
                           ?? throw new InvalidOperationException("Falta piper.json");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var voices = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var language in root.GetProperty("languages").EnumerateObject())
        {
            defaults[language.Name] = language.Value.GetProperty("default").GetString()!;
            foreach (var voice in language.Value.GetProperty("voices").EnumerateObject())
            {
                var v = voice.Value;
                var speakers = v.TryGetProperty("speakers", out var list) ? list.EnumerateArray().Select(s => s.GetString()!).ToArray() : [];
                voices[voice.Name] = new Entry(
                    new PiperVoice(voice.Name, language.Name, v.GetProperty("region").GetString()!, v.GetProperty("quality").GetString()!,
                        v.GetProperty("rate").GetInt32(), v.GetProperty("espeak").GetString()!, v.GetProperty("size").GetInt64(), speakers),
                    v.GetProperty("path").GetString()!, v.GetProperty("sha256").GetString()!, v.GetProperty("configSize").GetInt64(),
                    v.GetProperty("configSha256").GetString()!);
            }
        }
        return new Catalog(root.GetProperty("repository").GetString()!, defaults, voices);
    }
}
