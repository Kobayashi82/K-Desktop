using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 1.6: the packages of Dot and of the template are valid, and the manifest reader agrees with the schemas.</summary>
public class ManifestTests
{
    private static string Root => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", ".."));

    public static TheoryData<string, string> PackageFiles => new()
    {
        { "assistant", "samples/DotAssistant/Package/assistant.json" },
        { "settings", "samples/DotAssistant/Package/settings.json" },
        { "panel", "samples/DotAssistant/Package/panel.json" },
        { "assistant", "templates/kdassistant/Package/assistant.json" },
    };

    [Theory]
    [MemberData(nameof(PackageFiles))]
    public void ShippedPackagesAreValid(string schema, string file)
    {
        var errors = Contracts.ValidateFile(schema, Path.Combine(Root, file));
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void TheLanderExampleReads()
    {
        var manifest = AssistantManifest.Load(Contracts.LanderPackage);
        Assert.Equal("lander", manifest.Id);
        Assert.Equal(("lib/Lander.Assistant.dll", "Lander.Assistant.LanderAssistant"), manifest.EntryParts);
        Assert.Equal(1, manifest.SdkMajor);
        Assert.Equal(new Version(1, 0, 0), manifest.ParsedVersion);
        Assert.Equal("fly", manifest.Traits.Travel);
        Assert.Contains("pressButton", manifest.Traits.Gestures);
        Assert.Equal("Modo manual", manifest.Extras.Single(e => e.Id == "lander.manual").Name.In("es"));
        Assert.Equal("Manual mode", manifest.Extras.Single(e => e.Id == "lander.manual").Name.In("en-GB"));
        Assert.Equal("Modo manual", manifest.Extras.Single(e => e.Id == "lander.manual").Name.In("de")); // not there: the first
    }

    [Theory]
    [InlineData("""{"id":"Dot","name":"D","version":"1.0.0","author":"a","sdk":"1.0","entry":"lib/D.dll!D.D","traits":{"travel":"fly"}}""", "id")]
    [InlineData("""{"id":"dot","name":"D","version":"1.0","author":"a","sdk":"1.0","entry":"lib/D.dll!D.D","traits":{"travel":"fly"}}""", "versión")]
    [InlineData("""{"id":"dot","name":"D","version":"1.0.0","author":"a","sdk":"1.0","entry":"D.dll","traits":{"travel":"fly"}}""", "entrada")]
    [InlineData("""{"id":"dot","name":"D","version":"1.0.0","author":"a","sdk":"1.0","entry":"lib/D.dll!D.D"}""", "no es válido")]
    public void BadManifestsSayWhy(string json, string says) =>
        Assert.Contains(says, Assert.Throws<ManifestException>(() => AssistantManifest.Parse(json)).Message);

    /// <summary>SDK 1.1 (stage 2.3): a package built against a newer minor version needs a newer K-Desktop.</summary>
    [Theory]
    [InlineData("1.0", 1, 1, true)]
    [InlineData("1.1", 1, 1, true)]
    [InlineData("1", 1, 0, true)]
    [InlineData("1.2", 1, 1, false)]
    [InlineData("1.9", 1, 10, true)]
    [InlineData("1.10", 1, 9, false)]
    [InlineData("2.0", 1, 9, false)]
    [InlineData("1.1", 2, 0, false)]
    public void APackageRunsOnTheSameMajorAndAnEqualOrNewerMinor(string sdk, int major, int minor, bool runs)
    {
        var manifest = AssistantManifest.Parse($$$"""{"id":"dot","name":"D","version":"1.0.0","author":"a","sdk":"{{{sdk}}}","entry":"lib/D.dll!D.D","traits":{"travel":"fly"}}""");
        Assert.Equal(runs, manifest.RunsOn(new Version(major, minor)));
    }

    [Fact]
    public void ThisHostGivesSdk113() => Assert.Equal(new Version(1, 13), AssistantPackages.SdkVersion);

    [Fact]
    public void SettingDefaultsComeFromTheSettingsFile()
    {
        var defaults = AssistantSettingsValues.Defaults(File.ReadAllText(Path.Combine(Root, "samples/DotAssistant/Package/settings.json")));
        Assert.Equal(28, defaults["size"]!.GetValue<int>());
        Assert.Equal("blue", defaults["color"]!.GetValue<string>());
        Assert.True(defaults["wander"]!.GetValue<bool>());
    }
}
