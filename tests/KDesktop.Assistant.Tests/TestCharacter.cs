using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Tic, the assistant without code of the tests: made the first time a test asks for it, in a folder of its own under the
/// temporary folder (its JSON, a plain picture for each frame and a short sound for each sound). Pip, in
/// <c>samples/CharacterAssistant</c>, is the SDK's example and the catalog's: the tests do not depend on its pictures or
/// its animations (only <see cref="CharacterTests"/> checks that it stays a valid package).
/// </summary>
internal static partial class TestCharacter
{
    public const string Id = "tic";

    private static readonly Lock Gate = new();
    private static string? _folder;

    /// <summary>The package's folder (made once per run).</summary>
    public static string Folder
    {
        get
        {
            lock (Gate) return _folder ??= Make();
        }
    }

    /// <summary>Pip's folder, the SDK's example.</summary>
    public static string Sample => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package"));

    private const string Manifest = """
        {
          "id": "tic",
          "name": "Tic",
          "version": "1.0.0",
          "author": "K-Desktop tests",
          "description": { "en": "The assistant without code of the tests." },
          "license": "MIT",
          "sdk": "1.21",
          "entry": "character.json",
          "traits": { "travel": "walk", "travelSpeed": 70, "canMoveWindows": false, "canReachMaximized": false, "gestures": ["communicate", "react", "announce"] }
        }
        """;

    private const string Character = """
        {
          "height": 80,
          "ground": 4,
          "walkSpeed": 60,
          "animations": {
            "idle": [
              { "frames": ["frames/idle0.png", "frames/idle1.png"], "fps": 2.5, "weight": 8 },
              { "frames": ["frames/look0.png", "frames/look1.png"], "fps": 2.5, "cooldown": 20 },
              { "frames": ["frames/yawn0.png", "frames/yawn1.png"], "fps": 2.5, "weight": 0.6, "cooldown": 60 },
              { "frames": ["frames/hum0.png", "frames/hum1.png"], "fps": 3, "weight": 0.8, "cooldown": 40 }
            ],
            "walk": { "strip": "frames/walk.png", "frames": 6, "fps": 10 },
            "talk": { "frames": ["frames/talk0.png", "frames/talk1.png"], "fps": 8 },
            "listen": { "frames": ["frames/listen0.png"], "fps": 3 },
            "think": { "frames": ["frames/think0.png", "frames/think1.png"], "fps": 2 },
            "happy": { "frames": ["frames/happy0.png", "frames/happy1.png", "frames/happy0.png"], "fps": 8, "sound": "sounds/boing.wav" },
            "sad": { "frames": ["frames/sad0.png", "frames/sad1.png"], "fps": 3 },
            "surprised": { "frames": ["frames/surprised0.png", "frames/surprised1.png"], "fps": 6 },
            "click": { "frames": ["frames/squish0.png", "frames/squish1.png"], "fps": 9, "sound": "sounds/pop.wav" },
            "announce": { "frames": ["frames/hey0.png", "frames/hey1.png", "frames/hey1.png"], "fps": 6, "sound": "sounds/ding.wav" },
            "catch": { "frames": ["frames/gulp0.png", "frames/gulp1.png"], "fps": 5, "sound": "sounds/gulp.wav" },
            "drag": { "frames": ["frames/drag0.png", "frames/drag1.png"], "fps": 5 },
            "fall": { "frames": ["frames/fall0.png", "frames/fall1.png"], "fps": 8 },
            "sleep": { "frames": ["frames/sleep0.png", "frames/sleep1.png"], "fps": 1 },
            "enter": [
              { "frames": ["frames/wake0.png", "frames/wake1.png", "frames/wake2.png"], "fps": 4, "when": ["startup"] },
              { "frames": ["frames/rise0.png", "frames/rise1.png", "frames/rise2.png"], "fps": 5, "when": ["startup"] },
              { "frames": ["frames/drop0.png", "frames/drop1.png", "frames/drop2.png"], "fps": 8, "when": ["summoned"], "sound": "sounds/whoosh.wav" },
              { "frames": ["frames/pop0.png", "frames/pop1.png", "frames/pop2.png"], "fps": 6, "when": ["summoned"], "sound": "sounds/pop.wav" }
            ],
            "exit": [
              { "frames": ["frames/bye0.png", "frames/bye1.png", "frames/bye2.png"], "fps": 5, "when": ["dismissed"] },
              { "frames": ["frames/away0.png", "frames/away1.png", "frames/away2.png"], "fps": 7, "when": ["dismissed"], "sound": "sounds/whoosh.wav" },
              { "frames": ["frames/sink0.png", "frames/sink1.png", "frames/sink2.png"], "fps": 4, "when": ["uninstalling"] }
            ]
          }
        }
        """;

    private const int Side = 48;

    private static string Make()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "tic-" + Environment.ProcessId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(Path.Combine(folder, "frames"));
        Directory.CreateDirectory(Path.Combine(folder, "sounds"));
        File.WriteAllText(Path.Combine(folder, "assistant.json"), Manifest);
        File.WriteAllText(Path.Combine(folder, "character.json"), Character);
        var index = 0;
        foreach (var frame in Frames().Matches(Character).Select(m => m.Value).Distinct())
            Png(Path.Combine(folder, frame), frame.EndsWith("walk.png", StringComparison.Ordinal) ? 6 : 1, index++);
        foreach (var sound in Sounds().Matches(Character).Select(m => m.Value).Distinct())
            Wav(Path.Combine(folder, sound));
        return folder;
    }

    /// <summary>A square of one colour per frame (so that frames differ), with the transparent rows under its feet that «ground» says.</summary>
    private static void Png(string path, int count, int index)
    {
        var width = Side * count;
        var pixels = new byte[width * Side * 4];
        var colour = Color.FromRgb((byte)(40 + index * 37 % 200), (byte)(90 + index * 53 % 150), (byte)(160 + index * 29 % 90));
        for (var y = 0; y < Side - 4; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x % Side is < 6 or > Side - 7 || y < 8) continue;
                var at = (y * width + x) * 4;
                (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (colour.B, colour.G, colour.R, 255);
            }
        }
        var bitmap = BitmapSource.Create(width, Side, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>A tenth of a second of a quiet tone (16-bit mono, 22 050 Hz).</summary>
    private static void Wav(string path)
    {
        const int rate = 22050, samples = rate / 10;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 2000));
    }

    [GeneratedRegex(@"frames/\w+\.png")]
    private static partial Regex Frames();

    [GeneratedRegex(@"sounds/\w+\.wav")]
    private static partial Regex Sounds();
}
