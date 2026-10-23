using System.Globalization;
using System.Text;
using System.Text.Json;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Knowledge;

/// <summary>
/// search_knowledge (stage 19.3): the pieces of the user's own documents (the folders of Options → «Lo que sabe») that answer
/// a question, found by words and meaning, each with its file and page so that the answer says where it comes from. Only reads.
/// It also works while the folders are still being read (it says how far it got).
/// </summary>
public sealed class SearchKnowledgeTool(KnowledgeBase knowledge) : ITool
{
    /// <summary>Pieces given to the model at most, and letters in all (the context is 16 K tokens).</summary>
    public const int MostPieces = 6, MaxText = 7000;

    public string Name => "search_knowledge";

    public string Description => "Searches the user's own documents (the folders they chose for you: their notes, manuals, contracts, invoices, papers, books…) and gives the pieces that answer, each with its file and page. Use it whenever they ask about something that would be in their documents or files («según mis apuntes», «en el contrato», «¿qué dice el manual de…?», «¿cuánto pagué de…?»). Then answer from those pieces and say which document (and page) it comes from.";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{"
        + "\"query\":{\"type\":\"string\",\"description\":\"What to look for, as a question or the key words, with every name, figure or code the user said\"},"
        + "\"file\":{\"type\":\"string\",\"description\":\"Words of the name of the document to look in, when the user names one (\\\"contrato alquiler\\\"); empty: all\"}},"
        + "\"required\":[\"query\"]}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.search_knowledge.intent", ToolText.Clean(arguments.GetString("query")) is { Length: > 0 } q ? q : "?");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = ToolText.Clean(arguments.GetString("query")) is { Length: > 0 } asked ? asked : context.Heard ?? string.Empty;
        if (query.Length == 0) return ToolResult.Fail("What should be looked for? Give the query.");
        if (!knowledge.HasFolders)
            return new ToolResult("The user has not chosen any folder of documents for you yet. Say so, and that they can do it in Options → Assistant module → «Lo que sabe».")
            {
                Done = Loc.T("tool.search_knowledge.noFolders"),
                Failed = true,
            };
        var file = ToolText.Clean(arguments.GetString("file"));
        var found = await knowledge.SearchAsync(query, MostPieces, file, cancellationToken).ConfigureAwait(false);
        var (reading, done, files) = knowledge.Progress;
        var still = reading && files > 0
            ? $" (Their documents are still being read: {done} of {files} files so far, so the answer may be incomplete; say so.)"
            : string.Empty;
        if (found.Hits.Count == 0)
            return new ToolResult($"Nothing in the user's documents answers «{query}».{still} Say you did not find it in their documents; do not make it up.")
            {
                Done = Loc.T("tool.search_knowledge.none", query),
            };

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"From the user's documents, for «{query}»{still}:\n");
        var number = 0;
        foreach (var hit in found.Hits)
        {
            var piece = hit.Text.Trim();
            if (text.Length + piece.Length > MaxText && number > 0) break;
            number++;
            text.Append(CultureInfo.InvariantCulture, $"\n[{number}] {Path.GetFileName(hit.Path)}{(hit.Pages > 1 ? $", page {hit.Page}" : string.Empty)} ({hit.Path})\n");
            text.Append(piece.Length > MaxText ? piece[..MaxText] : piece).Append('\n');
        }
        // Small models leave the source out unless they see how to say it.
        var first = found.Hits[0];
        var example = Path.GetFileNameWithoutExtension(first.Path) + (first.Pages > 1 ? $", page {first.Page}" : string.Empty);
        text.Append(CultureInfo.InvariantCulture,
            $"\nAnswer from these pieces, in the user's language, and always say which document (and page) it comes from, by its name (like «according to {example}, …»). If they do not answer the question, say you did not find it in their documents.");
        var names = found.Hits.Take(number).Select(h => Path.GetFileName(h.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new ToolResult(text.ToString())
        {
            Done = Loc.T("tool.search_knowledge.done", query, string.Join(", ", names)),
        };
    }
}
