namespace KDesktop.Core.Assistant;

/// <summary>
/// The items of the showcase that the host launches itself (stage 13.9): entrances, exits, states, reactions, gestures and,
/// on the stage's windows (stage 16.1), what it does to a window. K-Desktop names them with its own texts (<c>showcase.item.&lt;id&gt;</c>); a clip of one of them in a
/// manifest (stage 13.10) needs no name of its own.
/// </summary>
public static class ShowcaseItems
{
    /// <summary>The host's own items, in the order K-Desktop shows them: (id, group).</summary>
    public static IReadOnlyList<(string Id, string Group)> Standard { get; } =
    [
        ("enter.startup", "entrances"), ("enter.summoned", "entrances"),
        ("exit.dismissed", "exits"), ("exit.uninstalling", "exits"),
        ("state.listening", "states"), ("state.thinking", "states"), ("state.thinkingHard", "states"),
        ("state.loadingBrain", "states"), ("state.busy", "states"), ("state.awaitingConfirmation", "states"),
        ("state.speaking", "states"), ("state.noBrain", "states"), ("state.sleeping", "states"),
        ("react.success", "reactions"), ("react.celebration", "reactions"), ("react.warning", "reactions"), ("react.failure", "reactions"),
        ("gesture.announce", "gestures"), ("gesture.communicate", "gestures"), ("gesture.mark", "gestures"),
        ("gesture.pointAt", "gestures"), ("gesture.goTo", "gestures"), ("gesture.scan", "gestures"), ("gesture.typing", "gestures"),
        ("window.minimize", "windows"), ("window.maximize", "windows"), ("window.close", "windows"), ("window.move", "windows"),
        ("window.activate", "windows"), ("window.open", "windows"),
    ];

    /// <summary>The groups of an assistant's own animations (<c>ShowcaseGroup</c> of the SDK, as on the pipe).</summary>
    public static IReadOnlyList<string> OwnGroups { get; } = ["idle", "moving", "playing", "feelings", "tricks", "other"];

    private static readonly HashSet<string> StandardIds = [.. Standard.Select(s => s.Id)];

    /// <summary>Whether an id is one of the host's items.</summary>
    public static bool IsStandard(string id) => StandardIds.Contains(id);
}
