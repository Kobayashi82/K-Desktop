using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop;

/// <summary>
/// K-Desktop's commands for the assistants (phase 4): <c>match</c> (which commands an order names), <c>run</c> (run one,
/// in the history as coming from the assistant) and <c>media</c> (a built-in media order). Answered from the pool:
/// matching by meaning and running take a while and need no window.
/// </summary>
public partial class App
{
    /// <summary>A request of the process that is not for the window (pipe thread): true when it was handled here.</summary>
    private bool OnAssistantCommandRequest(AssistantSupervisor supervisor, PipeMessage message)
    {
        switch (message)
        {
            case MatchMessage match:
                _ = Task.Run(() =>
                {
                    try { supervisor.Send(AssistantCommands.Match(match, AssistantCommands.Available(Config), Config.Settings.VoiceMedia, Voice.Semantic)); }
                    catch (Exception ex) { Log.Error("Asistente: no se ha podido buscar el comando", ex); }
                });
                return true;
            case RunMessage run:
                _ = Task.Run(async () =>
                {
                    var who = AssistantPackages.Find(run.Assistant)?.Manifest.Name ?? (run.Assistant.Length > 0 ? run.Assistant : "K-Desktop");
                    var end = await AssistantCommands.RunAsync(Engine, run, who, (command, risk) => AskCommandAsync(run, command, risk)).ConfigureAwait(false);
                    supervisor.Send(end);
                });
                return true;
            case DescribeMessage describe:
                supervisor.Send(AssistantCommands.Describe(Config, describe));
                return true;
            case RunEventMessage runEvent:
                _ = Task.Run(async () =>
                {
                    var who = AssistantPackages.Find(runEvent.Assistant)?.Manifest.Name ?? (runEvent.Assistant.Length > 0 ? runEvent.Assistant : "K-Desktop");
                    var end = await AssistantCommands.RunEventAsync(Engine, runEvent, who,
                        (rule, risk) => Dispatcher.InvokeAsync(() => AskUserAsync("event-" + runEvent.Run, runEvent.Assistant, Loc.T("assistantCommand.askEvent", rule.Name), false, CommandAskSeconds)).Task.Unwrap())
                        .ConfigureAwait(false);
                    supervisor.Send(end);
                });
                return true;
            case MediaMessage media:
                _ = Task.Run(async () =>
                {
                    var order = AssistantCommands.MediaOrderOf(media);
                    var (text, kind) = order is null ? (Loc.T("assistantCommand.notFound"), Core.Engine.OsdKind.Warning) : await Voice.RunMediaAsync(order).ConfigureAwait(false);
                    if (media.Run is { } id) supervisor.Send(new EndMessage(id, kind == Core.Engine.OsdKind.Warning ? "failed" : "ok", [text]));
                });
                return true;
        }
        return false;
    }

    /// <summary>Seconds to answer whether a command may run.</summary>
    private const int CommandAskSeconds = 20;

    /// <summary>
    /// Before an assistant runs a command that is not trusted, or does something that always asks: the same window as
    /// for its tools. «Yes, and don't ask again» (only for what asks once) makes the command trusted.
    /// </summary>
    private Task<string> AskCommandAsync(RunMessage run, Core.Model.Command command, Core.Assistant.ToolRisk risk) =>
        Dispatcher.InvokeAsync(async () =>
        {
            var choice = await AskUserAsync("run-" + run.Run, run.Assistant, Loc.T("assistantCommand.ask", command.Name), risk == Core.Assistant.ToolRisk.AskOnce, CommandAskSeconds);
            if (choice == "always" && !Config.Settings.AssistantTrustedCommands.Contains(command.Id))
                Config.Settings.AssistantTrustedCommands = [.. Config.Settings.AssistantTrustedCommands, command.Id];
            return choice;
        }).Task.Unwrap();
}
