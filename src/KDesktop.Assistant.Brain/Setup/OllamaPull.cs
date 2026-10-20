using System.Text;
using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Setup;

/// <summary>How a download goes: bytes done and in all.</summary>
public sealed record PullProgress(string Status, long Completed, long Total);

/// <summary>
/// Downloads a model in Ollama (<c>/api/pull</c>, streamed) and reports the progress. Ollama downloads by layers: each says
/// its size and how much it has; the whole is their sum.
/// </summary>
public sealed class OllamaPull(HttpClient http, string endpoint)
{
    private readonly Uri _endpoint = new(endpoint.TrimEnd('/') + "/");

    /// <summary>Downloads <paramref name="model"/>. Cancelled, Ollama keeps what it has and goes on another time.</summary>
    /// <param name="expectedBytes">What it is expected to take (so the bar does not jump as layers appear).</param>
    public async Task PullAsync(string model, IProgress<PullProgress>? progress, long expectedBytes, CancellationToken cancellationToken)
    {
        var tracker = new PullTracker(expectedBytes);
        try
        {
            var content = ModelHttp.Json(json =>
            {
                json.WriteStartObject();
                json.WriteString("model", model);
                json.WriteBoolean("stream", true);
                json.WriteEndObject();
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, "api/pull")) { Content = content };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                tracker.Feed(body.Trim().Length > 0 ? body : JsonSerializer.Serialize(new { error = response.ReasonPhrase }));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (tracker.Feed(line) is { } step) progress?.Report(step);
                if (tracker.Done) return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
        {
            throw new ChatModelException(ChatModelFailure.Unreachable, Loc.T("brain.error.unreachableOllama"), ex);
        }

        // Cut without «success»: it cannot be taken as downloaded.
        throw new ChatModelException(ChatModelFailure.ServerError, Loc.T("brain.error.pullCut"));
    }
}

/// <summary>Keeps count of a download from the lines Ollama sends.</summary>
internal sealed class PullTracker(long expectedBytes)
{
    private readonly Dictionary<string, (long Completed, long Total)> _layers = [];

    public bool Done { get; private set; }

    /// <summary>Takes one line. Throws when Ollama says there was an error.</summary>
    public PullProgress? Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // Not Ollama (or a version that does not understand the request).
            throw new ChatModelException(ChatModelFailure.ServerError, line.Length > 120 ? line[..120] + "…" : line);
        }

        using var _ = document;
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.GetString() ?? string.Empty;
            throw message.Contains("not exist", StringComparison.OrdinalIgnoreCase) || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? new ChatModelException(ChatModelFailure.ModelNotFound, Loc.T("brain.error.pullMissing"))
                : new ChatModelException(ChatModelFailure.ServerError, message);
        }

        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        if (root.TryGetProperty("digest", out var digest) && digest.GetString() is { } id && root.TryGetProperty("total", out var total))
            _layers[id] = (root.TryGetProperty("completed", out var c) ? c.GetInt64() : 0, total.GetInt64());
        if (status == "success") Done = true;

        var all = Math.Max(expectedBytes, _layers.Values.Sum(l => l.Total));
        return new PullProgress(status, Done ? all : _layers.Values.Sum(l => l.Completed), all);
    }
}
