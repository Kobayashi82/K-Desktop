using System.Text.Json;

namespace KDesktop.Assistant.Brain.Models;

public enum ChatRole { System, User, Assistant, Tool }

/// <summary>A tool the model asks to call.</summary>
public sealed record ToolCall(string Name, JsonElement Arguments, string? Id = null);

/// <summary>One message of the conversation.</summary>
/// <param name="Images">Pictures that go with it (JPEG or PNG), for a model that sees (stage 8.3); null: none.</param>
public sealed record ChatMessage(ChatRole Role, string Content, IReadOnlyList<ToolCall>? ToolCalls = null, string? ToolName = null, string? ToolCallId = null,
    IReadOnlyList<byte[]>? Images = null)
{
    public static ChatMessage System(string content) => new(ChatRole.System, content);

    public static ChatMessage User(string content) => new(ChatRole.User, content);

    /// <summary>The user's words with a picture (a screenshot).</summary>
    public static ChatMessage UserWithImage(string content, byte[] image) => new(ChatRole.User, content, Images: [image]);

    public static ChatMessage Assistant(string content, IReadOnlyList<ToolCall>? toolCalls = null) => new(ChatRole.Assistant, content, toolCalls);

    public static ChatMessage ToolResult(string toolName, string content, string? toolCallId = null) => new(ChatRole.Tool, content, ToolName: toolName, ToolCallId: toolCallId);
}

/// <summary>A tool the model may call, its parameters described with a JSON schema.</summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersSchemaJson);

public sealed record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ToolDefinition> Tools)
{
    public double Temperature { get; init; } = 0.6;

    /// <summary>Longest answer, in tokens.</summary>
    public int MaxTokens { get; init; } = 300;

    /// <summary>
    /// Tokens it may think before answering (0: none, the usual). Only when it thinks harder (stage 12: «piénsalo bien»,
    /// <c>think_harder</c>); measured with Bonsai in 5.0.105, always thinking did not answer dates and sums better, only
    /// slower. llama-server takes the budget per request (<c>thinking_budget_tokens</c>); Ollama only turns it on; on the
    /// others it changes nothing.
    /// </summary>
    public int ThinkTokens { get; init; }
}

/// <summary>A piece of a streamed answer: new text, tool calls, or the end.</summary>
public sealed record ChatChunk(string Content, IReadOnlyList<ToolCall>? ToolCalls = null, bool Done = false)
{
    /// <summary>The end came because the answer reached its most tokens, not because the model finished it (stage 14.2).</summary>
    public bool Cut { get; init; }
}

/// <summary>A model the server has.</summary>
/// <param name="Capabilities">What Ollama says it does («completion», «tools», «embedding»…); empty when the server does not say.</param>
public sealed record ModelInfo(string Name, double ParameterBillions, long SizeBytes, IReadOnlyList<string> Capabilities)
{
    public bool SupportsTools => Capabilities.Count == 0 || Capabilities.Contains("tools");

    public bool IsEmbeddingOnly => (Capabilities.Count > 0 && !Capabilities.Contains("completion")) || Name.Contains("embed", StringComparison.OrdinalIgnoreCase);
}

public enum ChatModelFailure
{
    /// <summary>No server listening there.</summary>
    Unreachable,
    /// <summary>The server does not have that model.</summary>
    ModelNotFound,
    /// <summary>The server answered with an error.</summary>
    ServerError,
    Timeout,
}

/// <summary>Talking to the model went wrong, with a reason the user can be told (in their language).</summary>
public sealed class ChatModelException(ChatModelFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public ChatModelFailure Failure { get; } = failure;
}

/// <summary>A language model to talk to (on this PC or another).</summary>
public interface IChatModel : IDisposable
{
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken);

    /// <summary>The models in memory now; empty when the server does not say.</summary>
    Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken);

    /// <summary>Loads the model so the first answer is quick (when the user starts talking, never at start-up).</summary>
    Task PreloadAsync(string model, CancellationToken cancellationToken);

    /// <summary>Frees the model's video memory (on request, or with something full screen). Does nothing if the server cannot.</summary>
    Task UnloadAsync(string model, CancellationToken cancellationToken);
}
