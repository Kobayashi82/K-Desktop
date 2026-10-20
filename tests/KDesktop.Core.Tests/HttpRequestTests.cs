using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

public sealed class HttpRequestTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _base;
    private readonly List<(string Method, string Path, Dictionary<string, string> Headers, string Body)> _seen = [];

    /// <summary>What the server answers: path → (status, body, delay).</summary>
    private readonly Dictionary<string, (int Status, string Body, int DelayMs)> _routes = new()
    {
        ["/ok"] = (200, "{\"state\":\"off\"}", 0),
        ["/missing"] = (404, "not here", 0),
        ["/boom"] = (500, "kaput", 0),
        ["/slow"] = (200, "late", 3000),
    };

    public HttpRequestTests()
    {
        var port = FreePort();
        _base = $"http://localhost:{port}";
        _listener.Prefixes.Add(_base + "/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(async () =>
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var headers = ctx.Request.Headers.AllKeys.Where(k => k is not null)
                    .ToDictionary(k => k!, k => ctx.Request.Headers[k]!, StringComparer.OrdinalIgnoreCase);
                lock (_seen) _seen.Add((ctx.Request.HttpMethod, ctx.Request.Url!.AbsolutePath, headers, body));
                var (status, text, delay) = _routes.GetValueOrDefault(ctx.Request.Url!.AbsolutePath, (404, "", 0));
                if (delay > 0) await Task.Delay(delay);
                try
                {
                    ctx.Response.StatusCode = status;
                    var bytes = Encoding.UTF8.GetBytes(text);
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
                catch { /* the client gave up */ }
            });
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose() => _listener.Close();

    [Fact]
    public async Task SendsMethodHeadersAndBody()
    {
        var a = new HttpRequestAction { Method = HttpVerb.Post, Url = _base + "/ok", Body = "{\"day\":\"{fecha}\"}" };
        a.Headers.Add(new HttpHeader { Name = "Authorization", Value = "Bearer abc", Secret = true });
        a.Headers.Add(new HttpHeader { Name = "X-Test", Value = "1" });

        var result = await HttpRequests.SendAsync(a, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.Status);
        Assert.Equal("{\"state\":\"off\"}", result.Body);
        var seen = Assert.Single(_seen);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("Bearer abc", seen.Headers["Authorization"]);
        Assert.Equal("1", seen.Headers["X-Test"]);
        Assert.StartsWith("application/json", seen.Headers["Content-Type"]);
        Assert.Equal($"{{\"day\":\"{DateTime.Now:yyyy-MM-dd}\"}}", seen.Body);
    }

    [Fact]
    public async Task OwnContentTypeIsUsed()
    {
        var a = new HttpRequestAction { Method = HttpVerb.Put, Url = _base + "/ok", Body = "hola", ContentType = "text/plain" };
        await HttpRequests.SendAsync(a, CancellationToken.None);
        Assert.StartsWith("text/plain", Assert.Single(_seen).Headers["Content-Type"]);
    }

    [Theory]
    [InlineData("/missing", 404)]
    [InlineData("/boom", 500)]
    public async Task ErrorCodesAreNotSuccess(string path, int code)
    {
        var result = await HttpRequests.SendAsync(new HttpRequestAction { Url = _base + path }, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(code, result.Status);
        Assert.Contains(code.ToString(CultureInfo.InvariantCulture), result.Describe());
    }

    [Fact]
    public async Task TimeoutComesBackAsAnError()
    {
        var a = new HttpRequestAction { Url = _base + "/slow", TimeoutSeconds = 1 };
        var started = Environment.TickCount64;
        var result = await HttpRequests.SendAsync(a, CancellationToken.None);
        Assert.Null(result.Status);
        Assert.NotNull(result.Error);
        Assert.InRange(Environment.TickCount64 - started, 800, 2500);
    }

    [Fact]
    public async Task CancellingThrows()
    {
        using var cts = new CancellationTokenSource(300);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HttpRequests.SendAsync(new HttpRequestAction { Url = _base + "/slow" }, cts.Token));
    }

    [Fact]
    public async Task ConnectionErrorComesBackAsAnError()
    {
        var result = await HttpRequests.SendAsync(new HttpRequestAction { Url = $"http://localhost:{FreePort()}/x", TimeoutSeconds = 5 }, CancellationToken.None);
        Assert.Null(result.Status);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task InvalidUrlComesBackAsAnError()
    {
        var result = await HttpRequests.SendAsync(new HttpRequestAction { Url = "ftp://example.com/x" }, CancellationToken.None);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("/boom", HttpResultDisplay.OnError, true)]
    [InlineData("/boom", HttpResultDisplay.Never, false)]
    [InlineData("/ok", HttpResultDisplay.OnError, false)]
    [InlineData("/ok", HttpResultDisplay.Always, true)]
    public async Task EngineShowsTheResultAsAsked(string path, HttpResultDisplay show, bool expected)
    {
        var ui = new FakeUi();
        var store = new ConfigStore();
        var command = new Command { Name = "http" };
        command.Actions.Add(new HttpRequestAction { Url = _base + path, ShowResult = show });
        store.Config.Commands.Add(command);
        var engine = new CommandEngine(store, ui);

        var feedback = new ExecutionFeedback();
        using (feedback.Begin()) await engine.RunAsync(command, TriggerSource.Manual);

        Assert.Equal(expected, ui.Osd.Count == 1);
        if (path == "/boom")
        {
            Assert.True(feedback.HasWarning);
            Assert.Contains(feedback.Messages, m => m.Contains("500"));
            if (expected) Assert.Equal(OsdKind.Warning, ui.Osd[0].Kind);
        }
    }
}
