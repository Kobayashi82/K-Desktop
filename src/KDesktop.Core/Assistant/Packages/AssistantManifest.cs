using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// assistant.json, the manifest of an assistant package (docs/assistant/esquemas/assistant.schema.json). K-Desktop
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
    public IReadOnlyList<string> ImportFrom { get; init; } = [];

    /// <summary>The assembly (relative to the package, «lib/X.dll») and the type that implements IAssistant.</summary>
    [JsonIgnore]
    public (string Assembly, string Type) EntryParts => Entry.Split('!', 2) is [var dll, var type] ? (dll, type) : (Entry, string.Empty);

    /// <summary>The version as a comparable value (pre-release tags are ignored for the order).</summary>
    [JsonIgnore]
    public Version ParsedVersion => System.Version.TryParse(Version.Split('-', 2)[0], out var v) ? v : new Version(0, 0, 0);

    /// <summary>Major version of the SDK it was built against.</summary>
    [JsonIgnore]
    public int SdkMajor => int.TryParse(Sdk.Split('.')[0], out var major) ? major : -1;

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

    private void Check()
    {
        if (!IdPattern().IsMatch(Id)) throw new ManifestException($"El id «{Id}» no es válido (minúsculas, números, puntos y guiones)");
        if (!VersionPattern().IsMatch(Version)) throw new ManifestException($"La versión «{Version}» no es válida (x.y.z)");
        if (SdkMajor < 0) throw new ManifestException($"La versión del SDK «{Sdk}» no es válida");
        if (!EntryPattern().IsMatch(Entry)) throw new ManifestException($"La entrada «{Entry}» no es válida (lib/Archivo.dll!Espacio.Tipo)");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^lib/[A-Za-z0-9_.-]+\.dll![A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex EntryPattern();
}

public sealed record ModelHint(string? Suggested = null, string? Min = null);

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
