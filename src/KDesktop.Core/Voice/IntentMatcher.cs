using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

public sealed record IntentCandidate(Command Command, double Score);

/// <param name="Command">The command to run, when there is a clear winner.</param>
/// <param name="Candidates">Best matches first (at most a few).</param>
public sealed record IntentResult(string Text, Command? Command, IReadOnlyList<IntentCandidate> Candidates)
{
    public double Confidence => Candidates.Count > 0 ? Candidates[0].Score : 0;
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

    private static readonly HashSet<string> Filler =
    [
        "el", "la", "los", "las", "lo", "un", "una", "unos", "unas", "de", "del", "al", "a", "en", "por", "para",
        "favor", "porfa", "porfavor", "me", "mi", "mis", "tu", "que", "y", "o", "con", "eh", "pues", "venga", "ahora",
        // Asking politely («¿puedes vaciar la papelera?», «oye, abre…»).
        "puedes", "puede", "pueden", "podrias", "podria", "podeis", "quieres", "oye", "hey",
        "the", "an", "to", "please", "my", "of", "and", "now", "can", "could", "would", "you",
    ];

    /// <summary>Verbs that name what every command does: leaving them out of the order is fine.</summary>
    private static readonly HashSet<string> OptionalVerbs =
        ["abrir", "abre", "ejecutar", "ejecuta", "lanzar", "lanza", "iniciar", "inicia", "mostrar", "muestra", "ver",
         "open", "run", "launch", "start", "show"];

    /// <summary>Words that only say «do it»: saying them without them being in the name costs little.</summary>
    private static readonly HashSet<string> CheapExtra =
        ["abrir", "abre", "abreme", "ejecutar", "ejecuta", "lanzar", "lanza", "iniciar", "inicia", "arranca", "mostrar",
         "muestra", "muestrame", "ensena", "ensename", "pon", "ponme", "quiero", "dame", "activa", "haz", "ver",
         "open", "run", "launch", "start", "show"];

    /// <summary>
    /// Spanish and English words for the same thing, so «cierra AIMP» finds "AIMP - Close". The first of each group is
    /// the key; the forms are listed one by one («cierra», «cerrar», «cierre»…).
    /// </summary>
    private static readonly string[][] Synonyms =
    [
        ["close", "cerrar", "cierra", "cierre", "cierro", "quit", "salir", "sal"],
        ["open", "abrir", "abre", "abreme"],
        ["mute", "silenciar", "silencia", "silencio", "mutear", "mutea"],
        ["volume", "volumen"],
        ["up", "subir", "sube", "mas"],
        ["down", "bajar", "baja", "menos"],
        ["next", "siguiente", "proxima", "proximo"],
        ["previous", "anterior", "prev"],
        ["play", "reproducir", "reproduce", "dale", "pon"],
        ["pause", "pausa", "pausar"],
        ["stop", "parar", "detener", "deten"],
        ["folder", "carpeta"],
        ["settings", "ajustes", "configuracion", "opciones"],
        ["audio", "sonido"],
        ["screen", "pantalla"],
        ["window", "ventana"],
        ["hidden", "ocultos", "ocultas", "oculto"],
        ["files", "archivos", "ficheros"],
        ["recycle", "papelera"],
        ["web", "pagina", "website"],
        ["shutdown", "apagar", "apaga"],
        ["restart", "reiniciar", "reinicia"],
        ["lock", "bloquear", "bloquea"],
        ["sleep", "suspender", "suspende"],
    ];

    private static readonly Dictionary<string, string> SynonymKey =
        Synonyms.SelectMany(g => g.Select(w => (w, g[0]))).GroupBy(p => p.w).ToDictionary(g => g.Key, g => g.First().Item2);

    private static double WordScore(string written, string said)
    {
        var s = TextSimilarity.WordSimilarity(written, said);
        if (s < 1 && SynonymKey.TryGetValue(written, out var a) && SynonymKey.TryGetValue(said, out var b) && a == b) s = Math.Max(s, 0.9);
        return s;
    }

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
        var content = words.Where(w => !Filler.Contains(w)).ToArray();
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
        var used = new bool[spoken.Length];
        double total = 0, weights = 0;
        foreach (var word in name)
        {
            var best = 0.0;
            var bestIndex = -1;
            for (var i = 0; i < spoken.Length; i++)
            {
                var s = WordScore(word, spoken[i]);
                // Two spoken words for one written («you tube» → youtube).
                if (i + 1 < spoken.Length) s = Math.Max(s, TextSimilarity.WordSimilarity(word, spoken[i] + spoken[i + 1]));
                if (s > best)
                {
                    best = s;
                    bestIndex = i;
                }
            }
            if (best < 0.6) best = 0;
            else used[bestIndex] = true;
            // A generic verb of the name («Abrir Papelera») may go unsaid: «papelera» is enough.
            var weight = OptionalVerbs.Contains(word) && best == 0 ? 0 : 1;
            total += best * weight;
            weights += weight;
        }
        if (weights == 0) return 0;
        var coverage = total / weights * (weights < name.Length ? 0.95 : 1);
        // Spoken words the name does not explain: «abre» costs little, «cierra» or «apaga» a lot (they ask for
        // something else: «cierra AIMP» is not "AIMP").
        var extra = 0.0;
        for (var i = 0; i < spoken.Length; i++)
            if (!used[i]) extra += CheapExtra.Contains(spoken[i]) ? 0.05 : 0.2;
        var score = coverage - Math.Min(0.4, extra);

        // «acestep» for "ACE Step", «drivee» for "Drive E".
        var joined = TextSimilarity.Ratio(string.Concat(spoken), string.Concat(name));
        return Math.Max(score, joined >= 0.8 ? joined : 0);
    }
}
