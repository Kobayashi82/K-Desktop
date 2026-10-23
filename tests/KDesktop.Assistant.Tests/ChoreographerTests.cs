using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 6.4, the general choreographer, with the real KDesktop.exe in assistant mode and Dot pretending to know every
/// gesture (DOT_GESTURES): one that commits at the instant of its gesture, one that never answers, one that takes too
/// long, one that fails and one that says it did it (a little off). Whichever: every step gets exactly one answer before
/// K-Desktop would stop waiting, and the order is answered at the end. Nothing is left half done.
/// </summary>
[Collection(OnTheDesktop.Name)]
public class ChoreographerTests
{
    private const int StepTimeout = 2500;

    private static string Exe { get; } = Path.GetFullPath(typeof(ChoreographerTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "tests", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila tests/DotAssistant primero");

    [Theory]
    [InlineData("commit", "go")]
    [InlineData("silent", "go")]
    [InlineData("slow", "go")]
    [InlineData("fail", "go")]
    [InlineData("baddone", "done")]
    public async Task NoAssistantLeavesAnOrderHalfDone(string behaviour, string answer)
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "gestures-" + Guid.NewGuid().ToString("N")[..8], "data");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
        var config = new JsonObject { ["brain"] = new JsonObject { ["model"] = "none" } };
        Process? process = null;
        var received = new ConcurrentQueue<(PipeMessage Message, long At)>();
        var clock = Stopwatch.StartNew();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false, config));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m => received.Enqueue((m, clock.ElapsedMilliseconds));
        server.Received += m =>
        {
            if (m is MatchMessage match)
                server.Send(new MatchedMessage(match.Id, 0.95, [], [new MatchedPart(match.Text, true, [new MatchedCandidate("w", "Ventanas", 0.95)])]));
        };
        server.Start();

        var start = new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false };
        start.Environment["DOT_GESTURES"] = behaviour;
        process = Process.Start(start)!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", DotPackage, Path.Combine(data, "Assistants", "Data", "dot"), "startup")));
            await Until(() => received.Any(r => r.Message is LoadedMessage));

            Assert.True(server.Send(new HeardMessage("ordena las ventanas", "panel", "dot")));
            await Until(() => received.Any(r => r.Message is RunMessage));
            var run = received.Select(r => r.Message).OfType<RunMessage>().Single();
            Assert.True(run.Choreography);

            // A window that does not exist: the gestures only need where it is said to be.
            var target = new StepTarget(0x7A11, "notepad", "Sin título", "normal", false, new StepRect(200, 200, 800, 600), new StepRect(0, 0, 960, 1040));
            StepMessage[] steps =
            [
                new(run.Run, 1, 2, 0, "Window", "Minimize", "Minimizar", StepTimeout, target),
                new(run.Run, 2, 2, 0, "Window", "Snap", "Colocar a la izquierda", StepTimeout, target),
            ];
            foreach (var step in steps)
            {
                var sent = clock.ElapsedMilliseconds;
                server.Send(step);
                await Until(() => Answers(received, run.Run, step.Index).Count > 0);
                var (got, at) = Answers(received, run.Run, step.Index).First();
                Assert.True(at - sent < StepTimeout, $"{behaviour}: el paso {step.Index} se contestó a los {at - sent} ms");
                // done only from the one that says it did it itself.
                Assert.Equal(answer, got.Type);
                server.Send(new StepResultMessage(run.Run, step.Index, "ok"));
            }
            server.Send(new EndMessage(run.Run, "ok", []));
            await Until(() => received.Any(r => r.Message is ChatLineMessage { Role: "assistant", Final: true }));
            await Task.Delay(300);
            foreach (var step in steps) Assert.Single(Answers(received, run.Run, step.Index));

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }
    }

    private static List<(PipeMessage Message, long At)> Answers(ConcurrentQueue<(PipeMessage Message, long At)> received, string run, int index) =>
        [.. received.Where(r => r.Message is GoMessage g && g.Run == run && g.Index == index || r.Message is DoneMessage d && d.Run == run && d.Index == index)];

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(50);
        }
    }
}
