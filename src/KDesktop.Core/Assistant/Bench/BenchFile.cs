using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Bench;

/// <summary>What an order of the bench should do: all the conditions it has must hold.</summary>
/// <param name="Tools">It must use one of these (from a rule or the AI); empty: no condition.</param>
/// <param name="NoTool">It must answer without tools.</param>
/// <param name="AnswerContains">Its answer must contain one of these (any case); empty: no condition.</param>
public sealed record BenchExpect(IReadOnlyList<string> Tools, bool NoTool, IReadOnlyList<string> AnswerContains)
{
    /// <summary>In a few words, for the results («save_note», «sin herramientas», «"43,2"»).</summary>
    public string Describe() => string.Join(" · ", new[]
    {
        Tools.Count > 0 ? string.Join(" / ", Tools) : null,
        NoTool ? Loc.T("bench.noTool") : null,
        AnswerContains.Count > 0 ? string.Join(" / ", AnswerContains.Select(a => $"«{a}»")) : null,
    }.Where(p => p is not null));
}

public sealed record BenchCase(string Say, BenchExpect Expect);

/// <summary>
/// The test bench of the assistants (stage 3.9, plan §5.4): orders and what each one should do, in JSON
/// (<c>{"cases":[{"say":"…","expect":{"tool":"save_note"}}]}</c>; <c>tool</c> may be a list, plus <c>noTool</c> and
/// <c>answerContains</c>). The user's own (<see cref="UserFile"/>) wins; without it, the built-in one in the user's
/// language (or English).
/// </summary>
public static class BenchFile
{
    /// <summary>The user's bench: their real orders.</summary>
    public static string UserFile => Path.Combine(AppPaths.DataDir, "Assistant", "bench.json");

    /// <summary>The built-in bench in a language (English when there is none in it).</summary>
    public static string BuiltIn(string language) =>
        EmbeddedAssets.ReadText("AssistantBench", $"bench.{language}.json") ?? EmbeddedAssets.ReadText("AssistantBench", "bench.en.json") ?? "{\"cases\":[]}";

    /// <summary>The bench to run, and whether it is the user's.</summary>
    public static (IReadOnlyList<BenchCase> Cases, bool Own) Load(string language)
    {
        if (File.Exists(UserFile))
        {
            try { return (Parse(File.ReadAllText(UserFile)), true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Warn($"Asistente: el banco de pruebas propio no se puede leer ({ex.Message}); se usa el incluido");
            }
        }
        return (Parse(BuiltIn(language)), false);
    }

    /// <summary>Creates the user's bench from the built-in one if there is none yet; returns its path.</summary>
    public static string EnsureUserFile(string language)
    {
        if (!File.Exists(UserFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
            File.WriteAllText(UserFile, BuiltIn(language));
        }
        return UserFile;
    }

    /// <exception cref="JsonException">It is not a bench.</exception>
    public static IReadOnlyList<BenchCase> Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new JsonException("Vacío");
        if (root["cases"] is not JsonArray cases) throw new JsonException("Falta «cases»");
        var result = new List<BenchCase>();
        foreach (var item in cases)
        {
            if (item?["say"] is not JsonValue sayValue || !sayValue.TryGetValue<string>(out var say) || say.Trim().Length == 0) continue;
            var expect = item["expect"] as JsonObject;
            result.Add(new BenchCase(say.Trim(), new BenchExpect(Strings(expect?["tool"]), expect?["noTool"] is JsonValue v && v.TryGetValue<bool>(out var no) && no,
                Strings(expect?["answerContains"]))));
        }
        return result;
    }

    private static List<string> Strings(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var one) && one.Length > 0 => [one],
        JsonArray list => [.. list.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>().Where(s => s.Length > 0)],
        _ => [],
    };
}
