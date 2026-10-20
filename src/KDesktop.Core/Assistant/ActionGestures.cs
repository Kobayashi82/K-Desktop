using KDesktop.Assistant.Sdk;
using KDesktop.Core.Model;

namespace KDesktop.Core.Assistant;

/// <summary>One gesture the assistant is asked for while a step runs.</summary>
/// <param name="Button">For <see cref="GestureKind.PressButton"/>: which caption button.</param>
/// <param name="Place">For GoTo/PointAt/Mark: where (window of the step, tray, clock or the cursor's point).</param>
/// <param name="Icon">For <see cref="GestureKind.Communicate"/>: the module icon shown in the bubble.</param>
/// <param name="Commits">This is the gesture in which the action happens (Commit / PerformedSelf); the others only lead to it.</param>
public sealed record GestureStep(GestureKind Kind, CaptionButton? Button = null, GesturePlace Place = GesturePlace.Window,
                                 string? Icon = null, bool Commits = true);

public enum GesturePlace { Window, Tray, Clock, Point, None }

/// <summary>
/// The general choreographer's table (§11.3 of the plan): which gestures show each K-Desktop action. Every
/// <see cref="ActionKind"/> has an entry; whatever the assistant does not know falls back to Communicate.
/// </summary>
public static class ActionGestures
{
    /// <summary>Gestures for one action. <paramref name="maximized"/>: the target window is maximized now.</summary>
    public static IReadOnlyList<GestureStep> For(CommandAction action, bool maximized = false) => action switch
    {
        WindowAction w => ForWindow(w.Operation, maximized),
        AppControlAction a => a.Mode switch
        {
            AppControlMode.Activate => [new(GestureKind.ActivateWindow)],
            AppControlMode.Close => [new(GestureKind.PressButton, CaptionButton.Close)],
            _ => [new(GestureKind.WaitForWindow, Place: GesturePlace.None)],
        },
        SendInputAction s => s.Mode is SendInputMode.MouseMoveTo or SendInputMode.MouseMoveBy
            ? [new(GestureKind.GoTo, Place: GesturePlace.Point)]
            : [new(GestureKind.Typing)],
        _ => ForKind(action.Kind),
    };

    /// <summary>The gestures by kind alone (no settings): what the table in docs/assistant/acciones.md lists.</summary>
    public static IReadOnlyList<GestureStep> ForKind(ActionKind kind) => kind switch
    {
        ActionKind.Window => ForWindow(WindowOperation.Snap, maximized: false),
        ActionKind.AppControl => [new(GestureKind.ActivateWindow)],
        ActionKind.SendInput => [new(GestureKind.Typing)],
        ActionKind.PlayMacro => [new(GestureKind.Typing)],

        ActionKind.RunApp or ActionKind.OpenFolder or ActionKind.OpenUrl => [new(GestureKind.WaitForWindow, Place: GesturePlace.None)],

        // Things that live in the notification area: go there, then say what changed.
        ActionKind.Volume => Tray("volume"),
        ActionKind.AudioOutput => Tray("speaker"),
        ActionKind.Radio => Tray("radio"),
        ActionKind.PowerPlan => Tray("battery"),
        ActionKind.WindowsTheme => Tray("theme"),
        ActionKind.Display => Tray("display"),

        ActionKind.TopMost => [new(GestureKind.Mark)],
        ActionKind.Screenshot => [new(GestureKind.Scan, Place: GesturePlace.None)],
        ActionKind.Magnifier => [new(GestureKind.PointAt, Place: GesturePlace.Point)],

        ActionKind.Power => Say("power"),
        ActionKind.Wait => Say("clock"),
        ActionKind.RunScript => Say("script"),
        ActionKind.HttpRequest => Say("web"),
        ActionKind.WakeOnLan => Say("network"),
        ActionKind.Clipboard => Say("clipboard"),
        ActionKind.PlaySound => Say("sound"),
        ActionKind.AudioRecord => Say("microphone"),
        ActionKind.ShowMessage => Say("message"),
        ActionKind.HiddenFiles => Say("folder"),
        ActionKind.ToggleDisabledIn => Say("pause"),
        ActionKind.KBoard => Say("keyboard"),
        ActionKind.OpenQuickMenu => Say("menu"),
        ActionKind.Launcher => Say("search"),
        ActionKind.CommandControl => Say("command"),

        // An extra is the assistant's own: whoever runs the command says so, and the one it belongs to does it.
        ActionKind.AssistantExtra => Say("assistant"),

        // «Si…» decides which actions run; it does nothing visible itself.
        ActionKind.Condition => [],

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Acción sin gesto: añádela a ActionGestures."),
    };

    private static IReadOnlyList<GestureStep> ForWindow(WindowOperation op, bool maximized) => op switch
    {
        WindowOperation.Minimize => [new(GestureKind.PressButton, CaptionButton.Minimize)],
        WindowOperation.Maximize => [new(GestureKind.PressButton, CaptionButton.Maximize)],
        WindowOperation.Restore => [new(GestureKind.PressButton, CaptionButton.Restore)],
        WindowOperation.ToggleMaximize => [new(GestureKind.PressButton, maximized ? CaptionButton.Restore : CaptionButton.Maximize)],
        WindowOperation.Opacity => [new(GestureKind.PointAt)],

        // Snap, Center, MoveToMonitor: a maximized window is restored first (that part is the assistant's, not the action).
        _ => maximized
            ? [new(GestureKind.PressButton, CaptionButton.Restore, Commits: false), new(GestureKind.MoveWindow)]
            : [new(GestureKind.MoveWindow)],
    };

    private static IReadOnlyList<GestureStep> Tray(string icon) =>
        [new(GestureKind.GoTo, Place: GesturePlace.Tray, Commits: false), new(GestureKind.Communicate, Place: GesturePlace.Tray, Icon: icon)];

    private static IReadOnlyList<GestureStep> Say(string icon) => [new(GestureKind.Communicate, Place: GesturePlace.None, Icon: icon)];
}
