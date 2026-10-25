using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Voice;

// Experiment: a decision model next to EmbeddingGemma (see tools\decision\README.md).
//   baseline --config <config.json> --cases <cases.json> --data <scratch folder> --out <baseline.json>
//   decide   --baseline <baseline.json> --method systemone|letter --url http://127.0.0.1:8091 --name <name> --out <file> [--top 8]
//   report   --baseline <baseline.json> --results <file>[,<file>…]
return args.Length == 0 ? Usage() : args[0] switch
{
    "baseline" => Bench.Baseline(Arg(args, "--config"), Arg(args, "--cases"), Arg(args, "--data"), Arg(args, "--out")),
    "decide" => await Bench.DecideAsync(Arg(args, "--baseline"), Arg(args, "--method"), Arg(args, "--url"), Arg(args, "--name"), Arg(args, "--out"),
                                        int.Parse(Arg(args, "--top", "8"), CultureInfo.InvariantCulture)),
    "report" => Bench.Report(Arg(args, "--baseline"), Arg(args, "--results", "").Split(',', StringSplitOptions.RemoveEmptyEntries)),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("DecisionBench baseline|decide|report … (see Program.cs)");
    return 2;
}

static string Arg(string[] args, string name, string? fallback = null)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback ?? throw new ArgumentException("Missing " + name);
}

internal static class Bench
{
    // What an assistant runs at once, as TalkService does: CommandTools.Direct, .Clear, .Ahead and TalkService.Close (in
    // KDesktop.Assistant.Tools / .Brain, not referenced here: keep them in step).
    private const double Direct = 0.80;
    private const double Clear = 0.75;
    private const double Ahead = 0.08;
    private const double Close = 0.08;

    /// <summary>Clear by meaning alone: this close and this far ahead of the next one and of the «not mine» phrases.</summary>
    private const double ClearScore = 0.80;
    private const double ClearLead = 0.08;

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---------------------------------------------------------------- baseline

    public static int Baseline(string configPath, string casesPath, string dataDir, string outPath)
    {
        AppPaths.UseDataDir(dataDir);
        Loc.Instance.Load("es");
        var config = Json.Deserialize<AppConfig>(File.ReadAllText(configPath)) ?? throw new InvalidOperationException("config");
        config.Upgrade();
        var media = config.Settings.VoiceMedia;
        var voiceCommands = VoiceService.VoiceCommands(config).ToList();
        var assistantCommands = AssistantCommands.Available(config);
        if (!SemanticMatcher.Available) throw new InvalidOperationException("EmbeddingGemma is not in " + VoiceModels.EmbeddingDir);
        using var semantic = new SemanticMatcher();
        semantic.Score("hola", voiceCommands, media);

        var cases = JsonNode.Parse(File.ReadAllText(casesPath))!.AsArray();
        var output = new JsonArray();
        foreach (var item in cases)
        {
            var say = item!["say"]!.GetValue<string>();
            var watch = Stopwatch.StartNew();
            var plan = IntentMatcher.Plan(say, voiceCommands, media, semantic);
            var voiceMs = watch.Elapsed.TotalMilliseconds;
            var matched = AssistantCommands.Match(new MatchMessage("bench", say), assistantCommands, media, semantic);
            var pieces = IntentMatcher.SplitOrders(say);
            if (pieces.Count < 2) pieces = [say];
            var pieceNodes = new JsonArray();
            foreach (var piece in pieces)
            {
                var scores = semantic.Score(piece, voiceCommands, media)!;
                var candidates = new JsonArray();
                foreach (var hit in scores.Ranked)
                    candidates.Add(new JsonObject
                    {
                        ["key"] = Key(hit),
                        ["name"] = hit.Media is { } kind ? new MediaOrder(kind).Name : hit.Command!.Name.Trim(),
                        ["desc"] = hit.Media is { } k ? "Orden incluida de K-Desktop: " + new MediaOrder(k).Name : Describe(hit.Command!),
                        ["score"] = Math.Round(hit.Score, 4),
                        ["power"] = hit.Command is { } c && SemanticMatcher.IsPower(c),
                    });
                pieceNodes.Add(new JsonObject
                {
                    ["text"] = piece,
                    ["none"] = Math.Round(scores.None, 4),
                    ["nonePhrase"] = scores.NonePhrase,
                    ["cands"] = candidates,
                });
            }
            output.Add(new JsonObject
            {
                ["say"] = say,
                ["lang"] = item["lang"]?.GetValue<string>() ?? "es",
                ["cat"] = item["cat"]!.GetValue<string>(),
                ["expect"] = item["expect"]!.DeepClone(),
                ["voice"] = plan.Parts.Count > 0 ? "run:" + string.Join("+", plan.Parts.Select(p => Key(p.Winner!))) : "none",
                ["assistant"] = AssistantRoute(matched),
                ["words"] = plan.Parts.Count > 0 && plan.Parts.All(p => p.Winner is { Semantic: false }),
                ["voiceMs"] = Math.Round(voiceMs, 1),
                ["note"] = plan.Whole.Note,
                ["pieces"] = pieceNodes,
            });
        }
        File.WriteAllText(outPath, output.ToJsonString(Pretty));
        Console.WriteLine($"{output.Count} cases → {outPath}");
        return 0;
    }

    private static string Key(SemanticHit hit) => hit.Media is { } kind ? "#" + kind : hit.Command!.Name.Trim();

    private static string Key(IntentCandidate c) => c.Media is { } m ? "#" + m.Kind : c.Command!.Name.Trim();

    // The pipe writes the media order in camel case («mute»).
    private static string Key(MatchedCandidate c) => c.Media is { Length: > 0 } m ? "#" + char.ToUpperInvariant(m[0]) + m[1..] : c.Name.Trim();

    /// <summary>The name and what its first actions do («Abrir carpeta: Papelera de reciclaje»), short.</summary>
    private static string Describe(Command command)
    {
        var parts = command.Actions.Where(a => a.Enabled && a is not ConditionAction).Take(2)
            .Select(a => $"{a.Info.Name}: {Short(a.Summary)}").ToList();
        return parts.Count == 0 ? "Comando de K-Desktop" : string.Join("; ", parts);
    }

    private static string Short(string text)
    {
        text = text.Trim();
        // A path says its program by its file name: «D:\…\ComfyUI\run.bat» → «ComfyUI\run.bat».
        if (text.Length > 3 && text[1] == ':' && text.Contains('\\', StringComparison.Ordinal))
        {
            var bits = text.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            text = string.Join('\\', bits.TakeLast(Math.Min(2, bits.Length)));
        }
        return text.Length <= 70 ? text : text[..70] + "…";
    }

    /// <summary>What TalkService does with K-Desktop's answer for a small model (not a question, no taught phrase).</summary>
    private static string AssistantRoute(MatchedMessage matched)
    {
        static bool IsDirect(MatchedPart part) => part.Sure && part.Candidates[0].Score >= Direct;
        var sure = matched.Parts.Where(IsDirect).Select(p => p.Candidates[0]).DistinctBy(Key).ToList();
        if (sure.Count == 0 && matched.Parts is [{ Sure: false, Candidates: [var only, ..] } whole] && only.Score >= Direct
            && (whole.Candidates.Count < 2 || only.Score - whole.Candidates[1].Score >= Ahead))
            return "run:" + Key(only);
        if (sure.Count > 0) return "run:" + string.Join("+", sure.Select(Key));
        if (matched.Parts is [{ Candidates: [var first, var second, ..] }] && first.Score >= Direct && second.Score >= Clear
            && first.Score - second.Score < Close)
            return $"ask:{Key(first)}|{Key(second)}";
        return "none";
    }

    // ---------------------------------------------------------------- decide

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private const string Letters = "ABCDEFGHIJ";

    public static async Task<int> DecideAsync(string baselinePath, string method, string url, string name, string outPath, int top)
    {
        var cases = JsonNode.Parse(File.ReadAllText(baselinePath))!.AsArray();
        var output = new JsonArray();
        // The first call of a shape costs more: one throwaway.
        await AskAsync(method, url, "abre steam", [("Steam", "Steam", "Programa: steam.exe")]).ConfigureAwait(false);
        foreach (var item in cases)
        {
            var pieces = new JsonArray();
            foreach (var piece in item!["pieces"]!.AsArray())
            {
                var options = piece!["cands"]!.AsArray().Take(top == 0 ? int.MaxValue : top)
                    .Select(c => (Key: c!["key"]!.GetValue<string>(), Name: c["name"]!.GetValue<string>(), Desc: c["desc"]!.GetValue<string>())).ToList();
                var (probs, ms, raw) = await AskAsync(method, url, piece["text"]!.GetValue<string>(), options).ConfigureAwait(false);
                var p = new JsonObject();
                foreach (var (k, v) in probs.OrderByDescending(x => x.Value)) p[k] = Math.Round(v, 4);
                pieces.Add(new JsonObject { ["probs"] = p, ["ms"] = Math.Round(ms, 1), ["raw"] = raw });
            }
            output.Add(new JsonObject { ["say"] = item["say"]!.GetValue<string>(), ["pieces"] = pieces });
            var best = pieces[0]!["probs"]!.AsObject().First();
            Console.WriteLine($"{item["say"]} → {best.Key} {best.Value} ({pieces[0]!["ms"]} ms)");
        }
        File.WriteAllText(outPath, new JsonObject { ["name"] = name, ["method"] = method, ["top"] = top, ["cases"] = output }.ToJsonString(Pretty));
        return 0;
    }

    /// <summary>The probability of each option (by key) and of «none», how long it took, and what came back raw (letter mode).</summary>
    private static async Task<(Dictionary<string, double> Probs, double Ms, string Raw)> AskAsync(string method, string url, string text,
                                                                                                    List<(string Key, string Name, string Desc)> options)
    {
        var watch = Stopwatch.StartNew();
        var probs = new Dictionary<string, double>();
        var raw = "";
        if (method == "systemone")
        {
            var criteria = new JsonObject();
            var byName = new Dictionary<string, string>();
            foreach (var (key, name, desc) in options)
            {
                var label = byName.ContainsKey(name) ? key : name;
                byName[label] = key;
                criteria[label] = Variant.Contains("nodesc", StringComparison.Ordinal) ? null : desc;
            }
            // «noul»: whether it is a command at all is a question of its own, not one more option.
            var separate = Variant.Contains("noul", StringComparison.Ordinal);
            if (!separate)
            {
                criteria[NoneLabel] = NoneDescription;
                byName[NoneLabel] = "none";
            }
            var body = new JsonObject
            {
                ["state"] = $"Order said to the PC assistant: «{text}»",
                ["questions"] = new JsonObject
                {
                    ["command"] = new JsonObject
                    {
                        ["type"] = "choice",
                        ["instructions"] = Instructions,
                        ["criteria"] = criteria,
                    },
                },
            };
            if (separate)
                body["questions"]!["fits"] = new JsonObject { ["type"] = "noul", ["instructions"] = FitsQuestion };
            using var response = await Http.PostAsJsonAsync(url + "/v1/systemone", body).ConfigureAwait(false);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!;
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(json.ToJsonString());
            foreach (var (label, value) in json["answers"]!["command"]!["probabilities"]!.AsObject())
                probs[byName[label]] = value!.GetValue<double>();
            if (separate)
            {
                var fits = json["answers"]!["fits"]!["noul"]!.GetValue<double>();
                foreach (var k in probs.Keys.ToList()) probs[k] *= fits;
                probs["none"] = 1 - fits;
            }
        }
        else
        {
            var list = new StringBuilder();
            var names = Variant.Contains("nodesc", StringComparison.Ordinal);
            for (var i = 0; i < options.Count; i++)
                list.Append(names ? $"{Letters[i]}) {options[i].Name}\n" : $"{Letters[i]}) {options[i].Name} — {options[i].Desc}\n");
            list.Append(CultureInfo.InvariantCulture, $"Z) {NoneDescription}\n");
            var body = new JsonObject
            {
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = "You route orders said to K-Desktop, the user's PC launcher, to one of their commands. Answer with one letter only." },
                    new JsonObject { ["role"] = "user", ["content"] = $"Order: «{text}»\n\n{Instructions}\n\n{list}\nAnswer with the letter." },
                },
                ["max_tokens"] = 1,
                ["temperature"] = 0,
                ["logprobs"] = true,
                ["top_logprobs"] = 20,
                ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
            };
            using var response = await Http.PostAsJsonAsync(url + "/v1/chat/completions", body).ConfigureAwait(false);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!;
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(json.ToJsonString());
            var first = json["choices"]![0]!["logprobs"]!["content"]![0]!;
            raw = first["token"]!.GetValue<string>();
            foreach (var t in first["top_logprobs"]!.AsArray())
            {
                var token = t!["token"]!.GetValue<string>().Trim();
                if (token.Length != 1) continue;
                var index = token == "Z" ? options.Count : Letters.IndexOf(token[0], StringComparison.Ordinal);
                if (index < 0 || index > options.Count) continue;
                var key = index == options.Count ? "none" : options[index].Key;
                probs[key] = probs.GetValueOrDefault(key) + Math.Exp(t["logprob"]!.GetValue<double>());
            }
            var sum = probs.Values.Sum();
            if (sum > 0) foreach (var k in probs.Keys.ToList()) probs[k] /= sum;
            else probs["none"] = 1;
        }
        return (probs, watch.Elapsed.TotalMilliseconds, raw);
    }

    private const string NoneLabel = "none";

    /// <summary>BENCH_VARIANT: «nodesc» (only the names), «noul» (is it a command: a question apart).</summary>
    private static readonly string Variant = Environment.GetEnvironmentVariable("BENCH_VARIANT") ?? "";

    private const string FitsQuestion =
        "Does this order ask to run a command or program on this PC? No for questions, devices that are not this PC (TV, console, " +
        "lights…) and small talk.";

    private const string Instructions =
        "Which of the user's K-Desktop commands does this order ask to run? Pick «none» when it asks for something else: a question, " +
        "a device that is not this PC (TV, console, lights…), small talk, or when no command fits.";

    private const string NoneDescription = "None of these: something else (a question, another device, small talk) or no command fits";

    // ---------------------------------------------------------------- report

    private sealed record Score(int Ok, int Total, int WrongRuns, int PowerWrong, int Misses, int Asks, int CompoundOk, int CompoundTotal)
    {
        public double Pct => Total == 0 ? 0 : 100.0 * Ok / Total;
    }

    public static int Report(string baselinePath, string[] resultPaths)
    {
        var cases = JsonNode.Parse(File.ReadAllText(baselinePath))!.AsArray().Select(n => n!).ToList();
        var doubtful = cases.Select(IsDoubtful).ToList();
        Console.WriteLine($"Casos: {cases.Count}; en la zona dudosa: {doubtful.Count(d => d)}");
        Console.WriteLine();
        Line("Voz de K-Desktop hoy", cases, doubtful, c => c["voice"]!.GetValue<string>());
        Line("Asistente hoy (sin IA)", cases, doubtful, c => c["assistant"]!.GetValue<string>());
        foreach (var path in resultPaths)
        {
            var result = JsonNode.Parse(File.ReadAllText(path))!;
            var name = result["name"]!.GetValue<string>();
            var decided = result["cases"]!.AsArray().Select(n => n!).ToList();
            var ms = decided.SelectMany(d => d["pieces"]!.AsArray().Select(p => p!["ms"]!.GetValue<double>())).Order().ToList();
            Console.WriteLine();
            Console.WriteLine($"## {name}  (p50 {Pct(ms, 0.5):0} ms, p95 {Pct(ms, 0.95):0} ms, {ms.Count} llamadas)");
            foreach (var run in new[] { 0.5, 0.6, 0.7, 0.8, 0.9 })
                Line($"híbrido ejecutar≥{run:0.0}", cases, doubtful, c => Hybrid(c, decided[cases.IndexOf(c)], run, 0.97, true));
            foreach (var run in new[] { 0.6, 0.8 })
                Line($"solo decisor ≥{run:0.0}", cases, doubtful, c => Hybrid(c, decided[cases.IndexOf(c)], run, 0.97, false));
        }
        return 0;
    }

    private static double Pct(List<double> sorted, double q) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];

    private static void Line(string label, List<JsonNode> cases, List<bool> doubtful, Func<JsonNode, string> decide)
    {
        var outcomes = cases.Select(decide).ToList();
        var all = Measure(cases, outcomes, _ => true);
        var zone = Measure(cases, outcomes, i => doubtful[i]);
        Console.WriteLine($"{label,-26} total {all.Ok,3}/{all.Total} {all.Pct,5:0.0}% · dudosa {zone.Ok,3}/{zone.Total} {zone.Pct,5:0.0}% · " +
                          $"ejecuta mal {all.WrongRuns,2} (apagar {all.PowerWrong}) · no hace {all.Misses,2} · pregunta {all.Asks,2} · compuestas {all.CompoundOk}/{all.CompoundTotal}");
        if (Environment.GetEnvironmentVariable("BENCH_FAILS") == "1")
            for (var i = 0; i < cases.Count; i++)
                if (!IsOk(outcomes[i], cases[i]["expect"]!.AsArray()))
                    Console.WriteLine($"    ✗ «{cases[i]["say"]}» → {outcomes[i]}   (quería {string.Join(" o ", cases[i]["expect"]!.AsArray().Select(e => e!.GetValue<string>()))})");
    }

    private static Score Measure(List<JsonNode> cases, List<string> outcomes, Func<int, bool> take)
    {
        int ok = 0, total = 0, wrong = 0, power = 0, misses = 0, asks = 0, cok = 0, ctotal = 0;
        for (var i = 0; i < cases.Count; i++)
        {
            if (!take(i)) continue;
            total++;
            var expect = cases[i]["expect"]!.AsArray();
            var outcome = outcomes[i];
            var good = IsOk(outcome, expect);
            if (good) ok++;
            if (outcome.StartsWith("ask:", StringComparison.Ordinal)) asks++;
            if (cases[i]["cat"]!.GetValue<string>() == "compound") { ctotal++; if (good) cok++; }
            if (good) continue;
            if (outcome.StartsWith("run:", StringComparison.Ordinal))
            {
                wrong++;
                if (outcome.Contains("Apagar Equipo", StringComparison.Ordinal)) power++;
            }
            else if (outcome == "none") misses++;
        }
        return new Score(ok, total, wrong, power, misses, asks, cok, ctotal);
    }

    private static bool IsOk(string outcome, JsonArray expect)
    {
        foreach (var e in expect.Select(e => e!.GetValue<string>()))
        {
            if (e == outcome) return true;
            if (e.StartsWith("run:", StringComparison.Ordinal) && outcome.StartsWith("run:", StringComparison.Ordinal)
                && e[4..].Split('+').ToHashSet().SetEquals(outcome[4..].Split('+')))
                return true;
            if (e.StartsWith("ask:", StringComparison.Ordinal) && outcome.StartsWith("ask:", StringComparison.Ordinal))
            {
                var allowed = e[4..].Split('|').ToHashSet();
                if (outcome[4..].Split('|').All(allowed.Contains)) return true;
            }
        }
        return false;
    }

    /// <summary>The words named nothing for sure and the meaning is not clear: where the decision model would come in.</summary>
    private static bool IsDoubtful(JsonNode c) =>
        !c["words"]!.GetValue<bool>() && c["pieces"]!.AsArray().Any(p => !IsClear(p!));

    private static bool IsClear(JsonNode piece)
    {
        var cands = piece["cands"]!.AsArray();
        if (cands.Count == 0) return false;
        var top = cands[0]!;
        var score = top["score"]!.GetValue<double>();
        var rival = Math.Max(cands.Count > 1 ? cands[1]!["score"]!.GetValue<double>() : 0, piece["none"]!.GetValue<double>());
        return score >= (top["power"]!.GetValue<bool>() ? SemanticMatcher.AcceptPower : ClearScore) && score - rival >= ClearLead;
    }

    /// <summary>
    /// The words (when they named something for sure) and a clear meaning as today; the rest, the decision model: run its
    /// choice from <paramref name="run"/>, what turns off the PC from <paramref name="power"/>; two commands that together
    /// pass it, ask; else nothing. Without <paramref name="keepClear"/>, it decides everything the words did not.
    /// </summary>
    private static string Hybrid(JsonNode c, JsonNode decided, double run, double power, bool keepClear)
    {
        if (c["words"]!.GetValue<bool>()) return c["voice"]!.GetValue<string>();
        var pieces = c["pieces"]!.AsArray();
        var runs = new List<string>();
        string? ask = null;
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i]!;
            var cands = piece["cands"]!.AsArray();
            if (keepClear && IsClear(piece))
            {
                runs.Add(cands[0]!["key"]!.GetValue<string>());
                continue;
            }
            var probs = decided["pieces"]![i]!["probs"]!.AsObject().Select(p => (p.Key, P: p.Value!.GetValue<double>())).OrderByDescending(p => p.P).ToList();
            if (probs.Count == 0) continue;
            var (key, p) = probs[0];
            if (key == "none") continue;
            var power0 = cands.FirstOrDefault(x => x!["key"]!.GetValue<string>() == key && x["power"]!.GetValue<bool>());
            // What turns off the PC keeps today's bar too: the decision model alone never turns it off.
            if (power0 is not null && power0["score"]!.GetValue<double>() < SemanticMatcher.AcceptPower) continue;
            if (p >= (power0 is not null ? power : run)) runs.Add(key);
            else if (pieces.Count == 1 && probs.Count > 1 && probs[1].Key != "none" && p + probs[1].P >= run) ask = $"{key}|{probs[1].Key}";
        }
        if (runs.Count > 0) return "run:" + string.Join("+", runs.Distinct());
        return ask is null ? "none" : "ask:" + ask;
    }
}
