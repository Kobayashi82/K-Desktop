using System.Net.Http;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using KDesktop.Views.Dialogs;

namespace KDesktop;

/// <summary>
/// The catalog of assistants (phase 12, Options → Catalog): what the catalogs of <c>assistantCatalogs</c> publish, read
/// when the page is opened (and kept 10 minutes) or with «Volver a mirar». Installing or updating one downloads its package
/// and opens the usual card, where the user decides; nothing is installed by itself.
/// </summary>
public partial class App
{
    private static readonly TimeSpan CatalogKept = TimeSpan.FromMinutes(10);

    private static readonly Lazy<HttpClient> CatalogHttp = new(() =>
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"K-Desktop/{Core.Updates.UpdateService.CurrentVersion.ToString(3)}");
        return http;
    });

    private readonly HashSet<string> _catalogDownloading = new(StringComparer.OrdinalIgnoreCase);
    private string _catalogRead = string.Empty;

    /// <summary>What the catalogs had the last time (null: not read yet).</summary>
    public CatalogResult? AssistantCatalogResult { get; private set; }

    public DateTime? AssistantCatalogRead { get; private set; }

    public bool LoadingAssistantCatalog { get; private set; }

    /// <summary>Raised (UI thread) when the catalog starts or ends loading, a package is downloading, or one is installed.</summary>
    public event Action? AssistantCatalogChanged;

    public bool IsDownloadingFromCatalog(string id) => _catalogDownloading.Contains(id);

    /// <summary>Reads the catalogs (again with <paramref name="force"/>; otherwise only when they changed or 10 minutes went by).</summary>
    public async Task LoadAssistantCatalogAsync(bool force)
    {
        var catalogs = Config.Settings.AssistantCatalogs;
        var key = string.Join("\n", catalogs);
        if (LoadingAssistantCatalog) return;
        if (!force && AssistantCatalogResult is not null && key == _catalogRead && DateTime.Now - AssistantCatalogRead < CatalogKept) return;
        LoadingAssistantCatalog = true;
        AssistantCatalogChanged?.Invoke();
        try
        {
            AssistantCatalogResult = await AssistantCatalog.LoadAsync(catalogs, AssistantPackages.Find, CatalogHttp.Value);
            AssistantCatalogRead = DateTime.Now;
            _catalogRead = key;
        }
        catch (Exception ex)
        {
            Log.Error("Asistentes: no se ha podido leer el catálogo", ex);
        }
        finally
        {
            LoadingAssistantCatalog = false;
            AssistantCatalogChanged?.Invoke();
        }
    }

    /// <summary>Downloads it and shows its card (install, or update); one that needs a newer K-Desktop is only said so.</summary>
    public async Task OpenFromCatalogAsync(CatalogItem item)
    {
        if (item.State == CatalogState.NeedsNewerKDesktop)
        {
            Ui.ShowOsd(Loc.T("assistantUpdates.needsKDesktop", item.Name, item.Manifest?.Version ?? "?"), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
            return;
        }
        if (item.State is not (CatalogState.Available or CatalogState.Update) || !_catalogDownloading.Add(item.Id)) return;
        AssistantCatalogChanged?.Invoke();
        try
        {
            var file = await AssistantPackages.DownloadAsync(item.Package, item.Name);
            AssistantPackageDialog.Open(this, _main, file, downloaded: true);
        }
        catch (PackageException ex)
        {
            Ui.ShowOsd(ex.Message, Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        }
        finally
        {
            _catalogDownloading.Remove(item.Id);
            AssistantCatalogChanged?.Invoke();
        }
    }

    /// <summary>Adds a catalog (false, and why, when the address is not valid or is already there).</summary>
    public bool AddAssistantCatalog(string url, out string? why)
    {
        url = url.Trim();
        why = !AssistantPackages.IsPublishAddress(url, out _) ? Loc.T("assistantCatalog.badUrl")
            : Config.Settings.AssistantCatalogs.Contains(url, StringComparer.OrdinalIgnoreCase) ? Loc.T("assistantCatalog.already")
            : null;
        if (why is not null) return false;
        Config.Settings.AssistantCatalogs = [.. Config.Settings.AssistantCatalogs, url];
        _ = LoadAssistantCatalogAsync(force: true);
        return true;
    }

    public void RemoveAssistantCatalog(string url)
    {
        Config.Settings.AssistantCatalogs = [.. Config.Settings.AssistantCatalogs.Where(c => !c.Equals(url, StringComparison.OrdinalIgnoreCase))];
        _ = LoadAssistantCatalogAsync(force: true);
    }

    /// <summary>After installing or removing one: the catalog says how each one is here now, without asking the servers again.</summary>
    private void FollowCatalog()
    {
        if (AssistantCatalogResult is not { } result) return;
        var items = result.Items.Select(i =>
        {
            if (i.Manifest is not { } manifest || i.State is CatalogState.Failed or CatalogState.NeedsNewerKDesktop) return i;
            var installed = AssistantPackages.Find(i.Id);
            var state = installed is null ? CatalogState.Available
                : AssistantPackages.CompareVersions(manifest.Version, installed.Version) > 0 ? CatalogState.Update
                : CatalogState.Installed;
            return i with { State = state, Installed = installed?.Version };
        }).ToList();
        AssistantCatalogResult = result with { Items = items };
        AssistantCatalogChanged?.Invoke();
    }
}
