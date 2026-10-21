using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 3.4 with the real KDesktop.exe in assistant mode, Dot on the desktop and a fake Ollama over HTTP: «¿qué hora
/// es?» is answered by a rule without asking the model; anything else goes to the model and comes back as <c>chat</c>,
/// with the assistant thinking first. The tools reach the model chosen by meaning (stage 3.5): this fake K-Desktop
/// answers <c>embed</c>. Dot shows for a few seconds; the data folder is a temporary one.
/// </summary>
public class TalkProcessTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(TalkProcessTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila samples/DotAssistant primero");

    [Fact]
    public async Task AnOrderGoesByTheRulesOrByTheAi()
    {
        var root = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "talk-" + Guid.NewGuid().ToString("N")[..8]);
        var data = Path.Combine(root, "data");
        using var ollama = new FakeOllama("Hola, soy Dot y **te escucho**.");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
        var config = new JsonObject { ["brain"] = new JsonObject { ["provider"] = "ollama", ["endpoint"] = ollama.Address, ["model"] = "" } };

        Process? process = null;
        var received = new ConcurrentQueue<PipeMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false, config));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += received.Enqueue;
        // K-Desktop's EmbeddingGemma: any vectors will do, of the same model.
        server.Received += m =>
        {
            if (m is EmbedMessage embed)
                server.Send(new EmbeddedMessage(embed.Id, [.. embed.Texts.Select(t => new[] { 1f, t.Length % 7 / 7f })], "fake"));
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", DotPackage, Path.Combine(data, "Assistants", "Data", "dot"), "startup")));
            await Until(() => received.OfType<LoadedMessage>().Any() && received.OfType<BrainStateMessage>().Any(b => b.State == "ready"));
            // Connected, it got the vectors of its tools' example phrases ready (kept by K-Desktop).
            await Until(() => received.OfType<EmbedMessage>().Any(e => e.Keep == true));
            Assert.True(received.OfType<LoadedMessage>().Single().Ok);

            // A rule: no model involved.
            Assert.True(server.Send(new HeardMessage("¿Qué hora es?", "panel", "dot")));
            await Until(() => Answers(received).Count >= 1);
            Assert.StartsWith("Son las ", Answers(received)[0].Text, StringComparison.Ordinal);
            Assert.Contains(received.OfType<ChatLineMessage>(), c => c is { Role: "user", Text: "¿Qué hora es?", Assistant: "dot" });
            Assert.Equal(0, ollama.Chats);
            await Until(() => received.OfType<LogMessage>().Any(l => l.Entry.Route == "rule"));

            // Anything else: the model, with Dot thinking first.
            Assert.True(server.Send(new HeardMessage("cuéntame algo", "panel", "dot")));
            await Until(() => Answers(received).Count >= 2);
            Assert.Equal("Hola, soy Dot y te escucho.", Answers(received)[1].Text);
            Assert.Equal(1, ollama.Chats);
            Assert.Contains(received.OfType<StatusMessage>(), s => s is { State: "thinking", Assistant: "dot" });
            using (var request = JsonDocument.Parse(ollama.LastRequest))
            {
                Assert.StartsWith("cuéntame algo\n", request.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString(), StringComparison.Ordinal);
                Assert.InRange(request.RootElement.GetProperty("tools").GetArrayLength(), 3, 5);
            }
            Assert.Contains(received.OfType<EmbedMessage>(), e => e.Keep is null && e.Texts.SequenceEqual(["cuéntame algo"]));
            // The second order came while Dot was still saying the first, which it replaced: one «idle» at the end.
            await Until(() => received.OfType<StatusMessage>().LastOrDefault(s => s.Assistant == "dot") is { State: "idle" });

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }

        var log = await File.ReadAllTextAsync(Path.Combine(data, "Logs", "assistant.log"));
        Assert.DoesNotContain("ha fallado", log);
        Assert.DoesNotContain("no se ha podido atender", log);
    }

    /// <summary>
    /// 5.0.105: what an assistant has of its own (the <c>personal</c> section). Dot speaks English and has the personality
    /// the user wrote, while the process speaks Spanish; with nothing of its own, all of it is the general again.
    /// </summary>
    [Fact]
    public async Task AnAssistantSpeaksInItsOwnLanguageWithItsOwnPersonality()
    {
        var root = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "personal-" + Guid.NewGuid().ToString("N")[..8]);
        var data = Path.Combine(root, "data");
        using var ollama = new FakeOllama("Beep. Hello.");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
        var config = new JsonObject
        {
            ["brain"] = new JsonObject { ["provider"] = "ollama", ["endpoint"] = ollama.Address, ["model"] = "" },
            ["personal"] = new JsonObject { ["dot"] = new JsonObject { ["language"] = "en", ["prompt"] = "You are Zorg, a grumpy little robot." } },
        };

        Process? process = null;
        var received = new ConcurrentQueue<PipeMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false, config));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += received.Enqueue;
        server.Received += m =>
        {
            if (m is EmbedMessage embed)
                server.Send(new EmbeddedMessage(embed.Id, [.. embed.Texts.Select(t => new[] { 1f, t.Length % 7 / 7f })], "fake"));
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", DotPackage, Path.Combine(data, "Assistants", "Data", "dot"), "startup")));
            await Until(() => received.OfType<LoadedMessage>().Any() && received.OfType<BrainStateMessage>().Any(b => b.State == "ready"));

            // A rule, said in Spanish, answered in its language.
            Assert.True(server.Send(new HeardMessage("¿Qué hora es?", "panel", "dot")));
            await Until(() => Answers(received).Count >= 1);
            Assert.StartsWith("It's ", Answers(received)[0].Text, StringComparison.Ordinal);

            // The AI: its personality and its language.
            Assert.True(server.Send(new HeardMessage("cuéntame algo", "panel", "dot")));
            await Until(() => Answers(received).Count >= 2);
            using (var request = JsonDocument.Parse(ollama.RequestFor("cuéntame algo")))
            {
                var messages = request.RootElement.GetProperty("messages").EnumerateArray().ToList();
                Assert.Contains("You are Zorg, a grumpy little robot.", messages[0].GetProperty("content").GetString(), StringComparison.Ordinal);
                Assert.EndsWith("(Answer in English.)", messages[^1].GetProperty("content").GetString(), StringComparison.Ordinal);
            }

            // Nothing of its own: the general again, without loading it again.
            Assert.True(server.Send(new ConfigMessage("personal", new JsonObject())));
            await Task.Delay(300);
            Assert.True(server.Send(new HeardMessage("cuéntame otra cosa", "panel", "dot")));
            await Until(() => Answers(received).Count >= 3);
            using (var request = JsonDocument.Parse(ollama.RequestFor("cuéntame otra cosa")))
            {
                var messages = request.RootElement.GetProperty("messages").EnumerateArray().ToList();
                Assert.DoesNotContain("Zorg", messages[0].GetProperty("content").GetString(), StringComparison.Ordinal);
                Assert.EndsWith("(Contesta en español.)", messages[^1].GetProperty("content").GetString(), StringComparison.Ordinal);
            }

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }
        var log = await File.ReadAllTextAsync(Path.Combine(data, "Logs", "assistant.log"));
        Assert.Contains("lo propio de «dot»: idioma en", log);
    }

    private static List<ChatLineMessage> Answers(ConcurrentQueue<PipeMessage> received) =>
        [.. received.OfType<ChatLineMessage>().Where(c => c is { Role: "assistant", Final: true })];

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(100);
        }
    }

    /// <summary>Ollama on a free local port: one model with tools; every chat answers <paramref name="answer"/> in two pieces.</summary>
    private sealed class FakeOllama : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _answer;
        private int _chats;

        public FakeOllama(string answer)
        {
            _answer = answer;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Address = $"http://localhost:{port}";
            _listener.Prefixes.Add(Address + "/");
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string Address { get; }

        /// <summary>Chats with messages (loading or unloading the model does not count).</summary>
        public int Chats => Volatile.Read(ref _chats);

        public string LastRequest { get; private set; } = string.Empty;

        private readonly ConcurrentQueue<string> _requests = new();

        /// <summary>The chat of the order <paramref name="said"/> (not the learning that may come after it).</summary>
        public string RequestFor(string said) => _requests.First(r =>
        {
            using var json = JsonDocument.Parse(r);
            return json.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!.StartsWith(said + "\n", StringComparison.Ordinal);
        });

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                _ = Task.Run(() => Answer(context));
            }
        }

        private async Task Answer(HttpListenerContext context)
        {
            using var response = context.Response;
            var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            string text;
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/api/tags":
                    text = """{"models":[{"name":"fake:4b","size":1000000000,"details":{"parameter_size":"4.0B"},"capabilities":["completion","tools"]}]}""";
                    break;
                case "/api/ps":
                    text = """{"models":[]}""";
                    break;
                case "/api/chat":
                    using (var json = JsonDocument.Parse(body))
                        if (json.RootElement.GetProperty("messages").GetArrayLength() == 0)
                        {
                            text = """{"done":true}""";
                            break;
                        }
                    Interlocked.Increment(ref _chats);
                    LastRequest = body;
                    _requests.Enqueue(body);
                    var half = _answer.Length / 2;
                    text = Line(_answer[..half]) + "\n" + Line(_answer[half..]) + "\n" + """{"message":{"role":"assistant","content":""},"done":true}""" + "\n";
                    break;
                default:
                    response.StatusCode = 404;
                    return;
            }
            var bytes = Encoding.UTF8.GetBytes(text);
            response.ContentType = "application/json";
            await response.OutputStream.WriteAsync(bytes);
        }

        private static string Line(string content) =>
            JsonSerializer.Serialize(new { message = new { role = "assistant", content }, done = false });

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}
