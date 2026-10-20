using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;

namespace KDesktop.Views;

/// <summary>An installed assistant as the pages show it: icon, name, version and description.</summary>
public sealed class AssistantRow
{
    public AssistantRow(InstalledAssistant installed, string? active)
    {
        Installed = installed;
        IsActive = installed.Id == active;
        Icon = AssistantLook.Icon(installed);
    }

    public InstalledAssistant Installed { get; }

    public string Id => Installed.Id;

    public string Name => Installed.Manifest.Name;

    public string Byline => Loc.T("assistantCard.by", Installed.Version, Installed.Manifest.Author);

    public string Description => Installed.Manifest.Description?.In(Loc.Instance.Language) ?? string.Empty;

    public bool HasDescription => Description.Length > 0;

    public ImageSource? Icon { get; }

    public bool NoIcon => Icon is null;

    /// <summary>The one on the desktop.</summary>
    public bool IsActive { get; }

    public bool IsInactive => !IsActive;
}

public static class AssistantLook
{
    /// <summary>
    /// The icon of an installed assistant (read into memory: its folder must stay free to be deleted), or null when it
    /// has none or it cannot be read.
    /// </summary>
    public static ImageSource? Icon(InstalledAssistant installed)
    {
        if (installed.Manifest.Icon is not { } icon || !AssistantPackages.IsSafePath(icon)) return null;
        var file = Path.Combine(installed.Folder, icon.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.DecodePixelWidth = 128;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// How the module is doing in a few words, and the brush of its colour (status icon, Options and the panel).
    /// </summary>
    public static (string Text, string Brush) Summary(App app)
    {
        if (app.Assistant is not { } supervisor) return (Loc.T("assistantState.off"), "TextFillColorTertiaryBrush");
        var active = app.Config.Settings.AssistantActive;
        var name = active is null ? null : AssistantPackages.Find(active)?.Manifest.Name ?? active;
        return supervisor.State switch
        {
            AssistantProcessState.Starting => (Loc.T("assistantState.starting"), "Kd.Warning"),
            AssistantProcessState.Restarting => (Loc.T("assistantState.restarting"), "Kd.Warning"),
            AssistantProcessState.Off => (Loc.T("assistantState.stopped"), "Kd.Danger"),
            _ when name is null => (Loc.T("assistantState.noBody"), "Kd.Success"),
            _ when supervisor.LoadedAssistant == active => (Loc.T("assistantState.onDesktop", name), "Kd.Success"),
            _ => (Loc.T("assistantState.loading", name), "Kd.Warning"),
        };
    }

    /// <summary>The installed assistants as rows, the one on the desktop marked.</summary>
    public static List<AssistantRow> Rows(string? active) =>
        [.. AssistantPackages.Installed().Select(a => new AssistantRow(a, active)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
}
