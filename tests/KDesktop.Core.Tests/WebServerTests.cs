using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Remote.Web;
using KDesktop.Core.Storage;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Tests;

/// <summary>
/// The web API over real HTTP on localhost (the server falls back to it without administrator rights). Power is never
/// real here: the server only gets a recorder as <see cref="WebServer.RunPower"/> (and PowerTools is off in the tests).
/// </summary>
public sealed class WebServerTests : IDisposable
{
    private sealed class Host(CommandEngine engine) : IWebHost
    {
        public CommandEngine Engine { get; } = engine;
        public T Invoke<T>(Func<T> func) => func();
        public WebIcon? Icon(Command? command, QuickMenuItem? item = null, bool light = false) => null;
        public IReadOnlyDictionary<string, long> Runs() => new Dictionary<string, long>();
        public bool VoiceReady => false;
        public Task<RemoteVoiceResult> VoiceAsync(float[] samples) => throw new NotSupportedException();
        public IWebAssistants? Assistants => null;
    }

    private readonly CommandEngine _engine;
    private readonly WebServer _server;
    private readonly HttpClient _http;
    private readonly List<(PowerMode Mode, string Device)> _power = [];

    public WebServerTests()
    {
        Assert.True(Platform.PowerTools.Disabled);
        _engine = new CommandEngine(new ConfigStore(), new FakeUi());
        _server = new WebServer(new Host(_engine));
        var port = FreePort();
        _server.Start(new WebServerOptions(port, string.Empty));
        Assert.True(_server.IsRunning);
        _http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("X-KD", "1");
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The fake that stands for switching off: it only writes down what was asked.</summary>
    private void RecordPower() => _server.RunPower = (mode, device) =>
    {
        lock (_power) _power.Add((mode, device));
        return Task.CompletedTask;
    };

    /// <summary>Pairs a device and returns its token.</summary>
    private async Task<string> PairAsync(string name = "Móvil")
    {
        var code = _server.Devices.NewCode().Code;
        using var answer = await _http.PostAsync("api/pair", Json(new { code, name }));
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return (await ReadAsync(answer)).GetProperty("token").GetString()!;
    }

    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage answer) =>
        JsonDocument.Parse(await answer.Content.ReadAsStringAsync()).RootElement.Clone();

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : Json(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpStatusCode> PostPowerAsync(string token, string action)
    {
        using var answer = await _http.SendAsync(Request(HttpMethod.Post, "api/power", token, new { action }));
        return answer.StatusCode;
    }

    [Fact]
    public async Task PingAndSessionSayWhichPcAndApiThisIs()
    {
        var ping = await ReadAsync(await _http.GetAsync("api/ping"));
        Assert.Equal(WebServer.ApiVersion, ping.GetProperty("api").GetInt32());
        var pc = ping.GetProperty("pc").GetString();
        Assert.False(string.IsNullOrEmpty(pc));

        var token = await PairAsync();
        var session = await ReadAsync(await _http.SendAsync(Request(HttpMethod.Get, "api/session", token)));
        Assert.Equal(pc, session.GetProperty("pc").GetString());
        Assert.Equal(WebServer.ApiVersion, session.GetProperty("api").GetInt32());
        Assert.False(session.GetProperty("power").GetBoolean());
        Assert.Equal(JsonValueKind.Array, session.GetProperty("wake").GetProperty("adapters").ValueKind);
        // Without «Energía» nothing of «Encender mediante URL» leaves the PC.
        Assert.False(session.GetProperty("wake").TryGetProperty("url", out _));
    }

    [Fact]
    public async Task TheWakeUrlAndItsHeaderOnlyWithPowerOn()
    {
        var token = await PairAsync();
        var settings = _engine.Config.Settings;
        settings.WebWakeUrl = "https://wol.example.com/wol";
        settings.WebWakeBody = "{\"mac\": \"{mac}\"}";
        settings.WebWakeHeaderValue = "Bearer secreto";

        var off = await ReadAsync(await _http.SendAsync(Request(HttpMethod.Get, "api/session", token)));
        Assert.False(off.GetProperty("wake").TryGetProperty("url", out _));

        _server.PowerAllowed = true;
        var on = await ReadAsync(await _http.SendAsync(Request(HttpMethod.Get, "api/session", token)));
        var url = on.GetProperty("wake").GetProperty("url");
        Assert.Equal("https://wol.example.com/wol", url.GetProperty("url").GetString());
        Assert.Equal("POST", url.GetProperty("method").GetString());
        Assert.Equal("Authorization", url.GetProperty("header").GetString());
        Assert.Equal("Bearer secreto", url.GetProperty("value").GetString());
    }

    [Fact]
    public async Task PowerNeedsItsSettingAndAKnownAction()
    {
        RecordPower();
        var token = await PairAsync("Móvil de pruebas");
        Assert.Equal(HttpStatusCode.Forbidden, await PostPowerAsync(token, "lock"));

        _server.PowerAllowed = true;
        Assert.Equal(HttpStatusCode.BadRequest, await PostPowerAsync(token, "explode"));
        Assert.Equal(HttpStatusCode.OK, await PostPowerAsync(token, "lock"));
        await Task.Delay(800);
        lock (_power) Assert.Equal([(PowerMode.Lock, "Móvil de pruebas")], _power);
    }

    [Fact]
    public async Task WithoutTheApplicationNothingIsSwitchedOff()
    {
        // No RunPower (as in every other test): even allowed, the server has nothing to switch off with.
        var token = await PairAsync();
        _server.PowerAllowed = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await PostPowerAsync(token, "lock"));
    }

    [Fact]
    public async Task WakeNeedsAMac()
    {
        var token = await PairAsync();
        using var bad = await _http.SendAsync(Request(HttpMethod.Post, "api/wake", token, new { mac = "no" }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task AnEventForOneDeviceOnlyReachesIt()
    {
        var first = await PairAsync("Uno");
        var second = await PairAsync("Dos");
        var firstId = _server.Devices.Authenticate(first, null)!.Id;

        using var a = await OpenEventsAsync(first);
        using var b = await OpenEventsAsync(second);
        await Task.Delay(200);

        _server.SendTo(firstId, "probe", new { n = 1 });
        Assert.Contains("event: probe", await ReadUntilAsync(a, "probe"));
        Assert.DoesNotContain("probe", await ReadUntilAsync(b, "probe", 800));
    }

    private async Task<StreamReader> OpenEventsAsync(string token)
    {
        var answer = await _http.SendAsync(Request(HttpMethod.Get, "api/events", token), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return new StreamReader(await answer.Content.ReadAsStreamAsync());
    }

    /// <summary>What arrives until <paramref name="text"/> shows up or the time runs out.</summary>
    private static async Task<string> ReadUntilAsync(StreamReader reader, string text, int milliseconds = 3000)
    {
        var seen = new StringBuilder();
        using var cts = new CancellationTokenSource(milliseconds);
        try
        {
            while (!seen.ToString().Contains(text, StringComparison.Ordinal))
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null) break;
                seen.AppendLine(line);
            }
        }
        catch (OperationCanceledException) { }
        return seen.ToString();
    }
}
