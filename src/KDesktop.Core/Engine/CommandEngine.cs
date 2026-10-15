using System.Collections.Concurrent;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Engine;

public enum TriggerSource { Hotkey, QuickMenu, Remote, Startup, Exit, Manual, Command, Launcher, Web }

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

    public CommandEngine(ConfigStore store, IUiServices ui)
    {
        _store = store;
        // Messages shown while a command runs also reach a remote caller (see ExecutionFeedback).
        _ui = new FeedbackUiServices(ui);
    }

    public AppConfig Config => _store.Config;

    /// <summary>Raised after a command finished (for statistics / UI feedback).</summary>
    public event Action<Command, TriggerSource>? Executed;

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
        try
        {
            if (!isRepeat && !ctx.Quiet) Sounds.Play(command.Sound);

            foreach (var action in command.Actions.ToArray())
            {
                if (cts.IsCancellationRequested) break;
                if (!action.Enabled) continue;
                if (isRepeat && action is not VolumeAction) continue;
                try
                {
                    if (!isRepeat) ActionStarting?.Invoke(action, ctx);
                    await ExecuteActionAsync(action, ctx).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"Error en '{command.Name}' → {action.Info.Name}", ex);
                }
            }
            if (!isRepeat) Executed?.Invoke(command, source);
        }
        finally
        {
            _running.TryRemove(new KeyValuePair<string, CancellationTokenSource>(command.Id, cts));
            cts.Dispose();
        }
    }

    /// <summary>Executes a single action outside of a command (the "test" button of the editor).</summary>
    public Task TestActionAsync(CommandAction action, Command owner) =>
        ExecuteActionAsync(action, new ExecutionContext(owner, TriggerSource.Manual, ForegroundWindow.Capture(), false, CancellationToken.None));

    /// <summary>Runs the "on startup" commands.</summary>
    public async Task RunStartupCommandsAsync()
    {
        foreach (var cmd in Config.Commands.Where(c => c.Enabled && c.RunOnStartup).ToArray())
            await RunAsync(cmd, TriggerSource.Startup).ConfigureAwait(false);
    }

    /// <summary>Runs the "on exit" commands (with a time limit so exiting never hangs).</summary>
    public async Task RunExitCommandsAsync()
    {
        var commands = Config.Commands.Where(c => c.Enabled && c.RunOnExit).ToArray();
        if (commands.Length == 0) return;
        var all = Task.WhenAll(commands.Select(c => RunAsync(c, TriggerSource.Exit)));
        await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
    }

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
