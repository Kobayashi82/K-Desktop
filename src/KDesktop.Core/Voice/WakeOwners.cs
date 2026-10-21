using KDesktop.Core.Assistant;

namespace KDesktop.Core.Voice;

/// <summary>
/// Whose each recorded wake word is (5.0.109): the recorded words are a pool and each one has one owner —K-Desktop (its
/// commands), an assistant, any assistant (<see cref="AssistantTargeting.AnyAssistant"/>) or nobody. A word nobody has,
/// or an assistant's while it is not on the desktop, is not listened to.
/// </summary>
public static class WakeOwners
{
    /// <summary>The owner «K-Desktop» (its commands).</summary>
    public const string KDesktop = "kdesktop";

    /// <summary>Its owner: <see cref="KDesktop"/>, an assistant's id, <see cref="AssistantTargeting.AnyAssistant"/> or null.</summary>
    public static string? Of(string wordId, IReadOnlyDictionary<string, string> assistantWords, IReadOnlyCollection<string>? kdesktopWords)
    {
        foreach (var (assistant, word) in assistantWords)
            if (word == wordId) return assistant;
        return kdesktopWords?.Contains(wordId) == true ? KDesktop : null;
    }

    /// <summary>
    /// The words that are not listened to now: those of nobody, and those of the assistants when the module is off or (an
    /// assistant's own) it is not on the desktop.
    /// </summary>
    public static HashSet<string> Asleep(IEnumerable<string> words, IReadOnlyDictionary<string, string> assistantWords, IReadOnlyCollection<string>? kdesktopWords,
        bool moduleOn, IReadOnlyCollection<string> onDesktop) =>
        [.. words.Where(id => Of(id, assistantWords, kdesktopWords) switch
        {
            null => true,
            KDesktop => false,
            AssistantTargeting.AnyAssistant => !moduleOn,
            var assistant => !moduleOn || !onDesktop.Contains(assistant),
        })];

    /// <summary>
    /// The words of before 5.0.109 for K-Desktop: those no assistant had (until then every such word called its commands).
    /// </summary>
    public static List<string> Migrated(IEnumerable<string> words, IReadOnlyDictionary<string, string> assistantWords)
    {
        var taken = assistantWords.Values.ToHashSet(StringComparer.Ordinal);
        return [.. words.Where(id => !taken.Contains(id))];
    }
}
