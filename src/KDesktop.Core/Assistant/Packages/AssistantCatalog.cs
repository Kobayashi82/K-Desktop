using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>How an assistant of a catalog is here.</summary>
public enum CatalogState
{
    /// <summary>Not installed: it can be installed.</summary>
    Available,

    /// <summary>Installed, with this same version (or a newer one).</summary>
    Installed,

    /// <summary>Installed, and the catalog has a newer version.</summary>
    Update,

    /// <summary>It needs a newer K-Desktop (a newer SDK).</summary>
    NeedsNewerKDesktop,

    /// <summary>It cannot be read or no K-Desktop from here on runs it (with why).</summary>
    Failed,
}

/// <summary>An assistant of a catalog, as its published package says (only its assistant.json and its icon are read).</summary>
/// <param name="Package">Where its package is published (https).</param>
/// <param name="Catalog">The address of the catalog it came from.</param>
/// <param name="Icon">Its icon (PNG, JPEG…), or null.</param>
/// <param name="Installed">The version installed here, or null.</param>
public sealed record CatalogItem(string Id, string Package, string Catalog, AssistantManifest? Manifest, byte[]? Icon, CatalogState State,
    string? Installed = null, string? Error = null)
{
    public string Name => Manifest?.Name ?? Id;
}

/// <summary>What reading the catalogs gave: their assistants, and the catalogs that could not be read (address → why).</summary>
public sealed record CatalogResult(IReadOnlyList<CatalogItem> Items, IReadOnlyDictionary<string, string> Failed);

/// <summary>
/// Catalogs of assistants (phase 12): a JSON file at an address (https, or http to this PC for testing) listing packages
/// by where they are published: <c>{"catalog": 1, "assistants": [{"id": "lander", "package": "https://…/lander.kai"}]}</c>
/// (<c>docs/assistant/esquemas/catalog.schema.json</c>). Of each package only its assistant.json and its icon are read,
/// with HTTP ranges (like the updates): the name, description, version and what it needs come from the package itself,
/// never from the list. Installing one is the usual card, after downloading it.
/// </summary>
public static class AssistantCatalog
{
    /// <summary>The catalog of K-Desktop's own assistants (the K-Desktop-Assistants repository).</summary>
    public const string Official = "https://raw.githubusercontent.com/Kobayashi82/K-Desktop-Assistants/main/catalog.json";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private const int MaxCatalog = 256 * 1024;
    private const int MaxEntries = 200;
    private const int AtOnce = 6;

    /// <summary>The entries of a catalog file: id → package address (the first one of a repeated id wins).</summary>
    public static IReadOnlyList<(string Id, string Package)> Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { throw new InvalidDataException(Loc.T("assistantCatalog.badList", ex.Message)); }
        if (root?["assistants"] is not JsonArray list) throw new InvalidDataException(Loc.T("assistantCatalog.badList", "assistants"));
        var entries = new List<(string, string)>();
        foreach (var item in list.Take(MaxEntries))
        {
            if (item?["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) || !AssistantManifest.IsValidId(id) || AssistantPaths.Reserved.Contains(id)) continue;
            if (item["package"] is not JsonValue urlValue || !urlValue.TryGetValue<string>(out var url) || !AssistantPackages.IsPublishAddress(url, out _)) continue;
            if (entries.Any(e => e.Item1.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            entries.Add((id, url.Trim()));
        }
        return entries;
    }

    /// <summary>Reads every catalog and each of their packages (never throws, except when <paramref name="ct"/> is cancelled).</summary>
    public static async Task<CatalogResult> LoadAsync(IEnumerable<string> catalogs, Func<string, InstalledAssistant?> installed, HttpClient http,
        CancellationToken ct = default)
    {
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Id, string Package, string Catalog)>();
        foreach (var catalog in catalogs.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var (id, package) in await ReadListAsync(catalog, http, ct).ConfigureAwait(false))
                    if (!entries.Any(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) entries.Add((id, package, catalog));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Warn($"Asistentes: no se ha podido leer el catálogo {catalog} ({ex.Message})");
                failed[catalog] = Describe(ex);
            }
        }

        using var gate = new SemaphoreSlim(AtOnce);
        var items = await Task.WhenAll(entries.Select(async e =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try { return await ReadItemAsync(e.Id, e.Package, e.Catalog, installed(e.Id), http, ct).ConfigureAwait(false); }
            finally { gate.Release(); }
        })).ConfigureAwait(false);
        return new CatalogResult(items, failed);
    }

    private static async Task<IReadOnlyList<(string Id, string Package)>> ReadListAsync(string url, HttpClient http, CancellationToken ct)
    {
        if (!AssistantPackages.IsPublishAddress(url, out var uri)) throw new ArgumentException(Loc.T("assistantUpdates.badUrl"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxCatalog) throw new InvalidDataException(Loc.T("assistantCatalog.badList", "size"));
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        if (bytes.Length > MaxCatalog) throw new InvalidDataException(Loc.T("assistantCatalog.badList", "size"));
        return Parse(System.Text.Encoding.UTF8.GetString(bytes));
    }

    private static async Task<CatalogItem> ReadItemAsync(string id, string package, string catalog, InstalledAssistant? installed, HttpClient http, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var (manifest, icon, fetched) = await AssistantUpdates.ReadPackageAsync(package, http, withIcon: true, timeout.Token).ConfigureAwait(false);
            Log.Info($"Asistentes: catálogo, «{manifest.Id}» {manifest.Version} ({fetched / 1024.0:0.#} KB leídos de {package})");
            if (!manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return new CatalogItem(id, package, catalog, null, null, CatalogState.Failed, installed?.Version, Loc.T("assistantUpdates.otherId", manifest.Id));
            var sdk = AssistantPackages.SdkVersion;
            var newer = installed is null || AssistantPackages.CompareVersions(manifest.Version, installed.Version) > 0;
            var state = !manifest.RunsOn(sdk)
                ? manifest.SdkMajor > sdk.Major || (manifest.SdkMajor == sdk.Major && manifest.SdkMinor > sdk.Minor)
                    ? (newer ? CatalogState.NeedsNewerKDesktop : CatalogState.Installed)
                    : CatalogState.Failed
                : installed is null ? CatalogState.Available
                : newer ? CatalogState.Update
                : CatalogState.Installed;
            var error = state == CatalogState.Failed ? Loc.T("assistantPkg.sdk", manifest.Name, manifest.Sdk, sdk.Major) : null;
            return new CatalogItem(manifest.Id, package, catalog, manifest, icon, state, installed?.Version, error);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warn($"Asistentes: catálogo, no se ha podido leer «{id}» en {package} ({ex.Message})");
            return new CatalogItem(id, package, catalog, null, null, CatalogState.Failed, installed?.Version, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => Loc.T("assistantCatalog.notFound"),
        HttpRequestException { StatusCode: { } code } => $"HTTP {(int)code}",
        HttpRequestException => Loc.T("updates.noConnection"),
        OperationCanceledException => Loc.T("updates.timeout"),
        InvalidDataException or ManifestException => ex.Message,
        _ => ex.Message,
    };
}
