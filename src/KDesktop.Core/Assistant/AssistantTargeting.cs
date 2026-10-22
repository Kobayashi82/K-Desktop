namespace KDesktop.Core.Assistant;

/// <summary>
/// Who the assistants are to K-Desktop: K-Desktop itself is one of them, and each one is called by its own word or its own
/// key (5.0.113: there is no word, key or choice «for the assistant»). <see cref="Pick"/> is only for what K-Desktop decides
/// on its own (showing the commands you run, «Probar»).
/// </summary>
public static class AssistantTargeting
{
    /// <summary>
    /// K-Desktop itself as an assistant (5.0.112): of the system, without a package, a figure or gestures. «On the desktop»
    /// is active: it answers with a notice and its voice, and runs commands without gestures. In the assistant process it is
    /// the one without an id (null), never a loaded package; its own model and what it has of its own go under this key.
    /// </summary>
    public const string KDesktop = "kdesktop";

    public static bool IsSystem(string? id) => id == KDesktop;

    /// <summary>
    /// The one on the desktop K-Desktop turns to by itself: the last one talked to, else the first one put there; null
    /// with nobody on the desktop.
    /// </summary>
    /// <param name="onDesktop">The assistants on the desktop, in the order they were put there.</param>
    public static string? Pick(string? lastTalkedTo, IReadOnlyList<string> onDesktop) =>
        onDesktop.Count == 0 ? null : lastTalkedTo is not null && onDesktop.Contains(lastTalkedTo) ? lastTalkedTo : onDesktop[0];

    /// <summary>The assistant a wake word calls (null: none of them).</summary>
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
