using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

public sealed record IntentCandidate(Command Command, double Score);

/// <param name="Command">The command to run, when there is a clear winner.</param>
/// <param name="Candidates">Best matches first (at most a few).</param>
public sealed record IntentResult(string Text, Command? Command, IReadOnlyList<IntentCandidate> Candidates)
{
    public double Confidence => Candidates.Count > 0 ? Candidates[0].Score : 0;
}

/// <summary>What to run for one spoken text: one command, or several («abre YouTube y vacía la papelera»).</summary>
/// <param name="Whole">The text matched as a single order (its candidates explain a failure).</param>
/// <param name="Parts">Commands to run, in order (empty: nothing clear).</param>
/// <param name="Unmatched">Pieces of a multiple order that matched nothing.</param>
public sealed record IntentPlan(IntentResult Whole, IReadOnlyList<IntentResult> Parts, IReadOnlyList<string> Unmatched)
{
    /// <summary>How good the plan is, to choose between transcriptions.</summary>
    public double Score => Parts.Count > 0 ? Parts.Average(p => p.Confidence) - 0.3 * Unmatched.Count : Whole.Confidence * 0.5;
}

/// <summary>
/// Spoken text → command, first layer: how much of a command's name is in what was said. Words may differ in their
/// ending («abre» / «abrir»), have small transcription slips, be split or joined («you tube» / YouTube), and filler
/// words («la», «por favor»…) are ignored. Later layers (embeddings, LLM) come into play when this one is unsure.
/// </summary>
public static class IntentMatcher
{
    /// <summary>Minimum score to run a command.</summary>
    public const double Accept = 0.82;

    /// <summary>The winner must beat the second by this much.</summary>
    public const double Margin = 0.08;

    /// <summary>Filler words, verbs, synonyms and joiners of every installed language (see <see cref="VoiceVocabulary"/>).</summary>
    private static VoiceVocabulary Words => VoiceVocabulary.Current;

    private static double WordScore(string written, string said)
    {
        var s = TextSimilarity.WordSimilarity(written, said);
        if (s < 1 && Words.Synonyms.TryGetValue(written, out var a) && Words.Synonyms.TryGetValue(said, out var b) && a == b) s = Math.Max(s, 0.9);
        return Math.Max(s, SoundsLike(written, said));
    }

    /// <summary>
    /// The same by sound, a little below spelling (Parakeet hears «vacía» as «va hacia», «youtube» as «yutub»). Only
    /// for words of four letters or more: short ones sound alike too easily.
    /// </summary>
    private static double SoundsLike(string written, string said)
    {
        if (written.Length < 4 || said.Length < 4) return 0;
        return 0.95 * PhoneticKey.Similarity(PhoneticKey.Encode(written), PhoneticKey.Encode(said));
    }

    /// <summary>
    /// The whole text as one order; if that names no command clearly, the text split at «y», «luego», «después»,
    /// commas… and each piece matched on its own.
    /// </summary>
    public static IntentPlan Plan(string text, IEnumerable<Command> commands)
    {
        var list = commands as IReadOnlyCollection<Command> ?? commands.ToList();
        var whole = Match(text, list);
        if (whole.Command is not null) return new IntentPlan(whole, [whole], []);
        var pieces = SplitOrders(text);
        if (pieces.Count < 2) return new IntentPlan(whole, [], []);
        var results = pieces.Select(p => Match(p, list)).ToList();
        var matched = results.Where(r => r.Command is not null).ToList();
        return matched.Count == 0
            ? new IntentPlan(whole, [], [])
            : new IntentPlan(whole, matched, results.Where(r => r.Command is null).Select(r => r.Text).ToList());
    }

    /// <summary>The orders of a multiple one; pieces without letters are dropped.</summary>
    public static List<string> SplitOrders(string text) =>
        Words.Joiners.Split(" " + text.Trim().TrimEnd('.', '!', '?') + " ")
            .Select(p => Words.LeadingJoiners.Replace(p.Trim(' ', '.', '!', '?', '¡', '¿'), ""))
            .Where(p => p.Any(char.IsLetterOrDigit))
            .ToList();


    public static IntentResult Match(string text, IEnumerable<Command> commands)
    {
        var spoken = Content(TextSimilarity.Words(text));
        if (spoken.Length == 0) return new IntentResult(text, null, []);

        var ranked = commands
            .Select(c => new IntentCandidate(c, Score(spoken, Content(TextSimilarity.Words(c.Name)))))
            .Where(c => c.Score > 0.3)
            .OrderByDescending(c => c.Score)
            .Take(5)
            .ToList();

        Command? winner = null;
        if (ranked.Count > 0 && ranked[0].Score >= Accept && (ranked.Count == 1 || ranked[0].Score - ranked[1].Score >= Margin))
            winner = ranked[0].Command;
        return new IntentResult(text, winner, ranked);
    }

    /// <summary>The words that carry meaning (all of them if they are all filler: a command may be called «La»).</summary>
    private static string[] Content(string[] words)
    {
        var filler = Words.Filler;
        var content = words.Where(w => !filler.Contains(w)).ToArray();
        return content.Length > 0 ? content : words;
    }

    /// <summary>
    /// Average, over the name's words, of how well each one was said; minus a little for each extra spoken word
    /// (a verb like «abre» costs little; a whole different phrase costs a lot). The name said as a whole, without
    /// spaces, also counts.
    /// </summary>
    internal static double Score(string[] spoken, string[] name)
    {
        if (name.Length == 0) return 0;
        var vocabulary = Words;
        var used = new bool[spoken.Length];
        double total = 0, weights = 0;
        foreach (var word in name)
        {
            var best = 0.0;
            var bestIndex = -1;
            var bestIsPair = false;
            for (var i = 0; i < spoken.Length; i++)
            {
                var s = WordScore(word, spoken[i]);
                if (s > best)
                {
                    best = s;
                    bestIndex = i;
                    bestIsPair = false;
                }
                // Two spoken words for one written («you tube» → youtube, «va hacia» → vacía): both are explained.
                if (i + 1 >= spoken.Length) continue;
                var together = spoken[i] + spoken[i + 1];
                var pair = Math.Max(TextSimilarity.WordSimilarity(word, together), SoundsLike(word, together));
                if (pair > best)
                {
                    best = pair;
                    bestIndex = i;
                    bestIsPair = true;
                }
            }
            if (best < 0.6) best = 0;
            else
            {
                used[bestIndex] = true;
                if (bestIsPair) used[bestIndex + 1] = true;
            }
            // A generic verb of the name («Abrir Papelera») may go unsaid: «papelera» is enough.
            var weight = vocabulary.Verbs.Contains(word) && best == 0 ? 0 : 1;
            total += best * weight;
            weights += weight;
        }
        if (weights == 0) return 0;
        var coverage = total / weights * (weights < name.Length ? 0.95 : 1);
        // Spoken words the name does not explain: «abre» costs little, «cierra» or «apaga» a lot (they ask for
        // something else: «cierra AIMP» is not "AIMP").
        // A second word of something already matched costs nothing («recycle bin» for «papelera»).
        var matchedConcepts = spoken.Where((_, i) => used[i]).Select(w => vocabulary.Synonyms.GetValueOrDefault(w)).OfType<string>().ToHashSet();
        var extra = 0.0;
        for (var i = 0; i < spoken.Length; i++)
        {
            if (used[i]) continue;
            if (vocabulary.Synonyms.TryGetValue(spoken[i], out var concept) && matchedConcepts.Contains(concept)) continue;
            extra += vocabulary.DoIt.Contains(spoken[i]) ? 0.05 : 0.2;
        }
        var score = coverage - Math.Min(0.4, extra);

        // «acestep» for "ACE Step", «drivee» for "Drive E".
        var joined = TextSimilarity.Ratio(string.Concat(spoken), string.Concat(name));
        return Math.Max(score, joined >= 0.8 ? joined : 0);
    }
}
