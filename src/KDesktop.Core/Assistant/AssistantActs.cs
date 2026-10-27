using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

/// <summary>
/// K-Desktop's side of an <see cref="ActMessage"/> (stage 8.1): the program the user names, found among the open windows
/// (and the running processes, for quitting or its volume), and a command made for the occasion with one action of the
/// catalogue: «Ventana» (minimise, maximise, restore, place, centre, another monitor, close that window), «Programa»
/// (bring to the front, quit) or «Volumen» (of that program). Only these: the assistant's process has no rights, and
/// K-Desktop must not run anything it asks for.
/// </summary>
public static class AssistantActs
{
    public static readonly IReadOnlyList<string> Ops =
    [
        "closeWindow", "quit", "minimize", "maximize", "restore", "front", "left", "right", "center", "otherMonitor",
        "mute", "unmute", "volumeUp", "volumeDown", "volume", "kill",
    ];

    /// <summary>
    /// Never ended by an assistant: Windows itself (ending one of these logs the user off or stops the PC) and K-Desktop.
    /// </summary>
    private static readonly HashSet<string> Vital = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "smss", "svchost", "dwm", "fontdrvhost",
        "sihost", "ctfmon", "audiodg", "spoolsv", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "runtimebroker", "taskhostw", "conhost", "dllhost", "system", "registry", "memcompression", "msmpeng",
    };

    /// <summary>Volume up or down, in %.</summary>
    public const int VolumeStep = 10;

    /// <summary>What to run, or why not (<see cref="EndMessage.Reason"/>), with the program's name for the user.</summary>
    public sealed record Plan(Command? Command, string Name, string? Reason);

    /// <param name="windows">The open windows, the one in front first (<see cref="AppFinder.OpenWindows"/>).</param>
    /// <param name="process">A running process by the user's words (<see cref="AppFinder.FindProcess"/>).</param>
    /// <param name="foreground">The window in front (for an order without a program: «minimiza esto»).</param>
    /// <param name="hung">Whether a window does not respond (for «kill» without a program); null: Windows says.</param>
    public static Plan Build(ActMessage act, IReadOnlyList<OpenWindow> windows, Func<string, string?> process, ForegroundWindow foreground,
        Func<IntPtr, bool>? hung = null)
    {
        var op = act.Op;
        if (!Ops.Contains(op)) throw new ArgumentException($"«{op}» no es algo que un asistente pueda hacer a un programa", nameof(act));
        if (op == "kill") return Kill(act, windows, process, foreground, hung ?? Native.Win32.IsHungAppWindow);
        var current = !AppFinder.NamesSomething(act.App);
        var window = current ? windows.FirstOrDefault(w => w.Hwnd == foreground.Handle || w.Hwnd == foreground.RootHandle) : AppFinder.FindWindow(act.App, windows);
        var isVolume = op is "mute" or "unmute" or "volumeUp" or "volumeDown" or "volume";
        // Quitting and the volume are of the program: it may have no window (Steam in the tray, a player).
        var processName = window?.Process ?? (current ? foreground.ProcessName : op == "quit" || isVolume ? process(act.App) : null);
        var name = window?.DisplayName ?? (current ? Loc.T("act.current") : processName ?? act.App.Trim());
        if (processName is not { Length: > 0 } && !(current && isVolume))
            return new Plan(null, name, current ? "notAllowed" : op == "quit" || isVolume ? "noProcess" : "noWindow");
        if (processName is { } own && (IsOwn(own) || own == "explorer" && op == "quit"))
            return new Plan(null, name, "notAllowed");
        if (window is null && !isVolume && op != "quit") return new Plan(null, name, current ? "notAllowed" : "noWindow");

        CommandAction action = op switch
        {
            "quit" => new AppControlAction { Mode = AppControlMode.Close, ProcessName = processName! },
            "front" => new AppControlAction { Mode = AppControlMode.Activate, ProcessName = processName! },
            "mute" or "unmute" or "volumeUp" or "volumeDown" or "volume" => new VolumeAction
            {
                Target = current ? VolumeTarget.ForegroundApp : VolumeTarget.App,
                ProcessName = current ? string.Empty : processName!,
                Mode = op switch
                {
                    "mute" => VolumeMode.Mute,
                    "unmute" => VolumeMode.Unmute,
                    "volumeUp" => VolumeMode.Up,
                    "volumeDown" => VolumeMode.Down,
                    _ => VolumeMode.Set,
                },
                Value = op == "volume" ? Math.Clamp(act.Value ?? 50, 0, 100) : VolumeStep,
            },
            _ => new WindowAction
            {
                Target = WindowTarget.Process,
                ProcessName = processName!,
                Handle = window!.Hwnd,
                Operation = op switch
                {
                    "closeWindow" => WindowOperation.Close,
                    "minimize" => WindowOperation.Minimize,
                    "maximize" => WindowOperation.Maximize,
                    "restore" => WindowOperation.Restore,
                    "center" => WindowOperation.Center,
                    "otherMonitor" => WindowOperation.MoveToMonitor,
                    _ => WindowOperation.Snap,
                },
                Snap = op == "right" ? WindowSnap.RightHalf : WindowSnap.LeftHalf,
                Monitor = MonitorChoice.Next,
            },
        };
        var command = new Command { Name = Loc.T("act." + op, name) };
        command.Actions.Add(action);
        return new Plan(command, name, null);
    }

    /// <summary>
    /// «mata Fall Guys», «cierra lo que se ha colgado»: the program named (its window or its process), or else the one
    /// that does not respond, or else the one in front; asked to close and, if still there after a moment, killed.
    /// </summary>
    private static Plan Kill(ActMessage act, IReadOnlyList<OpenWindow> windows, Func<string, string?> process, ForegroundWindow foreground, Func<IntPtr, bool> hung)
    {
        var current = !AppFinder.NamesSomething(act.App) || SaysHung(act.App);
        var window = current
            ? windows.FirstOrDefault(w => hung(w.Hwnd)) ?? windows.FirstOrDefault(w => w.Hwnd == foreground.Handle || w.Hwnd == foreground.RootHandle)
            : AppFinder.FindWindow(act.App, windows);
        var processName = current ? window?.Process ?? foreground.ProcessName : process(act.App) ?? window?.Process;
        var name = window is { } w && w.Process == processName ? w.DisplayName : processName ?? (current ? Loc.T("act.hung") : act.App.Trim());
        if (processName is not { Length: > 0 }) return new Plan(null, name, current ? "notAllowed" : "noProcess");
        if (IsOwn(processName) || Vital.Contains(processName)) return new Plan(null, name, "notAllowed");
        var command = new Command { Name = Loc.T("act.kill", name) };
        command.Actions.Add(new AppControlAction { Mode = AppControlMode.Close, ProcessName = processName, Force = true, AskFirst = true });
        return new Plan(command, name, null);
    }

    /// <summary>«lo que se ha colgado», «the frozen one»: no program named, the one that hangs (words of every language).</summary>
    private static bool SaysHung(string said) => Core.Voice.TextSimilarity.Words(said).Any(HungWords.Value.Contains);

    private static readonly Lazy<HashSet<string>> HungWords = new(() =>
    [
        .. Loc.Available().SelectMany(l => Core.Voice.TextSimilarity.Words(Loc.Strings(l.Code, "assistant.act.hungWords").GetValueOrDefault("assistant.act.hungWords") ?? string.Empty)),
    ]);

    /// <summary>
    /// Finds the program and does it (<see cref="AssistantCommands.RunCommandAsync"/>: asking first like a command, in the
    /// history, with the assistant's steps). Not found: <c>failed</c> with the <see cref="EndMessage.Reason"/>.
    /// </summary>
    /// <param name="allowed">An order programmed by an assistant: whether what is not safe was approved (see <see cref="ScheduledPermit"/>).</param>
    public static async Task<EndMessage> ActAsync(Engine.CommandEngine engine, ActMessage act, string who, Func<Command, ToolRisk, Task<string>> ask,
        AssistantChoreography? choreography = null, Func<Command, bool>? allowed = null)
    {
        if (engine.IsPaused) return new EndMessage(act.Run, "failed", [Loc.T("pause.refused")]);
        var plan = Build(act, AppFinder.OpenWindows(), AppFinder.FindProcess, ForegroundWindow.Capture());
        if (plan.Command is null)
            return new EndMessage(act.Run, "failed", [Loc.T("act.reason." + plan.Reason, plan.Name)], plan.Name, plan.Reason);
        var end = await AssistantCommands.RunCommandAsync(engine, plan.Command, act.Run, who, ask, choreography, allowed).ConfigureAwait(false);
        return end with { Name = plan.Name };
    }

    /// <summary>K-Desktop itself (its process, and the assistants' one, which is the same exe).</summary>
    private static bool IsOwn(string process) =>
        process.Equals(Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath), StringComparison.OrdinalIgnoreCase);
}
