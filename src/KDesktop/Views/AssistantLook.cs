using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using KDesktop.Services;
using Wpf.Ui.Controls;

namespace KDesktop.Views;

/// <summary>A choice of wake word for an assistant (null id: none).</summary>
public sealed record WakeOption(string? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A model to think with, for one assistant (stage 3.2): <paramref name="Value"/> empty is the general one.</summary>
public sealed record ModelOption(string Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>An installed assistant as the pages show it: icon, name, version, description and whether it is on the desktop.</summary>
public sealed class AssistantRow
{
    public AssistantRow(InstalledAssistant installed, App app)
    {
        Installed = installed;
        IsOnDesktop = app.IsOnDesktop(installed.Id);
        Icon = AssistantLook.Icon(installed);
        WakeWordId = app.Config.Settings.AssistantWakeWords.GetValueOrDefault(installed.Id);
        ModelName = app.Config.Settings.AssistantModels.GetValueOrDefault(installed.Id) ?? string.Empty;
    }

    public InstalledAssistant Installed { get; }

    public string Id => Installed.Id;

    public string Name => Installed.Manifest.Name;

    public string Byline => Loc.T("assistantCard.by", Installed.Version, Installed.Manifest.Author);

    public string Description => Installed.Manifest.Description?.In(Loc.Instance.Language) ?? string.Empty;

    public bool HasDescription => Description.Length > 0;

    /// <summary>Whether it brings settings of its own (settings.json).</summary>
    public bool HasSettings => AssistantLook.HasSettings(Installed);

    public ImageSource? Icon { get; }

    public bool NoIcon => Icon is null;

    public bool IsOnDesktop { get; }

    public bool IsOffDesktop => !IsOnDesktop;

    /// <summary>Its wake word (id of a recorded one), or null.</summary>
    public string? WakeWordId { get; }

    /// <summary>The wake words to choose from (none first; one of another assistant says whose it is). Filled by the list.</summary>
    public IReadOnlyList<WakeOption> WakeOptions { get; set; } = [];

    public WakeOption? SelectedWake => WakeOptions.FirstOrDefault(o => o.Id == WakeWordId);

    /// <summary>Its own model (stage 3.2), or empty: the general one.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>The models to choose from (the general one first). Filled by the list.</summary>
    public IReadOnlyList<ModelOption> ModelOptions { get; set; } = [];

    public ModelOption? SelectedModel => ModelOptions.FirstOrDefault(o => string.Equals(o.Value, ModelName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether there is a brain to choose a model of (not «no AI»).</summary>
    public bool HasBrain { get; set; }

    /// <summary>The model it suggests when Ollama does not have it (offered, never downloaded without asking), or null.</summary>
    public string? SuggestedModel { get; set; }

    public bool HasSuggestedModel => SuggestedModel is not null;

    /// <summary>Its offer: «Suggests X» with the button, or that it is downloading.</summary>
    public string SuggestedModelText { get; set; } = string.Empty;

    public bool CanDownloadSuggested { get; set; }

    /// <summary>The name it suggests to be called by (its manifest's wakeWord, or its name).</summary>
    public string SuggestedWakeWord => Installed.Manifest.WakeWord is { Length: > 0 } word ? word : Name;

    public override string ToString() => Name;
}

public static class AssistantLook
{
    /// <summary>
    /// The icon of an installed assistant (read into memory: its folder must stay free to be deleted), or null when it
    /// has none or it cannot be read.
    /// </summary>
    /// <summary>Whether an assistant brings settings of its own (its settings.json is there; it is not read).</summary>
    public static bool HasSettings(InstalledAssistant installed) =>
        AssistantPackages.IsSafePath(installed.Manifest.SettingsFile)
        && File.Exists(Path.Combine(installed.Folder, installed.Manifest.SettingsFile.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>A text of a package in K-Desktop's language (empty for none).</summary>
    public static string Text(LocalizedText? text) => text?.In(Loc.Instance.Language) ?? string.Empty;

    public static ImageSource? Icon(InstalledAssistant installed) =>
        installed.Manifest.Icon is { } icon ? Image(installed, icon, 128) : null;

    /// <summary>
    /// An image of a package (a path inside it: a gallery, an achievement…), read into memory, or null when the path is
    /// not safe or it cannot be read.
    /// </summary>
    public static ImageSource? Image(InstalledAssistant installed, string path, int decodeWidth)
    {
        if (path.Length == 0 || !AssistantPackages.IsSafePath(path)) return null;
        var file = Path.Combine(installed.Folder, path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file);
            image.DecodePixelWidth = decodeWidth;
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
    /// The symbol of a K-Desktop icon in a package («kd:Settings», «kd:Speaker2»: a Fluent symbol, with or without its
    /// size), or null for anything else.
    /// </summary>
    public static SymbolRegular? Symbol(string? icon)
    {
        if (icon is null || !icon.StartsWith(KdIconSpec.Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var name = icon[KdIconSpec.Prefix.Length..].Split(':')[0];
        foreach (var candidate in new[] { name + "24", name })
            if (Enum.TryParse<SymbolRegular>(candidate, ignoreCase: true, out var symbol) && !int.TryParse(candidate, out _)) return symbol;
        return null;
    }

    /// <summary>
    /// How the module is doing in a few words, and the brush of its colour (status icon, Options and the panel).
    /// </summary>
    public static (string Text, string Brush) Summary(App app)
    {
        if (ProcessSummary(app) is { } process) return process;
        var supervisor = app.Assistant!;
        var onDesktop = app.AssistantsOnDesktop;
        if (onDesktop.Count == 0) return (Loc.T("assistantState.noBody"), "Kd.Success");
        if (onDesktop.FirstOrDefault(id => !supervisor.IsLoaded(id)) is { } loading)
            return (Loc.T("assistantState.loading", App.AssistantName(loading)), "Kd.Warning");
        return onDesktop.Count == 1
            ? (Loc.T("assistantState.onDesktop", App.AssistantName(onDesktop[0])), "Kd.Success")
            : (Loc.T("assistantState.onDesktopMany", string.Join(", ", onDesktop.Select(App.AssistantName))), "Kd.Success");
    }

    /// <summary>How one assistant is doing (the hero of Panel → Assistant).</summary>
    public static (string Text, string Brush) Summary(App app, string id)
    {
        if (ProcessSummary(app) is { } process) return process;
        var name = App.AssistantName(id);
        if (!app.IsOnDesktop(id)) return (Loc.T("assistantState.notOnDesktop", name), "TextFillColorTertiaryBrush");
        return app.Assistant!.IsLoaded(id)
            ? (Loc.T("assistantState.onDesktop", name), "Kd.Success")
            : (Loc.T("assistantState.loading", name), "Kd.Warning");
    }

    /// <summary>The module off, or its process not running: that says it all.</summary>
    private static (string Text, string Brush)? ProcessSummary(App app)
    {
        if (app.Assistant is not { } supervisor) return (Loc.T("assistantState.off"), "TextFillColorTertiaryBrush");
        return supervisor.State switch
        {
            AssistantProcessState.Starting => (Loc.T("assistantState.starting"), "Kd.Warning"),
            AssistantProcessState.Restarting => (Loc.T("assistantState.restarting"), "Kd.Warning"),
            AssistantProcessState.Off => (Loc.T("assistantState.stopped"), "Kd.Danger"),
            _ => null,
        };
    }

    /// <summary>The installed assistants as rows, those on the desktop marked.</summary>
    public static List<AssistantRow> Rows(App app) =>
        [.. AssistantPackages.Installed().Select(a => new AssistantRow(a, app)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
}
