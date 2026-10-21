using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 6.3, the assistant's side, with the real KDesktop.exe in assistant mode and Dot on the desktop: a command it runs
/// asks for choreography, every <c>step</c> gets its answer, and an order cancelled while its command runs skips all that is
/// left. This fake K-Desktop answers <c>match</c> and plays the steps.
/// </summary>
public class ChoreographyProcessTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(ChoreographyProcessTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila samples/DotAssistant primero");

    [Fact]
    public async Task ACommandOfOneOnTheDesktopAnswersItsStepsAndCanBeCancelled()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "choreo-" + Guid.NewGuid().ToString("N")[..8], "data");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
        var config = new JsonObject { ["brain"] = new JsonObject { ["model"] = "none" } };
        Process? process = null;
        var received = new ConcurrentQueue<PipeMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false, config));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += received.Enqueue;
        // K-Desktop's commands: «papelera» names one, clearly.
        server.Received += m =>
        {
            if (m is MatchMessage match)
                server.Send(new MatchedMessage(match.Id, 0.95, [], [new MatchedPart(match.Text, true, [new MatchedCandidate("bin", "Vaciar papelera", 0.95)])]));
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", DotPackage, Path.Combine(data, "Assistants", "Data", "dot"), "startup")));
            await Until(() => received.OfType<LoadedMessage>().Any());

            // Dot is on the desktop: the command comes with choreography, and each step gets its answer.
            Assert.True(server.Send(new HeardMessage("vacía la papelera", "panel", "dot")));
            await Until(() => received.OfType<RunMessage>().Any());
            var run = received.OfType<RunMessage>().Single();
            Assert.True(run.Choreography);
            Assert.Equal(("dot", "bin"), (run.Assistant, run.Command));
            var target = new StepTarget(0, "", "", "normal", false);
            server.Send(new StepMessage(run.Run, 1, 2, 0, "ShowMessage", "", "Mostrar mensaje", 6000));
            await Until(() => received.OfType<GoMessage>().Any(g => g.Run == run.Run && g.Index == 1));
            server.Send(new StepResultMessage(run.Run, 1, "ok"));
            server.Send(new StepMessage(run.Run, 2, 2, 0, "Window", "Snap", "Colocar a la izquierda", 6000, target));
            await Until(() => received.OfType<GoMessage>().Any(g => g.Run == run.Run && g.Index == 2));
            server.Send(new StepResultMessage(run.Run, 2, "failed", "No hay ventana"));
            server.Send(new EndMessage(run.Run, "ok", []));
            await Until(() => received.OfType<ChatLineMessage>().Any(c => c is { Role: "assistant", Final: true }));
            Assert.DoesNotContain(received, m => m is SkipMessage);

            // Cancelled while its command runs: everything left is skipped.
            Assert.True(server.Send(new HeardMessage("vacía la papelera", "panel", "dot")));
            await Until(() => received.OfType<RunMessage>().Count() == 2);
            var second = received.OfType<RunMessage>().Last();
            server.Send(new StepMessage(second.Run, 1, 1, 0, "ShowMessage", "", "Mostrar mensaje", 6000));
            await Until(() => received.OfType<GoMessage>().Any(g => g.Run == second.Run));
            Assert.True(server.Send(new CancelMessage("panel", "dot")));
            await Until(() => received.OfType<SkipMessage>().Any(s => s is { All: true } && s.Run == second.Run));

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

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(100);
        }
    }
}
