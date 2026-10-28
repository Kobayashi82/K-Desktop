using System.Collections.Concurrent;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Engine;

public enum TriggerSource { Hotkey, QuickMenu, Remote, Startup, Exit, Manual, Command, Launcher, Web, Voice, Event, Assistant, Mcp, CommandLine }

/// <summary>Information about one execution of a command.</summary>
public sealed record ExecutionContext(
    Command Command,
    TriggerSource Source,
    ForegroundWindow Foreground,
    bool IsRepeat,
    CancellationToken Token,
    int Depth = 0,
    IChoreography? Choreography = null)
{
    public bool Quiet => Source is TriggerSource.Startup or TriggerSource.Exit;

    /// <summary>Run from a menu-like window (QuickMenu, launcher): actions that type or click go back to the window it was opened over.</summary>
    public bool FromPopup => Source is TriggerSource.QuickMenu or TriggerSource.Launcher;

    /// <summary>
    /// What stops the commands this one calls: a choreographed run stops whole (its assistant cancelled it); any other
    /// one as always, each nested command on its own.
    /// </summary>
    public CancellationToken ChoreographyToken => Choreography is null ? CancellationToken.None : Token;
}

/// <summary>Runs commands: sequential actions, cancellation, filters and the "Esc" cancel of long operations.</summary>
public sealed partial class CommandEngine
{
    private const int MaxNesting = 8;

    private readonly ConfigStore _store;
    private readonly IUiServices _ui;
    private readonly ConcurrentDictionary<string, RunningCommand> _running = new();

    /// <summary>A command in progress: what stops it and since when (the list shows only the ones that take a while).</summary>
    public sealed class RunningCommand(Command command, CancellationTokenSource cts)
    {
        public Command Command { get; } = command;
        public long Started { get; } = Environment.TickCount64;
        internal CancellationTokenSource Cts { get; } = cts;
    }
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _cancellable = new();

    public CommandEngine(ConfigStore store, IUiServices ui, ExecutionHistory? history = null)
    {
        _store = store;
        History = history ?? new ExecutionHistory(null);
        // Messages shown while a command runs also reach a remote caller (see ExecutionFeedback).
        _ui = new FeedbackUiServices(ui);
    }

    public AppConfig Config => _store.Config;

    /// <summary>«Lupa»: the zoom around the mouse (actions, the web app).</summary>
    public Magnifier Magnifier { get; } = new();

    /// <summary>«Grabar audio»: the recording in progress and the replay.</summary>
    public AudioRecording Recording { get; } = new();

    /// <summary>The «Registro»: the last runs, from where and how they went.</summary>
    public ExecutionHistory History { get; }

    /// <summary>Raised after a command finished (for statistics / UI feedback).</summary>
    public event Action<Command, TriggerSource>? Executed;

    /// <summary>An event fired (true: its «Al terminar» commands), before its commands run (statistics, «last time»).</summary>
    public event Action<EventRule, bool, TriggerSource>? EventFired;

    /// <summary>
    /// «Llamada remota»: fires the events that answer to <c>name</c> and says how many (set by the EventService; UDP and the
    /// web call it).
    /// </summary>
    public Func<string, string, int>? RemoteCall { get; set; }

    /// <summary>Raised when an action of a command is about to run (statistics).</summary>
    public event Action<CommandAction, ExecutionContext>? ActionStarting;

    /// <summary>Raised after Escape cancelled something.</summary>
    public event Action? Cancelled;

    /// <summary>Raised (on any thread) when a command starts or ends.</summary>
    public event Action? RunningChanged;

    /// <summary>The commands in progress (also the ones other commands call).</summary>
    public IReadOnlyList<RunningCommand> Running => [.. _running.Values.OrderBy(r => r.Started)];

    /// <summary>Stops a command in progress, like running it again would but without the new run.</summary>
    public bool Stop(string commandId)
    {
        if (!_running.TryGetValue(commandId, out var run)) return false;
        try { run.Cts.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    /// <summary>
    /// K-Desktop paused (setting): nothing runs (hotkeys, menus, remote, startup…) except what the user runs by hand
    /// from the editor.
    /// </summary>
    public bool IsPaused => Config.Settings.Paused;

    /// <summary>True while a cancellable operation (countdown, wait, message) is in progress.</summary>
    public bool HasCancellable => !_cancellable.IsEmpty;

    /// <summary>Cancels every cancellable operation (bound to Escape).</summary>
    public void CancelAll()
    {
        var any = false;
        foreach (var cts in _cancellable.Keys)
        {
            try { cts.Cancel(); any = true; } catch (ObjectDisposedException) { }
        }
        if (any) Cancelled?.Invoke();
    }

    public Task RunAsync(Command command, TriggerSource source, ForegroundWindow? foreground = null, bool isRepeat = false) =>
        RunAsync(command, source, foreground ?? ForegroundWindow.Capture(), isRepeat, 0);

    public Task RunByIdAsync(string id, TriggerSource source, ForegroundWindow? foreground = null) =>
        Config.FindCommand(id) is { } cmd ? RunAsync(cmd, source, foreground) : Task.CompletedTask;

    /// <summary>
    /// Runs a command with an assistant's choreography (phase 6): before each action (also those of the commands it
    /// calls) <paramref name="choreography"/> has its turn, at most its step time. <paramref name="cancellationToken"/>
    /// stops it like re-triggering would.
    /// </summary>
    public Task RunChoreographedAsync(Command command, TriggerSource source, IChoreography choreography, CancellationToken cancellationToken = default) =>
        RunAsync(command, source, ForegroundWindow.Capture(), false, 0, choreography, null, cancellationToken);

    /// <param name="variables">Shared with the caller (a command run by another, the commands of one event); null: its own.</param>
    private async Task RunAsync(Command command, TriggerSource source, ForegroundWindow foreground, bool isRepeat, int depth,
        IChoreography? choreography = null, CommandVariables? variables = null, CancellationToken cancellationToken = default)
    {
        if (depth > MaxNesting)
        {
            Log.Warn($"Demasiados comandos anidados, se detiene en '{command.Name}'");
            return;
        }
        if (!command.Enabled && source is not (TriggerSource.Manual or TriggerSource.CommandLine)) return;
        if (depth == 0 && IsPaused && source is not (TriggerSource.Manual or TriggerSource.CommandLine))
        {
            Log.Info($"En pausa: no se ejecuta '{command.Name}' ({source})");
            return;
        }

        // While a hotkey auto-repeats only volume actions are repeated (hold to change the volume).
        if (isRepeat && !command.Actions.Any(a => a.Enabled && a is VolumeAction)) return;

        // Re-triggering a running command restarts it.
        var cts = new CancellationTokenSource();
        if (!isRepeat && _running.TryRemove(command.Id, out var previous))
        {
            try { previous.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        var run = new RunningCommand(command, cts);
        if (!isRepeat)
        {
            _running[command.Id] = run;
            command.IsRunning = true;
            RunningChanged?.Invoke();
        }
        using var stop = cancellationToken.Register(() =>
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        });

        var ctx = new ExecutionContext(command, source, foreground, isRepeat, cts.Token, depth, choreography);
        CommandVariables.Use(variables ?? new CommandVariables());
        var started = Environment.TickCount64;
        var detail = depth == 0 ? ExecutionHistory.CurrentDetail : null;
        var outcome = RunOutcome.Ok;
        string? message = null;
        try
        {
            if (!isRepeat && !ctx.Quiet) Sounds.Play(command.Sound);

            var actions = command.Actions.ToArray();
            var skip = 0;
            // The steps of a choreographed run: the enabled actions (a «Si…» is not one: it only decides).
            var steps = choreography is null ? 0 : actions.Count(a => a.Enabled && a is not ConditionAction);
            var position = 0;
            foreach (var action in actions)
            {
                if (cts.IsCancellationRequested)
                {
                    outcome = RunOutcome.Cancelled;
                    break;
                }
                // «Si…» not met skips the next actions by their place in the list, enabled or not.
                if (skip > 0)
                {
                    skip--;
                    continue;
                }
                if (!action.Enabled) continue;
                if (isRepeat && action is not VolumeAction) continue;
                ChoreographyStep? step = null;
                try
                {
                    if (!isRepeat) ActionStarting?.Invoke(action, ctx);
                    if (choreography is not null && action is not ConditionAction)
                    {
                        step = new ChoreographyStep(action, ctx, ++position, steps);
                        if (await AwaitStepAsync(choreography, step, ctx.Token).ConfigureAwait(false) == StepGo.Skip)
                        {
                            Log.Info($"'{command.Name}' → {action.Info.Name}: el asistente lo salta");
                            choreography.After(step, StepOutcome.Skipped, null);
                            continue;
                        }
                    }
                    if (action is ConditionAction condition)
                    {
                        if (await Conditions.IsMetAsync(condition, ctx.Foreground, ctx.Token).ConfigureAwait(false)) continue;
                        var what = condition.Describe();
                        if (condition.Else == ConditionElse.Stop)
                        {
                            Log.Info($"'{command.Name}': no se cumple «{what}», se detiene");
                            outcome = RunOutcome.Stopped;
                            message = Localization.Loc.T("history.notMet", what);
                            break;
                        }
                        if (condition.Else == ConditionElse.RunCommand)
                        {
                            // Instead of the rest of this one, the other command (with the same variables).
                            outcome = RunOutcome.Stopped;
                            if (Config.FindCommand(condition.ElseCommandId) is { } instead && !ReferenceEquals(instead, command))
                            {
                                Log.Info($"'{command.Name}': no se cumple «{what}», ejecuta '{instead.Name}'");
                                message = Localization.Loc.T("history.notMetRan", what, instead.Name);
                                await RunAsync(instead, TriggerSource.Command, ctx.Foreground, false, depth + 1, choreography, CommandVariables.Current, ctx.ChoreographyToken).ConfigureAwait(false);
                            }
                            else
                            {
                                Log.Info($"'{command.Name}': no se cumple «{what}», se detiene (el comando de «si no» ya no existe)");
                                message = Localization.Loc.T("history.notMet", what);
                            }
                            break;
                        }
                        skip = Math.Max(1, condition.SkipCount);
                        Log.Info($"'{command.Name}': no se cumple «{what}», se salta{(skip == 1 ? " 1 acción" : $"n {skip} acciones")}");
                        continue;
                    }
                    await TakeSelectionAsync(action, ctx).ConfigureAwait(false);
                    await ExecuteActionAsync(action, ctx).ConfigureAwait(false);
                    if (step is not null) choreography!.After(step, StepOutcome.Ok, null);
                }
                catch (OperationCanceledException)
                {
                    outcome = RunOutcome.Cancelled;
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"Error en '{command.Name}' → {action.Info.Name}", ex);
                    if (outcome == RunOutcome.Ok)
                    {
                        outcome = RunOutcome.Error;
                        message = $"{action.Info.Name}: {ex.Message}";
                    }
                    if (step is not null) choreography!.After(step, StepOutcome.Failed, $"{action.Info.Name}: {ex.Message}");
                }
            }
            if (!isRepeat) Executed?.Invoke(command, source);
        }
        finally
        {
            if (!isRepeat)
                History.Add(new HistoryEntry(DateTime.Now, command.Id, command.Name, source, detail, outcome, message,
                    (int)Math.Min(int.MaxValue, Environment.TickCount64 - started), depth));
            if (!isRepeat && _running.TryRemove(new KeyValuePair<string, RunningCommand>(command.Id, run)))
            {
                command.IsRunning = _running.ContainsKey(command.Id);
                RunningChanged?.Invoke();
            }
            cts.Dispose();
        }
    }

    /// <summary>
    /// The assistant's turn before a step: at most <see cref="IChoreography.StepTimeout"/>; no answer, or an error of its
    /// own, and the action runs anyway (an assistant never holds a command back). Cancelling the run cancels the wait.
    /// </summary>
    private static async Task<StepGo> AwaitStepAsync(IChoreography choreography, ChoreographyStep step, CancellationToken token)
    {
        try
        {
            return await choreography.BeforeAsync(step, token).WaitAsync(choreography.TimeoutOf(step), token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Info($"'{step.Context.Command.Name}' → {step.Action.Info.Name}: el asistente no contesta en {choreography.TimeoutOf(step).TotalSeconds:0.#} s; se ejecuta");
            return StepGo.Run;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"'{step.Context.Command.Name}' → {step.Action.Info.Name}: la coreografía falló ({ex.Message}); se ejecuta");
            return StepGo.Run;
        }
    }

    /// <summary>
    /// {seleccion}: the first action that uses it copies what is selected (Ctrl+C in the window it was run over) and puts
    /// the clipboard back as it was. Nothing selected: empty.
    /// </summary>
    private static async Task TakeSelectionAsync(CommandAction action, ExecutionContext ctx)
    {
        if (CommandVariables.Current is not { Selection: null } variables) return;
        if (!TextVariables.UsesSelection(action)) return;
        variables.Selection = await CopySelectionTextAsync(ctx).ConfigureAwait(false);
        Log.Info($"'{ctx.Command.Name}': {{seleccion}} con {variables.Selection.Length} letras");
    }

    /// <summary>Executes a single action outside of a command (the "test" button of the editor).</summary>
    public async Task TestActionAsync(CommandAction action, Command owner)
    {
        var ctx = new ExecutionContext(owner, TriggerSource.Manual, ForegroundWindow.Capture(), false, CancellationToken.None);
        if (action is ConditionAction condition)
        {
            // «Probar» on «Si…» says whether it is met now.
            var met = await Conditions.IsMetAsync(condition, ctx.Foreground, ctx.Token).ConfigureAwait(false);
            _ui.ShowOsd(Localization.Loc.T(met ? "osd.ifMet" : "osd.ifNotMet", condition.Describe()), met ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("BranchFork24"));
            return;
        }
        await ExecuteActionAsync(action, ctx).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the commands of an event, one after another (<paramref name="end"/>: its «Al terminar» ones). A command that no
    /// longer exists is skipped. Conditions, «no repetir» and the like are the caller's business (EventService); the
    /// pause is checked here like everywhere else.
    /// </summary>
    /// <param name="variables">What the event knows for its commands (the file of «Archivo»: {archivo}); null: nothing.</param>
    public Task RunEventAsync(EventRule rule, bool end, TriggerSource source = TriggerSource.Event, ForegroundWindow? foreground = null,
        CommandVariables? variables = null) =>
        RunEventAsync(rule, end, source, foreground ?? ForegroundWindow.Capture(), 0, variables: variables);

    /// <param name="depth">Nesting: an event run by a command that an event ran… stops like nested commands do.</param>
    private async Task RunEventAsync(EventRule rule, bool end, TriggerSource source, ForegroundWindow foreground, int depth,
        IChoreography? choreography = null, CommandVariables? variables = null, CancellationToken cancellationToken = default)
    {
        if (depth > MaxNesting)
        {
            Log.Warn($"Demasiados comandos anidados, se detiene en el evento «{rule.Name}»");
            return;
        }
        if (IsPaused && source is not (TriggerSource.Manual or TriggerSource.CommandLine))
        {
            Log.Info($"En pausa: no se ejecuta el evento «{rule.Name}»");
            return;
        }
        EventFired?.Invoke(rule, end, source);
        // Its commands share the variables (one leaves {resultado} for the next); run by a command, the command's.
        variables ??= depth > 0 ? CommandVariables.Current ?? new CommandVariables() : new CommandVariables();
        using var origin = ExecutionHistory.Describe(Localization.Loc.T(end ? "history.eventEnd" : "history.event", rule.Name));
        foreach (var command in EventCommands(rule, end))
        {
            Log.Info($"Evento «{rule.Name}»{(end ? " (al terminar)" : "")} → «{command.Name}»");
            if (cancellationToken.IsCancellationRequested) break;
            await RunAsync(command, source, foreground, false, depth, choreography, variables, cancellationToken).ConfigureAwait(false);
        }
        if (!end && rule.Ask is { IsEmpty: false } ask && !cancellationToken.IsCancellationRequested)
        {
            Log.Info($"Evento «{rule.Name}» → pide a «{ask.Assistant}»: «{ask.Text.Trim()}»");
            if (ask.IsScheduled) _ui.AskAssistantScheduled(ask);
            else _ui.AskAssistant(ask.Assistant, ask.Text.Trim());
        }
    }

    private IEnumerable<Command> EventCommands(EventRule rule, bool end)
    {
        var disabled = end ? rule.DisabledEndCommands : rule.DisabledCommands;
        foreach (var id in (end ? rule.EndCommands : rule.Commands).ToArray())
        {
            if (disabled.Contains(id)) continue;
            if (Config.FindCommand(id) is { } command) yield return command;
            else Log.Warn($"Evento «{rule.Name}»: se salta un comando que ya no existe ({id})");
        }
    }

    /// <summary>The «Al iniciar / Al salir de K-Desktop» events that may run now (enabled, «Solo si» met), in list order.</summary>
    /// <param name="token">On exit, how long the checks may take (a ping that does not answer must not hold it).</param>
    private async Task<EventRule[]> AppEventsAsync<T>(CancellationToken token = default) where T : EventTrigger
    {
        var foreground = ForegroundWindow.Capture();
        var allowed = new List<EventRule>();
        foreach (var rule in Config.Events.Where(e => e.Enabled && e.Trigger is T).ToArray())
        {
            bool ok;
            string reason;
            try { (ok, reason) = await Conditions.AllowsAsync(rule.Conditions, foreground, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { (ok, reason) = (false, "no dio tiempo a comprobarlo"); }
            if (ok) allowed.Add(rule);
            else Log.Info($"Evento «{rule.Name}»: no se ejecuta (no se cumple «{reason}»)");
        }
        return [.. allowed];
    }

    /// <summary>Runs the «Al iniciar K-Desktop» events: their commands one after another, event after event.</summary>
    public async Task RunStartupCommandsAsync()
    {
        foreach (var rule in await AppEventsAsync<AppStartTrigger>().ConfigureAwait(false))
            await RunEventAsync(rule, false, TriggerSource.Startup).ConfigureAwait(false);
    }

    /// <summary>Runs the «Al salir de K-Desktop» events: every command at once, 5 s at most so exiting never hangs.</summary>
    public async Task RunExitCommandsAsync()
    {
        if (IsPaused)
        {
            Log.Info("En pausa: no se ejecutan los eventos al salir");
            return;
        }
        using var checks = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var rules = await AppEventsAsync<AppExitTrigger>(checks.Token).ConfigureAwait(false);
        if (rules.Length == 0) return;
        var runs = new List<Task>();
        foreach (var rule in rules)
        {
            EventFired?.Invoke(rule, false, TriggerSource.Exit);
            using var origin = ExecutionHistory.Describe(Localization.Loc.T("history.event", rule.Name));
            foreach (var command in EventCommands(rule, false))
            {
                Log.Info($"Evento «{rule.Name}» → «{command.Name}»");
                runs.Add(RunAsync(command, TriggerSource.Exit));
            }
        }
        await Task.WhenAny(Task.WhenAll(runs), Task.Delay(ExitLimit)).ConfigureAwait(false);
    }

    /// <summary>Longest wait for the «Al salir» commands.</summary>
    public static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(5);

    /// <summary>Handles a hotkey coming from <see cref="HotkeyRouter"/>.</summary>
    public void OnHotkey(HotkeyTrigger trigger)
    {
        foreach (var cmd in trigger.Commands)
            _ = RunAsync(cmd, TriggerSource.Hotkey, trigger.Foreground, trigger.IsRepeat, 0);
    }

    /// <summary>Wraps a long operation so Escape can cancel it.</summary>
    private CancellationTokenSource BeginCancellable(CancellationToken parent)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(parent);
        _cancellable[cts] = 0;
        return cts;
    }

    private void EndCancellable(CancellationTokenSource cts)
    {
        _cancellable.TryRemove(cts, out _);
        cts.Dispose();
    }

    /// <summary>A countdown on show stopped before its end (Esc, or its command stopped): Options › its sound, if any.</summary>
    private void CountdownCancelled() => Sounds.Play(Config.Settings.CountdownCancelSound);
}
