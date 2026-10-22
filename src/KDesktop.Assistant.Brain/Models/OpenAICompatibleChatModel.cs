using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>
/// OpenAI's chat API (<c>/v1/chat/completions</c> with server-sent events), which LM Studio, llama.cpp, vLLM, Jan,
/// llama-swap and Ollama itself offer.
/// </summary>
public sealed class OpenAICompatibleChatModel : IChatModel
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string? _apiKey;
    private bool _reasoningFlagsUnsupported;

    public OpenAICompatibleChatModel(string endpoint, string? apiKey = null, HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        var baseUrl = endpoint.TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) baseUrl += "/v1";
        Endpoint = new Uri(baseUrl + "/");
    }

    public Uri Endpoint { get; }

    /// <summary>Path from the server's root (not under /v1) that unloads the models, if it has one: <c>unload</c> in llama-swap.</summary>
    public string? UnloadPath { get; set; }

    /// <summary>Path from the server's root that lists the models running: <c>running</c> in llama-swap.</summary>
    public string? RunningPath { get; set; }

    /// <summary>
    /// Whether to find out, the first time it lists its models, if it is a llama-swap (it answers <c>/running</c>): then it
    /// also says what it has loaded and unloads (5.0.111: «llama-swap» is no longer a kind of its own in Options).
    /// </summary>
    public bool DetectLlamaSwap { get; init; }

    private bool _detected;

    /// <summary>It says what it has loaded and can unload it.</summary>
    public bool CanUnload => UnloadPath is not null && RunningPath is not null;

    private Uri Root => new(Endpoint, "..");

    /// <summary>A one-token answer: servers that load on demand load the model.</summary>
    public async Task PreloadAsync(string model, CancellationToken cancellationToken)
    {
        await foreach (var _ in StreamAsync(new ChatRequest(model, [ChatMessage.User("hi")], []) { MaxTokens = 1 }, cancellationToken).ConfigureAwait(false))
        {
        }
    }

    public async Task UnloadAsync(string model, CancellationToken cancellationToken)
    {
        if (UnloadPath is null) return;
        try
        {
            using var request = CreateRequest(HttpMethod.Get, new Uri(Root, UnloadPath));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                using var post = CreateRequest(HttpMethod.Post, new Uri(Root, UnloadPath));
                using var _ = await _http.SendAsync(post, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(new Uri(Endpoint, "models"), cancellationToken).ConfigureAwait(false);
        var models = new List<ModelInfo>();
        if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var item in data.EnumerateArray())
                if (item.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } name) models.Add(new ModelInfo(name, 0, 0, []));
        if (DetectLlamaSwap && !_detected) await DetectAsync(cancellationToken).ConfigureAwait(false);
        return models;
    }

    /// <summary>Whether it answers llama-swap's <c>/running</c> (asked once; a server that does not answer is asked again next time).</summary>
    private async Task DetectAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetJsonAsync(new Uri(Root, "running"), cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("running", out var running) && running.ValueKind == JsonValueKind.Array)
                (UnloadPath, RunningPath) = ("unload", "running");
            _detected = true;
        }
        catch (ChatModelException ex) when (ex.Failure is not (ChatModelFailure.Unreachable or ChatModelFailure.Timeout))
        {
            // It has no /running: an ordinary one.
            _detected = true;
        }
        catch (Exception ex) when (ex is ChatModelException or JsonException or OperationCanceledException)
        {
            // Not now: next time.
        }
    }

    public async Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken)
    {
        if (RunningPath is null) return [];
        using var document = await GetJsonAsync(new Uri(Root, RunningPath), cancellationToken).ConfigureAwait(false);
        var names = new List<string>();
        if (document.RootElement.TryGetProperty("running", out var running) && running.ValueKind == JsonValueKind.Array)
            foreach (var item in running.EnumerateArray())
                if (item.TryGetProperty("model", out var m) && m.GetString() is { Length: > 0 } name) names.Add(name);
        return names;
    }

    public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendChatAsync(request, !_reasoningFlagsUnsupported, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity && !_reasoningFlagsUnsupported)
            {
                // Strict servers that do not know the flags that turn the reasoning off.
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Dispose();
                if (!body.Contains("reasoning", StringComparison.OrdinalIgnoreCase) && !body.Contains("chat_template", StringComparison.OrdinalIgnoreCase))
                    throw new ChatModelException(ChatModelFailure.ServerError, ModelHttp.ReadError(body) ?? Loc.T("brain.error.rejected"));
                _reasoningFlagsUnsupported = true;
                response = await SendChatAsync(request, false, cancellationToken).ConfigureAwait(false);
            }
            await EnsureSuccessAsync(response, request.Model, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken) is { } translated)
        {
            throw translated;
        }

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var pending = new SortedDictionary<int, PendingCall>();
            var content = new StringBuilder();
            // The answer starts like a tool call written as text: held back until the end, to be read as one.
            var holding = false;
            Action<JsonElement>? timings = LogTimings ? LogTiming : null;
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken) is { } translated)
                {
                    throw translated;
                }
                if (line is null || line.StartsWith("data: [DONE]", StringComparison.Ordinal))
                {
                    foreach (var last in End(pending, content, holding)) yield return last;
                    yield break;
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var (text, finished) = ParseEvent(line[5..].Trim(), pending, timings);
                if (text.Length > 0)
                {
                    content.Append(text);
                    if (!holding && pending.Count == 0 && TextToolCalls.Starts(content.ToString())) holding = true;
                    if (!holding) yield return new ChatChunk(text);
                }
                if (finished)
                {
                    foreach (var last in End(pending, content, holding)) yield return last;
                    yield break;
                }
            }
        }
    }

    /// <summary>
    /// The end of an answer: its tool calls, or —when the server did not read them— those written as text
    /// (<c>&lt;function=find_command&gt; &lt;parameter=query&gt;…</c>, as Bonsai sometimes leaves them), with the text
    /// held back if it was not one after all.
    /// </summary>
    private static IEnumerable<ChatChunk> End(SortedDictionary<int, PendingCall> pending, StringBuilder content, bool holding)
    {
        IReadOnlyList<ToolCall>? calls = Complete(pending);
        if (calls is null && TextToolCalls.Parse(content.ToString()) is { Count: > 0 } written)
        {
            Log.Info($"Asistente: llamada escrita como texto leída: {string.Join(", ", written.Select(c => c.Name))}");
            calls = written;
        }
        else if (holding && content.Length > 0) yield return new ChatChunk(content.ToString());
        yield return new ChatChunk(string.Empty, calls, Done: true);
    }

    /// <summary>Writes how long the server took (llama-server's <c>timings</c>): to see what the context costs.</summary>
    public bool LogTimings { get; init; }

    private static void LogTiming(JsonElement timings)
    {
        static double Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;
        Log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Asistente: llama-server: prompt {Number(timings, "prompt_n") + Number(timings, "cache_n"):0} tokens ({Number(timings, "cache_n"):0} en caché) en {Number(timings, "prompt_ms"):0} ms, respuesta {Number(timings, "predicted_n"):0} tokens en {Number(timings, "predicted_ms"):0} ms"));
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    /// <summary>
    /// The request. With <paramref name="disableReasoning"/> it asks not to «reason» before answering:
    /// <c>reasoning_effort</c> (Ollama, LM Studio) and <c>enable_thinking</c> in the template (llama.cpp, vLLM). Without it a
    /// reasoning model spends all its tokens thinking and says nothing. With <see cref="ChatRequest.ThinkTokens"/> it
    /// thinks instead, that many tokens at most (llama-server's <c>thinking_budget_tokens</c>), on top of its answer.
    /// </summary>
    internal static byte[] BuildChatBody(ChatRequest request, bool disableReasoning = true)
    {
        var think = disableReasoning && request.ThinkTokens > 0;
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model);
            json.WriteBoolean("stream", true);
            json.WriteNumber("temperature", request.Temperature);
            json.WriteNumber("max_tokens", request.MaxTokens + (think ? request.ThinkTokens : 0));
            if (think)
            {
                json.WriteStartObject("chat_template_kwargs");
                json.WriteBoolean("enable_thinking", true);
                json.WriteEndObject();
                json.WriteNumber("thinking_budget_tokens", request.ThinkTokens);
            }
            else if (disableReasoning)
            {
                json.WriteString("reasoning_effort", "none");
                json.WriteStartObject("chat_template_kwargs");
                json.WriteBoolean("enable_thinking", false);
                json.WriteEndObject();
            }

            json.WriteStartArray("messages");
            foreach (var message in request.Messages)
            {
                json.WriteStartObject();
                json.WriteString("role", ModelHttp.Role(message.Role));
                if (message.Images is { Count: > 0 } images)
                {
                    // With pictures the content is a list of parts: the text, then each picture as a data address.
                    json.WriteStartArray("content");
                    json.WriteStartObject();
                    json.WriteString("type", "text");
                    json.WriteString("text", message.Content);
                    json.WriteEndObject();
                    foreach (var image in images)
                    {
                        json.WriteStartObject();
                        json.WriteString("type", "image_url");
                        json.WriteStartObject("image_url");
                        json.WriteString("url", $"data:{ModelHttp.ImageType(image)};base64,{Convert.ToBase64String(image)}");
                        json.WriteEndObject();
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                }
                else json.WriteString("content", message.Content);
                if (message.Role == ChatRole.Tool) json.WriteString("tool_call_id", message.ToolCallId ?? message.ToolName ?? "call");
                if (message.ToolCalls is { Count: > 0 } calls)
                {
                    json.WriteStartArray("tool_calls");
                    for (var i = 0; i < calls.Count; i++)
                    {
                        json.WriteStartObject();
                        json.WriteString("id", calls[i].Id ?? $"call_{i}");
                        json.WriteString("type", "function");
                        json.WriteStartObject("function");
                        json.WriteString("name", calls[i].Name);
                        json.WriteString("arguments", calls[i].Arguments.GetRawText());
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

    /// <summary>One server-sent event: the new text and whether the model has finished.</summary>
    internal static (string Content, bool Finished) ParseEvent(string data, SortedDictionary<int, PendingCall> pending, Action<JsonElement>? timings = null)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        if (timings is not null && root.TryGetProperty("timings", out var timing) && timing.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("choices", out var ended) && ended.ValueKind == JsonValueKind.Array && ended.GetArrayLength() > 0
            && ended[0].TryGetProperty("finish_reason", out var why) && why.ValueKind == JsonValueKind.String)
            timings(timing);
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) ? m.GetString() : error.ToString();
            throw new ChatModelException(ChatModelFailure.ServerError, message ?? Loc.T("brain.error.server"));
        }
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return (string.Empty, false);

        var choice = choices[0];
        var content = string.Empty;
        if (choice.TryGetProperty("delta", out var delta))
        {
            if (delta.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String) content = text.GetString() ?? string.Empty;
            if (delta.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    var index = call.TryGetProperty("index", out var i) && i.TryGetInt32(out var value) ? value : pending.Count;
                    if (!pending.TryGetValue(index, out var entry)) pending[index] = entry = new PendingCall();
                    if (call.TryGetProperty("id", out var id) && id.GetString() is { } callId) entry.Id = callId;
                    if (!call.TryGetProperty("function", out var function)) continue;
                    if (function.TryGetProperty("name", out var name) && name.GetString() is { } fn) entry.Name += fn;
                    if (function.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String) entry.Arguments.Append(args.GetString());
                }
            }
        }
        var finished = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String;
        return (content, finished);
    }

    private static List<ToolCall>? Complete(SortedDictionary<int, PendingCall> pending)
    {
        if (pending.Count == 0) return null;
        var calls = pending.Values
            .Select(entry => new ToolCall(entry.Name, ModelHttp.TryParse(entry.Arguments.Length == 0 ? "{}" : entry.Arguments.ToString()) ?? ModelHttp.EmptyObject(), entry.Id))
            .ToList();
        pending.Clear();
        return calls;
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ModelHttp.ListTimeout);
        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            await EnsureSuccessAsync(response, null, cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ModelHttp.Translate(ex, cancellationToken) is { } translated)
        {
            throw translated;
        }
    }

    private async Task<HttpResponseMessage> SendChatAsync(ChatRequest request, bool disableReasoning, CancellationToken cancellationToken)
    {
        using var message = CreateRequest(HttpMethod.Post, new Uri(Endpoint, "chat/completions"));
        message.Content = new ByteArrayContent(BuildChatBody(request, disableReasoning));
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (_apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string? model, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound && model is not null)
            throw new ChatModelException(ChatModelFailure.ModelNotFound, Loc.T("brain.error.modelUnavailable", model));
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.key"));
        throw new ChatModelException(ChatModelFailure.ServerError, ModelHttp.ReadError(body) ?? response.ReasonPhrase ?? Loc.T("brain.error.server"));
    }

    internal sealed class PendingCall
    {
        public string? Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public StringBuilder Arguments { get; } = new();
    }
}
