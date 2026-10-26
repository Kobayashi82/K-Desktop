using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using KDesktop.Views.Dialogs;

namespace KDesktop;

/// <summary>
/// Stage 9.5: new versions of the installed assistants, checked with K-Desktop's own updates (at startup, every 6 hours
/// and with «Comprobar»). They are only offered (a notice, the banner, Options → Updates and the row in Options →
/// Assistants); installing one is always the user's choice, with its card, even in the «Auto» mode of K-Desktop.
/// </summary>
public partial class App
{
    private IReadOnlyList<AssistantUpdateStatus> _assistantUpdates = [];
    private readonly HashSet<string> _assistantUpdatesNotified = [];
    private readonly HashSet<string> _assistantUpdatesDownloading = [];

    /// <summary>What the last check found, one per installed assistant that says where it is published.</summary>
    public IReadOnlyList<AssistantUpdateStatus> AssistantUpdateStatuses => _assistantUpdates;

    public bool CheckingAssistantUpdates { get; private set; }

    public DateTime? AssistantUpdatesChecked { get; private set; }

    /// <summary>Raised (UI thread) when a check starts or ends, an update is skipped or downloaded, or one is installed.</summary>
    public event Action? AssistantUpdatesChanged;

    private void RaiseAssistantUpdates()
    {
        AssistantUpdatesChanged?.Invoke();
        _main?.ShowBanners();
    }

    /// <summary>The newer version of <paramref name="id"/> the last check found, or null.</summary>
    public AssistantUpdateStatus? AssistantUpdateFor(string id) => _assistantUpdates.FirstOrDefault(s => s.Id == id && s.IsNewer);

    /// <summary>Whether the user said not to be told about this version again.</summary>
    public bool IsSkipped(AssistantUpdateStatus status) =>
        status.Latest is { } latest && Config.Settings.AssistantSkippedUpdates.GetValueOrDefault(status.Id) == latest;

    /// <summary>The versions to tell the user about: installable here and not skipped.</summary>
    public IReadOnlyList<AssistantUpdateStatus> PendingAssistantUpdates =>
        Config.Settings.AssistantEnabled
            ? [.. _assistantUpdates.Where(s => s.Result == AssistantUpdateResult.Available && !IsSkipped(s))]
            : [];

    public bool IsDownloadingAssistantUpdate(string id) => _assistantUpdatesDownloading.Contains(id);

    /// <summary>
    /// Checks the assistants' addresses (only with the module on: off, K-Desktop is the one of always). When it was not
    /// asked for, a new version is told once per run with a notice.
    /// </summary>
    public async Task CheckAssistantUpdatesAsync(bool manual)
    {
        if (!Config.Settings.AssistantEnabled || CheckingAssistantUpdates) return;
        var installed = AssistantPackages.Installed();
        if (!installed.Any(a => !string.IsNullOrWhiteSpace(a.Manifest.Updates)))
        {
            _assistantUpdates = [];
            return;
        }

        CheckingAssistantUpdates = true;
        RaiseAssistantUpdates();
        try
        {
            _assistantUpdates = await AssistantUpdates.CheckAsync(installed);
            AssistantUpdatesChecked = DateTime.Now;
        }
        catch (Exception ex)
        {
            Log.Error("Asistentes: no se han podido comprobar sus actualizaciones", ex);
        }
        finally
        {
            CheckingAssistantUpdates = false;
        }
        RaiseAssistantUpdates();
        if (manual) return;

        var fresh = PendingAssistantUpdates.Where(s => _assistantUpdatesNotified.Add($"{s.Id}/{s.Latest}")).ToList();
        if (fresh.Count == 1) NotifyUpdate(Loc.T("assistantUpdates.osdOne", fresh[0].Name, fresh[0].Latest!));
        else if (fresh.Count > 1) NotifyUpdate(Loc.T("assistantUpdates.osdMany", fresh.Count));
    }

    /// <summary>Not told about <paramref name="status"/>'s version again (or told again: <paramref name="skip"/> false).</summary>
    public void SkipAssistantUpdate(AssistantUpdateStatus status, bool skip)
    {
        var skipped = new Dictionary<string, string>(Config.Settings.AssistantSkippedUpdates, StringComparer.Ordinal);
        if (skip && status.Latest is { } latest) skipped[status.Id] = latest;
        else skipped.Remove(status.Id);
        Config.Settings.AssistantSkippedUpdates = skipped;
        RaiseAssistantUpdates();
    }

    /// <summary>
    /// Downloads the new version of <paramref name="id"/> and shows its card, where the user installs it (or not). One
    /// that needs a newer K-Desktop is only said so.
    /// </summary>
    public async Task OpenAssistantUpdateAsync(string id)
    {
        if (AssistantUpdateFor(id) is not { } status || !_assistantUpdatesDownloading.Add(id)) return;
        if (status.Result == AssistantUpdateResult.NeedsNewerKDesktop)
        {
            _assistantUpdatesDownloading.Remove(id);
            Ui.ShowOsd(Loc.T("assistantUpdates.needsKDesktop", status.Name, status.Latest!), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
            return;
        }
        RaiseAssistantUpdates();
        try
        {
            var file = await AssistantPackages.DownloadAsync(status.Url, status.Name);
            AssistantPackageDialog.Open(this, _main, file, downloaded: true);
        }
        catch (PackageException ex)
        {
            Ui.ShowOsd(ex.Message, Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
        }
        finally
        {
            _assistantUpdatesDownloading.Remove(id);
            RaiseAssistantUpdates();
        }
    }

    /// <summary>
    /// After installing or removing an assistant: what was found is kept up to date without asking the server again
    /// (an installed new version leaves it up to date; a removed one leaves the list).
    /// </summary>
    private void FollowInstalledAssistants()
    {
        if (_assistantUpdates.Count == 0) return;
        var now = _assistantUpdates
            .Select(s => AssistantPackages.Find(s.Id) is not { } installed ? null
                : installed.Version == s.Installed ? s
                : s.Latest is { } latest && AssistantPackages.CompareVersions(installed.Version, latest) >= 0
                    ? s with { Installed = installed.Version, Result = AssistantUpdateResult.UpToDate, Changes = null }
                    : s with { Installed = installed.Version })
            .OfType<AssistantUpdateStatus>()
            .ToList();
        if (now.SequenceEqual(_assistantUpdates)) return;
        _assistantUpdates = now;
        RaiseAssistantUpdates();
    }
}
