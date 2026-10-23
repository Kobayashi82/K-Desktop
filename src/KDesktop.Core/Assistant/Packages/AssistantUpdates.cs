using System.IO.Compression;
using System.Net.Http;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>What checking an assistant's <c>updates</c> address found.</summary>
public enum AssistantUpdateResult
{
    UpToDate,
    Available,

    /// <summary>There is a newer version, but it needs a newer SDK than this K-Desktop has: update K-Desktop first.</summary>
    NeedsNewerKDesktop,

    Failed,
}

/// <summary>An installed assistant that says where it is published, and what its address has now.</summary>
public sealed record AssistantUpdateStatus(string Id, string Name, string Installed, string Url, AssistantUpdateResult Result,
    string? Latest = null, LocalizedText? Changes = null, string? Error = null)
{
    /// <summary>Whether there is a version to offer (also when it needs a newer K-Desktop: it is said so).</summary>
    public bool IsNewer => Result is AssistantUpdateResult.Available or AssistantUpdateResult.NeedsNewerKDesktop;
}

/// <summary>
/// Stage 9.5: whether the installed assistants have a newer version at the <c>updates</c> address of their manifest.
/// Only the published package's assistant.json is read (HTTP ranges: a few KB; the whole package only from a server
/// that does not serve ranges). Nothing is downloaded to be installed here, and nothing is ever updated without asking:
/// K-Desktop offers it and the user installs it with its card.
/// </summary>
public static class AssistantUpdates
{
    /// <summary>Longest wait for each assistant's address.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private const long MaxManifest = 1 << 20;

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"K-Desktop/{Updates.UpdateService.CurrentVersion.ToString(3)}");
        return http;
    });

    /// <summary>Checks every installed assistant that says where it is published, at once.</summary>
    public static async Task<IReadOnlyList<AssistantUpdateStatus>> CheckAsync(IEnumerable<InstalledAssistant> installed,
        HttpClient? http = null, CancellationToken ct = default)
    {
        var checks = installed.Where(a => !string.IsNullOrWhiteSpace(a.Manifest.Updates))
                              .Select(a => CheckOneAsync(a, http ?? Http.Value, ct));
        return await Task.WhenAll(checks).ConfigureAwait(false);
    }

    /// <summary>Checks one assistant (never throws, except when <paramref name="ct"/> is cancelled).</summary>
    public static async Task<AssistantUpdateStatus> CheckOneAsync(InstalledAssistant assistant, HttpClient http, CancellationToken ct = default)
    {
        var manifest = assistant.Manifest;
        var url = manifest.Updates!.Trim();
        AssistantUpdateStatus Status(AssistantUpdateResult result, AssistantManifest? latest = null, string? error = null) =>
            new(assistant.Id, manifest.Name, assistant.Version, url, result, latest?.Version, latest?.Changes, error);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var (latest, fetched) = await ReadManifestAsync(url, http, timeout.Token).ConfigureAwait(false);
            Log.Info($"Asistentes: «{assistant.Id}» {assistant.Version}, publicado {latest.Version} ({fetched / 1024.0:0.#} KB leídos de {url})");
            if (latest.Id != assistant.Id) return Status(AssistantUpdateResult.Failed, error: Loc.T("assistantUpdates.otherId", latest.Id));
            if (AssistantPackages.CompareVersions(latest.Version, assistant.Version) <= 0) return Status(AssistantUpdateResult.UpToDate, latest);
            var sdk = AssistantPackages.SdkVersion;
            if (latest.RunsOn(sdk)) return Status(AssistantUpdateResult.Available, latest);
            // A newer SDK: a newer K-Desktop runs it. An older major one: no K-Desktop from here on will.
            return latest.SdkMajor > sdk.Major || (latest.SdkMajor == sdk.Major && latest.SdkMinor > sdk.Minor)
                ? Status(AssistantUpdateResult.NeedsNewerKDesktop, latest)
                : Status(AssistantUpdateResult.Failed, latest, Loc.T("assistantPkg.sdk", latest.Name, latest.Sdk, sdk.Major));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"Asistentes: no se ha podido comprobar «{assistant.Id}» en {url} ({ex.Message})");
            return Status(AssistantUpdateResult.Failed, error: Describe(ex));
        }
    }

    /// <summary>
    /// Reads the assistant.json of the package published at <paramref name="url"/>, and how many bytes that took.
    /// Throws when the address is not allowed, cannot be reached or is not a package.
    /// </summary>
    public static async Task<(AssistantManifest Manifest, long Fetched)> ReadManifestAsync(string url, HttpClient http, CancellationToken ct)
    {
        var (manifest, _, fetched) = await ReadPackageAsync(url, http, withIcon: false, ct).ConfigureAwait(false);
        return (manifest, fetched);
    }

    /// <summary>
    /// The assistant.json of the package published at <paramref name=url/> and, with <paramref name=withIcon/>, the
    /// bytes of its icon (null without one, or when it is not safe or too big), and how many bytes that took.
    /// </summary>
    public static async Task<(AssistantManifest Manifest, byte[]? Icon, long Fetched)> ReadPackageAsync(string url, HttpClient http, bool withIcon, CancellationToken ct)
    {
        if (!AssistantPackages.IsPublishAddress(url, out var uri)) throw new ArgumentException(Loc.T("assistantUpdates.badUrl"));
        Stream package;
        var ranges = await HttpRangeStream.OpenAsync(http, uri, ct).ConfigureAwait(false);
        if (ranges is not null) package = ranges;
        else package = await WholeAsync(http, uri, ct).ConfigureAwait(false);
        using (package)
        {
            var (manifest, icon) = await Task.Run(() => ReadFrom(package, withIcon), ct).ConfigureAwait(false);
            return (manifest, icon, ranges?.Fetched ?? package.Length);
        }
    }

    /// <summary>A server without ranges: the package into memory (as big as a package can be).</summary>
    private static async Task<MemoryStream> WholeAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > AssistantPackages.MaxSize) throw new InvalidDataException(Loc.T("assistantUpdates.notPackage"));
        var memory = new MemoryStream();
        var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > AssistantPackages.MaxSize) throw new InvalidDataException(Loc.T("assistantUpdates.notPackage"));
                memory.Write(buffer, 0, read);
            }
        }
        memory.Position = 0;
        return memory;
    }

    private const long MaxIcon = 512 * 1024;

    private static (AssistantManifest Manifest, byte[]? Icon) ReadFrom(Stream package, bool withIcon)
    {
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry? Find(string path) =>
            zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase));
        var entry = Find(AssistantManifest.FileName) ?? throw new InvalidDataException(Loc.T("assistantUpdates.notPackage"));
        if (entry.Length > MaxManifest) throw new InvalidDataException(Loc.T("assistantUpdates.notPackage"));
        AssistantManifest manifest;
        using (var reader = new StreamReader(entry.Open())) manifest = AssistantManifest.Parse(reader.ReadToEnd());
        if (!withIcon || manifest.Icon is not { } path || !AssistantPackages.IsSafePath(path) || Find(path) is not { Length: > 0 and <= MaxIcon } icon)
            return (manifest, null);
        using var memory = new MemoryStream();
        using (var stream = icon.Open()) stream.CopyTo(memory);
        return (manifest, memory.ToArray());
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"HTTP {(int)code}",
        HttpRequestException => Loc.T("updates.noConnection"),
        OperationCanceledException => Loc.T("updates.timeout"),
        InvalidDataException or ManifestException => Loc.T("assistantUpdates.notPackage"),
        _ => ex.Message,
    };
}
