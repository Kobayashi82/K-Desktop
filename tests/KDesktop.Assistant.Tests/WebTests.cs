using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 8.4: the internet for the assistant. Searching (DuckDuckGo without scripts) and reading the first result, and
/// the assistants' own browser for the pages that turn plain requests away or only show with scripts.
/// </summary>
public sealed class WebTests
{
    private static JsonElement Args(object arguments) => JsonSerializer.SerializeToElement(arguments);

    private const string Results = """
        <div class="serp__results"><div id="links" class="results">
        <div class="result results_links results_links_deep result--ad ">
          <a rel="nofollow" class="result__a" href="https://duckduckgo.com/y.js?ad_domain=compra.es">Compra telescopios</a>
          <a class="result__snippet" href="#">Anuncio</a></div>
        <div class="result results_links results_links_deep web-result ">
          <h2 class="result__title"><a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Feclipses.ign.es%2F&amp;rut=abc">Eclipses &amp; más - <b>IGN</b></a></h2>
          <div class="result__extras"><div class="result__extras__url">eclipses.ign.es</div></div>
          <a class="result__snippet" href="//duckduckgo.com/l/?uddg=x">El próximo <b>eclipse</b> total de Sol visible en España será el 12 de agosto de 2026.</a></div>
        <div class="result results_links results_links_deep web-result ">
          <h2 class="result__title"><a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D1&amp;rut=def">Vídeo del eclipse</a></h2>
          <a class="result__snippet" href="#">Míralo en directo</a></div>
        </div></div>
        """;

    [Fact]
    public void TheResultsWithoutAdsAndWithTheirRealAddress()
    {
        var results = DuckDuckGo.Results(Results);
        Assert.Equal(2, results.Count);
        Assert.Equal("Eclipses & más - IGN", results[0].Title);
        Assert.Equal("https://eclipses.ign.es/", results[0].Url.ToString());
        Assert.StartsWith("El próximo eclipse total", results[0].Snippet, StringComparison.Ordinal);
        Assert.True(DuckDuckGo.Unreadable(results[1].Url));
        Assert.Contains("kl=es-es", DuckDuckGo.Address("próximo eclipse", "es").Query, StringComparison.Ordinal);
    }

    /// <summary>The internet in a dictionary: address (without the query) → page; the rest is 404.</summary>
    private sealed class FakeInternet(Dictionary<string, (HttpStatusCode Status, string Html)> pages) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.GetLeftPart(UriPartial.Path);
            Asked.Add(key);
            var (status, html) = pages.TryGetValue(key, out var page) ? page : (HttpStatusCode.NotFound, "");
            var response = new HttpResponseMessage(status) { Content = new StringContent(html, Encoding.UTF8) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeBrowser(string text) : IWebBrowser
    {
        public int Pages { get; private set; }

        public Task<BrowsedPage?> ReadAsync(Uri uri, CancellationToken cancellationToken)
        {
            Pages++;
            return Task.FromResult<BrowsedPage?>(new BrowsedPage("Desde el navegador", uri.ToString(), text, $"<html><body>{text}</body></html>"));
        }
    }

    private const string Article = "<html><title>IGN</title><body><main><p>El 12 de agosto de 2026 habrá un eclipse total de Sol visible desde el norte de España, el primero desde 1905. La franja de totalidad cruzará Galicia, Asturias, Castilla y León, Aragón y Valencia.</p><p>Después llegará el anular de 2027, visible en el sur de la península, y el de 2028, que cruzará el país de oeste a este.</p></main></body></html>";

    [Fact]
    public async Task ItSearchesAndReadsTheFirstPage()
    {
        var internet = new FakeInternet(new()
        {
            ["https://html.duckduckgo.com/html/"] = (HttpStatusCode.OK, Results),
            ["https://eclipses.ign.es/"] = (HttpStatusCode.OK, Article),
        });
        var result = await new LookUpWebTool(new WebReader(internet)).RunAsync(Args(new { query = "próximo eclipse España" }), new ToolContext("lander"), default);
        Assert.False(result.Failed, result.Text);
        Assert.Contains("1. Eclipses & más - IGN — eclipses.ign.es", result.Text, StringComparison.Ordinal);
        Assert.Contains("el primero desde 1905", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("youtube.com/watch", internet.Asked);
        Assert.Contains("próximo eclipse", result.Done, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingFoundSaysSo()
    {
        var internet = new FakeInternet(new() { ["https://html.duckduckgo.com/html/"] = (HttpStatusCode.OK, "<html><body>No results.</body></html>") });
        var result = await new LookUpWebTool(new WebReader(internet)).RunAsync(Args(new { query = "zzqx" }), new ToolContext(null), default);
        Assert.True(result.Failed);
    }

    [Fact]
    public async Task APageThatTurnsRequestsAwayOrIsDrawnByScriptsGoesToTheBrowser()
    {
        var internet = new FakeInternet(new()
        {
            ["https://tienda.es/rtx"] = (HttpStatusCode.Forbidden, "Access denied"),
            ["https://app.es/"] = (HttpStatusCode.OK, "<html><body><div id=root></div><script>render()</script></body></html>"),
            ["https://normal.es/"] = (HttpStatusCode.OK, Article),
        });
        var browser = new FakeBrowser("RTX 5080: 1.199 €. Envío gratis. " + string.Concat(Enumerable.Repeat("Más texto de la ficha del producto. ", 20)));
        var reader = new WebReader(internet, browser);

        var turned = await reader.ReadAsync(new Uri("https://tienda.es/rtx"), 9000, default);
        Assert.Equal("Desde el navegador", turned.Title);
        Assert.Contains("1.199 €", turned.Text, StringComparison.Ordinal);

        var drawn = await reader.ReadAsync(new Uri("https://app.es/"), 9000, default);
        Assert.Contains("1.199 €", drawn.Text, StringComparison.Ordinal);
        Assert.Equal(2, browser.Pages);

        var plain = await reader.ReadAsync(new Uri("https://normal.es/"), 9000, default);
        Assert.Equal("IGN", plain.Title);
        Assert.Equal(2, browser.Pages); // plenty of text over HTTP: the browser is not needed

        // Without a browser, the refusal is the answer.
        await Assert.ThrowsAsync<HttpRequestException>(() => new WebReader(internet).ReadAsync(new Uri("https://tienda.es/rtx"), 9000, default));
    }
}
