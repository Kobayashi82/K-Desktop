using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

/// <summary>One thing an order may ask for: a command, or a built-in media order (<see cref="MediaOrders"/>).</summary>
/// <param name="Semantic">Found by its meaning (<see cref="SemanticMatcher"/>), not by its words.</param>
/// <param name="ByName">Its score comes from the command's name, not from one of its voice phrases.</param>
public sealed record IntentCandidate(Command? Command, double Score, MediaOrder? Media = null, string? Phrase = null, bool Semantic = false,
                                     bool ByName = false)
{
    public string Name => Media?.Name ?? Command!.Name;

    /// <summary>For the log: a media order with the phrase it matched; «≈» for the meaning.</summary>
    public string Describe => Media is null
        ? (Semantic ? "≈" : "") + Command!.Name
        : $"{(Semantic ? "≈" : "")}{Media.Name} [{Media.Kind}{(Media.Value is { } v ? " " + v : "")}{(Media.Amount != MediaAmount.Normal ? " " + Media.Amount : "")} · «{Phrase}»]";

}

/// <param name="Winner">What to run, when there is a clear winner.</param>
/// <param name="Candidates">Best matches first (at most a few).</param>
/// <param name="Note">For the log: what the meaning layer said, when it was asked.</param>
public sealed record IntentResult(string Text, IntentCandidate? Winner, IReadOnlyList<IntentCandidate> Candidates, string? Note = null)
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
/// Spoken text → command. First layer: how much of a command's name is in what was said. Words may differ in their
/// ending («abre» / «abrir»), have small transcription slips, be split or joined («you tube» / YouTube), and filler
/// words («la», «por favor»…) are ignored. When it names nothing clearly, the second layer: what the order means
/// (<see cref="SemanticMatcher"/>, when its model is installed). A later LLM layer may come after.
/// </summary>
public static class IntentMatcher
{
    /// <summary>Minimum score to run a command.</summary>
    public const double Accept = 0.82;

    /// <summary>The winner must beat the second by this much.</summary>
    public const double Margin = 0.08;

    /// <summary>
    /// A command of the user this close to a media order wins over it (it may be their own «Siguiente»); enough for a
    /// «pon» more or less.
    /// </summary>
    private const double UserFirst = 0.06;

    /// <summary>What a one-word order must measure by meaning alone (like a built-in order's phrase).</summary>
    private const double LoneWord = SemanticMatcher.AcceptOrder;

    /// <summary>Filler words, verbs, synonyms and joiners of every installed language (see <see cref="VoiceVocabulary"/>).</summary>
    private static VoiceVocabulary Words => VoiceVocabulary.Current;

    private static double WordScore(string written, string said, bool synonyms)
    {
        var s = TextSimilarity.WordSimilarity(written, said);
        if (synonyms && s < 1 && Words.Synonyms.TryGetValue(written, out var a) && Words.Synonyms.TryGetValue(said, out var b) && a == b) s = Math.Max(s, 0.9);
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
    /// <param name="media">Also the built-in media orders (Settings › Multimedia).</param>
    /// <param name="semantic">The meaning layer, or null for the words only.</param>
    /// <remarks>
    /// A whole order that only its meaning matched may be one of its parts («vacía la papelera y ábrela» is close to
    /// "Vaciar Papelera"): its pieces are matched too, and win when two or more of them name something. A piece that is
    /// only a verb and a pronoun («la abres», «ábrela», «open it») is about the thing of the piece before.
    /// </remarks>
    public static IntentPlan Plan(string text, IEnumerable<Command> commands, bool media = false, SemanticMatcher? semantic = null)
    {
        var list = commands as IReadOnlyCollection<Command> ?? commands.ToList();
        var whole = Match(text, list, media, semantic);
        if (whole.Winner is { Semantic: false }) return new IntentPlan(whole, [whole], []);
        var pieces = SplitOrders(text);
        if (pieces.Count < 2) return whole.Winner is null ? new IntentPlan(whole, [], []) : new IntentPlan(whole, [whole], []);
        var results = new List<IntentResult>();
        var referred = false;
        for (var i = 0; i < pieces.Count; i++)
        {
            var result = Match(pieces[i], list, media, semantic);
            // Resolved even when no command takes it: what is left says what it means («minimiza steam», not «minimízalo»).
            if (result.Winner is null && i > 0 && WithThing(pieces[i], results[i - 1].Text) is { } meant)
            {
                result = Match(meant, list, media, semantic);
                referred = true;
            }
            results.Add(result);
        }
        var matched = results.Where(r => r.Winner is not null).ToList();
        // A piece about the thing before («… y ciérralo») is another order for sure: one part named is enough.
        if (whole.Winner is not null && matched.Count < (referred ? 1 : 2)) return new IntentPlan(whole, [whole], []);
        return matched.Count == 0
            ? new IntentPlan(whole, [], [])
            : new IntentPlan(whole, matched, results.Where(r => r.Winner is null).Select(r => r.Text).ToList());
    }

    /// <summary>
    /// «la abres» after «vacía la papelera» → «abres la papelera»: a piece that is a verb and a pronoun (on its own or at
    /// the end of the verb: «ábrela») with the thing of <paramref name="before"/> (its words after the first, its verb).
    /// Null when it is not that («la vuelta»: no verb).
    /// </summary>
    internal static string? WithThing(string piece, string before)
    {
        var vocabulary = Words;
        string? verb = null;
        var pronoun = false;
        foreach (var word in TextSimilarity.Words(piece))
        {
            if (vocabulary.Pronouns.Contains(word)) pronoun = true;
            else if (vocabulary.Filler.Contains(word)) continue;
            else if (verb is not null) return null;
            else verb = word;
        }
        if (verb is null) return null;
        if (!pronoun && vocabulary.Enclitics.FirstOrDefault(e => verb.Length >= e.Length + 3 && verb.EndsWith(e, StringComparison.Ordinal)) is { } ending
            && ActionOf(verb[..^ending.Length]) is { } stem)
        {
            verb = stem;
            pronoun = true;
        }
        if (!pronoun || ActionOf(verb) is not { } action) return null;
        verb = action;
        var words = before.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = Array.FindIndex(words, w => TextSimilarity.Words(w) is [var f, ..] && !vocabulary.Filler.Contains(f));
        var thing = first >= 0 && words.Skip(first + 1).Any(w => TextSimilarity.Words(w).Any(f => !vocabulary.Filler.Contains(f)))
            ? string.Join(' ', words.Skip(first + 1))
            : before;
        return verb + " " + thing.Trim(' ', '.', ',', '!', '?');
    }

    /// <summary>
    /// The word, if it is a verb of doing something to a thing; the verb it is a slip of (Parakeet's «habre» → «abre»);
    /// or null.
    /// </summary>
    private static string? ActionOf(string word)
    {
        if (Words.Actions.Contains(word)) return word;
        return Words.Actions.Where(a => a.Length >= 3)
            .Select(a => (Verb: a, Score: TextSimilarity.WordSimilarity(a, word)))
            .Where(p => p.Score >= 0.8)
            .OrderByDescending(p => p.Score)
            .Select(p => p.Score >= 0.85 ? word : p.Verb)
            .FirstOrDefault();
    }

    /// <summary>
    /// «no anterior», «no, la papelera no»: the order starts by saying no, so it does not ask for a command unless the
    /// command itself says it («No molestar»).
    /// </summary>
    public static bool Refuses(string said, IntentCandidate candidate)
    {
        var vocabulary = Words;
        if (TextSimilarity.Words(said).FirstOrDefault(w => !vocabulary.Filler.Contains(w) || vocabulary.Negation.Contains(w)) is not { } first
            || !vocabulary.Negation.Contains(first))
            return false;
        var named = candidate.Media is null ? candidate.Command!.Name + " " + candidate.Phrase : candidate.Phrase ?? candidate.Media.Name;
        return !TextSimilarity.Words(named).Any(vocabulary.Negation.Contains);
    }

    /// <summary>The orders of a multiple one; pieces without letters are dropped.</summary>
    public static List<string> SplitOrders(string text) =>
        Words.Joiners.Split(" " + text.Trim().TrimEnd('.', '!', '?') + " ")
            .Select(p => Words.LeadingJoiners.Replace(p.Trim(' ', '.', '!', '?', '¡', '¿'), ""))
            .Where(p => p.Any(char.IsLetterOrDigit))
            .ToList();


    public static IntentResult Match(string text, IEnumerable<Command> commands, bool media = false, SemanticMatcher? semantic = null)
    {
        var list = commands as IReadOnlyCollection<Command> ?? commands.ToList();
        var words = MatchWords(text, list, media);
        var result = words.Winner is not null || semantic is null || !SemanticMatcher.Available ? words : MatchMeaning(words, list, media, semantic);
        return result.Winner is { } winner && Refuses(text, winner) ? result with { Winner = null } : result;
    }

    /// <summary>
    /// The words named nothing clearly: the closest command in meaning, if close enough and ahead of the next command
    /// and of the «not mine» phrases. When the words already pointed to the same command, a little less is enough.
    /// </summary>
    private static IntentResult MatchMeaning(IntentResult words, IReadOnlyCollection<Command> commands, bool media, SemanticMatcher semantic)
    {
        SemanticScores? scores;
        try
        {
            scores = semantic.Score(words.Text, commands, media);
        }
        catch (Exception ex)
        {
            Log.Error("Error en la capa de significado", ex);
            return words;
        }
        if (scores is not { Ranked: [var top, ..] }) return words;
        var rival = Math.Max(scores.Ranked.Count > 1 ? scores.Ranked[1].Score : -1, scores.None);
        var agrees = words.Candidates is [{ Score: >= 0.6 } guess, ..]
                     && (top.Command is not null ? guess.Command == top.Command : guess.Media?.Kind == top.Media);
        // The words point to another one that the meaning puts nearly as close: the words know better («ejecuta el abrir
        // papelera» was "Vaciar Papelera" 0.75 by meaning, "Abrir Papelera" 0.72).
        var contradicted = !agrees && words.Candidates is [{ Score: >= 0.6 } said, ..]
                           && scores.Ranked.FirstOrDefault(r => said.Command is not null ? r.Command == said.Command : r.Media == said.Media?.Kind) is { } named
                           && top.Score - named.Score < Margin;
        // A lone word makes a poor vector: «coño» was "Kokoro" at 0.71. Unless the words agree, it must be much closer.
        var lone = Content(TextSimilarity.Words(words.Text)).Length == 1 && !agrees;
        var accepted = !contradicted && (!lone || top.Score >= LoneWord) && scores.None < top.Score
                       && (top.Media is null || top.Score >= SemanticMatcher.AcceptOrder)
                       && (top.Command is null || !SemanticMatcher.IsPower(top.Command) || top.Score >= SemanticMatcher.AcceptPower)
                       && (top.Score >= SemanticMatcher.Accept && top.Score - rival >= SemanticMatcher.Margin
                           || agrees && top.Score >= SemanticMatcher.Accept - SemanticMatcher.Margin && top.Score > rival);
        var found = scores.Ranked.Take(3).Select(r => r.Media is { } kind
            ? new IntentCandidate(null, r.Score, new MediaOrder(kind), r.Anchor, Semantic: true)
            : new IntentCandidate(r.Command, r.Score, Semantic: true)).ToList();
        var note = $"significado: {string.Join(", ", found.Select(f => $"{f.Describe} {f.Score:0.00}"))}; «{scores.NonePhrase}» {scores.None:0.00}" +
                   (accepted ? "" : " → nada");
        // The winner first; otherwise the words' guesses as before, and what the meaning suggests after them (nothing
        // when an everyday «not mine» request was closer: «¿Querías decir Apagar Equipo?» for «apaga la tele» is no help).
        List<IntentCandidate> candidates = accepted ? [found[0], .. words.Candidates, .. found.Skip(1)]
            : scores.None >= top.Score ? [.. words.Candidates]
            : [.. words.Candidates, .. found];
        return new IntentResult(words.Text, accepted ? found[0] : null, candidates, note);
    }

    private static IntentResult MatchWords(string text, IEnumerable<Command> commands, bool media)
    {
        var spoken = Content(TextSimilarity.Words(text));
        if (spoken.Length == 0) return new IntentResult(text, null, []);

        // The name or any of its voice phrases, whichever fits best.
        var all = commands.Select(c =>
        {
            var byName = Score(spoken, Content(TextSimilarity.Words(c.Name)));
            var byPhrase = c.VoicePhraseList.Select(p => Score(spoken, Content(TextSimilarity.Words(p)))).DefaultIfEmpty(0).Max();
            return new IntentCandidate(c, Math.Max(byName, byPhrase), ByName: byName >= byPhrase);
        });
        // «Cancela» is always there; the rest of the media orders only when they are on.
        all = all.Concat(MediaOrders.Match(text).Where(m => media || m.Order.Kind == MediaOrderKind.Cancel)
            .Select(m => new IntentCandidate(null, m.Score, m.Order, m.Phrase)));
        var ranked = all
            .Where(c => c.Score > 0.3)
            .OrderByDescending(c => c.Score)
            .Take(5)
            .ToList();

        if (ranked.Count == 0) return new IntentResult(text, null, ranked);
        var top = ranked[0];
        var rivals = ranked.Skip(1).ToList();
        // A command of the user about as good as the best is what they meant (their own «Siguiente» or «Música»): it
        // goes first and the media orders do not count against it.
        if (ranked.FirstOrDefault(c => c.Command is not null) is { } own && top.Score - own.Score <= UserFirst)
        {
            rivals = ranked.Where(c => c.Command is not null && c != own).ToList();
            ranked.Remove(own);
            ranked.Insert(0, own);
            top = own;
        }
        var second = rivals.FirstOrDefault();
        var winner = top.Score >= Accept && (second is null || top.Score - second.Score >= Margin) ? top : null;
        // A tie between a command's name and another one's voice phrase («Patata Frita» and a phrase «patata frita»):
        // the name wins, it is what the user sees in the list.
        if (winner is null && top.Score >= Accept && rivals.Where(r => top.Score - r.Score < Margin).Prepend(top).ToList() is var close
            && close.All(c => c.Command is not null) && close.Count(c => c.ByName) == 1
            && close.Single(c => c.ByName) is var named && named.Score >= top.Score)
        {
            winner = named;
            ranked.Remove(named);
            ranked.Insert(0, named);
        }
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
    /// spaces, also counts. Without <paramref name="synonyms"/> words of the same concept do not count as alike (the
    /// media phrases list every wording themselves, and «pon» must not pass for «riprendi»).
    /// </summary>
    internal static double Score(string[] spoken, string[] name, bool synonyms = true)
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
                var s = WordScore(word, spoken[i], synonyms);
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
        var matchedConcepts = synonyms
            ? spoken.Where((_, i) => used[i]).Select(w => vocabulary.Synonyms.GetValueOrDefault(w)).OfType<string>().ToHashSet()
            : [];
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
        score = Math.Max(score, joined >= 0.8 ? joined : 0);

        // A name said as it sounds: «abre Kamovis» for "K-Movies" (a lone letter read out, English read the Spanish way).
        if (SoundScore(spoken, name) is { Similarity: >= SoundsAlike } sound)
        {
            var rest = 0.0;
            for (var i = 0; i < spoken.Length; i++)
                if (i < sound.From || i >= sound.From + sound.Length) rest += vocabulary.DoIt.Contains(spoken[i]) ? 0.05 : 0.2;
            score = Math.Max(score, 0.95 * sound.Similarity - Math.Min(0.4, rest));
        }
        return score;
    }

    /// <summary>How alike the sound of a name and of some words said together must be to count.</summary>
    public const double SoundsAlike = 0.8;

    /// <summary>Words said together, at most, for the sound of one name.</summary>
    private const int SoundWindow = 3;

    /// <summary>
    /// How much a few words said in a row sound like the whole name (<see cref="PhoneticKey"/>; a lone letter of the name by
    /// its spoken name, «K» = «ka»): the best run of words and how alike, 0–1. Names that sound like less than five letters
    /// do not count (short ones sound alike too easily).
    /// </summary>
    public static (double Similarity, int From, int Length) SoundScore(string[] spoken, string[] name)
    {
        var key = PhoneticKey.Of(string.Join(' ', name));
        if (key.Length < 5 || spoken.Length == 0) return (0, 0, 0);
        var best = (Similarity: 0.0, From: 0, Length: 0);
        for (var from = 0; from < spoken.Length; from++)
            for (var length = 1; length <= SoundWindow && from + length <= spoken.Length; length++)
            {
                var said = PhoneticKey.Of(string.Join(' ', spoken, from, length));
                var similarity = PhoneticKey.Similarity(key, said);
                if (similarity > best.Similarity) best = (similarity, from, length);
            }
        return best;
    }
}
