using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// settings.json of a package (Assistant/Packages/Schemas/settings.schema.json) as K-Desktop draws it: pages, sections and
/// items. Only read here; the package was checked against the schema when it was installed.
/// </summary>
public sealed record SettingsLayout(IReadOnlyList<SettingsPage> Pages)
{
    /// <summary>The settings of an installed assistant, or null when it has none (or they cannot be read).</summary>
    public static SettingsLayout? Load(InstalledAssistant assistant) =>
        AssistantUi.LoadFile(assistant, assistant.Manifest.SettingsFile, Parse);

    public static SettingsLayout Parse(string json) =>
        JsonSerializer.Deserialize<SettingsLayout>(json, AssistantUi.Options) ?? throw new JsonException("settings.json está vacío");

    /// <summary>Every item, page after page.</summary>
    [JsonIgnore]
    public IEnumerable<SettingItem> Items => Pages.SelectMany(p => p.Sections).SelectMany(s => s.Items);

    /// <summary>Key → default of every item with both.</summary>
    public JsonObject Defaults()
    {
        var result = new JsonObject();
        foreach (var item in Items)
            if (item.Key is { } key && item.Default is { } value) result[key] = value.DeepClone();
        return result;
    }
}

public sealed record SettingsPage(LocalizedText Title, IReadOnlyList<SettingsSection> Sections, string? Icon = null);

public sealed record SettingsSection(IReadOnlyList<SettingItem> Items, LocalizedText? Title = null);

/// <summary>One control of a settings page. Which fields count depends on <see cref="Type"/>.</summary>
public sealed record SettingItem
{
    /// <summary>toggle, slider, number, choice, text, color, gallery, apps, hotkey, button, info or keys.</summary>
    public required string Type { get; init; }
    public string? Key { get; init; }
    public LocalizedText? Label { get; init; }
    public LocalizedText? Description { get; init; }
    public string? Icon { get; init; }

    /// <summary>Key of a toggle that has to be on («!key»: off) for this one to be usable.</summary>
    public string? EnabledWhen { get; init; }

    public JsonNode? Default { get; init; }

    // slider, number
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string? Format { get; init; }
    public LocalizedText? MinLabel { get; init; }
    public LocalizedText? MaxLabel { get; init; }

    // choice
    public IReadOnlyList<SettingOption> Options { get; init; } = [];
    /// <summary>«list» or «buttons» (null: buttons for up to four options, a list for more).</summary>
    public string? Style { get; init; }

    // text
    public LocalizedText? Placeholder { get; init; }
    public int? MaxLength { get; init; }

    // gallery: «source» (published by the assistant) or «items»; keys: «items» too, with another shape
    public string? Source { get; init; }
    [JsonPropertyName("items")]
    public JsonArray? RawItems { get; init; }

    // hotkey, button
    public string? Extra { get; init; }
    public bool ResetData { get; init; }
    public LocalizedText? Confirm { get; init; }

    // info
    public LocalizedText? Text { get; init; }

    /// <summary>The fixed images of a gallery without «source».</summary>
    [JsonIgnore]
    public IReadOnlyList<GalleryEntry> GalleryItems => Type == "gallery" && Source is null ? GalleryEntry.List(RawItems) : [];

    /// <summary>The rows of a «keys» table.</summary>
    [JsonIgnore]
    public IReadOnlyList<KeyRow> KeyRows => Type == "keys" && RawItems is { } rows
        ? [.. rows.OfType<JsonObject>().Select(r => new KeyRow(
            [.. (r["keys"] as JsonArray ?? []).Select(k => k?.GetValue<string>() ?? string.Empty)],
            AssistantUi.TextOf(r["text"]) ?? new LocalizedText(string.Empty)))]
        : [];
}

/// <summary>An option of a choice: a plain text (value and label at once) or { value, label }.</summary>
[JsonConverter(typeof(SettingOptionConverter))]
public sealed record SettingOption(JsonNode Value, LocalizedText Label);

internal sealed class SettingOptionConverter : JsonConverter<SettingOption>
{
    public override SettingOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        return node switch
        {
            JsonValue v when v.TryGetValue<string>(out var text) => new SettingOption(JsonValue.Create(text), new LocalizedText(text)),
            JsonObject o when o["value"] is { } value => new SettingOption(value.DeepClone(), AssistantUi.TextOf(o["label"]) ?? new LocalizedText(value.ToString())),
            _ => throw new JsonException("Una opción es un texto o { value, label }"),
        };
    }

    public override void Write(Utf8JsonWriter writer, SettingOption value, JsonSerializerOptions options) =>
        new JsonObject { ["value"] = value.Value.DeepClone(), ["label"] = value.Label.In("") }.WriteTo(writer);
}

/// <summary>An image of a gallery. <see cref="Image"/> is a path inside the package.</summary>
public sealed record GalleryEntry(string Id, LocalizedText Name, string Image, bool Locked = false, LocalizedText? LockText = null)
{
    /// <summary>
    /// Entries of a gallery: its fixed «items», or what the assistant published under its «source»
    /// ([{ id, name, image, locked, lockText }]). Anything without an id is skipped.
    /// </summary>
    public static IReadOnlyList<GalleryEntry> List(JsonNode? items) =>
        items is JsonArray array
            ? [.. array.OfType<JsonObject>()
                .Where(o => o["id"] is JsonValue)
                .Select(o => new GalleryEntry(
                    AssistantUi.Plain(o["id"]),
                    AssistantUi.TextOf(o["name"]) ?? new LocalizedText(AssistantUi.Plain(o["id"])),
                    o["image"] is JsonValue image && image.TryGetValue<string>(out var path) ? path : string.Empty,
                    o["locked"] is JsonValue locked && locked.TryGetValue<bool>(out var l) && l,
                    AssistantUi.TextOf(o["lockText"])))]
            : [];
}

public sealed record KeyRow(IReadOnlyList<string> Keys, LocalizedText Text);

/// <summary>panel.json of a package: the cards of its tab in Panel → Assistant.</summary>
public sealed record PanelLayout(IReadOnlyList<PanelCard> Cards)
{
    public static PanelLayout? Load(InstalledAssistant assistant) =>
        AssistantUi.LoadFile(assistant, assistant.Manifest.PanelFile, Parse);

    public static PanelLayout Parse(string json) =>
        JsonSerializer.Deserialize<PanelLayout>(json, AssistantUi.Options) ?? throw new JsonException("panel.json está vacío");
}

/// <summary>A card of the panel. Which fields count depends on <see cref="Type"/>.</summary>
public sealed record PanelCard
{
    /// <summary>stat, progress, list, achievements, buttons or text.</summary>
    public required string Type { get; init; }
    public LocalizedText? Label { get; init; }
    public string? Bind { get; init; }
    public string? Format { get; init; }
    public string? Icon { get; init; }

    /// <summary>progress: a number or the key of a published one.</summary>
    public JsonNode? Max { get; init; }

    /// <summary>list: what it says while the list is empty.</summary>
    public LocalizedText? Empty { get; init; }

    public LocalizedText? Text { get; init; }
    public IReadOnlyList<PanelButton> Items { get; init; } = [];
}

public sealed record PanelButton(LocalizedText Label, string Extra, string? Icon = null);

/// <summary>An achievement of an «achievements» card. A plain text in the data is a name, unlocked.</summary>
public sealed record PanelAchievement(string Name, string? Description, bool Unlocked, double? Progress, string? Icon)
{
    public static IReadOnlyList<PanelAchievement> List(JsonNode? data, string language) =>
        data is JsonArray array
            ? [.. array.Select(node => node switch
            {
                JsonObject o => new PanelAchievement(
                    AssistantUi.TextOf(o["name"])?.In(language) ?? AssistantUi.Plain(o["id"]),
                    AssistantUi.TextOf(o["description"])?.In(language),
                    o["unlocked"] is JsonValue u && u.TryGetValue<bool>(out var unlocked) && unlocked,
                    AssistantUi.Number(o["progress"]) is { } p ? Math.Clamp(p, 0, 1) : null,
                    o["icon"] is JsonValue i && i.TryGetValue<string>(out var icon) ? icon : null),
                _ => new PanelAchievement(AssistantUi.Plain(node), null, true, null, null),
            }).Where(a => a.Name.Length > 0)]
            : [];
}

/// <summary>What the drawing of an assistant's settings and panel shares: binds, merging, numbers and texts.</summary>
public static class AssistantUi
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        RespectRequiredConstructorParameters = true,
    };

    internal static T? LoadFile<T>(InstalledAssistant assistant, string relative, Func<string, T> parse) where T : class
    {
        if (!AssistantPackages.IsSafePath(relative)) return null;
        var file = Path.Combine(assistant.Folder, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return null;
        try { return parse(File.ReadAllText(file)); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn($"Asistente «{assistant.Id}»: {relative} no se puede leer ({ex.Message})");
            return null;
        }
    }

    /// <summary>A text of a package (a string or language → text), or null.</summary>
    public static LocalizedText? TextOf(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var text) => new LocalizedText(text),
        JsonObject o => new LocalizedText(o.Where(p => p.Value is JsonValue).ToDictionary(p => p.Key, p => Plain(p.Value))),
        _ => null,
    };

    /// <summary>A value as plain text: strings as they are, numbers in invariant form, anything else as JSON.</summary>
    public static string Plain(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue v when v.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString(),
    };

    /// <summary>A published value as a number (a number, or a text that is one), else null.</summary>
    public static double? Number(JsonNode? node) => node switch
    {
        // The text of the number: a value made in memory may hold an int, which GetValue<double> refuses.
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    /// <summary>The value at a dotted key («pilot.level») of published data, or null.</summary>
    public static JsonNode? Resolve(JsonObject? data, string? path)
    {
        if (data is null || string.IsNullOrEmpty(path)) return null;
        JsonNode? node = data;
        foreach (var part in path.Split('.'))
        {
            if (node is not JsonObject o || !o.TryGetPropertyValue(part, out node)) return null;
        }
        return node;
    }

    /// <summary>
    /// Puts what an assistant published on top of what it had published: objects are merged key by key, anything
    /// else (numbers, texts, lists, null) replaces the old value.
    /// </summary>
    public static void Merge(JsonObject target, JsonObject update)
    {
        foreach (var (key, value) in update)
        {
            if (value is JsonObject inner && target[key] is JsonObject existing) Merge(existing, inner);
            else target[key] = value?.DeepClone();
        }
    }

    /// <summary>Whether an item with this «enabledWhen» can be used with these values (a missing key counts as off).</summary>
    public static bool IsEnabled(string? enabledWhen, JsonObject values)
    {
        if (string.IsNullOrEmpty(enabledWhen)) return true;
        var negate = enabledWhen[0] == '!';
        var on = values[negate ? enabledWhen[1..] : enabledWhen] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        return on != negate;
    }

    /// <summary>A value of a slider or a number box: inside its limits and on its steps.</summary>
    public static double Snap(double value, double min, double max, double? step)
    {
        if (max < min) (min, max) = (max, min);
        if (step is > 0 and var s) value = min + Math.Round((value - min) / s) * s;
        // Steps like 0.1 leave 0.30000000000000004 behind.
        return Math.Clamp(Math.Round(value, 10), min, max);
    }

    /// <summary>A number as it is saved: whole when the item only has whole values (an int for the assistant), else a double.</summary>
    public static JsonNode NumberNode(double value, double min, double? step) =>
        IsWhole(min) && IsWhole(step ?? 1) && IsWhole(value) ? JsonValue.Create((long)Math.Round(value)) : JsonValue.Create(value);

    private static bool IsWhole(double d) => Math.Abs(d - Math.Round(d)) < 1e-9;

    /// <summary>
    /// A number with the item's «format» («{0} s», «{0:0} px», «{0:P0}»; «{1}» is the maximum of a progress card), or
    /// a plain short number without one. A broken format shows the number alone.
    /// </summary>
    public static string FormatNumber(string? format, double value, IFormatProvider culture, double? max = null)
    {
        if (!string.IsNullOrEmpty(format))
        {
            try { return string.Format(culture, format, value, max ?? 0); }
            catch (FormatException) { }
        }
        var plain = value.ToString(IsWhole(value) ? "N0" : "0.##", culture);
        return max is { } m ? $"{plain} / {m.ToString(IsWhole(m) ? "N0" : "0.##", culture)}" : plain;
    }

    /// <summary>A published value for a «stat» or «text» card: numbers with the format, texts as they are, «—» for nothing.</summary>
    public static string Display(JsonNode? node, string? format, IFormatProvider culture, string language) => node switch
    {
        null => "—",
        JsonObject => TextOf(node)?.In(language) ?? "—",
        _ when Number(node) is { } n && node.GetValueKind() == JsonValueKind.Number => FormatNumber(format, n, culture),
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "✓" : "✗",
        JsonArray a => string.Join(", ", a.Select(Plain)),
        _ when !string.IsNullOrEmpty(format) => SafeFormat(format, Plain(node), culture),
        _ => Plain(node),
    };

    private static string SafeFormat(string format, string text, IFormatProvider culture)
    {
        try { return string.Format(culture, format, text); }
        catch (FormatException) { return text; }
    }

    /// <summary>The texts of a «list» card (a list of texts; anything else is shown as text).</summary>
    public static IReadOnlyList<string> Lines(JsonNode? node, string language) =>
        node is JsonArray array ? [.. array.Where(n => n is not null).Select(n => TextOf(n)?.In(language) ?? Plain(n))] : [];
}
