using System.Collections.Concurrent;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Engine;

public enum TriggerSource { Hotkey, QuickMenu, Remote, Startup, Exit, Manual, Command, Launcher, Web, Voice, Event, Assistant }

/// <summary>Information about one execution of a command.</summary>
public sealed record ExecutionContext(
    Command Command,
    TriggerSource Source,
    ForegroundWindow Foreground,
    bool IsRepeat,
    CancellationToken Token,
    int Depth = 0)
{
    public bool Quiet => Source is TriggerSource.Startup or TriggerSource.Exit;

    /// <summary>Run from a menu-like window (QuickMenu, launcher): actions that type or click go back to the window it was opened over.</summary>
    public bool FromPopup => Source is TriggerSource.QuickMenu or TriggerSource.Launcher;
}

/// <summary>Runs commands: sequential actions, cancellation, filters and the "Esc" cancel of long operations.</summary>
public sealed partial class CommandEngine
{
    private const int MaxNesting = 8;

    private readonly ConfigStore _store;
    private readonly IUiServices _ui;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();
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

    private async Task RunAsync(Command command, TriggerSource source, ForegroundWindow foreground, bool isRepeat, int depth)
    {
        if (depth > MaxNesting)
        {
            Log.Warn($"Demasiados comandos anidados, se detiene en '{command.Name}'");
            return;
        }
        if (!command.Enabled && source is not TriggerSource.Manual) return;
        if (depth == 0 && IsPaused && source is not TriggerSource.Manual)
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
            try { previous.Cancel(); } catch (ObjectDisposedException) { }
        }
        if (!isRepeat) _running[command.Id] = cts;

        var ctx = new ExecutionContext(command, source, foreground, isRepeat, cts.Token, depth);
        var started = Environment.TickCount64;
        var detail = depth == 0 ? ExecutionHistory.CurrentDetail : null;
        var outcome = RunOutcome.Ok;
        string? message = null;
        try
        {
            if (!isRepeat && !ctx.Quiet) Sounds.Play(command.Sound);

            var actions = command.Actions.ToArray();
            var skip = 0;
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
                try
                {
                    if (!isRepeat) ActionStarting?.Invoke(action, ctx);
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
                        skip = Math.Max(1, condition.SkipCount);
                        Log.Info($"'{command.Name}': no se cumple «{what}», se salta{(skip == 1 ? " 1 acción" : $"n {skip} acciones")}");
                        continue;
                    }
                    await ExecuteActionAsync(action, ctx).ConfigureAwait(false);
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
                }
            }
            if (!isRepeat) Executed?.Invoke(command, source);
        }
        finally
        {
            if (!isRepeat)
                History.Add(new HistoryEntry(DateTime.Now, command.Id, command.Name, source, detail, outcome, message,
                    (int)Math.Min(int.MaxValue, Environment.TickCount64 - started), depth));
            _running.TryRemove(new KeyValuePair<string, CancellationTokenSource>(command.Id, cts));
            cts.Dispose();
        }
    }

    /// <summary>Executes a single action outside of a command (the "test" button of the editor).</summary>
    public async Task TestActionAsync(CommandAction action, Command owner)
    {
        var ctx = new ExecutionContext(owner, TriggerSource.Manual, ForegroundWindow.Capture(), false, CancellationToken.None);
        if (action is ConditionAction condition)
        {
            // «Probar» on «Si…» says whether it is met now.
            var met = await Conditions.IsMetAsync(condition, ctx.Foreground, ctx.Token).ConfigureAwait(false);
            _ui.ShowOsd(Localization.Loc.T(met ? "osd.ifMet" : "osd.ifNotMet", condition.Describe()), met ? OsdKind.Success : OsdKind.Warning);
            return;
        }
        await ExecuteActionAsync(action, ctx).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the commands of an event, one after another (<paramref name="end"/>: its «Al terminar» ones). A command that no
    /// longer exists is skipped. Conditions, «no repetir» and the like are the caller's business (EventService); the
    /// pause is checked here like everywhere else.
    /// </summary>
    public Task RunEventAsync(EventRule rule, bool end, TriggerSource source = TriggerSource.Event, ForegroundWindow? foreground = null) =>
        RunEventAsync(rule, end, source, foreground ?? ForegroundWindow.Capture(), 0);

    /// <param name="depth">Nesting: an event run by a command that an event ran… stops like nested commands do.</param>
    private async Task RunEventAsync(EventRule rule, bool end, TriggerSource source, ForegroundWindow foreground, int depth)
    {
        if (depth > MaxNesting)
        {
            Log.Warn($"Demasiados comandos anidados, se detiene en el evento «{rule.Name}»");
            return;
        }
        if (IsPaused && source is not TriggerSource.Manual)
        {
            Log.Info($"En pausa: no se ejecuta el evento «{rule.Name}»");
            return;
        }
        EventFired?.Invoke(rule, end, source);
        using var origin = ExecutionHistory.Describe(Localization.Loc.T(end ? "history.eventEnd" : "history.event", rule.Name));
        foreach (var command in EventCommands(rule, end))
        {
            Log.Info($"Evento «{rule.Name}»{(end ? " (al terminar)" : "")} → «{command.Name}»");
            await RunAsync(command, source, foreground, false, depth).ConfigureAwait(false);
        }
    }

    private IEnumerable<Command> EventCommands(EventRule rule, bool end)
    {
        foreach (var id in (end ? rule.EndCommands : rule.Commands).ToArray())
        {
            if (Config.FindCommand(id) is { } command) yield return command;
            else Log.Warn($"Evento «{rule.Name}»: se salta un comando que ya no existe ({id})");
        }
    }

    /// <summary>The «Al iniciar / Al salir de K-Desktop» events that may run now (enabled, «Solo si» met), in list order.</summary>
    private EventRule[] AppEvents<T>() where T : EventTrigger
    {
        var now = DateTime.Now;
        HashSet<string>? running = null;
        bool IsRunning(string name) => (running ??= ProcessSnapshot.Names()).Contains(name);
        return Config.Events.Where(e => e.Enabled && e.Trigger is T).ToArray().Where(e =>
        {
            if (e.Conditions.Allows(now, IsRunning, out var reason)) return true;
            Log.Info($"Evento «{e.Name}»: no se ejecuta ({reason})");
            return false;
        }).ToArray();
    }

    /// <summary>Runs the «Al iniciar K-Desktop» events: their commands one after another, event after event.</summary>
    public async Task RunStartupCommandsAsync()
    {
        foreach (var rule in AppEvents<AppStartTrigger>())
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
        var rules = AppEvents<AppExitTrigger>();
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
}
