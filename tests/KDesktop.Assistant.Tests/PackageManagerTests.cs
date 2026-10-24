using System.IO.Compression;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 1.7: the package manager checks a .kai without loading it, installs, updates and removes it, and
/// turns down broken ones. Every test uses its own id (the data folder is the temporary one of TestSetup).
/// </summary>
public class PackageManagerTests
{
    private static string DotFolder =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "tests", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila tests/DotAssistant primero");

    private static readonly string Work = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "packages-" + Guid.NewGuid().ToString("N")[..6]);

    /// <summary>A copy of Dot's package with another id (and whatever else the test changes).</summary>
    private static string MakePackage(string id, string version = "1.0.0", Action<JsonObject>? manifest = null,
        Action<ZipArchive>? extra = null, Func<string, bool>? skip = null)
    {
        Directory.CreateDirectory(Work);
        var file = Path.Combine(Work, $"{id}-{version}-{Guid.NewGuid().ToString("N")[..4]}.kai");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var path in Directory.EnumerateFiles(DotFolder, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetRelativePath(DotFolder, path).Replace('\\', '/');
                if (skip?.Invoke(name) == true) continue;
                if (name == AssistantManifest.FileName)
                {
                    var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                    json["id"] = id;
                    json["version"] = version;
                    manifest?.Invoke(json);
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(json.ToJsonString());
                }
                else zip.CreateEntryFromFile(path, name);
            }
            extra?.Invoke(zip);
        }
        return file;
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    private static PackageProblem Rejected(string file) => Assert.Throws<PackageException>(() => AssistantPackages.Inspect(file)).Problem;

    [Fact]
    public void TheBuiltDotPackageIsValid()
    {
        var built = Directory.GetFiles(Path.GetDirectoryName(DotFolder)!, "dot-*.kai").Single();
        var info = AssistantPackages.Inspect(built);
        Assert.Equal("dot", info.Manifest.Id);
        Assert.NotNull(info.Icon);
        Assert.Contains("communicate", info.Gestures);
        Assert.True(info.Size > 0);
    }

    [Fact]
    public void InstallsUpdatesAndRemoves()
    {
        const string id = "dot-life";
        var first = AssistantPackages.Inspect(MakePackage(id));
        Assert.Equal(PackageAction.New, first.Action);
        var installed = AssistantPackages.Install(first);
        Assert.True(File.Exists(Path.Combine(installed.Folder, "lib", "DotAssistant.dll")));
        Assert.Equal(installed.Folder, AssistantPackages.Find(id)?.Folder);
        Assert.Contains(AssistantPackages.Installed(), a => a.Id == id);

        // Its data is kept across versions.
        Directory.CreateDirectory(AssistantPaths.Data(id));
        File.WriteAllText(Path.Combine(AssistantPaths.Data(id), "progress.json"), "{}");

        Assert.Equal(PackageAction.Reinstall, AssistantPackages.Inspect(MakePackage(id)).Action);
        var update = AssistantPackages.Inspect(MakePackage(id, "1.1.0"));
        Assert.Equal(PackageAction.Update, update.Action);
        Assert.Equal("1.0.0", update.Installed?.Version);
        var updated = AssistantPackages.Install(update);
        Assert.Equal("1.1.0", AssistantPackages.Find(id)?.Version);
        Assert.False(Directory.Exists(installed.Folder), "La versión anterior sigue ahí");
        Assert.Single(Directory.GetDirectories(Path.Combine(AssistantPaths.Root, id)));
        Assert.True(File.Exists(Path.Combine(AssistantPaths.Data(id), "progress.json")));

        Assert.Equal(PackageAction.Downgrade, AssistantPackages.Inspect(MakePackage(id, "1.0.5")).Action);
        Assert.Equal(PackageAction.Update, AssistantPackages.Inspect(MakePackage(id, "2.0.0-beta.1")).Action);

        AssistantPackages.Uninstall(id, keepData: true);
        Assert.Null(AssistantPackages.Find(id));
        Assert.False(Directory.Exists(updated.Folder));
        Assert.True(Directory.Exists(AssistantPaths.Data(id)));

        AssistantPackages.Install(AssistantPackages.Inspect(MakePackage(id)));
        AssistantPackages.Uninstall(id, keepData: false);
        Assert.False(Directory.Exists(AssistantPaths.Data(id)));
        Assert.False(Directory.Exists(Path.Combine(AssistantPaths.Root, id)));
    }

    [Fact]
    public void ExtrasOfTheInstalledAssistantsBecomeCommands()
    {
        const string id = "dot-extras";
        AssistantPackages.Install(AssistantPackages.Inspect(MakePackage(id, manifest: m =>
        {
            m["name"] = "Punto";
            m["extras"] = JsonNode.Parse("""[{ "id": "dot.jump", "name": { "es": "Saltar ahora", "en": "Jump now" }, "hotkey": "CTRL + ALT + J" }]""");
        })));
        var extra = AssistantExtras.Find(id, "dot.jump");
        Assert.NotNull(extra);
        Assert.Equal("Punto: Saltar ahora", extra.DisplayName("es"));
        Assert.Equal("Punto: Jump now", extra.DisplayName("en-GB"));
        Assert.Null(AssistantExtras.Find(id, "dot.nope"));
        Assert.Contains(AssistantExtras.All(), e => e.Target == $"{id}/dot.jump");

        // The command the card offers: its name, the suggested hotkey and the action.
        var config = new KDesktop.Core.Model.AppConfig();
        Assert.Empty(AssistantExtras.CommandsFor(config, id, "dot.jump"));
        var command = AssistantExtras.NewCommand(extra, "es", extra.Extra.Hotkey);
        config.Commands.Add(command);
        Assert.Equal("Punto: Saltar ahora", command.Name);
        Assert.Equal([KDesktop.Core.Input.Hotkey.Normalize("CTRL + ALT + J")], command.Hotkeys);
        var action = Assert.IsType<KDesktop.Core.Model.AssistantExtraAction>(Assert.Single(command.Actions));
        Assert.Equal($"{id}/dot.jump", action.Target);
        Assert.Same(command, Assert.Single(AssistantExtras.CommandsFor(config, id, "dot.jump")));
        action.Enabled = false;
        Assert.Empty(AssistantExtras.CommandsFor(config, id, "dot.jump"));

        AssistantPackages.Uninstall(id, keepData: false);
        Assert.Null(AssistantExtras.Find(id, "dot.jump"));
        Assert.Null(KDesktop.Core.Model.AppConfig.ExtraName(id, "dot.jump"));
    }

    [Fact]
    public void FilesInUseAreDeletedLater()
    {
        const string id = "dot-busy";
        var old = AssistantPackages.Install(AssistantPackages.Inspect(MakePackage(id)));
        // As if the assistant process still had its code loaded.
        using (new FileStream(Path.Combine(old.Folder, "lib", "DotAssistant.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Reinstalling the same version goes to a folder of its own; the one in use waits.
            var again = AssistantPackages.Install(AssistantPackages.Inspect(MakePackage(id)));
            Assert.NotEqual(old.Folder, again.Folder);
            Assert.Equal(again.Folder, AssistantPackages.Find(id)?.Folder);
            Assert.True(Directory.Exists(old.Folder));
        }
        AssistantPackages.CleanUp();
        Assert.False(Directory.Exists(old.Folder));
        Assert.Single(AssistantPackages.Installed(), a => a.Id == id);
        AssistantPackages.Uninstall(id, keepData: false);
    }

    [Fact]
    public void AFileChangedAfterItsCardIsNotInstalled()
    {
        var file = MakePackage("dot-changed");
        var info = AssistantPackages.Inspect(file);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Update)) AddText(zip, "assets/more.txt", "x");
        Assert.Equal(PackageProblem.Changed, Assert.Throws<PackageException>(() => AssistantPackages.Install(info)).Problem);
        Assert.Null(AssistantPackages.Find("dot-changed"));
        Assert.Empty(Directory.GetDirectories(AssistantPaths.Root, ".tmp-*"));
    }

    [Fact]
    public void BrokenPackagesAreRejected()
    {
        var notZip = Path.Combine(Work, "text.kai");
        Directory.CreateDirectory(Work);
        File.WriteAllText(notZip, "hola");
        Assert.Equal(PackageProblem.NotAPackage, Rejected(notZip));

        Assert.Equal(PackageProblem.Missing, Rejected(MakePackage("dot-x1", skip: n => n == AssistantManifest.FileName)));
        Assert.Equal(PackageProblem.Missing, Rejected(MakePackage("dot-x2", skip: n => n.EndsWith(".dll", StringComparison.Ordinal))));
        Assert.Equal(PackageProblem.Missing, Rejected(MakePackage("dot-x3", manifest: m => m["voice"] = "voice/dot.json")));
        Assert.Equal(PackageProblem.Missing, Rejected(MakePackage("dot-x4", manifest: m => m["persona"] = "persona/{lang}.json")));
        Assert.Equal(PackageProblem.Invalid, Rejected(MakePackage("dot-x5", manifest: m => m["runAsAdmin"] = true)));
        Assert.Equal(PackageProblem.Invalid, Rejected(MakePackage("dot-x6", manifest: m => m["traits"]!["travel"] = "swim")));
        Assert.Equal(PackageProblem.Sdk, Rejected(MakePackage("dot-x7", manifest: m => m["sdk"] = "2.0")));
        Assert.Equal(PackageProblem.ReservedId, Rejected(MakePackage("data")));
        // K-Desktop is an assistant of the system (5.0.112): no package can take its id.
        Assert.Equal(PackageProblem.ReservedId, Rejected(MakePackage("kdesktop")));
        Assert.Equal(PackageProblem.BadImage, Rejected(MakePackage("dot-x8", skip: n => n == "icon.png",
            extra: z => AddText(z, "icon.png", "no soy una imagen"))));
        Assert.Equal(PackageProblem.Invalid, Rejected(MakePackage("dot-x9", skip: n => n == "settings.json",
            extra: z => AddText(z, "settings.json", """{ "pages": [ { "title": "P", "sections": [ { "items": [ { "type": "web", "key": "x", "label": "X" } ] } ] } ] }"""))));
        Assert.Equal(PackageProblem.Invalid, Rejected(MakePackage("dot-x10", manifest: m => m["persona"] = "persona/{lang}.json",
            extra: z => AddText(z, "persona/es.json", """{ "prompt": "x" }"""))));
        Assert.Equal(PackageProblem.UnsafePath, Rejected(MakePackage("dot-x11", extra: z => AddText(z, "../evil.txt", "x"))));
        Assert.Equal(PackageProblem.UnsafePath, Rejected(MakePackage("dot-x12", extra: z => AddText(z, "assets/CON.txt", "x"))));
        Assert.Equal(PackageProblem.UnsafePath, Rejected(MakePackage("dot-x13", extra: z => AddText(z, "C:/Windows/evil.dll", "x"))));

        // A persona per language, one of them valid, is fine.
        var persona = AssistantPackages.Inspect(MakePackage("dot-ok", manifest: m => m["persona"] = "persona/{lang}.json",
            extra: z => AddText(z, "persona/en.json", """{ "prompt": "A small dot that helps on the desktop." }""")));
        Assert.Equal(PackageAction.New, persona.Action);
        Assert.DoesNotContain(AssistantPackages.Installed(), a => a.Id.StartsWith("dot-x", StringComparison.Ordinal));
    }

    /// <summary>Stage 13.10: the model it suggests to think harder with, and the clips of its showcase (checked, never loaded).</summary>
    [Fact]
    public void ThinkModelsAndShowcaseClipsAreRead()
    {
        static void Bytes(ZipArchive zip, string name, byte[] bytes)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
        byte[] webp = [.. "RIFF"u8, 30, 0, 0, 0, .. "WEBPVP8X"u8, 10, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        void Clips(ZipArchive zip)
        {
            Bytes(zip, "showcase/enter.webp", webp);
            Bytes(zip, "showcase/hop.gif", [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0]);
        }
        void Manifest(JsonObject m)
        {
            m["model"] = new JsonObject { ["suggested"] = "unsloth/Qwen3.5-4B-GGUF:Q4_K_M", ["think"] = new JsonArray("unsloth/Qwen3.5-9B-GGUF:Q4_K_M") };
            m["showcase"] = new JsonArray(
                new JsonObject { ["item"] = "enter.summoned", ["clip"] = "showcase/enter.webp" },
                new JsonObject { ["item"] = "dot.hop", ["clip"] = "showcase/hop.gif", ["name"] = new JsonObject { ["es"] = "Saltar", ["en"] = "Hop" }, ["group"] = "tricks" });
        }

        var info = AssistantPackages.Inspect(MakePackage("dot-clips", manifest: Manifest, extra: Clips));
        Assert.Equal("unsloth/Qwen3.5-4B-GGUF", Assert.Single(info.Manifest.Model!.Models).Repo);
        Assert.Equal("unsloth/Qwen3.5-9B-GGUF", info.Manifest.Model.ThinkModels[0].Repo);
        Assert.Equal(["enter.summoned", "dot.hop"], info.Manifest.Showcase.Select(c => c.Item));
        Assert.Equal("Hop", info.Manifest.Showcase[1].Name!.In("en"));

        // A clip that is not there or is not a picture, the same item twice, and one of its own without a name.
        Assert.Equal(PackageProblem.Missing, Rejected(MakePackage("dot-c1", manifest: Manifest)));
        Assert.Equal(PackageProblem.BadImage, Rejected(MakePackage("dot-c2", manifest: Manifest,
            extra: z => { AddText(z, "showcase/enter.webp", "no soy un clip"); Bytes(z, "showcase/hop.gif", [.. "GIF89a"u8]); })));
        Assert.Equal(PackageProblem.Invalid, Rejected(MakePackage("dot-c3", extra: Clips, manifest: m =>
        {
            Manifest(m);
            m["showcase"]!.AsArray().Add(new JsonObject { ["item"] = "enter.summoned", ["clip"] = "showcase/hop.gif" });
        })));
        Assert.Equal(PackageProblem.Invalid, Rejected(MakePackage("dot-c4", extra: Clips, manifest: m =>
        {
            Manifest(m);
            m["showcase"]![1]!.AsObject().Remove("name");
        })));
        Assert.Equal(PackageProblem.Invalid, Rejected(MakePackage("dot-c5", extra: Clips, manifest: m =>
        {
            Manifest(m);
            m["showcase"]![1]!["group"] = "dancing";
        })));

        // Stage 16.3: the clips are only for the catalog (it reads them from the package): they are not installed.
        var installed = AssistantPackages.Install(info);
        Assert.False(File.Exists(Path.Combine(installed.Folder, "showcase", "enter.webp")));
        Assert.False(File.Exists(Path.Combine(installed.Folder, "showcase", "hop.gif")));
        Assert.True(File.Exists(Path.Combine(installed.Folder, "lib", "DotAssistant.dll")));
        Assert.Equal(2, installed.Manifest.Showcase.Count);
        AssistantPackages.Uninstall("dot-clips", keepData: false);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0.0", "1.0.0-beta.1", 1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.1", 1)]
    [InlineData("0.9.9", "1.0.0-alpha", -1)]
    public void VersionsAreOrdered(string a, string b, int sign)
    {
        Assert.Equal(sign, Math.Sign(AssistantPackages.CompareVersions(a, b)));
        Assert.Equal(-sign, Math.Sign(AssistantPackages.CompareVersions(b, a)));
    }

    [Theory]
    [InlineData("lib/Dot.dll", true)]
    [InlineData("assets/skins/red.png", true)]
    [InlineData("../x", false)]
    [InlineData("lib/../../x", false)]
    [InlineData("/x", false)]
    [InlineData("C:/x", false)]
    [InlineData("lib//x", false)]
    [InlineData("lib/x.", false)]
    [InlineData("lib/nul", false)]
    [InlineData("lib/com1.dll", false)]
    [InlineData("lib/a<b", false)]
    public void PathsArePlainRelativeOnes(string path, bool safe) => Assert.Equal(safe, AssistantPackages.IsSafePath(path));

    [Theory]
    [InlineData("http://example.com/dot.kai")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("not an address")]
    public async Task OnlyHttpsDownloads(string url)
    {
        var ex = await Assert.ThrowsAsync<PackageException>(() => AssistantPackages.DownloadAsync(url, "Dot"));
        Assert.Equal(PackageProblem.NotAPackage, ex.Problem);
    }

    [Fact]
    public void OnlyDownloadsAreForgotten()
    {
        Directory.CreateDirectory(AssistantPackages.DownloadFolder);
        var downloaded = Path.Combine(AssistantPackages.DownloadFolder, $"{Guid.NewGuid():N}{AssistantPackages.Extension}");
        File.WriteAllText(downloaded, "x");
        var mine = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{AssistantPackages.Extension}");
        File.WriteAllText(mine, "x");
        try
        {
            AssistantPackages.ForgetDownload(downloaded);
            AssistantPackages.ForgetDownload(mine);
            Assert.False(File.Exists(downloaded));
            Assert.True(File.Exists(mine)); // a package the user opened is never deleted
        }
        finally
        {
            File.Delete(mine);
        }
    }
}
