using System.Net.Http.Headers;
using System.Text.Json;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>What the clients of the providers share: JSON bodies and turning network errors into <see cref="ChatModelException"/>.</summary>
internal static class ModelHttp
{
    /// <summary>Waiting for the list of models: a local server that is not there says no at once.</summary>
    public static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(5);

    public static ByteArrayContent Json(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer)) write(json);
        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    public static string Role(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.Assistant => "assistant",
        ChatRole.Tool => "tool",
        _ => "user",
    };

    /// <summary>The <c>error</c> of a JSON body (a text, or an object with <c>message</c>), or the body itself.</summary>
    public static string? ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error)) return null;
            return error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.GetString() : error.ValueKind == JsonValueKind.String ? error.GetString() : error.ToString();
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        }
    }

    /// <summary>A network error as the user can be told; null for what must go up as it is (cancellations).</summary>
    public static ChatModelException? Translate(Exception exception, CancellationToken cancellationToken, string unreachable = "brain.error.unreachable") => exception switch
    {
        ChatModelException => null,
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OperationCanceledException or TimeoutException => new ChatModelException(ChatModelFailure.Timeout, Loc.T("brain.error.timeout"), exception),
        HttpRequestException or IOException => new ChatModelException(ChatModelFailure.Unreachable, Loc.T(unreachable), exception),
        JsonException => new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.server"), exception),
        _ => null,
    };

    public static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    public static JsonElement? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
