using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Services;

namespace KDesktop;

/// <summary>
/// «Asistente: acción» (stage 2.6): the extras of each assistant as K-Desktop actions, so they can have a hotkey, go in
/// a QuickMenu or the launcher, or be said. Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>How long an extra waits for its assistant to come out onto the desktop.</summary>
    private static readonly TimeSpan PendingExtraTime = TimeSpan.FromSeconds(20);

    /// <summary>Extras for an assistant that was not loaded yet: sent when it says it is (one per assistant, the last).</summary>
    private readonly Dictionary<string, (string Extra, DateTime At)> _pendingExtras = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs an extra from a command. One whose assistant is not on the desktop brings it out and is sent once it is
    /// loaded; with the module off, or the assistant or its extra not installed, an OSD says so.
    /// </summary>
    private void RunExtraFromCommand(string id, string extra)
    {
        if (extra.Length == 0) return;
        if (_assistant is null)
        {
            Ui.ShowOsd(Loc.T("assistantExtra.moduleOff"), Core.Engine.OsdKind.Warning);
            return;
        }
        if (AssistantExtras.Find(id, extra) is null)
        {
            Ui.ShowOsd(AssistantPackages.Find(id) is null
                ? Loc.T("assistantExtra.notInstalled", id)
                : Loc.T("assistantExtra.unknown", AssistantName(id), extra), Core.Engine.OsdKind.Warning);
            return;
        }
        if (IsAssistantLoaded(id))
        {
            RunAssistantExtra(id, extra);
            return;
        }
        // Not there yet (hidden, coming out, or its process starting again): it does it as soon as it is loaded.
        Log.Info($"Asistente: extra «{extra}» de «{id}» en espera de que salga");
        _pendingExtras[id] = (extra, DateTime.UtcNow);
        if (!IsOnDesktop(id)) ShowAssistant(id);
    }

    /// <summary>An assistant said it is loaded (or that it could not be): the extra it was waiting for goes now.</summary>
    private void SendPendingExtra(LoadedMessage loaded)
    {
        if (!_pendingExtras.Remove(loaded.Assistant, out var pending) || !loaded.Ok) return;
        if (DateTime.UtcNow - pending.At > PendingExtraTime)
        {
            Log.Info($"Asistente: el extra «{pending.Extra}» de «{loaded.Assistant}» llegó tarde y no se manda");
            return;
        }
        RunAssistantExtra(loaded.Assistant, pending.Extra);
    }

    /// <summary>Assistants were installed or removed: the names and icons of their extras in the commands change.</summary>
    private void RefreshExtraNames()
    {
        Config.RefreshReferenceNames();
        IconProvider.ForgetAssistantIcons();
    }

    /// <summary>
    /// The command that runs an extra (the simplest one if there are several), or a new one, added to the commands,
    /// when <paramref name="create"/> is set and there is none.
    /// </summary>
    public Command? ExtraCommand(InstalledExtra extra, bool create)
    {
        if (AssistantExtras.CommandsFor(Config, extra.Assistant.Id, extra.Extra.Id).FirstOrDefault() is { } existing) return existing;
        if (!create) return null;
        var command = AssistantExtras.NewCommand(extra, Loc.Instance.Language, hotkey: null);
        AddExtraCommand(command);
        return command;
    }

    /// <summary>
    /// After installing an assistant: creates the commands the user accepted on its card, for the extras that suggest a
    /// hotkey (Ctrl+Alt+L for the Lander's manual mode). One that already has a command is left as it is.
    /// </summary>
    public void CreateExtraCommands(IEnumerable<InstalledExtra> extras)
    {
        foreach (var extra in extras)
        {
            if (AssistantExtras.CommandsFor(Config, extra.Assistant.Id, extra.Extra.Id).Any()) continue;
            var command = AssistantExtras.NewCommand(extra, Loc.Instance.Language, extra.Extra.Hotkey);
            AddExtraCommand(command);
            Log.Info($"Asistente: comando «{command.Name}» creado ({command.FirstHotkey})");
        }
    }

    private void AddExtraCommand(Command command)
    {
        Config.Commands.Add(command);
        Achievements.Increment(Core.Achievements.Stat.CommandsCreated);
        Achievements.Increment(Core.Achievements.Stat.ActionsCreated);
    }
}
