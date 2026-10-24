using System.IO.Compression;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Phase 12: assistants without code (a character.json and its pictures), with Tic (<see cref="TestCharacter"/>); and Pip,
/// the SDK's example, stays a valid package.
/// </summary>
public class CharacterTests
{
    private static readonly string Work = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "characters-" + Guid.NewGuid().ToString("N")[..6]);

    /// <summary>A package's folder (Tic's unless another is given) as a .kai, leaving out what <paramref name="skip"/> says.</summary>
    private static string Pack(Func<string, bool>? skip = null, string? folder = null)
    {
        folder ??= TestCharacter.Folder;
        Directory.CreateDirectory(Work);
        var file = Path.Combine(Work, $"{Path.GetFileName(folder)}-{Guid.NewGuid().ToString("N")[..4]}.kai");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (skip?.Invoke(name) != true) zip.CreateEntryFromFile(path, name);
        }
        return file;
    }

    [Fact]
    public void TicIsAValidPackage()
    {
        Assert.Empty(Contracts.ValidateFile("character", Path.Combine(TestCharacter.Folder, "character.json")));
        Assert.Empty(Contracts.ValidateFile("assistant", Path.Combine(TestCharacter.Folder, "assistant.json")));
        var info = AssistantPackages.Inspect(Pack());
        Assert.True(info.Manifest.IsCharacter);
        Assert.Equal(TestCharacter.Id, info.Manifest.Id);
    }

    /// <summary>Pip, the SDK's example (and the catalog's), stays a package K-Desktop takes.</summary>
    [Fact]
    public void TheSampleIsAValidPackage()
    {
        Assert.Empty(Contracts.ValidateFile("character", Path.Combine(TestCharacter.Sample, "character.json")));
        Assert.Empty(Contracts.ValidateFile("assistant", Path.Combine(TestCharacter.Sample, "assistant.json")));
        var info = AssistantPackages.Inspect(Pack(folder: TestCharacter.Sample));
        Assert.True(info.Manifest.IsCharacter);
        Assert.Equal("pip", info.Manifest.Id);
        // Its pictures leave room under the feet, and it says so (or it floats over the taskbar).
        Assert.True(CharacterFile.Parse(File.ReadAllText(Path.Combine(TestCharacter.Sample, "character.json"))).Ground > 0);
    }

    [Fact]
    public void AMissingFrameIsRejected()
    {
        var problem = Assert.Throws<PackageException>(() => AssistantPackages.Inspect(Pack(name => name == "frames/sad1.png"))).Problem;
        Assert.Equal(PackageProblem.Missing, problem);
    }

    [Fact]
    public void TheCharacterReadsItsAnimationsAndFallsBack()
    {
        var tic = CharacterFile.Parse(File.ReadAllText(Path.Combine(TestCharacter.Folder, "character.json")));
        Assert.Equal(80, tic.Height);
        Assert.Equal(4, tic.Ground);
        Assert.Equal(6, tic.For("walk").FrameCount);
        Assert.True(tic.For("walk").Strip);
        Assert.False(tic.For("happy").Loop);
        Assert.True(tic.For("talk").Loop);
        Assert.Equal("sounds/pop.wav", tic.For("click").Sound);
        Assert.Equal(0, CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["a.png"] } } }""").Ground);

        // Without click or catch: the fallbacks.
        var happy = CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["a.png"] }, "happy": { "frames": ["b.png"] } } }""");
        Assert.Same(happy.For("happy"), happy.For("click"));
        Assert.Same(happy.VariantsFor("happy"), happy.VariantsFor("catch"));

        var tiny = CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["a.png"] } } }""");
        Assert.Same(tiny.For("idle"), tiny.For("think"));
        Assert.Same(tiny.For("idle"), tiny.For("drag"));
        Assert.Throws<ManifestException>(() => CharacterFile.Parse("""{ "animations": { "walk": { "frames": ["a.png"] } } }"""));
        Assert.Throws<ManifestException>(() => CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["../fuera.png"] } } }"""));
    }

    /// <summary>Stage 13.11: an animation can be a list of variants, with a weight, a rest and (entrance and exit) a reason.</summary>
    [Fact]
    public void AnimationsCanHaveVariants()
    {
        var tic = CharacterFile.Parse(File.ReadAllText(Path.Combine(TestCharacter.Folder, "character.json")));
        Assert.Equal(4, tic.VariantsFor("idle").Count);
        Assert.Equal((8.0, 0.0), (tic.VariantsFor("idle")[0].Weight, tic.VariantsFor("idle")[0].Cooldown));
        Assert.Equal(20, tic.VariantsFor("idle")[1].Cooldown);
        Assert.Same(tic.VariantsFor("idle")[0], tic.For("idle"));
        Assert.Contains("frames/hum0.png", tic.Files);
        // An entrance and an exit for each reason; none for a reason it does not have.
        Assert.Equal(["frames/wake0.png", "frames/rise0.png"], tic.VariantsFor("enter", "startup").Select(v => v.Frames[0]));
        Assert.Equal(["frames/drop0.png", "frames/pop0.png"], tic.VariantsFor("enter", "summoned").Select(v => v.Frames[0]));
        Assert.Equal(2, tic.VariantsFor("exit", "dismissed").Count);
        Assert.Equal("frames/sink0.png", Assert.Single(tic.VariantsFor("exit", "uninstalling")).Frames[0]);
        var anyReason = CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["a.png"] }, "enter": [ { "frames": ["b.png"] } ] } }""");
        Assert.Single(anyReason.VariantsFor("enter", "summoned"));
        Assert.Empty(anyReason.VariantsFor("exit", "dismissed"));

        // «when» only on enter and exit, and only their own reasons; at most 16 variants.
        Assert.Throws<ManifestException>(() => CharacterFile.Parse("""{ "animations": { "idle": [ { "frames": ["a.png"], "when": ["startup"] } ] } }"""));
        Assert.Throws<ManifestException>(() => CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["a.png"] }, "enter": [ { "frames": ["b.png"], "when": ["dismissed"] } ] } }"""));
        Assert.Throws<ManifestException>(() => CharacterFile.Parse("""{ "animations": { "idle": [] } }"""));
        var many = string.Join(",", Enumerable.Repeat("""{ "frames": ["a.png"] }""", 17));
        Assert.Throws<ManifestException>(() => CharacterFile.Parse($$"""{ "animations": { "idle": [ {{many}} ] } }"""));
        Assert.NotEmpty(Contracts.Validate("character", """{ "animations": { "idle": [ { "frames": ["a.png"], "weight": 0 } ] } }"""));
        Assert.Empty(Contracts.Validate("character", """{ "animations": { "idle": [ { "frames": ["a.png"], "weight": 2, "cooldown": 5 } ], "exit": [ { "frames": ["b.png"], "when": ["dismissed", "uninstalling"] } ] } }"""));
    }

    [Fact]
    public void TheHostLoadsItWithoutCode()
    {
        var package = PackageLoader.Load(TestCharacter.Folder, Path.Combine(Work, "data"));
        Assert.Null(package.Context);
        Assert.Equal("CharacterAssistant", package.Assistant.GetType().Name);
        package.Assistant.Dispose();
    }
}
