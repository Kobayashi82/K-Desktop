using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// What the user says about the assistant, in any language: that it got something wrong (<c>assistant.words.wrong</c>, its
/// answer is checked again) or thanks and praise (<c>assistant.words.praise</c>, it is pleased: stage 13.3).
/// </summary>
public static class UserSignals
{
    /// <summary>Praise counts only in a short order («gracias», «muy bien, gracias»): in a long one it is courtesy.</summary>
    public const int PraiseWords = 8;

    private static readonly Lazy<string[]> WrongWords = new(() => Phrases("assistant.words.wrong"));

    private static readonly Lazy<string[]> PraiseList = new(() => Phrases("assistant.words.praise"));

    private static string[] Phrases(string key) =>
    [
        .. Loc.Available()
            .Select(l => Loc.Strings(l.Code, key).GetValueOrDefault(key) ?? string.Empty)
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => " " + string.Join(' ', TextSimilarity.Words(p)) + " ")
            .Where(p => p.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Whether the order says something was wrong («está mal», «te equivocas», «that's wrong»), in any language; not when a
    /// «no» goes right before («no está mal» is praise).
    /// </summary>
    public static bool IsWrong(string said) => Says(said, WrongWords.Value);

    /// <summary>
    /// Whether the order thanks or praises it («gracias», «bien hecho», «eres genial»), in any language, in at most
    /// <see cref="PraiseWords"/> words; not with a «no» right before («no muy bien»), nor when it also says it was wrong.
    /// </summary>
    public static bool IsPraise(string said) =>
        TextSimilarity.Words(said).Length <= PraiseWords && Says(said, PraiseList.Value) && !IsWrong(said);

    private static bool Says(string said, string[] phrases)
    {
        var folded = " " + string.Join(' ', TextSimilarity.Words(said)) + " ";
        foreach (var phrase in phrases)
        {
            var at = folded.IndexOf(phrase, StringComparison.Ordinal);
            if (at < 0) continue;
            var before = folded[..at].TrimEnd();
            var last = before[(before.LastIndexOf(' ') + 1)..];
            if (last is not ("no" or "not" or "nicht" or "pas" or "non" or "nao")) return true;
        }
        return false;
    }
}
