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
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var matched = AssistantCommands.Match(match, AssistantCommands.Available(Config), Config.Settings.VoiceMedia, Voice.Semantic);
                        supervisor.Send(matched);
                        Log.Info($"Asistente: comandos buscados para «{match.Text}» en {watch.ElapsedMilliseconds} ms");
                    }
                    catch (Exception ex) { Log.Error("Asistente: no se ha podido buscar el comando", ex); }
                });
                return true;
            case RunMessage run:
                RunForAssistant(supervisor, run.Assistant, run.Run, run.Choreography,
                    (who, choreography) => AssistantCommands.RunAsync(Engine, run, who, (command, risk) => AskCommandAsync(run.Run, run.Assistant, command, risk, remember: true), choreography));
                return true;
            case ActMessage act:
                // A command made for the occasion (stage 8.1): nothing to trust for next time.
                RunForAssistant(supervisor, act.Assistant, act.Run, act.Choreography,
                    (who, choreography) => AssistantActs.ActAsync(Engine, act, who, (command, risk) => AskCommandAsync(act.Run, act.Assistant, command, risk, remember: false), choreography));
                return true;
            case WindowMessage window:
                // One after another, off the pipe's thread (a window that hangs only holds these back).
                lock (_windowGate)
                    _windowChain = _windowChain.ContinueWith(_ => supervisor.Send(new WindowDoneMessage(window.Id, AssistantWindows.Apply(window))), TaskScheduler.Default);
                return true;
            case GoMessage or DoneMessage or SkipMessage:
                var runId = message switch { GoMessage g => g.Run, DoneMessage d => d.Run, SkipMessage s => s.Run, _ => "" };
                if (_choreographies.TryGetValue(runId, out var running)) running.Choreography.OnAnswer(message);
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

    /// <summary>
    /// Runs something for an assistant off the pipe's thread and sends its <c>end</c>; with choreography (stage 6.3) the
    /// assistant has its turn before each step, over the pipe.
    /// </summary>
    private void RunForAssistant(AssistantSupervisor supervisor, string assistant, string runId, bool choreographed,
        Func<string, AssistantChoreography?, Task<EndMessage>> run) =>
        _ = Task.Run(async () =>
        {
            var who = AssistantPackages.Find(assistant)?.Manifest.Name ?? (assistant.Length > 0 ? assistant : "K-Desktop");
            var settings = Config.Settings;
            using var choreography = choreographed
                ? new AssistantChoreography(runId, m => supervisor.Send(m)) { Speed = settings.AssistantManoeuvres, AnimateMax = Math.Max(0, settings.AssistantAnimateMax) }
                : null;
            if (choreography is not null) _choreographies[runId] = (assistant, choreography);
            try
            {
                supervisor.Send(await run(who, choreography).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Log.Error("Asistente: no se ha podido ejecutar lo que pedía", ex);
                supervisor.Send(new EndMessage(runId, "failed", [ex.Message]));
            }
            finally
            {
                if (choreography is not null) _choreographies.TryRemove(runId, out _);
            }
        });

    /// <summary>Seconds to answer whether a command may run.</summary>
    private const int CommandAskSeconds = 20;

    /// <summary>The windows changed for the assistants (stage 6.5), in the order they were asked.</summary>
    private Task _windowChain = Task.CompletedTask;

    private readonly object _windowGate = new();

    /// <summary>The choreographed runs going on (run id → whose and its choreography).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Assistant, AssistantChoreography Choreography)> _choreographies = new(StringComparer.Ordinal);

    /// <summary>The process stopped: the commands it was running with gestures go on without them.</summary>
    private void AbandonChoreographies()
    {
        foreach (var (_, running) in _choreographies) running.Choreography.Abandon();
    }

    /// <summary>The user stopped an assistant: the commands it was running with gestures stop too.</summary>
    private void CancelChoreographies(string? assistant)
    {
        foreach (var (_, running) in _choreographies)
            if (assistant is null || running.Assistant == assistant) running.Choreography.Cancel();
    }

    /// <summary>
    /// Before an assistant runs a command that is not trusted, or does something that always asks: the same window as
    /// for its tools. «Yes, and don't ask again» (only for what asks once, and a command that stays: <paramref name="remember"/>)
    /// makes the command trusted.
    /// </summary>
    private Task<string> AskCommandAsync(string run, string assistant, Core.Model.Command command, Core.Assistant.ToolRisk risk, bool remember) =>
        Dispatcher.InvokeAsync(async () =>
        {
            var choice = await AskUserAsync("run-" + run, assistant, Loc.T("assistantCommand.ask", command.Name), remember && risk == Core.Assistant.ToolRisk.AskOnce, CommandAskSeconds);
            if (remember && choice == "always" && !Config.Settings.AssistantTrustedCommands.Contains(command.Id))
                Config.Settings.AssistantTrustedCommands = [.. Config.Settings.AssistantTrustedCommands, command.Id];
            return choice;
        }).Task.Unwrap();
}
