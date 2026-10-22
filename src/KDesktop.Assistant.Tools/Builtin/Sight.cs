using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>A picture of the screen: what it shows for the model («the window «X» of code»), the picture as JPEG and, for people, the window's title (null: a monitor).</summary>
public sealed record ScreenPicture(byte[] Jpeg, string What, int Width, int Height, string? Window = null);

/// <summary>What the host lends the tools to see the desktop (stage 8.3): pictures of it and the page open in the browser.</summary>
public interface IScreen
{
    /// <summary>The window in front (the user's, never K-Desktop's) or, with <paramref name="wholeScreen"/>, the monitor with the mouse; null when it cannot.</summary>
    Task<ScreenPicture?> CaptureAsync(bool wholeScreen, CancellationToken cancellationToken);

    /// <summary>The address of the page open in the browser in front (Chrome, Edge, Firefox…), or null.</summary>
    Task<string?> BrowserAddressAsync(CancellationToken cancellationToken);
}

/// <summary>The model looking at a picture (stage 8.3): the assistant's own model when it sees.</summary>
public interface IVision
{
    /// <summary>Whether the model <paramref name="assistant"/> talks with can see pictures now.</summary>
    bool CanSee(string? assistant);

    /// <summary>What the model says about <paramref name="jpeg"/> for <paramref name="question"/>; null when it did not answer.</summary>
    Task<string?> LookAsync(string? assistant, byte[] jpeg, string question, CancellationToken cancellationToken);
}

/// <summary>
/// «¿Qué pone aquí?», «¿qué error me sale?», «¿qué ves en la pantalla?» (stage 8.3): a picture of the window in front
/// (or of the monitor) to the assistant's model, which must see; what it says goes back as text. Nothing is kept.
/// </summary>
public sealed class LookAtScreenTool(IScreen? screen, IVision? vision) : ITool
{
    public string Name => "look_at_screen";

    public string Description => "Looks at the user's screen (the window in front, or the whole monitor) and tells what is there: reads its text, an error message, what a picture shows. Use it when the user asks about what is on their screen (\"what does it say here\", \"what is this\").";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"question":{"type":"string","description":"What to look for or answer about the screen, e.g. read the error message"},"whole_screen":{"type":"boolean","description":"The whole monitor instead of the window in front"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.look_at_screen.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (screen is null || vision is null || !vision.CanSee(context.Assistant))
            return new ToolResult("You cannot see the screen: the model in use has no vision. Say so: a model that sees can be chosen in Options → Brain.")
            {
                Done = Loc.T("tool.look_at_screen.blind"), Reply = Loc.T("tool.look_at_screen.blind"), Failed = true,
            };
        var whole = string.Equals(arguments.GetString("whole_screen"), "true", StringComparison.OrdinalIgnoreCase);
        if (await screen.CaptureAsync(whole, cancellationToken).ConfigureAwait(false) is not { } picture)
            return ToolResult.Fail("The screen could not be captured.");
        var question = arguments.GetString("question")?.Trim() is { Length: > 0 } asked ? asked : "What is on the screen?";
        // The question quoted and the task said apart: a 4B model given the question bare only repeated it.
        var prompt = $"The user asks about this screenshot of {picture.What}: «{question}». Look at the picture and answer: say what is shown and copy the important visible text word for word.";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var seen = await vision.LookAsync(context.Assistant, picture.Jpeg, prompt, cancellationToken).ConfigureAwait(false);
        Log.Info($"Asistente: pantalla ({picture.What}, {picture.Width}×{picture.Height}, {picture.Jpeg.Length / 1024} KB) vista en {watch.ElapsedMilliseconds} ms");
        if (string.IsNullOrWhiteSpace(seen)) return ToolResult.Fail("The model did not say what it saw.");
        return new ToolResult($"On the screen ({picture.What}): {seen.Trim()}\nAnswer the user from this, in their language.")
        {
            Done = Loc.T("tool.look_at_screen.done", picture.Window is { Length: > 0 } title ? ToolText.Clean(title) : Loc.T("tool.look_at_screen.monitor")),
        };
    }
}

/// <summary>
/// «Léeme esta página», «resúmeme este artículo», «¿qué dice la web de…?» (stage 8.3): a web page as text for the model,
/// the one given or the one open in the browser in front. Only http and https.
/// </summary>
public sealed class ReadWebTool(IScreen? screen, HttpMessageHandler? network = null) : ITool
{
    /// <summary>How much of the page goes to the model (about 2 500 tokens).</summary>
    public const int MaxChars = 9000;

    private const long MaxBytes = 3 * 1024 * 1024;

    // Pages in windows-1252 and the like.
    static ReadWebTool() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private readonly HttpClient _http = MakeClient(network);

    public string Name => "read_web";

    public string Description => "Reads a web page as text, to summarise it or answer about it: the address given, or (empty) the page open in the user's browser (\"this page\", \"this article\"). It does not open anything.";

    public string ParametersSchemaJson => ToolText.Optional(("url", "string", "The address; empty: the page open in the browser"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

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
            page = await ReadAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException && !cancellationToken.IsCancellationRequested)
        {
            Log.Warn($"Asistente: no se ha podido leer {uri.Host}: {ex.Message}");
            return new ToolResult($"The page {uri.Host} could not be read ({ex.Message}). Say so.") { Done = Loc.T("tool.read_web.failed", uri.Host), Failed = true };
        }
        if (page.Text.Length == 0)
            return new ToolResult($"{uri.Host} has no text to read (it may need a browser to show it). Say so.") { Done = Loc.T("tool.read_web.failed", uri.Host), Failed = true };
        return new ToolResult($"Page: {page.Title}\nAddress: {uri}\n{(page.Cut ? "(Only its beginning.)\n" : string.Empty)}\n{page.Text}\n\nAnswer the user from this page, briefly, in their language.")
        {
            Done = Loc.T("tool.read_web.done", page.Title.Length > 0 ? ToolText.Clean(page.Title) : uri.Host),
        };
    }

    public sealed record WebPage(string Title, string Text, bool Cut);

    private async Task<WebPage> ReadAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var type = response.Content.Headers.ContentType?.MediaType ?? "text/html";
        if (!type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && !type.Contains("xml", StringComparison.OrdinalIgnoreCase) && !type.Contains("json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"it is {type}, not a page");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0 && buffer.Length < MaxBytes) buffer.Write(chunk, 0, read);
        var charset = response.Content.Headers.ContentType?.CharSet;
        Encoding encoding;
        try { encoding = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        var body = encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return type.Contains("html", StringComparison.OrdinalIgnoreCase) ? WebText.Of(body, MaxChars) : WebText.Plain(body, MaxChars);
    }

    private static HttpClient MakeClient(HttpMessageHandler? network)
    {
        var http = network is null
            ? new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
            : new HttpClient(network, disposeHandler: false);
        http.Timeout = Timeout.InfiniteTimeSpan;
        // Some sites only answer browsers.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(Loc.Instance.Language + ",en;q=0.7");
        return http;
    }
}

/// <summary>A web page's readable text: without scripts, styles, menus and the like; the article or main part when it has one.</summary>
public static partial class WebText
{
    public static ReadWebTool.WebPage Of(string html, int maxChars)
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
    public static ReadWebTool.WebPage Plain(string text, int maxChars)
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
