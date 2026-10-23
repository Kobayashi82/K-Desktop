using System.IO.Compression;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Tests;

/// <summary>Phase 12: assistants without code (a character.json and its pictures), with Pip, the example.</summary>
public class CharacterTests
{
    private static string PipFolder => Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package");

    private static readonly string Work = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "characters-" + Guid.NewGuid().ToString("N")[..6]);

    /// <summary>Pip's folder as a .kai (leaving out what <paramref name="skip"/> says).</summary>
    private static string Pack(Func<string, bool>? skip = null)
    {
        Directory.CreateDirectory(Work);
        var file = Path.Combine(Work, $"pip-{Guid.NewGuid().ToString("N")[..4]}.kai");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var path in Directory.EnumerateFiles(PipFolder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(PipFolder, path).Replace('\\', '/');
            if (skip?.Invoke(name) != true) zip.CreateEntryFromFile(path, name);
        }
        return file;
    }

    [Fact]
    public void PipIsAValidPackage()
    {
        Assert.Empty(Contracts.ValidateFile("character", Path.Combine(PipFolder, "character.json")));
        Assert.Empty(Contracts.ValidateFile("assistant", Path.Combine(PipFolder, "assistant.json")));
        var info = AssistantPackages.Inspect(Pack());
        Assert.True(info.Manifest.IsCharacter);
        Assert.Equal("pip", info.Manifest.Id);
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
        var pip = CharacterFile.Parse(File.ReadAllText(Path.Combine(PipFolder, "character.json")));
        Assert.Equal(80, pip.Height);
        Assert.Equal(6, pip.For("walk").FrameCount);
        Assert.True(pip.For("walk").Strip);
        Assert.False(pip.For("happy").Loop);
        Assert.True(pip.For("talk").Loop);
        // Pip has no click or catch: the fallbacks.
        Assert.Same(pip.For("happy"), pip.For("click"));
        Assert.Same(pip.VariantsFor("happy"), pip.VariantsFor("catch"));

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
        var pip = CharacterFile.Parse(File.ReadAllText(Path.Combine(PipFolder, "character.json")));
        Assert.Equal(2, pip.VariantsFor("idle").Count);
        Assert.Equal((6.0, 0.0), (pip.VariantsFor("idle")[0].Weight, pip.VariantsFor("idle")[0].Cooldown));
        Assert.Equal(20, pip.VariantsFor("idle")[1].Cooldown);
        Assert.Same(pip.VariantsFor("idle")[0], pip.For("idle"));
        Assert.Contains("frames/wiggle0.png", pip.Files);
        // An entrance and an exit for each reason; none for a reason it does not have.
        Assert.Equal("frames/wake0.png", Assert.Single(pip.VariantsFor("enter", "startup")).Frames[0]);
        Assert.Equal("frames/drop0.png", Assert.Single(pip.VariantsFor("enter", "summoned")).Frames[0]);
        Assert.Equal("frames/sink0.png", Assert.Single(pip.VariantsFor("exit", "uninstalling")).Frames[0]);
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
        var package = PackageLoader.Load(Path.GetFullPath(PipFolder), Path.Combine(Work, "data"));
        Assert.Null(package.Context);
        Assert.Equal("CharacterAssistant", package.Assistant.GetType().Name);
        package.Assistant.Dispose();
    }
}
