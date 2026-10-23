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
public sealed record CharacterAnimation(IReadOnlyList<string> Frames, bool Strip, int Count, double Fps, bool Loop, string? Sound)
{
    public int FrameCount => Strip ? Count : Frames.Count;
}

/// <summary>
/// An assistant without code (phase 12): its <c>character.json</c>, the entry of its manifest. Pictures and a few
/// numbers; the host's own character (<c>KDesktop.Assistant.Host.Characters</c>) walks it along the taskbar and the
/// tops of the windows, lets it be dragged and dropped, and plays one animation for each thing it does
/// (<see cref="Names"/>). Only <c>idle</c> is required: whatever is missing uses another (<see cref="Fallbacks"/>).
/// Schema: <c>docs/assistant/esquemas/character.schema.json</c>.
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

    /// <summary>Those that play once and then go back (the others repeat), unless the file says otherwise.</summary>
    private static readonly HashSet<string> Once = ["happy", "sad", "surprised", "click", "enter", "exit", "announce", "catch"];

    /// <summary>Its height at 100 % scale (DIP): every frame is drawn this tall, keeping its proportions.</summary>
    public double Height { get; private init; } = 96;

    /// <summary>The way its frames look: true when they look left (walking right, they are mirrored, and the other way round).</summary>
    public bool FacesLeft { get; private init; }

    /// <summary>How fast it walks (DIP per second); 0 = it never walks.</summary>
    public double WalkSpeed { get; private init; } = 70;

    public IReadOnlyDictionary<string, CharacterAnimation> Animations { get; private init; } = new Dictionary<string, CharacterAnimation>();

    /// <summary>The animation to play for <paramref name="name"/>: its own, or the one it falls back to.</summary>
    public CharacterAnimation For(string name)
    {
        for (var i = 0; i < 8; i++)
        {
            if (Animations.TryGetValue(name, out var animation)) return animation;
            if (!Fallbacks.TryGetValue(name, out var next)) break;
            name = next;
        }
        return Animations["idle"];
    }

    /// <summary>Every file it names (frames and sounds), to check that the package has them.</summary>
    public IEnumerable<string> Files => Animations.Values.SelectMany(a => a.Sound is { } s ? a.Frames.Append(s) : a.Frames).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a character.json (it was checked against its schema). Throws <see cref="ManifestException"/> when it makes no sense.</summary>
    public static CharacterFile Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { throw new ManifestException(ex.Message); }
        if (root?["animations"] is not JsonObject animations) throw new ManifestException("character.json: falta «animations»");

        var parsed = new Dictionary<string, CharacterAnimation>(StringComparer.Ordinal);
        foreach (var (name, node) in animations)
        {
            if (!Names.Contains(name) || node is not JsonObject animation) continue;
            var fps = Number(animation["fps"]) is { } f ? Math.Clamp(f, 0.5, 60) : 8;
            var loop = animation["loop"] is JsonValue l && l.TryGetValue<bool>(out var repeats) ? repeats : !Once.Contains(name);
            var sound = Text(animation["sound"]);
            if (sound is not null && !AssistantPackages.IsSafePath(sound)) throw new ManifestException($"character.json: «{sound}» no es una ruta del paquete");
            if (Text(animation["strip"]) is { } strip)
            {
                if (!AssistantPackages.IsSafePath(strip)) throw new ManifestException($"character.json: «{strip}» no es una ruta del paquete");
                var count = (int)Math.Clamp(Number(animation["frames"]) ?? 1, 1, 120);
                parsed[name] = new CharacterAnimation([strip], true, count, fps, loop, sound);
            }
            else if (animation["frames"] is JsonArray list)
            {
                var frames = list.Select(Text).OfType<string>().ToList();
                if (frames.Count == 0) throw new ManifestException($"character.json: «{name}» no tiene fotogramas");
                if (frames.FirstOrDefault(f => !AssistantPackages.IsSafePath(f)) is { } bad) throw new ManifestException($"character.json: «{bad}» no es una ruta del paquete");
                parsed[name] = new CharacterAnimation(frames, false, frames.Count, fps, loop, sound);
            }
            else throw new ManifestException($"character.json: «{name}» necesita «frames» o «strip»");
        }
        if (!parsed.ContainsKey("idle")) throw new ManifestException("character.json: falta la animación «idle»");

        return new CharacterFile
        {
            Height = Number(root["height"]) is { } h ? Math.Clamp(h, 24, 512) : 96,
            FacesLeft = Text(root["faces"]) == "left",
            WalkSpeed = Number(root["walkSpeed"]) is { } w ? Math.Clamp(w, 0, 600) : 70,
            Animations = parsed,
        };
    }

    private static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s.Replace('\\', '/') : null;
}
