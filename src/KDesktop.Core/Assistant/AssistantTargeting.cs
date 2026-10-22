using KDesktop.Core.Model;

namespace KDesktop.Core.Assistant;

/// <summary>
/// Who takes what is not addressed to anyone (a command with gestures run by a hotkey or a QuickMenu, a notice, the
/// generic wake word, the panel's chat without choosing): the choice of Options → Assistants. A wake word of its own
/// always goes to its assistant; this is only for the rest.
/// </summary>
public static class AssistantTargeting
{
    /// <summary>
    /// The key of <c>assistantWakeWords</c> for the word «for the assistant» (stage 5.1): not of anyone in particular, it
    /// goes to the one <see cref="Pick"/> gives, or to the module without a body when nobody is on the desktop.
    /// </summary>
    public const string AnyAssistant = "*";

    /// <summary>
    /// K-Desktop itself as an assistant (5.0.112): of the system, without a package, a figure or gestures. «On the desktop»
    /// is active: it answers with a notice and its voice, and runs commands without gestures. In the assistant process it is
    /// the one without an id (null), never a loaded package; its own model and what it has of its own go under this key.
    /// </summary>
    public const string KDesktop = "kdesktop";

    public static bool IsSystem(string? id) => id == KDesktop;

    /// <summary>
    /// The assistant for one order, or null with nobody on the desktop (then there is no assistant: nobody answers). Call
    /// it once per order: «at random» picks again on every call.
    /// </summary>
    /// <param name="onDesktop">The assistants on the desktop, in the order they were put there.</param>
    public static string? Pick(AssistantTarget target, string? fixedId, string? lastTalkedTo, IReadOnlyList<string> onDesktop, Random? random = null)
    {
        if (onDesktop.Count == 0) return null;
        switch (target)
        {
            case AssistantTarget.Random:
                return onDesktop[(random ?? Random.Shared).Next(onDesktop.Count)];
            case AssistantTarget.Fixed when fixedId is not null && onDesktop.Contains(fixedId):
                return fixedId;
            default:
                // The last one talked to; until then (or if it left the desktop), the first one put there.
                return lastTalkedTo is not null && onDesktop.Contains(lastTalkedTo) ? lastTalkedTo : onDesktop[0];
        }
    }

    /// <summary>The assistant a wake word calls (null: none of them; it is K-Desktop's).</summary>
    public static string? ForWakeWord(IReadOnlyDictionary<string, string> wakeWords, string wakeWordId) =>
        wakeWords.FirstOrDefault(p => p.Value == wakeWordId).Key;

    /// <summary>
    /// The wake words with <paramref name="wakeWordId"/> given to <paramref name="assistant"/> (taken from any other one:
    /// a word calls one assistant at most); null <paramref name="wakeWordId"/> leaves it without one. A new dictionary.
    /// </summary>
    public static Dictionary<string, string> Assign(IReadOnlyDictionary<string, string> wakeWords, string assistant, string? wakeWordId)
    {
        var result = wakeWords.Where(p => p.Key != assistant && p.Value != wakeWordId).ToDictionary(p => p.Key, p => p.Value);
        if (wakeWordId is not null) result[assistant] = wakeWordId;
        return result;
    }
}
