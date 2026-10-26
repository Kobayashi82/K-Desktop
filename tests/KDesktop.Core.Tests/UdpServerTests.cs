using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Remote;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

public sealed class UdpServerTests
{
    private static readonly IPAddress ThisPc = IPAddress.Loopback;
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress Vpn = IPAddress.Parse("10.0.0.1");
    private static readonly IPAddress Outside = IPAddress.Parse("8.8.8.8");

    private readonly List<string> _ran = [];

    private UdpServer Server(UdpOptions? options = null)
    {
        var store = new ConfigStore();
        foreach (var (name, remote, hidden) in new[] { ("Beta", true, false), ("Álbum", true, false), ("Secreto", true, true), ("Local", false, false), ("Mostrar escritorio", true, false) })
        {
            var command = new Command { Name = name, Remote = remote, Hidden = hidden };
            command.Actions.Add(new WaitAction { Seconds = 0 });
            store.Config.Commands.Add(command);
        }
        store.Config.Events.Add(new EventRule { Name = "Puerta", Trigger = new RemoteCallTrigger() });
        store.Config.Events.Add(new EventRule { Name = "Otro", Trigger = new RemoteCallTrigger { Name = "timbre" } });
        store.Config.Events.Add(new EventRule { Name = "Apagado", Enabled = false, Trigger = new RemoteCallTrigger() });
        store.Config.Events.Add(new EventRule { Name = "Al iniciar", Trigger = new AppStartTrigger() });
        var engine = new CommandEngine(store, new FakeUi());
        engine.Executed += (c, _) => { lock (_ran) _ran.Add(c.Name); };
        var udp = new UdpServer(engine, new FakeUi());
        udp.Configure(options ?? new UdpOptions(2501));
        return udp;
    }

    private async Task<List<string>> Ran()
    {
        await Task.Delay(300);
        lock (_ran) return [.. _ran];
    }

    [Fact]
    public async Task ListsAndRunsRemoteCommandsByNumberOrName()
    {
        var udp = Server();
        var list = udp.Receive("list", ThisPc)!;
        Assert.StartsWith("OK", list);
        Assert.Contains("1. Álbum", list);
        Assert.Contains("2. Beta", list);
        Assert.DoesNotContain("Secreto", list);
        Assert.DoesNotContain("Local", list);

        Assert.StartsWith("OK", udp.Receive("2", ThisPc));
        Assert.StartsWith("OK", udp.Receive("ALBUM", ThisPc)); // capitals and accents don't matter
        Assert.StartsWith("ERROR", udp.Receive("Local", ThisPc)); // not remote
        Assert.StartsWith("ERROR", udp.Receive("Secreto", ThisPc)); // hidden
        Assert.Equal(["Beta", "Álbum"], (await Ran()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task HiddenCommandsOnlyWithTheirSwitch()
    {
        var udp = Server(new UdpOptions(2501, AllowHidden: true));
        Assert.Contains("Secreto", udp.Receive("list", ThisPc));
        Assert.StartsWith("OK", udp.Receive("run secreto", ThisPc));
        Assert.Equal(["Secreto"], await Ran());
    }

    [Fact]
    public void ScopeDecidesWhoGetsAnAnswer()
    {
        var pc = Server(new UdpOptions(2501, RemoteScope.ThisPc));
        Assert.NotNull(pc.Receive("list", ThisPc));
        Assert.Null(pc.Receive("list", Lan));

        var lan = Server(new UdpOptions(2501, RemoteScope.LocalNetwork));
        Assert.NotNull(lan.Receive("list", Lan));
        Assert.NotNull(lan.Receive("list", Vpn));
        Assert.NotNull(lan.Receive("list", IPAddress.Parse("::ffff:192.168.1.20")));
        Assert.Null(lan.Receive("list", Outside));

        // Internet without a key stays on the local network.
        var open = Server(new UdpOptions(2501, RemoteScope.Internet));
        Assert.Null(open.Receive("list", Outside));
    }

    [Fact]
    public void TheKeyGoesInFrontExceptFromThisPc()
    {
        var udp = Server(new UdpOptions(2501, Key: "s3cret"));
        Assert.Null(udp.Receive("list", Lan));
        Assert.Null(udp.Receive("wrong list", Lan));
        Assert.StartsWith("OK", udp.Receive("s3cret list", Lan));
        Assert.StartsWith("OK", udp.Receive("list", ThisPc));
        Assert.Null(udp.Receive("{\"cmd\":\"list\"}", Lan)); // JSON without signature
    }

    [Fact]
    public void FromTheInternetOnlySignedOrders()
    {
        var udp = Server(new UdpOptions(2501, RemoteScope.Internet, "s3cret"));
        Assert.Null(udp.Receive("s3cret list", Outside));
        var reply = udp.Receive(Signed("s3cret", new { cmd = "list", t = Now(), id = "a1" }), Outside);
        using var json = JsonDocument.Parse(reply!);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(3, json.RootElement.GetProperty("commands").GetArrayLength());
    }

    [Fact]
    public void SignedOrdersCannotBeReplayedForgedOrOld()
    {
        var udp = Server(new UdpOptions(2501, RemoteScope.Internet, "s3cret"));
        var message = Signed("s3cret", new { cmd = "list", t = Now(), id = "once" });
        Assert.NotNull(udp.Receive(message, Outside));
        Assert.Null(udp.Receive(message, Outside)); // same id
        Assert.Null(udp.Receive(Signed("other", new { cmd = "list", t = Now(), id = "b" }), Outside));
        Assert.Null(udp.Receive(Signed("s3cret", new { cmd = "list", t = Now() - 600, id = "c" }), Outside));
        Assert.Null(udp.Receive(Signed("s3cret", new { cmd = "list", t = Now() }), Outside)); // no id
    }

    [Fact]
    public void TooManyFailuresBlockTheAddress()
    {
        var udp = Server(new UdpOptions(2501, Key: "s3cret"));
        for (var i = 0; i < 5; i++) Assert.Null(udp.Receive("bad list", Lan));
        Assert.Null(udp.Receive("s3cret list", Lan));
        Assert.NotNull(udp.Receive("s3cret list", IPAddress.Parse("192.168.1.21")));
    }

    [Fact]
    public void SwitchesTurnOrdersOff()
    {
        var udp = Server(new UdpOptions(2501, AllowEvents: false));
        Assert.StartsWith("ERROR", udp.Receive("keys ctrl+c", ThisPc));
        Assert.StartsWith("ERROR", udp.Receive("type hola", ThisPc));
        Assert.StartsWith("ERROR", udp.Receive("event puerta", ThisPc));
        Assert.StartsWith("ERROR", udp.Receive("ask lander hola", ThisPc));
    }

    [Fact]
    public void JsonOrdersGetJsonReplies()
    {
        var udp = Server();
        using var json = JsonDocument.Parse(udp.Receive("{\"cmd\":\"run\",\"arg\":\"nada\"}", Lan)!);
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("notFound", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void TextOrdersAreParsed()
    {
        Assert.Equal(new UdpServer.Request("ask", "qué hora es", "Lander"), UdpServer.ParseText("ask Lander qué hora es"));
        Assert.Equal(new UdpServer.Request("run", "Abrir Chrome"), UdpServer.ParseText("Abrir Chrome"));
        Assert.Equal(new UdpServer.Request("media", "next"), UdpServer.ParseText("MEDIA next"));
        Assert.Null(UdpServer.ParseJson("{\"cmd\":\"format c:\"}"));
    }

    [Fact]
    public async Task OrdersAlsoInTheLanguageButCommandNamesWin()
    {
        // The tests run in Spanish.
        Assert.Equal(new UdpServer.Request("list"), UdpServer.ParseText("Lista"));
        Assert.Equal(new UdpServer.Request("keys", "ctrl+c"), UdpServer.ParseText("teclas ctrl+c"));
        Assert.Equal(new UdpServer.Request("show"), UdpServer.ParseText("mostrar"));

        var udp = Server();
        Assert.Contains("1. Álbum", udp.Receive("lista", ThisPc));
        Assert.StartsWith("OK", udp.Receive("mostrar escritorio", ThisPc));
        Assert.Equal(["Mostrar escritorio"], await Ran());
    }

    [Fact]
    public void ListsTheCallsEventsWaitFor()
    {
        var udp = Server();
        Assert.Equal("OK\nPuerta\ntimbre", udp.Receive("eventos", ThisPc));
        using var json = JsonDocument.Parse(udp.Receive("{\"cmd\":\"events\"}", ThisPc)!);
        Assert.Equal(2, json.RootElement.GetProperty("events").GetArrayLength());
        Assert.StartsWith("ERROR", Server(new UdpOptions(2501, AllowEvents: false)).Receive("events", ThisPc));
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Signed(string key, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(json))) + "." + json;
    }
}
