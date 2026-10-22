using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>A page as a browser shows it, after its scripts: title, address, its visible text and its HTML.</summary>
public sealed record BrowsedPage(string Title, string Address, string Text, string Html);

/// <summary>
/// The assistants' own browser (stage 8.4): hidden, with a session of its own (never the user's cookies), for the pages
/// that only show with scripts or that turn away what is not a browser.
/// </summary>
public interface IWebBrowser
{
    /// <summary>The page at <paramref name="uri"/> once it has drawn, or null when it did not load.</summary>
    Task<BrowsedPage?> ReadAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>
/// Reading pages for the tools (stages 8.3–8.4): plainly over HTTP, which is quick; when a page says nothing that way
/// (it is drawn by scripts) or turns the request away, with the assistants' browser.
/// </summary>
public sealed class WebReader
{
    /// <summary>Less text than this over HTTP: the page is probably drawn by scripts.</summary>
    private const int Little = 300;

    private const long MaxBytes = 3 * 1024 * 1024;

    // Pages in windows-1252 and the like.
    static WebReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private readonly HttpClient _http;
    private readonly IWebBrowser? _browser;

    public WebReader(HttpMessageHandler? network = null, IWebBrowser? browser = null)
    {
        _browser = browser;
        _http = network is null
            ? new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
            : new HttpClient(network, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        // Some sites only answer browsers.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(Loc.Instance.Language + ",en;q=0.7");
    }

    /// <summary>The readable text of a page, at most <paramref name="maxChars"/> characters. Throws when it could not be read either way.</summary>
    public async Task<WebPage> ReadAsync(Uri uri, int maxChars, CancellationToken cancellationToken)
    {
        WebPage? page = null;
        Exception? failure = null;
        try
        {
            var (type, body) = await GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !type.Contains("xml", StringComparison.OrdinalIgnoreCase) && !type.Contains("json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"it is {type}, not a page");
            page = type.Contains("html", StringComparison.OrdinalIgnoreCase) ? WebText.Of(body, maxChars) : WebText.Plain(body, maxChars);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Unauthorized or null)
        {
            failure = ex; // turned away (or no answer): the browser may still get it
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            failure = ex;
        }
        if ((page is null || page.Text.Length < Little) && _browser is not null
            && await _browser.ReadAsync(uri, cancellationToken).ConfigureAwait(false) is { } browsed && browsed.Text.Trim().Length > (page?.Text.Length ?? 0))
        {
            Log.Info($"Asistente: {uri.Host} leída con el navegador interno ({browsed.Text.Length} caracteres)");
            return WebText.Plain(browsed.Text, maxChars) with { Title = browsed.Title };
        }
        return page ?? throw failure ?? new InvalidDataException("nothing to read");
    }

    /// <summary>The HTML of a page (a list of search results): over HTTP, or with the browser if that is turned away or empty.</summary>
    public async Task<string?> HtmlAsync(Uri uri, Func<string, bool> enough, CancellationToken cancellationToken)
    {
        try
        {
            var (_, body) = await GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (enough(body)) return body;
        }
        catch (HttpRequestException ex)
        {
            Log.Warn($"Asistente: {uri.Host} no contesta: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        if (_browser is null) return null;
        var browsed = await _browser.ReadAsync(uri, cancellationToken).ConfigureAwait(false);
        return browsed?.Html;
    }

    private async Task<(string Type, string Body)> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var type = response.Content.Headers.ContentType?.MediaType ?? "text/html";
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0 && buffer.Length < MaxBytes) buffer.Write(chunk, 0, read);
        var charset = response.Content.Headers.ContentType?.CharSet;
        Encoding encoding;
        try { encoding = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        return (type, encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
    }
}

/// <summary>What a page says: its title, its text and whether it was cut.</summary>
public sealed record WebPage(string Title, string Text, bool Cut);

/// <summary>
/// «Léeme esta página», «resúmeme este artículo», «¿qué dice la web de…?» (stage 8.3): a web page as text for the model,
/// the one given or the one open in the browser in front. Only http and https.
/// </summary>
public sealed class ReadWebTool(IScreen? screen, WebReader? reader = null) : ITool
{
    /// <summary>How much of the page goes to the model (about 2 500 tokens).</summary>
    public const int MaxChars = 9000;

    private readonly WebReader _reader = reader ?? new WebReader();

    public string Name => "read_web";

    public string Description => "Reads a web page as text, to summarise it or answer about it: the address given, or (empty) the page open in the user's browser (\"this page\", \"this article\"). It does not open anything.";

    public string ParametersSchemaJson => ToolText.Optional(("url", "string", "The address; empty: the page open in the browser"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) =>
        Browser.Address(arguments.GetString("url")) is { } uri ? Loc.T("tool.read_web.intent", uri.Host) : Loc.T("tool.read_web.intentOpen");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var given = arguments.GetString("url")?.Trim();
        Uri? uri;
        if (string.IsNullOrEmpty(given))
        {
            var open = screen is null ? null : await screen.BrowserAddressAsync(cancellationToken).ConfigureAwait(false);
            uri = Browser.Address(open);
            if (uri is null) return ToolResult.Fail("There is no web page open in the browser in front. Say so (the user may give the address).");
        }
        else if ((uri = Browser.Address(given)) is null) return ToolResult.Fail("That is not a web address.");

        WebPage page;
        try
        {
            page = await _reader.ReadAsync(uri, MaxChars, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException && !cancellationToken.IsCancellationRequested)
        {
            Log.Warn($"Asistente: no se ha podido leer {uri.Host}: {ex.Message}");
            return new ToolResult($"The page {uri.Host} could not be read ({ex.Message}). Say so.") { Done = Loc.T("tool.read_web.failed", uri.Host), Failed = true };
        }
        if (page.Text.Length == 0)
            return new ToolResult($"{uri.Host} has no text to read. Say so.") { Done = Loc.T("tool.read_web.failed", uri.Host), Failed = true };
        return new ToolResult($"Page: {page.Title}\nAddress: {uri}\n{(page.Cut ? "(Only its beginning.)\n" : string.Empty)}\n{page.Text}\n\nAnswer the user from this page, briefly, in their language.")
        {
            Done = Loc.T("tool.read_web.done", page.Title.Length > 0 ? ToolText.Clean(page.Title) : uri.Host),
        };
    }
}

/// <summary>
/// «¿Cuándo es el próximo eclipse?», «¿a qué hora abre el Mercadona de mi barrio?», «¿cuánto cuesta la RTX 5080?» (stage
/// 8.4): searches the internet for the assistant (DuckDuckGo, without a key) and reads the results and the first page,
/// so the model answers with today's facts. The user sees nothing; <see cref="SearchWebTool"/> opens the search for them.
/// </summary>
public sealed class LookUpWebTool(WebReader? reader = null) : ITool
{
    /// <summary>How many results go to the model, and how much of the first page.</summary>
    public const int Results = 6, FirstPageChars = 5000;

    private readonly WebReader _reader = reader ?? new WebReader();

    public string Name => "look_up_web";

    public string Description => "Searches the internet and reads the results, to answer a question with current facts (news, dates, prices, opening hours, who/what/when). The user sees nothing; to show a search in their browser use search_web.";

    public string ParametersSchemaJson => ToolText.OneString("query", "What to search for, as you would type it in a search engine");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.look_up_web.intent", ToolText.Clean(arguments.GetString("query")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.GetString("query")?.Trim();
        if (string.IsNullOrEmpty(query)) return ToolResult.Fail("There is nothing to search for.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var search = DuckDuckGo.Address(query, Loc.Instance.Language);
        var html = await _reader.HtmlAsync(search, body => DuckDuckGo.Results(body).Count > 0, cancellationToken).ConfigureAwait(false);
        var results = html is null ? [] : DuckDuckGo.Results(html).Take(Results).ToList();
        if (results.Count == 0)
            return new ToolResult("The search gave nothing (or the search engine did not answer). Say so.") { Done = Loc.T("tool.look_up_web.failed", ToolText.Clean(query)), Failed = true };

        var text = new StringBuilder($"Results for «{query}»:\n");
        for (var i = 0; i < results.Count; i++) text.Append(CultureInfo.InvariantCulture, $"{i + 1}. {results[i].Title} — {results[i].Url.Host}\n   {results[i].Snippet}\n");
        // The first result that is a page to read (a video says nothing in text).
        if (results.FirstOrDefault(r => !DuckDuckGo.Unreadable(r.Url)) is { } first)
        {
            try
            {
                var page = await _reader.ReadAsync(first.Url, FirstPageChars, cancellationToken).ConfigureAwait(false);
                if (page.Text.Length > 0) text.Append(CultureInfo.InvariantCulture, $"\nText of «{first.Title}» ({first.Url.Host}):\n{page.Text}\n");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException && !cancellationToken.IsCancellationRequested)
            {
                Log.Info($"Asistente: no se ha podido leer el primer resultado ({first.Url.Host}): {ex.Message}");
            }
        }
        text.Append("\nAnswer the user from this, briefly, in their language; if the results do not say it, say you did not find it.");
        Log.Info($"Asistente: búsqueda «{query}» → {results.Count} resultados en {watch.ElapsedMilliseconds} ms");
        return new ToolResult(text.ToString()) { Done = Loc.T("tool.look_up_web.done", ToolText.Clean(query)) };
    }
}

/// <summary>DuckDuckGo's page of results without scripts (html.duckduckgo.com): its address and its results.</summary>
public static partial class DuckDuckGo
{
    public sealed record Result(string Title, Uri Url, string Snippet);

    /// <summary>The search in the user's region (dates, shops and prices from where they are).</summary>
    public static Uri Address(string query, string language) =>
        new($"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}&kl={language switch
        {
            "es" => "es-es", "fr" => "fr-fr", "de" => "de-de", "it" => "it-it", "pt" => "br-pt", _ => "us-en",
        }}");

    /// <summary>The results of the page, without ads.</summary>
    public static IReadOnlyList<Result> Results(string html)
    {
        var results = new List<Result>();
        foreach (var block in BlockRx().Split(html).Skip(1))
        {
            if (block.Contains("result--ad", StringComparison.Ordinal)) continue;
            if (LinkRx().Match(block) is not { Success: true } link || Target(WebUtility.HtmlDecode(link.Groups[1].Value)) is not { } url) continue;
            var snippet = SnippetRx().Match(block) is { Success: true } s ? Clean(s.Groups[1].Value) : string.Empty;
            results.Add(new Result(Clean(link.Groups[2].Value), url, snippet));
        }
        return results;
    }

    /// <summary>Pages that say nothing in text (videos, social networks that ask to sign in).</summary>
    public static bool Unreadable(Uri url) =>
        url.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("tiktok.com", StringComparison.OrdinalIgnoreCase)
        || url.Host.EndsWith("instagram.com", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("facebook.com", StringComparison.OrdinalIgnoreCase)
        || url.Host.EndsWith("x.com", StringComparison.OrdinalIgnoreCase) || url.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>The address behind DuckDuckGo's redirect (<c>//duckduckgo.com/l/?uddg=…</c>), or the link itself.</summary>
    private static Uri? Target(string href)
    {
        if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return null;
        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            var target = System.Web.HttpUtility.ParseQueryString(uri.Query)["uddg"];
            return target is not null && Uri.TryCreate(target, UriKind.Absolute, out var real) && real.Scheme is "http" or "https" ? real : null;
        }
        return uri.Scheme is "http" or "https" ? uri : null;
    }

    private static string Clean(string html) => SpacesRx().Replace(WebUtility.HtmlDecode(TagRx().Replace(html, string.Empty)), " ").Trim();

    [GeneratedRegex(@"<div[^>]+class=""[^""]*\bresult\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlockRx();

    [GeneratedRegex(@"class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRx();

    [GeneratedRegex(@"class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SnippetRx();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRx();
}

/// <summary>A web page's readable text: without scripts, styles, menus and the like; the article or main part when it has one.</summary>
public static partial class WebText
{
    public static WebPage Of(string html, int maxChars)
    {
        var title = Clean(TitleRx().Match(html).Groups[1].Value);
        var body = DropRx().Replace(html, " ");
        // The article or main part, when the page marks it: without menus, sidebars and footers.
        var main = ArticleRx().Match(body) is { Success: true } article && article.Length > 500 ? article.Value
            : MainRx().Match(body) is { Success: true } part && part.Length > 500 ? part.Value
            : body;
        main = NoiseRx().Replace(main, " ");
        main = BlockRx().Replace(main, "\n");
        main = TagRx().Replace(main, " ");
        return Plain(WebUtility.HtmlDecode(main), maxChars) with { Title = title };
    }

    /// <summary>Plain text, with its lines tidied and cut at a sentence near <paramref name="maxChars"/>.</summary>
    public static WebPage Plain(string text, int maxChars)
    {
        var lines = text.Split('\n').Select(l => SpacesRx().Replace(l, " ").Trim()).Where(l => l.Length > 1);
        var joined = string.Join('\n', lines);
        if (joined.Length <= maxChars) return new(string.Empty, joined, false);
        var cut = joined[..maxChars];
        var end = cut.LastIndexOfAny(['.', '\n', '!', '?']);
        return new(string.Empty, end > maxChars / 2 ? cut[..(end + 1)] : cut, true);
    }

    private static string Clean(string text) => SpacesRx().Replace(WebUtility.HtmlDecode(text), " ").Trim();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRx();

    [GeneratedRegex(@"<!--.*?-->|<(script|style|noscript|svg|template|iframe|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropRx();

    [GeneratedRegex(@"<(nav|header|footer|aside|button|select)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NoiseRx();

    [GeneratedRegex(@"<article\b[^>]*>.*</article\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ArticleRx();

    [GeneratedRegex(@"<main\b[^>]*>.*</main\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MainRx();

    [GeneratedRegex(@"<(br|/p|/div|/li|/h[1-6]|/tr|/section|/article|/blockquote|/pre|/dd|/dt)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockRx();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex SpacesRx();
}
