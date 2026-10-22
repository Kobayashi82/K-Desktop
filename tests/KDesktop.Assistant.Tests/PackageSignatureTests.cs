using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 12: signed packages. A package signed with the publisher's key is whole and «verificado» when K-Desktop knows the
/// key; changing, adding or taking out any file after signing breaks it, and a broken one is not installed. An update of
/// a signed assistant that is not signed by the same key says so.
/// </summary>
public sealed class PackageSignatureTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "signature-" + Guid.NewGuid().ToString("N")[..6]);
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public PackageSignatureTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_work, true); } catch (IOException) { }
    }

    private static string DotFolder =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila samples/DotAssistant primero");

    private TrustedPublisher Me => new("Pruebas", Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()));

    /// <summary>A copy of the built Dot package, with another id and version if asked.</summary>
    private string Dot(string id = "dot", string version = "1.2.0")
    {
        var built = Directory.GetFiles(Path.GetDirectoryName(DotFolder)!, "dot-*.kai").Single();
        var file = Path.Combine(_work, $"{id}-{version}-{Guid.NewGuid().ToString("N")[..4]}.kai");
        File.Copy(built, file);
        if (id != "dot" || version != "1.2.0")
            Change(file, zip =>
            {
                var entry = zip.GetEntry(AssistantManifest.FileName)!;
                JsonObject json;
                using (var reader = new StreamReader(entry.Open())) json = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
                entry.Delete();
                json["id"] = id;
                json["version"] = version;
                Write(zip, AssistantManifest.FileName, json.ToJsonString());
            });
        return file;
    }

    private static void Change(string file, Action<ZipArchive> change)
    {
        using var zip = ZipFile.Open(file, ZipArchiveMode.Update);
        change(zip);
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    [Fact]
    public void ASignedPackageIsWholeAndVerifiedByAKnownKey()
    {
        var file = Dot();
        Assert.Equal(SignatureState.NotSigned, PackageSignature.Check(file, [Me]).State);
        PackageSignature.Sign(file, _key, "Pruebas");

        var known = PackageSignature.Check(file, [Me]);
        Assert.Equal(SignatureState.Verified, known.State);
        Assert.Equal("Pruebas", known.Publisher);
        Assert.Equal(Me.Fingerprint, known.Fingerprint);
        // Whoever K-Desktop does not know is only who it says it is.
        var unknown = PackageSignature.Check(file, []);
        Assert.Equal(SignatureState.Unknown, unknown.State);
        Assert.Equal("Pruebas", unknown.Publisher);

        // Signing again replaces the signature (one signature.json).
        PackageSignature.Sign(file, _key, "Pruebas");
        using var zip = ZipFile.OpenRead(file);
        Assert.Single(zip.Entries, e => e.FullName == PackageSignature.FileName);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("signature")]
    public void AnythingDoneAfterSigningBreaksIt(string what)
    {
        var file = Dot();
        PackageSignature.Sign(file, _key, "Pruebas");
        Change(file, zip =>
        {
            switch (what)
            {
                case "changed":
                    zip.GetEntry("assistant.json")!.Delete();
                    Write(zip, "assistant.json", File.ReadAllText(Path.Combine(DotFolder, "assistant.json")).Replace("\"Dot\"", "\"Dót\"", StringComparison.Ordinal));
                    break;
                case "added":
                    Write(zip, "lib/extra.dll", "MZ");
                    break;
                case "removed":
                    zip.GetEntry("lib/DotAssistant.pdb")!.Delete();
                    break;
                case "signature":
                    // Another publisher's name over the same files: the signature no longer matches.
                    var entry = zip.GetEntry(PackageSignature.FileName)!;
                    string text;
                    using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
                    entry.Delete();
                    Write(zip, PackageSignature.FileName, text.Replace("\"Pruebas\"", "\"Otro\"", StringComparison.Ordinal));
                    break;
            }
        });
        Assert.Equal(SignatureState.Broken, PackageSignature.Check(file, [Me]).State);
        Assert.Equal(PackageProblem.BadSignature, Assert.Throws<PackageException>(() => AssistantPackages.Inspect(file)).Problem);
    }

    [Fact]
    public void AnUpdateNotSignedByTheSameKeySaysSo()
    {
        const string id = "dot-signed";
        var first = Dot(id, "1.0.0");
        PackageSignature.Sign(first, _key, "Pruebas");
        var card = AssistantPackages.Inspect(first);
        Assert.Equal(SignatureState.Unknown, card.Signature.State);
        Assert.False(card.SignerChanged);
        var installed = AssistantPackages.Install(card);
        Assert.True(File.Exists(Path.Combine(installed.Folder, PackageSignature.FileName)));
        try
        {
            // Signed by the same key: nothing to say.
            var same = Dot(id, "1.1.0");
            PackageSignature.Sign(same, _key, "Pruebas");
            Assert.False(AssistantPackages.Inspect(same).SignerChanged);

            // Unsigned, or by someone else: it is said.
            Assert.True(AssistantPackages.Inspect(Dot(id, "1.1.0")).SignerChanged);
            using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var stranger = Dot(id, "1.1.0");
            PackageSignature.Sign(stranger, other, "Pruebas");
            Assert.True(AssistantPackages.Inspect(stranger).SignerChanged);
        }
        finally
        {
            AssistantPackages.Uninstall(id, keepData: false);
        }
    }

    [Fact]
    public void KDesktopTrustsItsAuthor() =>
        Assert.Contains(PackageSignature.Trusted, p => p.Name == "Kobayashi82" && p.Fingerprint == "2a596fe1d9e39921ee030e1e00673417");
}
