using System.Text.Json;
using System.Text.Json.Nodes;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// One animation of a character: its frames (images of the package, or one strip cut into <c>Count</c> equal frames),
/// how fast and whether it repeats.
/// </summary>
/// <param name="Frames">The images, in order (with a strip: just the strip).</param>
/// <param name="Strip">The frames are one image, side by side.</param>
/// <param name="Count">How many frames the strip has (1 without one).</param>
/// <param name="Sound">A sound of the package played when it starts, or null.</param>
/// <param name="Weight">How often it is chosen among the variants of its animation (stage 13.11).</param>
/// <param name="Cooldown">Seconds before it can be chosen again (while another variant can).</param>
/// <param name="When">
/// For <c>enter</c> and <c>exit</c>, the reasons it is for (<c>startup</c>, <c>summoned</c>; <c>dismissed</c>,
/// <c>uninstalling</c>); null: any.
/// </param>
public sealed record CharacterAnimation(IReadOnlyList<string> Frames, bool Strip, int Count, double Fps, bool Loop, string? Sound,
    double Weight = 1, double Cooldown = 0, IReadOnlyList<string>? When = null)
{
    public int FrameCount => Strip ? Count : Frames.Count;

    /// <summary>How long it takes once.</summary>
    public double Seconds => FrameCount / Fps;

    /// <summary>Whether it is for this reason (one without <see cref="When"/> is for all).</summary>
    public bool IsFor(string reason) => When is null || When.Contains(reason);
}

/// <summary>
/// An assistant without code (phase 12): its <c>character.json</c>, the entry of its manifest. Pictures and a few
/// numbers; the host's own character (<c>KDesktop.Assistant.Host.Characters</c>) walks it along the taskbar and the
/// tops of the windows, lets it be dragged and dropped, and plays one animation for each thing it does
/// (<see cref="Names"/>). Only <c>idle</c> is required: whatever is missing uses another (<see cref="Fallbacks"/>).
/// Since stage 13.11 each animation can be a list of variants (<see cref="Variants"/>): the host picks one each time it
/// plays it, by weight and without repeating, and an entrance or an exit by its reason.
/// Schema: <c>docs/assistant/schemas/character.schema.json</c>.
/// </summary>
public sealed class CharacterFile
{
    /// <summary>The animations a character can have.</summary>
    public static readonly IReadOnlyList<string> Names =
        ["idle", "walk", "talk", "listen", "think", "happy", "sad", "surprised", "click", "drag", "fall", "sleep", "enter", "exit", "announce", "catch"];

    /// <summary>What plays when an animation is missing (and, in the end, <c>idle</c>).</summary>
    public static readonly IReadOnlyDictionary<string, string> Fallbacks = new Dictionary<string, string>
    {
        ["walk"] = "idle", ["talk"] = "idle", ["listen"] = "idle", ["think"] = "listen", ["happy"] = "idle", ["sad"] = "idle",
        ["surprised"] = "idle", ["click"] = "happy", ["drag"] = "fall", ["fall"] = "idle", ["sleep"] = "idle", ["enter"] = "idle", ["exit"] = "idle",
        ["announce"] = "happy", ["catch"] = "happy",
    };

    /// <summary>The reasons an entrance can be for, and an exit (the names of the pipe: <c>load.entrance</c>, <c>unload.reason</c>).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Reasons = new Dictionary<string, IReadOnlyList<string>>
    {
        ["enter"] = ["startup", "summoned"],
        ["exit"] = ["dismissed", "uninstalling"],
    };

    /// <summary>The most variants an animation can have.</summary>
    public const int MaxVariants = 16;

    /// <summary>Those that play once and then go back (the others repeat), unless the file says otherwise.</summary>
    private static readonly HashSet<string> Once = ["happy", "sad", "surprised", "click", "enter", "exit", "announce", "catch"];

    /// <summary>Its height at 100 % scale (DIP): every frame is drawn this tall, keeping its proportions.</summary>
    public double Height { get; private init; } = 96;

    /// <summary>The way its frames look: true when they look left (walking right, they are mirrored, and the other way round).</summary>
    public bool FacesLeft { get; private init; }

    /// <summary>How fast it walks (DIP per second); 0 = it never walks.</summary>
    public double WalkSpeed { get; private init; } = 70;

    /// <summary>The first variant of each animation it has.</summary>
    public IReadOnlyDictionary<string, CharacterAnimation> Animations { get; private init; } = new Dictionary<string, CharacterAnimation>();

    /// <summary>Every variant of each animation it has (one, when the file gives an object and not a list).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CharacterAnimation>> Variants { get; private init; } = new Dictionary<string, IReadOnlyList<CharacterAnimation>>();

    /// <summary>The animation to play for <paramref name="name"/>: its own (the first variant), or the one it falls back to.</summary>
    public CharacterAnimation For(string name) => VariantsFor(name)[0];

    /// <summary>The variants to choose from for <paramref name="name"/>: its own, or those of the one it falls back to.</summary>
    public IReadOnlyList<CharacterAnimation> VariantsFor(string name)
    {
        for (var i = 0; i < 8; i++)
        {
            if (Variants.TryGetValue(name, out var variants)) return variants;
            if (!Fallbacks.TryGetValue(name, out var next)) break;
            name = next;
        }
        return Variants["idle"];
    }

    /// <summary>Its own variants of an entrance or an exit for that reason (none: it has no such animation for it).</summary>
    public IReadOnlyList<CharacterAnimation> VariantsFor(string name, string reason) =>
        Variants.TryGetValue(name, out var variants) ? [.. variants.Where(v => v.IsFor(reason))] : [];

    /// <summary>Every file it names (frames and sounds), to check that the package has them.</summary>
    public IEnumerable<string> Files => Variants.Values.SelectMany(v => v).SelectMany(a => a.Sound is { } s ? a.Frames.Append(s) : a.Frames).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a character.json (it was checked against its schema). Throws <see cref="ManifestException"/> when it makes no sense.</summary>
    public static CharacterFile Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { throw new ManifestException(ex.Message); }
        if (root?["animations"] is not JsonObject animations) throw new ManifestException("character.json: falta «animations»");

        var parsed = new Dictionary<string, IReadOnlyList<CharacterAnimation>>(StringComparer.Ordinal);
        foreach (var (name, node) in animations)
        {
            if (!Names.Contains(name)) continue;
            // One animation, or a list of variants (stage 13.11).
            var variants = node switch
            {
                JsonObject one => [ReadAnimation(name, one)],
                JsonArray list => list.OfType<JsonObject>().Select(v => ReadAnimation(name, v)).ToList(),
                _ => [],
            };
            if (variants.Count == 0) throw new ManifestException($"character.json: «{name}» no tiene ninguna variante");
            if (variants.Count > MaxVariants) throw new ManifestException($"character.json: «{name}» tiene más de {MaxVariants} variantes");
            parsed[name] = variants;
        }
        if (!parsed.ContainsKey("idle")) throw new ManifestException("character.json: falta la animación «idle»");

        return new CharacterFile
        {
            Height = Number(root["height"]) is { } h ? Math.Clamp(h, 24, 512) : 96,
            FacesLeft = Text(root["faces"]) == "left",
            WalkSpeed = Number(root["walkSpeed"]) is { } w ? Math.Clamp(w, 0, 600) : 70,
            Animations = parsed.ToDictionary(p => p.Key, p => p.Value[0], StringComparer.Ordinal),
            Variants = parsed,
        };
    }

    private static CharacterAnimation ReadAnimation(string name, JsonObject animation)
    {
        var fps = Number(animation["fps"]) is { } f ? Math.Clamp(f, 0.5, 60) : 8;
        var loop = animation["loop"] is JsonValue l && l.TryGetValue<bool>(out var repeats) ? repeats : !Once.Contains(name);
        var sound = Text(animation["sound"]);
        if (sound is not null && !AssistantPackages.IsSafePath(sound)) throw new ManifestException($"character.json: «{sound}» no es una ruta del paquete");
        var weight = Number(animation["weight"]) is { } w ? Math.Clamp(w, 0.01, 100) : 1;
        var cooldown = Number(animation["cooldown"]) is { } c ? Math.Clamp(c, 0, 86400) : 0;
        IReadOnlyList<string>? when = null;
        if (animation["when"] is JsonArray reasons)
        {
            when = [.. reasons.Select(Text).OfType<string>().Distinct(StringComparer.Ordinal)];
            if (!Reasons.TryGetValue(name, out var allowed)) throw new ManifestException($"character.json: «when» solo vale en enter y exit, no en «{name}»");
            if (when.FirstOrDefault(r => !allowed.Contains(r)) is { } wrong) throw new ManifestException($"character.json: «{name}» no tiene el motivo «{wrong}» (sí: {string.Join(", ", allowed)})");
        }
        if (Text(animation["strip"]) is { } strip)
        {
            if (!AssistantPackages.IsSafePath(strip)) throw new ManifestException($"character.json: «{strip}» no es una ruta del paquete");
            var count = (int)Math.Clamp(Number(animation["frames"]) ?? 1, 1, 120);
            return new CharacterAnimation([strip], true, count, fps, loop, sound, weight, cooldown, when);
        }
        if (animation["frames"] is JsonArray list)
        {
            var frames = list.Select(Text).OfType<string>().ToList();
            if (frames.Count == 0) throw new ManifestException($"character.json: «{name}» no tiene fotogramas");
            if (frames.FirstOrDefault(f => !AssistantPackages.IsSafePath(f)) is { } bad) throw new ManifestException($"character.json: «{bad}» no es una ruta del paquete");
            return new CharacterAnimation(frames, false, frames.Count, fps, loop, sound, weight, cooldown, when);
        }
        throw new ManifestException($"character.json: «{name}» necesita «frames» o «strip»");
    }

    private static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s.Replace('\\', '/') : null;
}
