using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 13.7 with the real KDesktop.exe in assistant mode and a copy of Dot that says <c>"speech": "own"</c> (DOT_SPEECH
/// makes it write what it «shows» to the log): it gets the answer while it grows and whole, and hides it when it has been
/// said; with K-Desktop's bubble chosen for it, nothing reaches it; one that cannot show the whole answer leaves it to the
/// host's bubble.
/// </summary>
[Collection(OnTheDesktop.Name)]
public class OwnSpeechTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(OwnSpeechTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private const string Answer = "Hola, soy Dot y te escucho.";

    [Fact]
    public async Task ItShowsTheAnswerAsItGrowsUntilTheUserPrefersTheBubble()
    {
        await using var run = await Run.StartAsync("own");

        Assert.True(run.Server.Send(new HeardMessage("cuéntame algo", "panel", "dot")));
        await Run.UntilAsync(() => run.LogHas("Dot esconde lo que dice"));
        var log = run.Log();
        Assert.Contains("Dot muestra (escribiendo): «Hola, soy", log, StringComparison.Ordinal);
        Assert.Contains($"Dot muestra (terminado): «{Answer}»", log, StringComparison.Ordinal);
        Assert.Contains("«dot» enseña lo que dice con su propio texto", log, StringComparison.Ordinal);
        Assert.True(log.IndexOf("Dot muestra (escribiendo)", StringComparison.Ordinal) < log.IndexOf("Dot muestra (terminado)", StringComparison.Ordinal));
        await Run.UntilAsync(() => run.Received.OfType<StatusMessage>().LastOrDefault(s => s.Assistant == "dot") is { State: "idle" });

        // «Usar siempre el bocadillo de K-Desktop»: the next answer does not reach it.
        Assert.True(run.Server.Send(new ConfigMessage("common", new JsonObject { ["dot"] = new JsonObject { ["kdesktopBubble"] = true } })));
        await Run.UntilAsync(() => run.LogHas("bocadillo de K-Desktop"));
        var before = run.Log().Length;
        Assert.True(run.Server.Send(new HeardMessage("dime algo", "panel", "dot")));
        await Run.UntilAsync(() => run.Answers().Count >= 2);
        await Run.UntilAsync(() => run.Received.OfType<StatusMessage>().LastOrDefault(s => s.Assistant == "dot") is { State: "idle" });
        Assert.DoesNotContain("Dot muestra", run.Log()[before..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneThatCannotShowTheWholeAnswerLeavesItToTheBubble()
    {
        await using var run = await Run.StartAsync("unfinished");

        Assert.True(run.Server.Send(new HeardMessage("cuéntame algo", "panel", "dot")));
        await Run.UntilAsync(() => run.LogHas("no puede enseñar lo que dice: bocadillo de K-Desktop"));
        var log = run.Log();
        Assert.Contains("Dot muestra (escribiendo)", log, StringComparison.Ordinal);
        Assert.DoesNotContain("Dot muestra (terminado)", log, StringComparison.Ordinal);
        Assert.Contains("Dot esconde lo que dice", log, StringComparison.Ordinal);
        await Run.UntilAsync(() => run.Answers().Count >= 1);
        Assert.Equal(Answer, run.Answers()[0].Text);
    }

    /// <summary>The assistant process with the copy of Dot on the desktop and a fake Ollama that writes its answer slowly.</summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly TalkProcessTests.FakeOllama _ollama;
        private readonly string _log;
        private Process? _process;

        private Run(TalkProcessTests.FakeOllama ollama, AssistantPipeServer server, string log)
        {
            _ollama = ollama;
            Server = server;
            _log = log;
        }

        public AssistantPipeServer Server { get; }

        public ConcurrentQueue<PipeMessage> Received { get; } = new();

        public static async Task<Run> StartAsync(string speech)
        {
            var root = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "speech-" + Guid.NewGuid().ToString("N")[..8]);
            var data = Path.Combine(root, "data");
            var package = CopyDot(Path.Combine(root, "dot"));
            var ollama = new TalkProcessTests.FakeOllama(Answer) { Pause = TimeSpan.FromMilliseconds(700) };
            var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
            var config = new JsonObject
            {
                ["general"] = new JsonObject { ["sound"] = false },
                ["brain"] = new JsonObject { ["model"] = "", ["remotes"] = new JsonArray(new JsonObject { ["id"] = "r1", ["kind"] = "ollama", ["endpoint"] = ollama.Address }) },
            };
            Run? run = null;
            var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == run?._process?.Id), () => new WelcomeMessage("es", "dark", false, config));
            run = new Run(ollama, server, Path.Combine(data, "Logs", "assistant.log"));
            var hello = new TaskCompletionSource();
            server.Connected += _ => hello.TrySetResult();
            server.Received += run.Received.Enqueue;
            server.Received += m =>
            {
                if (m is EmbedMessage embed)
                    server.Send(new EmbeddedMessage(embed.Id, [.. embed.Texts.Select(t => new[] { 1f, t.Length % 7 / 7f })], "fake"));
            };
            server.Start();

            var start = new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false };
            start.Environment["DOT_SPEECH"] = speech;
            run._process = Process.Start(start)!;
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", package, Path.Combine(data, "Assistants", "Data", "dot"), LoadMessage.Startup)));
            await Run.UntilAsync(() => run.Received.OfType<LoadedMessage>().Any() && run.Received.OfType<BrainStateMessage>().Any(b => b.State == "ready"));
            Assert.True(run.Received.OfType<LoadedMessage>().Single().Ok);
            return run;
        }

        /// <summary>Dot's built package, with <c>"speech": "own"</c> in its manifest.</summary>
        private static string CopyDot(string to)
        {
            var from = DevLoader.FindPackage(Path.Combine(Contracts.Root, "tests", "DotAssistant"))
                ?? throw new InvalidOperationException("Compila tests/DotAssistant primero");
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var manifest = Path.Combine(to, "assistant.json");
            var json = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            json["speech"] = "own";
            File.WriteAllText(manifest, json.ToJsonString());
            return to;
        }

        public List<ChatLineMessage> Answers() =>
            [.. Received.OfType<ChatLineMessage>().Where(c => c is { Role: "assistant", Final: true })];

        public string Log()
        {
            try
            {
                using var stream = new FileStream(_log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return new StreamReader(stream).ReadToEnd();
            }
            catch (IOException)
            {
                return string.Empty;
            }
        }

        public bool LogHas(string text) => Log().Contains(text, StringComparison.Ordinal);

        public static async Task UntilAsync(Func<bool> condition)
        {
            var limit = DateTime.UtcNow.AddSeconds(30);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
                await Task.Delay(100);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_process is { HasExited: false } process)
                {
                    Server.Send(new ByeMessage("exit"));
                    using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await process.WaitForExitAsync(exited.Token); }
                    catch (OperationCanceledException) { process.Kill(); }
                }
            }
            finally
            {
                _process?.Dispose();
                await Server.DisposeAsync();
                _ollama.Dispose();
            }
        }
    }
}
