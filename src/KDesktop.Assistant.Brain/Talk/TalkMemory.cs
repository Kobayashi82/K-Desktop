using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>A memory proposed by the model after a conversation.</summary>
public sealed record MemoryCandidate(string Text, string Kind, double Importance);

/// <summary>
/// What the assistants remember about the user while talking (stage 3.7, from Kobii): before the AI answers, what comes
/// to mind for the order (the memories that share words with it or stand out by meaning, and the most important facts)
/// goes into its instructions; after it answers, the same model reads the exchange and says what is worth remembering
/// (small models seldom use the «remember» tool on their own). What it learns goes through the «remember» tool, so it
/// is in the activity and can be undone. Thread-safe.
/// </summary>
public sealed class TalkMemory(MemoryStore store, ToolRunner tools, IMeaning? meaning)
{
    /// <summary>Memories given to the model with an order, at most.</summary>
    public const int Recalled = 4;

    /// <summary>Memories learned from one exchange, at most.</summary>
    public const int LearnedPerExchange = 2;

    /// <summary>What it learns by itself never counts as more important than this (what the user says can).</summary>
    public const double LearnedImportance = 0.8;

    private readonly MeaningShelf? _shelf = meaning is null ? null : new MeaningShelf(meaning, "recuerdo(s)");

    public MemoryStore Store => store;

    /// <summary>What comes to mind for an order (<paramref name="order"/>: its vector, null without the meaning).</summary>
    public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(string said, MeaningVectors? order, CancellationToken cancellationToken)
    {
        Func<string, double>? alike = null;
        var texts = store.Texts;
        if (texts.Count == 0) return [];
        if (_shelf is not null && order is not null)
        {
            try
            {
                if (await _shelf.GetAsync(texts, order.Model, cancellationToken).ConfigureAwait(false) is { } got)
                {
                    var vector = order.Vectors[0];
                    alike = text => got.Vectors.TryGetValue(text, out var v) ? TextEmbedder.Similarity(vector, v) : double.NaN;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Log.Warn($"Asistente: sin significado para recordar: {ex.Message}");
            }
        }
        var recalled = store.Recall(said, Recalled, alike);
        if (recalled.Count > 0) Log.Info($"Asistente: le vienen a la cabeza {recalled.Count} recuerdo(s)");
        return recalled;
    }

    /// <summary>
    /// Reads an exchange and remembers what is worth it (through the «remember» tool, route <c>memory</c>). Quiet when it
    /// fails: it is a bonus, never in the way of an order.
    /// </summary>
    public async Task LearnAsync(IChatModel model, string modelName, string said, string answer, ToolContext context, CancellationToken cancellationToken)
    {
        try
        {
            var known = store.All.Take(30).Select(m => m.Text).ToList();
            var text = new StringBuilder();
            if (known.Count > 0)
            {
                text.AppendLine(Loc.T("assistant.memory.known"));
                foreach (var fact in known) text.AppendLine(CultureInfo.InvariantCulture, $"- {fact}");
                text.AppendLine();
            }
            text.AppendLine(Loc.T("assistant.memory.exchange"));
            text.AppendLine(CultureInfo.InvariantCulture, $"{Loc.T("assistant.memory.user")}: {said}");
            text.AppendLine(CultureInfo.InvariantCulture, $"{Loc.T("assistant.memory.assistant")}: {answer}");
            var request = new ChatRequest(modelName, [ChatMessage.System(Loc.T("assistant.memory.learn")), ChatMessage.User(text.ToString())], [])
            {
                Temperature = 0.1,
                MaxTokens = 200,
            };
            var reply = new StringBuilder();
            await foreach (var chunk in model.StreamAsync(request, cancellationToken).ConfigureAwait(false)) reply.Append(chunk.Content);
            foreach (var candidate in Parse(reply.ToString()).Take(LearnedPerExchange))
            {
                var arguments = new JsonObject
                {
                    ["fact"] = candidate.Text,
                    ["kind"] = candidate.Kind,
                    ["importance"] = Math.Min(candidate.Importance, LearnedImportance),
                    ["source"] = "learned",
                };
                await tools.RunAsync("remember", JsonSerializer.SerializeToElement(arguments), context, said, "memory", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Another order came: it is not worth it now.
        }
        catch (ChatModelException ex)
        {
            Log.Warn($"Asistente: no se ha podido aprender de la conversación ({ex.Failure})");
        }
    }

    /// <summary>The model's answer, read with tolerance: the JSON object even with text around it.</summary>
    public static IReadOnlyList<MemoryCandidate> Parse(string reply)
    {
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start) return [];
        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            if (!document.RootElement.TryGetProperty("memories", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            var result = new List<MemoryCandidate>();
            foreach (var item in list.EnumerateArray())
            {
                var text = item.ValueKind == JsonValueKind.String ? item.GetString()
                    : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text) || text.Length > MemoryStore.MaxLength) continue;
                var kind = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("kind", out var k) && k.GetString() is "like" or "event"
                    ? k.GetString()! : "fact";
                var importance = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("importance", out var i) && i.TryGetDouble(out var value)
                    ? Math.Clamp(value, 0, 1) : 0.5;
                result.Add(new MemoryCandidate(text.Trim(), kind, importance));
            }
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
