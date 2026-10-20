using KDesktop.Core.Model;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>One extra of an installed assistant, as «Asistente: acción» offers it.</summary>
public sealed record InstalledExtra(InstalledAssistant Assistant, ManifestExtra Extra)
{
    /// <summary>The value of <see cref="AssistantExtraAction.Target"/> that runs it.</summary>
    public string Target => $"{Assistant.Id}/{Extra.Id}";

    /// <summary>«Lander: Modo manual», in <paramref name="language"/>.</summary>
    public string DisplayName(string language) => $"{Assistant.Manifest.Name}: {Extra.Name.In(language)}";
}

/// <summary>
/// The extras of the installed assistants (their manifests' <c>extras</c>), which «Asistente: acción» runs, and the
/// commands that already run each one.
/// </summary>
public static class AssistantExtras
{
    /// <summary>Every extra of every installed assistant, by assistant name and then in the manifest's order.</summary>
    public static IReadOnlyList<InstalledExtra> All() =>
        [.. AssistantPackages.Installed()
            .OrderBy(a => a.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(a => a.Manifest.Extras.Select(e => new InstalledExtra(a, e)))];

    /// <summary>One extra of an installed assistant, or null when the assistant is not installed or no longer has it.</summary>
    public static InstalledExtra? Find(string assistant, string extra) =>
        AssistantPackages.Find(assistant) is { } installed && installed.Manifest.Extras.FirstOrDefault(e => e.Id == extra) is { } found
            ? new InstalledExtra(installed, found)
            : null;

    /// <summary>The commands with an enabled «Asistente: acción» for this extra (the simplest first: fewest actions).</summary>
    public static IEnumerable<Command> CommandsFor(AppConfig config, string assistant, string extra) =>
        config.Commands
            .Where(c => c.Actions.OfType<AssistantExtraAction>().Any(a => a.Enabled && a.Assistant == assistant && a.Extra == extra))
            .OrderBy(c => c.Actions.Count);

    /// <summary>A new command that runs an extra («Lander: Modo manual», with its hotkey if any).</summary>
    public static Command NewCommand(InstalledExtra extra, string language, string? hotkey)
    {
        var name = extra.DisplayName(language);
        var command = new Command { Name = name };
        if (!string.IsNullOrWhiteSpace(hotkey)) command.Hotkeys.Add(Input.Hotkey.Normalize(hotkey));
        command.Actions.Add(new AssistantExtraAction { Assistant = extra.Assistant.Id, Extra = extra.Extra.Id, ExtraName = name });
        return command;
    }
}
