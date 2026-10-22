using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>
/// Every voice profile (stage 9.4): K-Desktop's own (<see cref="VoiceProfile.BuiltIn"/>, never changed) and the user's,
/// one JSON file each (the schema of <c>voice.schema.json</c>) in <c>&lt;datos&gt;\Assistant\Voices</c>; the file's name is
/// its id. Read by K-Desktop (Options) and by the assistant's process (who speaks with them); a file changed is read
/// again. Thread-safe.
/// </summary>
public static class VoiceProfiles
{
    public static string Folder => Path.Combine(AppPaths.DataDir, "Assistant", "Voices");

    private static readonly object Gate = new();
    private static string? _seen;
    private static IReadOnlyList<VoiceProfile> _own = [];

    /// <summary>The user's, by name.</summary>
    public static IReadOnlyList<VoiceProfile> Own
    {
        get
        {
            lock (Gate)
            {
                var files = Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json") : [];
                var seen = string.Join('|', files.Select(f => f + File.GetLastWriteTimeUtc(f).Ticks.ToString(CultureInfo.InvariantCulture)));
                if (seen != _seen)
                {
                    _seen = seen;
                    _own = [.. files.Select(f => VoiceProfile.Load(f, Path.GetFileNameWithoutExtension(f))).OfType<VoiceProfile>()
                        .Where(p => !IsBuiltIn(p.Id)).OrderBy(p => p.Name, StringComparer.Create(CultureInfo.CurrentCulture, true))];
                }
                return _own;
            }
        }
    }

    /// <summary>K-Desktop's first, then the user's.</summary>
    public static IReadOnlyList<VoiceProfile> All => [.. VoiceProfile.BuiltIn, .. Own];

    public static bool IsBuiltIn(string id) => VoiceProfile.BuiltIn.Any(p => p.Id == id);

    /// <summary>One of K-Desktop's or of the user's; null if there is none with that id.</summary>
    public static VoiceProfile? Find(string? id) =>
        id is not { Length: > 0 } ? null : VoiceProfile.Find(id) ?? Own.FirstOrDefault(p => p.Id == id);

    /// <summary>Its name for people: K-Desktop's in the user's language, the user's as they named it.</summary>
    public static string NameOf(VoiceProfile profile) =>
        IsBuiltIn(profile.Id) && Loc.T("assistantVoice.profile." + profile.Id) is var own && own != "assistantVoice.profile." + profile.Id ? own : profile.Name;

    public static string NameOf(string id) => Find(id) is { } profile ? NameOf(profile) : id;

    /// <summary>Keeps a profile of the user's (a new id from its name when it has none or it is one of K-Desktop's).</summary>
    /// <returns>Its id.</returns>
    public static string Save(VoiceProfile profile)
    {
        var id = profile.Id is { Length: > 0 } known && !IsBuiltIn(known) && IsId(known) ? known : NewId(profile.Name);
        Directory.CreateDirectory(Folder);
        var json = WithoutId(profile with { Id = id }).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var path = PathOf(id);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
        return id;
    }

    public static void Delete(string id)
    {
        if (IsBuiltIn(id) || !IsId(id)) return;
        try { File.Delete(PathOf(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Perfil de voz {id}: no se ha podido borrar: {ex.Message}"); }
    }

    /// <summary>A profile as a file to give someone (the schema, without its id).</summary>
    public static string Export(VoiceProfile profile) =>
        WithoutId(profile with { Name = NameOf(profile) }).ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>A profile from a file someone gave (checked against the schema), kept as one of the user's.</summary>
    /// <returns>Its id, or null with what is wrong in <paramref name="errors"/>.</returns>
    public static string? Import(string json, out IReadOnlyList<string> errors)
    {
        errors = PackageSchemas.Validate("voice", json);
        if (errors.Count > 0) return null;
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
        node.Remove("$schema");
        return Save(VoiceProfile.Parse(node, string.Empty));
    }

    /// <summary>Whether the schema takes it as it is (before keeping it).</summary>
    public static IReadOnlyList<string> Check(VoiceProfile profile) => PackageSchemas.Validate("voice", WithoutId(profile).ToJsonString());

    private static JsonObject WithoutId(VoiceProfile profile)
    {
        var json = profile.ToJson();
        json.Remove("id");
        return json;
    }

    private static string PathOf(string id) => Path.Combine(Folder, id + ".json");

    private static bool IsId(string id) => id.Length is > 0 and <= 60 && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    /// <summary>«Mi robot» → «mi-robot» (or «mi-robot-2» when it is taken).</summary>
    public static string NewId(string name)
    {
        var folded = new StringBuilder();
        foreach (var c in name.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)) folded.Append(c);
            else if ((c == ' ' || c == '-' || c == '_') && folded.Length > 0 && folded[^1] != '-') folded.Append('-');
        }
        var stem = folded.ToString().Trim('-');
        if (stem.Length == 0) stem = "voz";
        if (stem.Length > 50) stem = stem[..50].Trim('-');
        var id = stem;
        for (var n = 2; IsBuiltIn(id) || File.Exists(PathOf(id)); n++) id = $"{stem}-{n}";
        return id;
    }
}

/// <summary>A value of an effect: its name in the JSON, its range, its step and the value it has when it is added.</summary>
/// <param name="Toggle">A yes or no (<c>beep</c>, <c>squelch</c>): <see cref="Default"/> 1 = yes.</param>
public sealed record VoiceEffectValue(string Name, double Min, double Max, double Step, double Default, bool Toggle = false, bool Whole = false);

/// <summary>
/// The effects of a voice and their values (stage 9.4): what the editor of Options shows as switches and sliders, and the
/// margins <see cref="Random"/> stays within. The ranges are those of <c>voice.schema.json</c>, narrowed to what sounds like
/// a voice; only the values the effects really use (<c>VoiceChain</c>).
/// </summary>
public static class VoiceEffectCatalog
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<VoiceEffectValue>> Effects = new Dictionary<string, IReadOnlyList<VoiceEffectValue>>(StringComparer.Ordinal)
    {
        ["pitch"] = [new("semitones", -12, 12, 0.5, 3)],
        ["formant"] = [new("shift", 0.5, 2, 0.05, 1.2)],
        ["ring"] = [new("freq", 5, 400, 1, 50), new("mix", 0, 1, 0.05, 0.4)],
        ["vocoder"] = [new("note", 50, 500, 1, 131), new("bands", 4, 64, 1, 16, Whole: true)],
        ["bitcrush"] = [new("bits", 2, 16, 1, 8, Whole: true), new("rate", 2000, 24000, 100, 8000)],
        ["radio"] = [new("bandwidth", 300, 8000, 50, 3000), new("noise", 0, 0.3, 0.01, 0.03), new("beep", 0, 1, 1, 1, Toggle: true), new("squelch", 0, 1, 1, 1, Toggle: true)],
        ["chorus"] = [new("rate", 0.1, 10, 0.1, 1.5), new("depth", 0, 1, 0.05, 0.5)],
        ["flanger"] = [new("rate", 0.05, 2, 0.05, 0.3), new("depth", 0, 1, 0.05, 0.7)],
        ["echo"] = [new("ms", 1, 800, 1, 120), new("feedback", 0, 0.9, 0.05, 0.3), new("mix", 0, 1, 0.05, 0.3)],
        ["glitch"] = [new("chance", 0, 0.5, 0.01, 0.1)],
    };

    /// <summary>The effects in the order the editor offers them.</summary>
    public static IReadOnlyList<string> Names { get; } = ["pitch", "formant", "ring", "vocoder", "bitcrush", "radio", "chorus", "flanger", "echo", "glitch"];

    /// <summary>A new effect with its values as it is added.</summary>
    public static JsonObject New(string fx)
    {
        var effect = new JsonObject { ["fx"] = fx };
        foreach (var value in Effects[fx]) effect[value.Name] = value.Toggle ? value.Default >= 0.5 : Json(value, value.Default);
        return effect;
    }

    /// <summary>A value as it goes in the JSON (a whole number for <c>bits</c> and <c>bands</c>; rounded to its step).</summary>
    public static JsonNode Json(VoiceEffectValue value, double number)
    {
        number = Math.Clamp(Math.Round(number / value.Step) * value.Step, value.Min, value.Max);
        return value.Whole ? JsonValue.Create((int)Math.Round(number)) : JsonValue.Create(Math.Round(number, 4));
    }

    /// <summary>
    /// A chain of effects at random within margins that still sound like a voice (stage 9.4, «Aleatorio»): two to four
    /// different effects, never the vocoder with the bitcrusher or the radio twice; the values from the middle of their range.
    /// </summary>
    public static JsonArray Random(Random random)
    {
        var pool = new List<string> { "pitch", "formant", "ring", "vocoder", "bitcrush", "chorus", "flanger", "echo", "radio" };
        var count = random.Next(2, 5);
        var chosen = new List<string>();
        while (chosen.Count < count && pool.Count > 0)
        {
            var fx = pool[random.Next(pool.Count)];
            pool.Remove(fx);
            if (fx is "vocoder" or "bitcrush") pool.RemoveAll(p => p is "vocoder" or "bitcrush");
            if (fx is "chorus" or "flanger") pool.RemoveAll(p => p is "chorus" or "flanger");
            chosen.Add(fx);
        }
        // Pitch and formants first, then what colours it, then echo, radio last (as a radio would carry it).
        string[] order = ["pitch", "formant", "vocoder", "ring", "bitcrush", "chorus", "flanger", "echo", "radio"];
        var chain = new JsonArray();
        foreach (var fx in chosen.OrderBy(f => Array.IndexOf(order, f)))
        {
            var effect = new JsonObject { ["fx"] = fx };
            foreach (var value in Effects[fx])
            {
                if (value.Toggle) { effect[value.Name] = random.NextDouble() < 0.5; continue; }
                // The middle half of its range (the ends sound broken), around its usual value.
                var (low, high) = Middle(fx, value);
                effect[value.Name] = Json(value, low + random.NextDouble() * (high - low));
            }
            chain.Add(effect);
        }
        return chain;
    }

    /// <summary>Where a random value goes: narrower than the editor allows, where it still sounds like someone talking.</summary>
    private static (double Low, double High) Middle(string fx, VoiceEffectValue value) => (fx, value.Name) switch
    {
        ("pitch", _) => (-5, 6),
        ("formant", _) => (0.8, 1.5),
        ("ring", "freq") => (20, 120),
        ("ring", "mix") => (0.15, 0.5),
        ("vocoder", "note") => (90, 220),
        ("vocoder", "bands") => (12, 32),
        ("bitcrush", "bits") => (5, 10),
        ("bitcrush", "rate") => (6000, 14000),
        ("radio", "bandwidth") => (2000, 4000),
        ("radio", "noise") => (0.01, 0.06),
        ("echo", "ms") => (10, 200),
        ("echo", "feedback") => (0.1, 0.4),
        ("echo", "mix") => (0.1, 0.35),
        _ => (value.Min + (value.Max - value.Min) * 0.25, value.Min + (value.Max - value.Min) * 0.75),
    };
}
