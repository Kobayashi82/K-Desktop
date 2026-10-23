using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// assistant.json, the manifest of an assistant package (docs/assistant/schemas/assistant.schema.json). K-Desktop
/// reads it (and only it: it never loads an assistant's code); the assistant process reads it to load the entry.
/// </summary>
public sealed partial record AssistantManifest
{
    public const string FileName = "assistant.json";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Author { get; init; }
    public LocalizedText? Description { get; init; }
    public string? Homepage { get; init; }
    public string? License { get; init; }
    public required string Sdk { get; init; }
    public required string Entry { get; init; }
    public string? Icon { get; init; }
    public string? Preview { get; init; }
    public string? WakeWord { get; init; }
    public ModelHint? Model { get; init; }
    public string? Voice { get; init; }
    public string? Persona { get; init; }
    public string? Settings { get; init; }
    public string? Panel { get; init; }
    public required ManifestTraits Traits { get; init; }
    public IReadOnlyList<ManifestExtra> Extras { get; init; } = [];
    public string? Updates { get; init; }

    /// <summary>What this version brings (shown when it is offered as an update and on its card).</summary>
    public LocalizedText? Changes { get; init; }

    public IReadOnlyList<string> ImportFrom { get; init; } = [];

    /// <summary>
    /// Short recorded clips of what it shows in its showcase (stage 13.10), for the catalog (phase 16): K-Desktop can show
    /// them without loading its code, and only downloads the one that is opened.
    /// </summary>
    public IReadOnlyList<ManifestClip> Showcase { get; init; } = [];

    /// <summary>
    /// The settings every assistant has the same way that it takes (stage 13.13: <c>size</c>, <c>volume</c>,
    /// <c>liveliness</c>, <c>dragging</c>); K-Desktop shows those in the «General» tab of its card.
    /// </summary>
    public IReadOnlyList<string> Common { get; init; } = [];

    /// <summary>Whether it takes one of the common settings (<see cref="AssistantCommon.Keys"/>): one without code takes them all.</summary>
    public bool Takes(string common) => IsCharacter ? AssistantCommon.Keys.Contains(common) : Common.Contains(common);

    /// <summary>
    /// Who draws what it says (stage 13.7): <c>host</c> (or nothing), K-Desktop's bubble; <c>own</c>, the assistant itself
    /// with <c>IAssistant.ShowSpeech</c> (the host's bubble when it cannot, or when the user asks for it).
    /// </summary>
    public string? Speech { get; init; }

    public const string OwnSpeechValue = "own";

    /// <summary>It draws what it says itself (<see cref="Speech"/> <c>own</c>; never one without code).</summary>
    [JsonIgnore]
    public bool OwnSpeech => !IsCharacter && string.Equals(Speech, OwnSpeechValue, StringComparison.Ordinal);

    /// <summary>The assembly (relative to the package, «lib/X.dll») and the type that implements IAssistant.</summary>
    [JsonIgnore]
    public (string Assembly, string Type) EntryParts => Entry.Split('!', 2) is [var dll, var type] ? (dll, type) : (Entry, string.Empty);

    /// <summary>
    /// An assistant without code (phase 12): the entry is its <see cref="CharacterFile"/> («character.json»), and the host
    /// moves it. No code of the package is ever loaded.
    /// </summary>
    [JsonIgnore]
    public bool IsCharacter => Entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>The version as a comparable value (pre-release tags are ignored for the order).</summary>
    [JsonIgnore]
    public Version ParsedVersion => System.Version.TryParse(Version.Split('-', 2)[0], out var v) ? v : new Version(0, 0, 0);

    /// <summary>Major version of the SDK it was built against.</summary>
    [JsonIgnore]
    public int SdkMajor => int.TryParse(Sdk.Split('.')[0], out var major) ? major : -1;

    /// <summary>Minor version of the SDK it was built against (0 when it does not say).</summary>
    [JsonIgnore]
    public int SdkMinor => Sdk.Split('.') is [_, var minor, ..] && int.TryParse(minor, out var value) ? value : 0;

    /// <summary>
    /// Whether a host with SDK <paramref name="host"/> can run it: the same major version, and a minor version at least
    /// as new as the one it was built against (a newer minor adds members an older host does not have).
    /// </summary>
    public bool RunsOn(Version host) => SdkMajor == host.Major && SdkMinor <= host.Minor;

    /// <summary>settings.json of the package (the manifest may name another file).</summary>
    [JsonIgnore]
    public string SettingsFile => Settings ?? "settings.json";

    [JsonIgnore]
    public string PanelFile => Panel ?? "panel.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Reads a manifest and checks what loading needs. Throws <see cref="ManifestException"/> with a clear message.</summary>
    public static AssistantManifest Parse(string json)
    {
        AssistantManifest manifest;
        try { manifest = JsonSerializer.Deserialize<AssistantManifest>(json, Options) ?? throw new ManifestException("assistant.json está vacío"); }
        catch (JsonException ex) { throw new ManifestException($"assistant.json no es válido: {ex.Message}", ex); }
        manifest.Check();
        return manifest;
    }

    public static AssistantManifest Load(string folder) =>
        Parse(File.ReadAllText(Path.Combine(folder, FileName)));

    /// <summary>Whether <paramref name="id"/> can be an assistant's id (also a safe folder name).</summary>
    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    private void Check()
    {
        if (!IdPattern().IsMatch(Id)) throw new ManifestException($"El id «{Id}» no es válido (minúsculas, números, puntos y guiones)");
        if (!VersionPattern().IsMatch(Version)) throw new ManifestException($"La versión «{Version}» no es válida (x.y.z)");
        if (SdkMajor < 0) throw new ManifestException($"La versión del SDK «{Sdk}» no es válida");
        if (!EntryPattern().IsMatch(Entry)) throw new ManifestException($"La entrada «{Entry}» no es válida (lib/Archivo.dll!Espacio.Tipo, o character.json)");
        var items = new HashSet<string>(StringComparer.Ordinal);
        foreach (var clip in Showcase)
        {
            if (!items.Add(clip.Item)) throw new ManifestException($"El escaparate tiene dos clips de «{clip.Item}»");
            // K-Desktop names the host's items; its own ones are not known without loading its code.
            if (!ShowcaseItems.IsStandard(clip.Item) && clip.Name is null) throw new ManifestException($"El clip de «{clip.Item}» necesita un nombre (name)");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^(lib/[A-Za-z0-9_.-]+\.dll![A-Za-z_][A-Za-z0-9_.]*|[A-Za-z0-9_-]+\.json)$")]
    private static partial Regex EntryPattern();
}

/// <summary>
/// The AI model an assistant suggests (5.0.107): one or several models of Hugging Face («owner/repo/file.gguf», or
/// «owner/repo:Q4_K_M»), best first. K-Desktop offers the first one that fits in the graphics card, never downloading it
/// without asking. <paramref name="Think"/> (stage 13.10): the same for the model it thinks harder with («Para pensar
/// más»), offered apart and unticked.
/// </summary>
public sealed record ModelHint(
    [property: JsonConverter(typeof(OneOrManyConverter))] IReadOnlyList<string>? Suggested = null,
    string? Min = null,
    [property: JsonConverter(typeof(OneOrManyConverter))] IReadOnlyList<string>? Think = null)
{
    /// <summary>The suggestions that can be read as models of Hugging Face, best first.</summary>
    [JsonIgnore]
    public IReadOnlyList<Brain.HfRef> Models => Read(Suggested);

    /// <summary>The models it suggests to think harder with, best first.</summary>
    [JsonIgnore]
    public IReadOnlyList<Brain.HfRef> ThinkModels => Read(Think);

    private static Brain.HfRef[] Read(IReadOnlyList<string>? list) => [.. (list ?? []).Select(Brain.HuggingFace.Parse).OfType<Brain.HfRef>()];
}

/// <summary>
/// A clip of its showcase (stage 13.10): <see cref="Item"/> is the id of what it shows (one of the host's,
/// <see cref="ShowcaseItems.Standard"/>, or one of its own); <see cref="Clip"/> the file in the package (animated WebP, GIF
/// or PNG). Its own ones carry their <see cref="Name"/> and <see cref="Group"/> (<see cref="ShowcaseItems.OwnGroups"/>).
/// </summary>
public sealed record ManifestClip
{
    public required string Item { get; init; }
    public required string Clip { get; init; }
    public LocalizedText? Name { get; init; }
    public string? Group { get; init; }
}

/// <summary>A text or a list of texts, read as a list.</summary>
public sealed class OneOrManyConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return [reader.GetString()!];
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("se espera un texto o una lista de textos");
        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("se espera una lista de textos");
            list.Add(reader.GetString()!);
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}

public sealed record ManifestTraits
{
    public required string Travel { get; init; }
    public double TravelSpeed { get; init; } = 600;
    public bool CanMoveWindows { get; init; }
    public bool CanReachMaximized { get; init; }
    public IReadOnlyList<string> Gestures { get; init; } = [];
}

public sealed record ManifestExtra
{
    public required string Id { get; init; }
    public required LocalizedText Name { get; init; }
    public LocalizedText? Description { get; init; }
    public string? Icon { get; init; }
    public string? Hotkey { get; init; }
}

public sealed class ManifestException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A text of a package: one string (in the assistant's language) or language → text. K-Desktop shows its own
/// language when there is one, else the first.
/// </summary>
[JsonConverter(typeof(LocalizedTextConverter))]
public sealed class LocalizedText
{
    public LocalizedText(IReadOnlyDictionary<string, string> texts) => Texts = texts;

    public LocalizedText(string text) => Texts = new Dictionary<string, string> { [""] = text };

    /// <summary>Language → text («» for a plain string).</summary>
    public IReadOnlyDictionary<string, string> Texts { get; }

    /// <summary>The text in <paramref name="language"/> («es», «es-ES»…), else the first one.</summary>
    public string In(string language)
    {
        if (Texts.TryGetValue(language, out var exact)) return exact;
        var two = language.Split('-')[0];
        foreach (var (lang, text) in Texts)
            if (lang.Split('-')[0].Equals(two, StringComparison.OrdinalIgnoreCase)) return text;
        return Texts.Values.FirstOrDefault() ?? string.Empty;
    }

    public override string ToString() => In("es");

    private sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
    {
        public override LocalizedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => new LocalizedText(reader.GetString()!),
            JsonTokenType.StartObject => new LocalizedText(JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader)!),
            _ => throw new JsonException("Un texto es una cadena o un objeto idioma → texto"),
        };

        public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
        {
            if (value.Texts.Count == 1 && value.Texts.ContainsKey("")) writer.WriteStringValue(value.Texts[""]);
            else JsonSerializer.Serialize(writer, value.Texts, options);
        }
    }
}
