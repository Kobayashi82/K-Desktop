using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>
/// Ollama's own API (<c>/api/chat</c> streamed, <c>/api/tags</c>, <c>/api/ps</c>). Turns off the «thinking» of the models
/// that have it: the assistant answers at once, it does not write essays.
/// </summary>
public sealed class OllamaChatModel : IChatModel
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private bool _thinkUnsupported;

    public OllamaChatModel(string endpoint, HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        Endpoint = new Uri(endpoint.TrimEnd('/') + "/");
    }

    public Uri Endpoint { get; }

    /// <summary>How long Ollama keeps the model in memory after each answer: short, so it never takes a game's video memory for long.</summary>
    public string KeepAlive { get; init; } = "3m";

    public Task PreloadAsync(string model, CancellationToken cancellationToken) => SendKeepAliveAsync(model, KeepAlive, cancellationToken);

    public Task UnloadAsync(string model, CancellationToken cancellationToken) => SendKeepAliveAsync(model, "0", cancellationToken);

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var root = await GetJsonAsync("api/tags", cancellationToken).ConfigureAwait(false);
        var models = new List<ModelInfo>();
        if (root.RootElement.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray()) models.Add(ParseModel(item));
        return models;
    }

    public async Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken)
    {
        using var root = await GetJsonAsync("api/ps", cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        if (root.RootElement.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
                if (item.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name) names.Add(name);
        return names;
    }

    public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendChatAsync(request, includeThink: !_thinkUnsupported, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.BadRequest && !_thinkUnsupported)
            {
                // Some versions or models do not take «think»: again without it.
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Dispose();
                if (!body.Contains("think", StringComparison.OrdinalIgnoreCase))
                    throw new ChatModelException(ChatModelFailure.ServerError, ModelHttp.ReadError(body) ?? Loc.T("brain.error.rejected"));
                _thinkUnsupported = true;
                response = await SendChatAsync(request, includeThink: false, cancellationToken).ConfigureAwait(false);
            }
            await EnsureSuccessAsync(response, request.Model, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken, "brain.error.unreachableOllama") is { } translated)
        {
            throw translated;
        }

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken, "brain.error.unreachableOllama") is { } translated)
                {
                    throw translated;
                }
                if (line is null) yield break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var chunk = ParseChunk(line);
                yield return chunk;
                if (chunk.Done) yield break;
            }
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    internal static byte[] BuildChatBody(ChatRequest request, bool includeThink, string keepAlive)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model);
            json.WriteBoolean("stream", true);
            if (includeThink) json.WriteBoolean("think", false);
            json.WriteString("keep_alive", keepAlive);
            json.WriteStartObject("options");
            json.WriteNumber("temperature", request.Temperature);
            json.WriteNumber("num_predict", request.MaxTokens);
            json.WriteEndObject();

            json.WriteStartArray("messages");
            foreach (var message in request.Messages)
            {
                json.WriteStartObject();
                json.WriteString("role", ModelHttp.Role(message.Role));
                json.WriteString("content", message.Content);
                if (message.ToolName is not null) json.WriteString("tool_name", message.ToolName);
                if (message.ToolCalls is { Count: > 0 } calls)
                {
                    json.WriteStartArray("tool_calls");
                    foreach (var call in calls)
                    {
                        json.WriteStartObject();
                        json.WriteStartObject("function");
                        json.WriteString("name", call.Name);
                        json.WritePropertyName("arguments");
                        call.Arguments.WriteTo(json);
                        json.WriteEndObject();
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();

            if (request.Tools.Count > 0)
            {
                json.WriteStartArray("tools");
                foreach (var tool in request.Tools)
                {
                    json.WriteStartObject();
                    json.WriteString("type", "function");
                    json.WriteStartObject("function");
                    json.WriteString("name", tool.Name);
                    json.WriteString("description", tool.Description);
                    json.WritePropertyName("parameters");
                    json.WriteRawValue(tool.ParametersSchemaJson);
                    json.WriteEndObject();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }
            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    internal static ChatChunk ParseChunk(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new ChatModelException(ChatModelFailure.ServerError, error.GetString() ?? Loc.T("brain.error.server"));

        var content = string.Empty;
        List<ToolCall>? calls = null;
        if (root.TryGetProperty("message", out var message))
        {
            if (message.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String) content = text.GetString() ?? string.Empty;
            if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                calls = [];
                foreach (var call in toolCalls.EnumerateArray())
                {
                    if (!call.TryGetProperty("function", out var function) || !function.TryGetProperty("name", out var name)) continue;
                    var arguments = function.TryGetProperty("arguments", out var args) ? args.Clone() : ModelHttp.EmptyObject();
                    // Some models give the arguments as JSON text.
                    if (arguments.ValueKind == JsonValueKind.String) arguments = ModelHttp.TryParse(arguments.GetString()) ?? ModelHttp.EmptyObject();
                    calls.Add(new ToolCall(name.GetString() ?? string.Empty, arguments, call.TryGetProperty("id", out var id) ? id.GetString() : null));
                }
            }
        }
        var done = root.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True;
        return new ChatChunk(content, calls, done);
    }

    internal static ModelInfo ParseModel(JsonElement item)
    {
        var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var size = item.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
        var parameters = item.TryGetProperty("details", out var details) && details.TryGetProperty("parameter_size", out var p) ? ParseParameterSize(p.GetString()) : 0;
        var capabilities = new List<string>();
        if (item.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
            capabilities.AddRange(caps.EnumerateArray().Select(c => c.GetString() ?? string.Empty));
        return new ModelInfo(name, parameters, size, capabilities);
    }

    /// <summary>«4.7B» → 4.7; «600M» → 0.6.</summary>
    internal static double ParseParameterSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        text = text.Trim().ToUpperInvariant();
        var multiplier = text.EndsWith('M') ? 0.001 : text.EndsWith('K') ? 0.000001 : 1;
        return double.TryParse(text.TrimEnd('B', 'M', 'K'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value * multiplier : 0;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ModelHttp.ListTimeout);
        try
        {
            using var response = await _http.GetAsync(new Uri(Endpoint, path), timeout.Token).ConfigureAwait(false);
            await EnsureSuccessAsync(response, null, cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken, "brain.error.unreachableOllama") is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>A chat without messages only loads the model; with keep_alive 0 it unloads it.</summary>
    private async Task SendKeepAliveAsync(string model, string keepAlive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(model)) return;
        var content = ModelHttp.Json(json =>
        {
            json.WriteStartObject();
            json.WriteString("model", model);
            json.WriteStartArray("messages");
            json.WriteEndArray();
            json.WriteString("keep_alive", keepAlive);
            json.WriteEndObject();
        });
        try
        {
            using var response = await _http.PostAsync(new Uri(Endpoint, "api/chat"), content, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, model, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken, "brain.error.unreachableOllama") is { } translated)
        {
            throw translated;
        }
    }

    private async Task<HttpResponseMessage> SendChatAsync(ChatRequest request, bool includeThink, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(BuildChatBody(request, includeThink, KeepAlive));
        content.Headers.ContentType = new("application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, "api/chat")) { Content = content };
        return await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string? model, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound && model is not null)
            throw new ChatModelException(ChatModelFailure.ModelNotFound, Loc.T("brain.error.modelMissing", model));
        throw new ChatModelException(ChatModelFailure.ServerError, ModelHttp.ReadError(body) ?? response.ReasonPhrase ?? Loc.T("brain.error.server"));
    }
}
