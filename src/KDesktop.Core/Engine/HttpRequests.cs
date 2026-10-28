using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

/// <summary>What came back from «Petición web».</summary>
/// <param name="Status">HTTP code, or null when there was no answer (see <paramref name="Error"/>).</param>
public sealed record HttpResult(int? Status, string Reason, string Body, long Milliseconds, string? Error = null)
{
    public bool Success => Error is null && Status is >= 200 and < 300;

    /// <summary>«200 OK · {"state":"off"}» with the first <paramref name="max"/> characters of the answer on one line.</summary>
    public string Describe(int max = 200)
    {
        if (Error is not null) return Error;
        var text = $"{Status} {Reason}".Trim();
        var body = string.Join(' ', Body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (body.Length > max) body = body[..max] + "…";
        return body.Length > 0 ? $"{text} · {body}" : text;
    }
}

/// <summary>Sends the requests of «Petición web» with one shared client (two: with and without certificate checks).</summary>
public static class HttpRequests
{
    /// <summary>Larger answers are cut: only the start is ever shown.</summary>
    private const int MaxBody = 64 * 1024;

    private static readonly Lazy<HttpClient> Client = new(() => Create(insecure: false));
    private static readonly Lazy<HttpClient> Insecure = new(() => Create(insecure: true));

    private static HttpClient Create(bool insecure)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        // The action's own option «accept any certificate» (self-signed devices on the LAN): the user's choice.
#pragma warning disable CA5359
        if (insecure) handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
#pragma warning restore CA5359
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"K-Desktop/{typeof(HttpRequests).Assembly.GetName().Version?.ToString(3)}");
        return client;
    }

    /// <summary>
    /// Sends the request. Never throws for network problems or error codes (they come back in the result); only
    /// <paramref name="token"/> cancelling it (Esc) throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<HttpResult> SendAsync(HttpRequestAction a, CancellationToken token)
    {
        var started = Environment.TickCount64;
        long Elapsed() => Environment.TickCount64 - started;

        var url = CommandEngine.NormalizeUrl(TextVariables.ExpandUrl(a.Url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new HttpResult(null, string.Empty, string.Empty, 0, Loc.T("http.invalidUrl", a.Url));

        using var request = new HttpRequestMessage(new HttpMethod(a.Method.ToString().ToUpperInvariant()), uri);
        var contentType = a.ContentType.Trim();
        var contentHeaders = new List<HttpHeader>();
        foreach (var header in a.Headers)
        {
            var name = header.Name.Trim();
            if (name.Length == 0) continue;
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (contentType.Length == 0) contentType = header.Value.Trim();
                continue;
            }
            if (!request.Headers.TryAddWithoutValidation(name, header.Value)) contentHeaders.Add(header);
        }

        if (a.Body.Length > 0)
        {
            if (contentType.Length == 0) contentType = "application/json";
            var json = contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
            var body = TextVariables.Expand(a.Body, json ? JsonEscape : null);
            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var media) ? media : new MediaTypeHeaderValue("application/json");
            content.Headers.ContentType.CharSet ??= "utf-8";
            foreach (var header in contentHeaders) content.Headers.TryAddWithoutValidation(header.Name.Trim(), header.Value);
            request.Content = content;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (a.TimeoutSeconds > 0) timeout.CancelAfter(TimeSpan.FromSeconds(a.TimeoutSeconds));
        try
        {
            var client = a.IgnoreCertificateErrors ? Insecure.Value : Client.Value;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var text = await ReadAsync(response, timeout.Token).ConfigureAwait(false);
            return new HttpResult((int)response.StatusCode, response.ReasonPhrase ?? string.Empty, text, Elapsed());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new HttpResult(null, string.Empty, string.Empty, Elapsed(), Loc.T("http.timeout", a.TimeoutSeconds));
        }
        catch (HttpRequestException ex)
        {
            return new HttpResult(null, string.Empty, string.Empty, Elapsed(), Innermost(ex).Message);
        }
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[MaxBody];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), token).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }
        Encoding encoding;
        try { encoding = response.Content.Headers.ContentType?.CharSet is { Length: > 0 } cs ? Encoding.GetEncoding(cs.Trim('"')) : Encoding.UTF8; }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        return encoding.GetString(buffer, 0, read);
    }

    /// <summary>A value put inside a JSON string: quotes, backslashes and line breaks escaped.</summary>
    private static string JsonEscape(string value)
    {
        var quoted = JsonSerializer.Serialize(value, Json.Options);
        return quoted[1..^1];
    }

    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex;
    }
}
