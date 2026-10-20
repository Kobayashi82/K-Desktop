using System.IO.Pipes;
using System.Text;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 1.2 over a real pipe: the handshake, the checks of who connects, the heartbeat and the line limit. Server
/// and client run in this process, so «the same executable» is the test host.
/// </summary>
public class PipeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly Heartbeat Fast = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(50));

    private static string UniqueName() => "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

    private static ClientPolicy Me(Func<int, bool>? pid = null) => new(Environment.ProcessPath!, pid ?? (_ => true));

    private static WelcomeMessage Welcome() => new("es", "dark", false, null);

    private static HelloMessage Hello(int protocol = AssistantPipe.Protocol) => new(protocol, "1.0", Environment.ProcessId);

    [Fact]
    public async Task TheyGreetAndTalk()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, Me(), Welcome);
        var connected = new TaskCompletionSource<HelloMessage>();
        var fromClient = new TaskCompletionSource<PipeMessage>();
        server.Connected += h => connected.TrySetResult(h);
        server.Received += m => fromClient.TrySetResult(m);
        server.Start();

        await using var client = new AssistantPipeClient(name);
        var fromServer = new TaskCompletionSource<PipeMessage>();
        client.Received += m => fromServer.TrySetResult(m);
        Assert.Equal(ConnectOutcome.Welcomed, await client.ConnectAsync(Hello(), Wait, CancellationToken.None));
        Assert.Equal("es", client.Welcome!.Lang);
        Assert.Equal(Environment.ProcessId, (await connected.Task.WaitAsync(Wait)).Pid);

        Assert.True(client.Send(new StatusMessage("idle")));
        Assert.Equal("idle", Assert.IsType<StatusMessage>(await fromClient.Task.WaitAsync(Wait)).State);
        Assert.True(server.Send(new PauseMessage(true)));
        Assert.True(Assert.IsType<PauseMessage>(await fromServer.Task.WaitAsync(Wait)).Paused);
    }

    [Fact]
    public async Task AnotherExecutableIsTurnedAway()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(@"C:\Windows\notepad.exe", _ => true), Welcome);
        var rejected = new TaskCompletionSource<string>();
        server.Rejected += r => rejected.TrySetResult(r);
        server.Start();

        await using var client = new AssistantPipeClient(name);
        Assert.Equal(ConnectOutcome.Rejected, await client.ConnectAsync(Hello(), Wait, CancellationToken.None));
        Assert.Contains("otro ejecutable", await rejected.Task.WaitAsync(Wait));
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task AProcessThatWasNotLaunchedIsTurnedAway()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, Me(pid => pid == 4), Welcome);
        var rejected = new TaskCompletionSource<string>();
        server.Rejected += r => rejected.TrySetResult(r);
        server.Start();

        await using var client = new AssistantPipeClient(name);
        Assert.Equal(ConnectOutcome.Rejected, await client.ConnectAsync(Hello(), Wait, CancellationToken.None));
        Assert.Contains("no es el proceso", await rejected.Task.WaitAsync(Wait));
    }

    [Fact]
    public async Task AnotherProtocolIsRejectedWithItsReason()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, Me(), Welcome);
        server.Start();

        await using var client = new AssistantPipeClient(name);
        Assert.Equal(ConnectOutcome.Rejected, await client.ConnectAsync(Hello(protocol: 99), Wait, CancellationToken.None));
        Assert.Equal("protocol", client.RejectReason);
    }

    [Fact]
    public async Task NoPipeMeansUnavailable()
    {
        await using var client = new AssistantPipeClient(UniqueName());
        Assert.Equal(ConnectOutcome.Unavailable, await client.ConnectAsync(Hello(), TimeSpan.FromMilliseconds(200), CancellationToken.None));
    }

    [Fact]
    public async Task TheHeartbeatKeepsAQuietConnectionAlive()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, Me(), Welcome, Fast);
        var dropped = false;
        server.Disconnected += _ => dropped = true;
        server.Start();

        await using var client = new AssistantPipeClient(name, Fast);
        Assert.Equal(ConnectOutcome.Welcomed, await client.ConnectAsync(Hello(), Wait, CancellationToken.None));
        await Task.Delay(TimeSpan.FromSeconds(2)); // more than three «dead» periods without a single real message
        Assert.True(client.IsConnected);
        Assert.False(dropped);
    }

    [Fact]
    public async Task APeerThatStopsAnsweringIsGivenUp()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, Me(), Welcome, Fast);
        var gone = new TaskCompletionSource<string>();
        server.Disconnected += r => gone.TrySetResult(r);
        server.Start();

        // A raw client: says hello, reads nothing more and never answers the pings.
        await using var raw = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await raw.ConnectAsync(5000);
        await raw.WriteAsync(Encoding.UTF8.GetBytes(PipeJson.Serialize(Hello()) + "\n"));
        await raw.FlushAsync();
        Assert.Contains("no responde", await gone.Task.WaitAsync(Wait));
    }

    [Fact]
    public async Task ALineOverTheLimitClosesTheConnection()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, Me(), Welcome);
        var gone = new TaskCompletionSource<string>();
        server.Disconnected += r => gone.TrySetResult(r);
        server.Start();

        await using var raw = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await raw.ConnectAsync(5000);
        await raw.WriteAsync(Encoding.UTF8.GetBytes(PipeJson.Serialize(Hello()) + "\n"));
        var huge = Encoding.UTF8.GetBytes("{\"t\":\"log\",\"x\":\"" + new string('a', PipeJson.MaxLine + 10) + "\"}\n");
        try { await raw.WriteAsync(huge); } catch (IOException) { /* the server may close while we write */ }
        Assert.Contains("1 MiB", await gone.Task.WaitAsync(Wait));
    }

    [Fact]
    public async Task OnlyOneClientAtATime()
    {
        var name = UniqueName();
        await using var server = new AssistantPipeServer(name, Me(), Welcome);
        server.Start();

        await using var first = new AssistantPipeClient(name);
        Assert.Equal(ConnectOutcome.Welcomed, await first.ConnectAsync(Hello(), Wait, CancellationToken.None));
        await using var second = new AssistantPipeClient(name);
        Assert.Equal(ConnectOutcome.Unavailable, await second.ConnectAsync(Hello(), TimeSpan.FromMilliseconds(500), CancellationToken.None));

        // When the first goes, the next one is welcome.
        await first.CloseAsync("prueba");
        await using var third = new AssistantPipeClient(name);
        Assert.Equal(ConnectOutcome.Welcomed, await third.ConnectAsync(Hello(), Wait, CancellationToken.None));
    }
}
