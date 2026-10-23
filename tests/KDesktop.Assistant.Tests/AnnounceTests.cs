using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 13.6: before saying something on its own, the assistant catches the user's eye (<see cref="Gesture.Announce"/>).</summary>
[Collection(OnTheDesktop.Name)]
public class AnnounceTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(AnnounceTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string PipFolder => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package"));

    [Fact]
    public void ANewKindAtTheEndKeepsTheNumbersOfTheOthers()
    {
        // Packages built before SDK 1.14 compare the old kinds by number.
        Assert.Equal(10, (int)GestureKind.React);
        Assert.Equal(11, (int)GestureKind.Announce);
        Assert.Equal(GestureKind.Announce, new Gesture.Announce(AnnounceReason.Timer, null, 0).Kind);
    }

    [Fact]
    public void ACharacterWithoutItsOwnAnnounceIsHappy()
    {
        Assert.Contains("announce", CharacterFile.Names);
        var character = CharacterFile.Parse("""{ "animations": { "idle": { "frames": ["a.png"] }, "happy": { "frames": ["b.png"] } } }""");
        Assert.Same(character.Animations["happy"], character.For("announce"));
    }

    [Fact]
    public async Task PipCatchesTheEyeBeforeItsTimerAndWhatItIsToldToSay()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "announce-" + Guid.NewGuid().ToString("N")[..8]);
        var pipData = Path.Combine(data, "Assistants", "Data", "pip");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new TaskCompletionSource();
        var config = new JsonObject { ["general"] = new JsonObject { ["sound"] = false, ["manoeuvres"] = "fast" } };
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false, config));
        var hello = new TaskCompletionSource();
        var timerSaid = new TaskCompletionSource<string>();
        var messageSaid = new TaskCompletionSource<string>();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage) loaded.TrySetResult();
            if (m is ChatLineMessage { Role: "assistant", Final: true } line)
            {
                if (line.Text.Contains("temporizador", StringComparison.OrdinalIgnoreCase)) timerSaid.TrySetResult(line.Text);
                if (line.Text.Contains("Hora de estirarse", StringComparison.Ordinal)) messageSaid.TrySetResult(line.Text);
            }
        };
        server.Start();

        var logFile = Path.Combine(data, "Logs", "assistant.log");
        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("pip", "1.0.0", PipFolder, pipData, LoadMessage.Startup)));
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));

            // «Asistente: decir»; in «Fast» it takes half a second at most before it speaks.
            var asked = Stopwatch.StartNew();
            Assert.True(server.Send(new SayMessage("Hora de estirarse.", "pip")));
            await messageSaid.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(asked.Elapsed < TimeSpan.FromSeconds(2), $"Ha tardado {asked.Elapsed.TotalSeconds:0.0} s en decirlo");
            await Until(() => LogHas(logFile, "«pip» llama la atención (Message) antes de hablar"));

            // A timer: when it rings (once the answer before is over), first it catches the eye, then it reacts and says it.
            Assert.True(server.Send(new HeardMessage("pon un temporizador de 2 segundos", "panel", "pip")));
            await Until(() => LogHas(logFile, "«pip» reacciona: Success (Tool set_timer"));
            await Until(() => LogHas(logFile, "«pip» reacciona: Success (Notice timer"));
            var log = Read(logFile);
            var eye = log.IndexOf("«pip» llama la atención (Timer) antes de hablar", StringComparison.Ordinal);
            Assert.True(eye >= 0, "No ha llamado la atención antes del temporizador");
            Assert.True(eye < log.IndexOf("«pip» reacciona: Success (Notice timer", StringComparison.Ordinal));
            await timerSaid.Task.WaitAsync(TimeSpan.FromSeconds(10));

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

    private static string Read(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static bool LogHas(string file, string text) => Read(file).Contains(text, StringComparison.Ordinal);

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(100);
        }
    }
}
