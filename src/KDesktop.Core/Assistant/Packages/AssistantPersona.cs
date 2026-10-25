using System.Text.Json;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// How an assistant is and talks (persona/&lt;lang&gt;.json of its package, plan §11.6): the instructions for the AI and
/// its fixed phrases (what it says without the AI: «ok», «cancelled»…). A phrase the package does not have is the
/// module's (<c>assistant.phrase.&lt;key&gt;</c> of the language files, several separated by «|»). The module without a
/// body, or an assistant without a persona, uses the module's (<c>assistant.persona.default</c>).
/// </summary>
/// <param name="Name">What it is called («Lander»; «K-Desktop» for the module without a body).</param>
/// <param name="Prompt">Its instructions for the AI, in the user's language when the package has it.</param>
public sealed record AssistantPersona(string Name, string Prompt, IReadOnlyDictionary<string, IReadOnlyList<string>> Phrases)
{
    /// <summary>The module's own: for the module without a body and the assistants that bring none.</summary>
    public static AssistantPersona Module(string? name = null) =>
        new(name ?? Loc.T("assistant.persona.name"), Loc.T("assistant.persona.default"), new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>
    /// Reads the persona of a package folder in <paramref name="language"/> (or English, or any it has). Falls back to the
    /// module's when the package has none or it cannot be read.
    /// </summary>
    public static AssistantPersona Read(string folder, AssistantManifest manifest, string language)
    {
        if (manifest.Persona is not { Length: > 0 } pattern) return Module(manifest.Name);
        var candidates = pattern.Contains("{lang}", StringComparison.Ordinal)
            ? new[] { language, language.Split('-')[0], "en" }.Distinct().Select(l => pattern.Replace("{lang}", l, StringComparison.Ordinal)).ToList()
            : [pattern];
        if (pattern.Contains("{lang}", StringComparison.Ordinal))
        {
            // Any language it brings is better than none.
            var dir = Path.GetDirectoryName(Path.Combine(folder, pattern.Replace("{lang}", "xx", StringComparison.Ordinal)))!;
            var name = Path.GetFileName(pattern).Replace("{lang}", "*", StringComparison.Ordinal);
            if (Directory.Exists(dir))
                candidates.AddRange(Directory.EnumerateFiles(dir, name).Order(StringComparer.Ordinal).Select(f => Path.GetRelativePath(folder, f)));
        }
        foreach (var relative in candidates)
        {
            var path = Path.GetFullPath(Path.Combine(folder, relative));
            // Never outside the package.
            if (!path.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
            try
            {
                if (Parse(manifest.Name, File.ReadAllText(path)) is { } persona) return persona;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Warn($"Asistente: no se ha podido leer la personalidad {path}: {ex.Message}");
            }
        }
        return Module(manifest.Name);
    }

    /// <summary>A persona file (null when it has no prompt).</summary>
    public static AssistantPersona? Parse(string name, string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("prompt", out var p) || p.GetString() is not { Length: > 0 } prompt) return null;
        var phrases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("phrases", out var list) && list.ValueKind == JsonValueKind.Object)
            foreach (var entry in list.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.Array)
                {
                    var items = entry.Value.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).Where(t => t.Length > 0).ToList();
                    if (items.Count > 0) phrases[entry.Name] = items;
                }
        return new AssistantPersona(name, prompt.Trim(), phrases);
    }

    /// <summary>
    /// One of its phrases for <paramref name="key"/> («ok», «cancelled»…), chosen at random among its own or, when it has
    /// none, the module's; with <paramref name="args"/> for «{0}». Empty when nobody has one.
    /// </summary>
    public string Phrase(string key, Random? random = null, params object?[] args)
    {
        var options = PhrasesFor(key);
        if (options.Count == 0) return string.Empty;
        var text = options[(random ?? Random.Shared).Next(options.Count)];
        if (args.Length == 0) return text;
        try { return string.Format(Loc.Instance.Culture, text, args); }
        catch (FormatException) { return text; }
    }

    /// <summary>All its phrases for <paramref name="key"/>: its own or, when it has none, the module's.</summary>
    public IReadOnlyList<string> PhrasesFor(string key) => Phrases.TryGetValue(key, out var own) && own.Count > 0 ? own : ModulePhrases(key);

    private static IReadOnlyList<string> ModulePhrases(string key)
    {
        var name = "assistant.phrase." + key;
        var text = Loc.T(name);
        return text == name ? [] : text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
