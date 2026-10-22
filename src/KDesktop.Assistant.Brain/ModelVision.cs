using System.Text;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools.Builtin;

namespace KDesktop.Assistant.Brain;

/// <summary>
/// The assistant's model looking at a picture (stage 8.3): a request of its own with the picture and the question,
/// apart from the conversation (the answer of a tool can only be text). Only when the model sees (llama-server with its
/// projector, an Ollama model with vision).
/// </summary>
public sealed class ModelVision(BrainService brain) : IVision
{
    private const string Looking = "You see screenshots of the user's PC and describe what is really in them, reading the visible text exactly. Never repeat the question.";

    public bool CanSee(string? assistant) =>
        brain.ModelFor(assistant) is { } model && brain.State.Models?.FirstOrDefault(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase)) is { Vision: true };

    public async Task<string?> LookAsync(string? assistant, byte[] jpeg, string question, CancellationToken cancellationToken)
    {
        if (brain.Client is not { } client || brain.ModelFor(assistant) is not { } model) return null;
        var request = new ChatRequest(model, [ChatMessage.System(Looking), ChatMessage.UserWithImage(question, jpeg)], []) { Temperature = 0.2, MaxTokens = 500 };
        var said = new StringBuilder();
        await foreach (var chunk in client.StreamAsync(request, cancellationToken).ConfigureAwait(false))
            said.Append(chunk.Content);
        var text = said.ToString();
        var thought = text.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return (thought >= 0 ? text[(thought + 8)..] : text).Trim();
    }
}
