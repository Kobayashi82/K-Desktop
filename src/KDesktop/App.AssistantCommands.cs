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
                RunForAssistant(supervisor, run.Assistant, run.Shared, run.Run, run.Choreography,
                    (who, choreography) => AssistantCommands.RunAsync(Engine, run, who, (command, risk) => AskCommandAsync(run.Run, run.Assistant, command, risk, remember: true), choreography,
                        CommandPermit(run.Scheduled, act: false)));
                return true;
            case ActMessage act:
                // A command made for the occasion (stage 8.1): nothing to trust for next time.
                RunForAssistant(supervisor, act.Assistant, act.Shared, act.Run, act.Choreography,
                    (who, choreography) => AssistantActs.ActAsync(Engine, act, who, (command, risk) => AskCommandAsync(act.Run, act.Assistant, command, risk, remember: false), choreography,
                        CommandPermit(act.Scheduled, act: true)));
                return true;
            case SystemMessage system:
                // One action of the catalogue for one group of tools (B9), made for the occasion: nothing to trust for next time.
                RunForAssistant(supervisor, system.Assistant, system.Shared, system.Run, system.Choreography,
                    (who, choreography) => AssistantSystem.RunAsync(Engine, system, who, (command, risk) => AskCommandAsync(system.Run, system.Assistant, command, risk, remember: false), choreography,
                        CommandPermit(system.Scheduled, act: true)));
                return true;
            case SendPhoneMessage phone:
                _ = Task.Run(() => supervisor.Send(AssistantPhone.Handle(phone, AssistantName(phone.Assistant))));
                return true;
            case ScheduleMessage schedule:
                OnSchedule(supervisor, schedule);
                return true;
            case CalendarMessage calendar:
                OnCalendar(supervisor, calendar);
                return true;
            case MakeEventMessage makeEvent:
                OnMakeEvent(supervisor, makeEvent);
                return true;
            case TextMessage { Op: "history" } history:
                // clipboard_history: only reads the history K-Desktop keeps, never the clipboard itself.
                supervisor.Send(AssistantText.History(history, ClipboardUi?.History, Config.Settings.ClipboardHistory && Config.Settings.ClipboardAssistants));
                return true;
            case TranscribeMessage transcribe:
                // transcribe_audio (D5): with the voice's Parakeet, off the pipe's thread (an hour of audio takes a few minutes).
                _ = Task.Run(async () =>
                {
                    var folder = Core.Engine.CommandEngine.FolderFor(null, Config.Settings.RecordFolder, Environment.SpecialFolder.MyMusic);
                    supervisor.Send(await AssistantTranscribe.HandleAsync(transcribe, Voice.SpeechToText, folder, Engine.Recording.CurrentFile,
                        Engine.Recording.ReadyAsync, CancellationToken.None).ConfigureAwait(false));
                });
                return true;
            case TextMessage text:
                // Ctrl+C / Ctrl+V and the clipboard (transform_text): one at a time, off the pipe's thread.
                lock (_windowGate)
                    _windowChain = _windowChain.ContinueWith(async _ =>
                    {
                        if (Engine.IsPaused) supervisor.Send(new TextDoneMessage(text.Run, false, Reason: "paused"));
                        else supervisor.Send(await AssistantText.HandleAsync(text, new SystemTextDesk()).ConfigureAwait(false));
                    }, TaskScheduler.Default).Unwrap();
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
            // B3: the «Registro», the statistics and why a command or an event does not work (read only).
            case LogQueryMessage query:
                supervisor.Send(AssistantLog.Query(Engine.History.Entries, Events.Skips, query));
                return true;
            case UsageMessage usage:
                supervisor.Send(AssistantLog.Usage(Achievements.Snapshot(), Engine.History.Entries, Config, usage, DateTime.Now));
                return true;
            case DiagnoseMessage diagnose:
                _ = Task.Run(async () =>
                {
                    try
                    {
                        supervisor.Send(await AssistantLog.DiagnoseAsync(Config, Engine.History.Entries, Events.Skips, diagnose, Engine.IsPaused,
                            async rule =>
                            {
                                var (allowed, reason) = await Core.Engine.Conditions.AllowsAsync(rule.Conditions, Core.Input.ForegroundWindow.Capture()).ConfigureAwait(false);
                                return (allowed, reason);
                            }).ConfigureAwait(false));
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Asistente: no se ha podido revisar el comando", ex);
                        supervisor.Send(new DiagnosedMessage(diagnose.Id, false, string.Empty, string.Empty, [], [], [], []));
                    }
                });
                return true;
            case PhraseMessage phrase:
                // The voice phrases are shown in the command editor: changed on its thread (the config saves itself).
                _ = Dispatcher.InvokeAsync(() =>
                {
                    var done = AssistantCommands.Phrase(Config, phrase);
                    Log.Info($"Asistente: frase de voz «{phrase.Phrase}» {(phrase.Remove ? "quitada de" : "para")} «{done.Name}»: {(done.Ok ? "hecho" : done.Reason)}");
                    supervisor.Send(done);
                });
                return true;
            case RunEventMessage runEvent:
                _ = Task.Run(async () =>
                {
                    var who = runEvent.Shared ?? AssistantName(runEvent.Assistant);
                    // An order programmed by an assistant: events run only if run_event was approved for it.
                    bool? allowed = runEvent.Scheduled is null ? null : PermitOf(runEvent.Scheduled)?.Tools.Contains("run_event") == true;
                    var end = await AssistantCommands.RunEventAsync(Engine, runEvent, who,
                        (rule, risk) => Dispatcher.InvokeAsync(() => AskUserAsync("event-" + runEvent.Run, runEvent.Assistant, Loc.T("assistantCommand.askEvent", rule.Name), false, CommandAskSeconds)).Task.Unwrap(),
                        allowed).ConfigureAwait(false);
                    supervisor.Send(end);
                });
                return true;
            case MediaMessage media:
                _ = Task.Run(async () =>
                {
                    var order = AssistantCommands.MediaOrderOf(media);
                    var (text, kind) = order is null ? (Loc.T("assistantCommand.notFound"), Core.Engine.OsdKind.Warning) : await Voice.RunMediaAsync(order).ConfigureAwait(false);
                    // «Cancel» with nothing counting down or waiting: «none» (the assistant says there was nothing to stop).
                    var status = kind == Core.Engine.OsdKind.Warning ? "failed"
                        : order?.Kind == Core.Voice.MediaOrderKind.Cancel && kind == Core.Engine.OsdKind.Info ? "none" : "ok";
                    if (media.Run is { } id) supervisor.Send(new EndMessage(id, status, [text]));
                });
                return true;
        }
        return false;
    }

    /// <summary>
    /// Runs something for an assistant off the pipe's thread and sends its <c>end</c>; with choreography (stage 6.3) the
    /// assistant has its turn before each step, over the pipe.
    /// </summary>
    /// <param name="shared">The program that asked through the MCP server (its name goes in the history), or null.</param>
    private void RunForAssistant(AssistantSupervisor supervisor, string assistant, string? shared, string runId, bool choreographed,
        Func<string, AssistantChoreography?, Task<EndMessage>> run) =>
        _ = Task.Run(async () =>
        {
            var who = shared ?? AssistantName(assistant);
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
