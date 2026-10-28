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
        if (op == Arrange) return BuildArrange(act, windows, foreground, WindowLayout.WorkAreas(), WindowLayout.WorkAreaOf);
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

    /// <summary>«el navegador y VS Code uno al lado del otro» (B7): several windows placed at once, not one of <see cref="Ops"/>.</summary>
    public const string Arrange = "arrange";

    /// <summary>
    /// How <see cref="Arrange"/> shares the monitor: <c>columns</c> (side by side), <c>rows</c> (one above the other),
    /// <c>grid</c> (2 × 2; with 3, a big one on the left) or <c>main</c> (the first big on the left, the rest on the right).
    /// </summary>
    public static readonly IReadOnlyList<string> Layouts = ["columns", "rows", "grid", "main"];

    /// <summary>The most windows arranged at once.</summary>
    public const int MaxArranged = 6;

    /// <summary>
    /// Finds each program among the open windows (a different window each: «dos de Chrome» are two of its windows) and
    /// makes a command with one «Colocar» per window, at its exact place (<see cref="WindowAction.Place"/>) on one monitor:
    /// the one asked for (<see cref="ActMessage.Value"/>), or the first window's. With the assistant's steps, it carries
    /// them one by one.
    /// </summary>
    public static Plan BuildArrange(ActMessage act, IReadOnlyList<OpenWindow> windows, ForegroundWindow foreground,
        IReadOnlyList<ScreenRect> areas, Func<IntPtr, ScreenRect> areaOf)
    {
        var apps = (act.Apps ?? []).Select(a => a?.Trim() ?? string.Empty).Take(MaxArranged).ToList();
        if (apps.Count == 0) return new Plan(null, Loc.T("act.current"), "noWindow");
        var chosen = new List<OpenWindow>();
        foreach (var app in apps)
        {
            var left = windows.Where(w => !chosen.Contains(w) && !IsOwn(w.Process)).ToList();
            var window = AppFinder.NamesSomething(app)
                ? AppFinder.FindWindow(app, left)
                : left.FirstOrDefault(w => w.Hwnd == foreground.Handle || w.Hwnd == foreground.RootHandle);
            if (window is null) return new Plan(null, AppFinder.NamesSomething(app) ? app : Loc.T("act.current"), AppFinder.NamesSomething(app) ? "noWindow" : "notAllowed");
            chosen.Add(window);
        }
        var names = string.Join(", ", chosen.Select(w => w.DisplayName));
        var area = act.Value is { } number && areas.Count > 0 ? areas[Math.Clamp(number, 1, areas.Count) - 1] : areaOf(chosen[0].Hwnd);
        var places = ArrangeRects(area, chosen.Count, act.Layout);
        var command = new Command { Name = Loc.T("act.arrange", names) };
        for (var i = 0; i < chosen.Count; i++)
        {
            command.Actions.Add(new WindowAction
            {
                Target = WindowTarget.Process,
                ProcessName = chosen[i].Process,
                Handle = chosen[i].Hwnd,
                Operation = WindowOperation.Snap,
                Snap = WindowSnap.Custom,
                Place = places[i],
            });
        }
        return new Plan(command, names, null);
    }

    /// <summary>Where each of <paramref name="count"/> windows goes in <paramref name="area"/> for <paramref name="layout"/> (<see cref="Layouts"/>).</summary>
    public static IReadOnlyList<ScreenRect> ArrangeRects(ScreenRect area, int count, string? layout)
    {
        count = Math.Clamp(count, 1, MaxArranged);
        layout = layout is { } given && Layouts.Contains(given) ? given : count >= 4 ? "grid" : "columns";
        ScreenRect Part(double left, double top, double width, double height)
        {
            int X(double f) => area.Left + (int)Math.Round(area.Width * f);
            int Y(double f) => area.Top + (int)Math.Round(area.Height * f);
            return new ScreenRect(X(left), Y(top), X(left + width) - X(left), Y(top + height) - Y(top));
        }
        if (count == 1) return [area];
        switch (layout)
        {
            case "rows":
                return [.. Enumerable.Range(0, count).Select(i => Part(0, i / (double)count, 1, 1.0 / count))];
            case "main":
                // The first takes the left 60 %; the rest share the right, one above the other.
                var rest = count - 1;
                return [Part(0, 0, 0.6, 1), .. Enumerable.Range(0, rest).Select(i => Part(0.6, i / (double)rest, 0.4, 1.0 / rest))];
            case "grid" when count == 2:
            case "columns":
                return [.. Enumerable.Range(0, count).Select(i => Part(i / (double)count, 0, 1.0 / count, 1))];
            default:
                if (count == 3) return [Part(0, 0, 0.5, 1), Part(0.5, 0, 0.5, 0.5), Part(0.5, 0.5, 0.5, 0.5)];
                // Two rows: the top one gets the odd one out (5 → 3 above and 2 below).
                var top = (count + 1) / 2;
                var bottom = count - top;
                return
                [
                    .. Enumerable.Range(0, top).Select(i => Part(i / (double)top, 0, 1.0 / top, 0.5)),
                    .. Enumerable.Range(0, bottom).Select(i => Part(i / (double)bottom, 0.5, 1.0 / bottom, 0.5)),
                ];
        }
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
        var end = await AssistantCommands.RunCommandAsync(engine, plan.Command, act.Run, who, ask, choreography, allowed, AssistantCommands.SourceOf(act.Shared)).ConfigureAwait(false);
        return end with { Name = plan.Name };
    }

    /// <summary>K-Desktop itself (its process, and the assistants' one, which is the same exe).</summary>
    private static bool IsOwn(string process) =>
        process.Equals(Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath), StringComparison.OrdinalIgnoreCase);
}
