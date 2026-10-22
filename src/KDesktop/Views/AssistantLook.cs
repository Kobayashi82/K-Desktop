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

/// <summary>
/// An assistant as the pages show it: icon, name, version, description and whether it is on the desktop. K-Desktop
/// (5.0.112) is one too, without a package (<see cref="Installed"/> null): of the system, it is not removed.
/// </summary>
public sealed class AssistantRow
{
    public AssistantRow(InstalledAssistant installed, App app) : this(installed.Id, app)
    {
        Installed = installed;
        Icon = AssistantLook.Icon(installed);
    }

    private AssistantRow(string id, App app)
    {
        Id = id;
        IsOnDesktop = app.IsOnDesktop(id);
        // How it is called, in a few words: its word and its key to talk (5.0.113).
        var word = app.Config.Settings.AssistantWakeWords.GetValueOrDefault(id) is { } wordId
            ? app.Voice.WakeWords.Words.FirstOrDefault(w => w.Id == wordId)?.Name : null;
        var key = app.TalkHotkeyOf(id);
        CallText = (word, key.Length > 0 ? key : null) switch
        {
            ({ } w, { } k) => Loc.T("assistantOptions.callBoth", w, k),
            ({ } w, null) => Loc.T("assistantOptions.callWord", w),
            (null, { } k) => Loc.T("assistantOptions.callKey", k),
            _ => Loc.T("assistantOptions.callNone"),
        };
    }

    /// <summary>K-Desktop as an assistant.</summary>
    public static AssistantRow System(App app) => new(AssistantTargeting.KDesktop, app) { Icon = AssistantLook.SystemIcon };

    /// <summary>Its package; null for K-Desktop.</summary>
    public InstalledAssistant? Installed { get; }

    public string Id { get; }

    public bool IsSystem => Installed is null;

    /// <summary>Only the assistants of a package are removed.</summary>
    public bool CanRemove => !IsSystem;

    public string Name => Installed?.Manifest.Name ?? "K-Desktop";

    public string Byline => Installed is { } installed ? Loc.T("assistantCard.by", installed.Version, installed.Manifest.Author) : Loc.T("assistantSystem.byline");

    public string Description => Installed is { } installed ? installed.Manifest.Description?.In(Loc.Instance.Language) ?? string.Empty : Loc.T("assistantSystem.description");

    public bool HasDescription => Description.Length > 0;

    /// <summary>«Poner en el escritorio» / «Esconder»; K-Desktop, without a figure, is made active or not.</summary>
    public string PutText => Loc.T(IsSystem ? "assistantSystem.activate" : "assistantOptions.putOnDesktop");

    public string HideText => Loc.T(IsSystem ? "assistantSystem.deactivate" : "assistantOptions.hide");

    public string OnDesktopText => Loc.T(IsSystem ? "assistantSystem.active" : "assistantOptions.onDesktop");

    /// <summary>Where it is, in two words (Panel → Assistant's list).</summary>
    public string StateText => IsOnDesktop ? OnDesktopText : Loc.T(IsSystem ? "assistantSystem.inactive" : "assistantPanel.hidden");

    public ImageSource? Icon { get; private init; }

    public bool NoIcon => Icon is null;

    public bool IsOnDesktop { get; }

    public bool IsOffDesktop => !IsOnDesktop;

    /// <summary>How it is called: «Se le llama «Dot» o con F8», «Sin palabra ni tecla».</summary>
    public string CallText { get; }

    /// <summary>The line under its name in the list: its version (or that it is K-Desktop's) and how it is called.</summary>
    public string Line => $"{Byline} · {CallText}";

    /// <summary>The name of its button to open its card (screen readers).</summary>
    public string SettingsName => Loc.T("assistantSettings.title", Name);

    /// <summary>The name it suggests to be called by (its manifest's wakeWord, or its name).</summary>
    public string SuggestedWakeWord => Installed?.Manifest.WakeWord is { Length: > 0 } word ? word : Name;

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

    /// <summary>K-Desktop's own icon, as an assistant.</summary>
    public static ImageSource? SystemIcon => IconProvider.Asset("KDesktop");

    /// <summary>The icon of an assistant by its id (K-Desktop's for the system one), or null.</summary>
    public static ImageSource? IconOf(string? id) =>
        id is null or "" || AssistantTargeting.IsSystem(id) ? SystemIcon : AssistantPackages.Find(id) is { } installed ? Icon(installed) : null;

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
        if (onDesktop.Count == 0) return (Loc.T("assistantState.noBody"), "Kd.Warning");
        if (onDesktop.FirstOrDefault(id => !AssistantTargeting.IsSystem(id) && !supervisor.IsLoaded(id)) is { } loading)
            return (Loc.T("assistantState.loading", App.AssistantName(loading)), "Kd.Warning");
        return onDesktop.Count == 1
            ? (OnDesktopText(onDesktop[0]), "Kd.Success")
            : (Loc.T("assistantState.onDesktopMany", string.Join(", ", onDesktop.Select(App.AssistantName))), "Kd.Success");
    }

    /// <summary>How one assistant is doing (the hero of Panel → Assistant).</summary>
    public static (string Text, string Brush) Summary(App app, string id)
    {
        if (ProcessSummary(app) is { } process) return process;
        var name = App.AssistantName(id);
        var system = AssistantTargeting.IsSystem(id);
        if (!app.IsOnDesktop(id)) return (Loc.T(system ? "assistantState.systemInactive" : "assistantState.notOnDesktop", name), "TextFillColorTertiaryBrush");
        return system || app.Assistant!.IsLoaded(id)
            ? (OnDesktopText(id), "Kd.Success")
            : (Loc.T("assistantState.loading", name), "Kd.Warning");
    }

    /// <summary>«X está en el escritorio»; K-Desktop, without a figure, «está activo».</summary>
    private static string OnDesktopText(string id) =>
        Loc.T(AssistantTargeting.IsSystem(id) ? "assistantState.systemActive" : "assistantState.onDesktop", App.AssistantName(id));

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

    /// <summary>The assistants as rows, K-Desktop first and then the installed ones, those on the desktop marked.</summary>
    public static List<AssistantRow> Rows(App app) =>
        [AssistantRow.System(app), .. AssistantPackages.Installed().Select(a => new AssistantRow(a, app)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
}
