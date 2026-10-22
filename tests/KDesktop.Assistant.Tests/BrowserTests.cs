using System.Net;
using System.Net.Sockets;
using System.Text;
using KDesktop.Assistant.Host.Runtime;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 8.4: the assistants' own browser for real (WebView2, which Windows 11 has): a page whose text only its script
/// writes, served on a local port, read after the script ran; and one page after another with the same browser.
/// </summary>
[Collection(OnTheDesktop.Name)]
public sealed class BrowserTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-browser-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly HttpListener _listener = new();
    private readonly string _address;

    public BrowserTests()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _address = $"http://localhost:{port}/";
        _listener.Prefixes.Add(_address);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        _listener.Close();
        try { Directory.Delete(_folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            var page = context.Request.Url!.AbsolutePath == "/second"
                ? "<html><head><title>Segunda</title></head><body><p>Una página normal, sin scripts.</p></body></html>"
                : "<html><head><title>Pintada</title></head><body><div id=r></div><script>setTimeout(function () { document.getElementById('r').innerText = 'Texto pintado por el script: 1.199 €'; document.title = 'Pintada ya'; }, 300);</script></body></html>";
            var bytes = Encoding.UTF8.GetBytes(page);
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    [Fact]
    public async Task ItReadsWhatTheScriptsDrewAndThenAnotherPage()
    {
        using var browser = new ModuleBrowser(_folder);
        var drawn = await browser.ReadAsync(new Uri(_address), CancellationToken.None);
        Assert.NotNull(drawn);
        Assert.Contains("Texto pintado por el script: 1.199 €", drawn.Text, StringComparison.Ordinal);
        Assert.Equal("Pintada ya", drawn.Title);
        Assert.Contains("<div id=\"r\">", drawn.Html, StringComparison.Ordinal);

        var second = await browser.ReadAsync(new Uri(_address + "second"), CancellationToken.None);
        Assert.Equal("Segunda", second?.Title);
        Assert.Contains("sin scripts", second?.Text, StringComparison.Ordinal);
        // Its own session, in its folder.
        Assert.True(Directory.Exists(Path.Combine(_folder, "EBWebView")));
    }
}
