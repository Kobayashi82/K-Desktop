using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Core.Tests;

/// <summary>
/// Stage 9.5: the new version of an installed assistant is read from the package at its <c>updates</c> address
/// without downloading the package (HTTP ranges), and is only offered when it is newer and runs here.
/// </summary>
public class AssistantUpdatesTests
{
    private const string Url = "https://example.com/releases/latest/download/dot.kai";

    private static string Manifest(string id, string version, string sdk = "1.0", string? changes = null)
    {
        var json = new JsonObject
        {
            ["id"] = id,
            ["name"] = "Dot",
            ["version"] = version,
            ["author"] = "K",
            ["sdk"] = sdk,
            ["entry"] = "lib/Dot.dll!Dot.Dot",
            ["traits"] = new JsonObject { ["travel"] = "teleport" },
            ["updates"] = Url,
        };
        if (changes is not null) json["changes"] = new JsonObject { ["es"] = changes, ["en"] = "News" };
        return json.ToJsonString();
    }

    /// <summary>A package of about 2 MB that does not compress: reading it whole would show in the bytes fetched.</summary>
    private static byte[] Package(string manifest)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var noise = new byte[2 << 20];
            new Random(7).NextBytes(noise);
            using (var lib = zip.CreateEntry("lib/Dot.dll", CompressionLevel.NoCompression).Open()) lib.Write(noise);
            using var writer = new StreamWriter(zip.CreateEntry(AssistantManifest.FileName).Open());
            writer.Write(manifest);
        }
        return memory.ToArray();
    }

    private static InstalledAssistant Installed(string version) =>
        new(AssistantManifest.Parse(Manifest("dot", version)), Path.GetTempPath());

    /// <summary>Serves one file, with ranges or (<paramref name="ranges"/> false) always whole.</summary>
    private sealed class FakeServer(byte[]? file, bool ranges = true, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public long Sent { get; private set; }

        public int Requests { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => Answer(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Answer(request));

        private HttpResponseMessage Answer(HttpRequestMessage request)
        {
            Requests++;
            if (file is null || status != HttpStatusCode.OK) return new HttpResponseMessage(file is null ? HttpStatusCode.NotFound : status) { RequestMessage = request };
            if (ranges && request.Headers.Range?.Ranges.SingleOrDefault() is { From: { } from, To: { } to })
            {
                var last = Math.Min(to, file.Length - 1);
                var part = file.AsSpan((int)from, (int)(last - from + 1)).ToArray();
                Sent += part.Length;
                var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { RequestMessage = request, Content = new ByteArrayContent(part) };
                partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, last, file.Length);
                return partial;
            }
            Sent += file.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(file) };
        }
    }

    private static async Task<AssistantUpdateStatus> CheckAsync(FakeServer server, string installed = "1.0.0")
    {
        using var http = new HttpClient(server);
        return await AssistantUpdates.CheckOneAsync(Installed(installed), http);
    }

    [Fact]
    public async Task ANewerVersionIsFoundReadingOnlyAFewKilobytes()
    {
        var server = new FakeServer(Package(Manifest("dot", "1.1.0", changes: "Salta más alto")));
        var status = await CheckAsync(server);

        Assert.Equal(AssistantUpdateResult.Available, status.Result);
        Assert.Equal("1.1.0", status.Latest);
        Assert.Equal("1.0.0", status.Installed);
        Assert.Equal("Salta más alto", status.Changes?.In("es"));
        Assert.True(status.IsNewer);
        Assert.InRange(server.Sent, 1, 256 * 1024);
    }

    [Fact]
    public async Task AServerWithoutRangesSendsThePackageWhole()
    {
        var server = new FakeServer(Package(Manifest("dot", "1.1.0")), ranges: false);
        var status = await CheckAsync(server);

        Assert.Equal(AssistantUpdateResult.Available, status.Result);
        Assert.True(server.Sent > 2_000_000);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.5")]
    public async Task TheSameOrAnOlderVersionIsUpToDate(string published)
    {
        var status = await CheckAsync(new FakeServer(Package(Manifest("dot", published))));
        Assert.Equal(AssistantUpdateResult.UpToDate, status.Result);
        Assert.False(status.IsNewer);
    }

    [Fact]
    public async Task OneThatNeedsANewerSdkAsksForANewerKDesktop()
    {
        var sdk = AssistantPackages.SdkVersion;
        var status = await CheckAsync(new FakeServer(Package(Manifest("dot", "2.0.0", sdk: $"{sdk.Major}.{sdk.Minor + 1}"))));
        Assert.Equal(AssistantUpdateResult.NeedsNewerKDesktop, status.Result);
        Assert.True(status.IsNewer);
    }

    [Fact]
    public async Task AnotherAssistantAtTheAddressIsNotOffered()
    {
        var status = await CheckAsync(new FakeServer(Package(Manifest("blob", "9.0.0"))));
        Assert.Equal(AssistantUpdateResult.Failed, status.Result);
        Assert.Contains("blob", status.Error);
    }

    [Fact]
    public async Task AMissingOrBrokenPackageFailsWithAReason()
    {
        var missing = await CheckAsync(new FakeServer(null));
        Assert.Equal(AssistantUpdateResult.Failed, missing.Result);
        Assert.Equal("HTTP 404", missing.Error);

        var broken = await CheckAsync(new FakeServer("esto no es un zip"u8.ToArray()));
        Assert.Equal(AssistantUpdateResult.Failed, broken.Result);
        Assert.False(string.IsNullOrEmpty(broken.Error));
    }

    [Theory]
    [InlineData("https://example.com/dot.kai", true)]
    [InlineData("http://localhost:8080/dot.kai", true)]
    [InlineData("http://127.0.0.1/dot.kai", true)]
    [InlineData("http://example.com/dot.kai", false)]
    [InlineData("file:///C:/dot.kai", false)]
    [InlineData("dot.kai", false)]
    public void OnlyHttpsOrThisPcIsAPublishAddress(string url, bool allowed) =>
        Assert.Equal(allowed, AssistantPackages.IsPublishAddress(url, out _));

    /// <summary>A package with 2 MB of code, two clips of 1 MB and a voice: the catalog's detail window opens it (stage 16.3).</summary>
    private static byte[] PackageWithClips(byte[] clip)
    {
        var manifest = JsonNode.Parse(Manifest("dot", "1.1.0"))!.AsObject();
        manifest["voice"] = "voice/dot.json";
        manifest["showcase"] = new JsonArray(
            new JsonObject { ["item"] = "react.success", ["clip"] = "showcase/react.success.webp" },
            new JsonObject { ["item"] = "react.failure", ["clip"] = "showcase/react.failure.webp" });
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var noise = new byte[2 << 20];
            new Random(7).NextBytes(noise);
            using (var lib = zip.CreateEntry("lib/Dot.dll", CompressionLevel.NoCompression).Open()) lib.Write(noise);
            foreach (var name in new[] { "react.success", "react.failure" })
                using (var stream = zip.CreateEntry($"showcase/{name}.webp", CompressionLevel.NoCompression).Open()) stream.Write(clip);
            using (var voice = new StreamWriter(zip.CreateEntry("voice/dot.json").Open()))
                voice.Write("""{ "name": "Dot", "engine": { "type": "piper" }, "chain": [] }""");
            using var writer = new StreamWriter(zip.CreateEntry(AssistantManifest.FileName).Open());
            writer.Write(manifest.ToJsonString());
        }
        return memory.ToArray();
    }

    [Fact]
    public async Task ThePublishedPackageIsOpenedAndOnlyTheClipAskedForIsDownloaded()
    {
        var clip = new byte[1 << 20];
        new Random(3).NextBytes(clip);
        var package = PackageWithClips(clip);
        var server = new FakeServer(package);
        using var http = new HttpClient(server);
        using var remote = await RemotePackage.OpenAsync(Url, http, CancellationToken.None);

        Assert.Equal("1.1.0", remote.Manifest.Version);
        Assert.Equal(2, remote.Manifest.Showcase.Count);
        Assert.Equal(package.Length, remote.Size);
        Assert.InRange(server.Sent, 1, 256 * 1024);

        var voice = await remote.ReadVoiceAsync(CancellationToken.None);
        Assert.Equal(("Dot", "piper"), (voice?.Name, voice?.Engine));

        var before = (server.Sent, server.Requests);
        var read = await remote.ReadAsync("showcase/react.failure.webp", 8 << 20, CancellationToken.None);
        Assert.Equal(clip, read);
        // Only that clip (some of it may have come with the directory, a block on each side at most), in very few requests,
        // and never the whole package.
        Assert.InRange(server.Sent - before.Sent, 1, clip.Length + 3 * 64 * 1024);
        Assert.InRange(server.Requests - before.Requests, 1, 3);
        Assert.True(server.Sent < package.Length / 2, $"{server.Sent} de {package.Length}");
        Assert.Null(await remote.ReadAsync("showcase/nothing.webp", 8 << 20, CancellationToken.None));
        Assert.Null(await remote.ReadAsync("showcase/react.success.webp", 1024, CancellationToken.None));
        Assert.Null(await remote.ReadAsync("../outside.webp", 8 << 20, CancellationToken.None));
    }

    [Fact]
    public async Task OnlyAssistantsWithAnAddressAreChecked()
    {
        var without = new InstalledAssistant(AssistantManifest.Parse(Manifest("dot", "1.0.0").Replace(Url, "")) with { Updates = null }, Path.GetTempPath());
        using var http = new HttpClient(new FakeServer(Package(Manifest("dot", "1.1.0"))));
        var found = await AssistantUpdates.CheckAsync([without, Installed("1.0.0")], http);
        Assert.Single(found);
    }
}
