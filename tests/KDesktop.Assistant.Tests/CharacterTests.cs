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
        // Pip has no click, enter or exit: the fallbacks.
        Assert.Same(pip.For("happy"), pip.For("click"));
        Assert.Same(pip.For("idle"), pip.For("enter"));

        var tiny = CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["a.png"] } } }""");
        Assert.Same(tiny.For("idle"), tiny.For("think"));
        Assert.Same(tiny.For("idle"), tiny.For("drag"));
        Assert.Throws<ManifestException>(() => CharacterFile.Parse("""{ "animations": { "walk": { "frames": ["a.png"] } } }"""));
        Assert.Throws<ManifestException>(() => CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["../fuera.png"] } } }"""));
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
