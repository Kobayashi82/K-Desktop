using System.Diagnostics;
using System.Reflection;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 13.3: the context of a reaction (where it comes from, the streak, the first of the day) and when the host reacts.</summary>
[Collection(OnTheDesktop.Name)]
public class ReactionTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(ReactionTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string PipFolder => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package"));

    [Fact]
    public void TheStreakCountsTheSameKindInARow()
    {
        var now = new DateTimeOffset(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
        var log = new ReactionLog(null, () => now);
        var first = log.Next("pip", Reaction.Failure, ReactionSource.Tool, "weather");
        Assert.Equal(new ReactionContext(ReactionSource.Tool, 1, true, "weather"), first);
        now += TimeSpan.FromMinutes(1);
        Assert.Equal(2, log.Next("pip", Reaction.Failure, ReactionSource.Order, null).Streak);
        Assert.Equal(3, log.Next("pip", Reaction.Failure, ReactionSource.Tool, null).Streak);
        // Another kind starts again; so does half an hour without reactions.
        Assert.Equal(1, log.Next("pip", Reaction.Success, ReactionSource.Tool, null).Streak);
        now += ReactionLog.StreakGap + TimeSpan.FromSeconds(1);
        var later = log.Next("pip", Reaction.Success, ReactionSource.Tool, null);
        Assert.Equal(1, later.Streak);
        Assert.False(later.FirstToday);
        // Each assistant has its own.
        Assert.True(log.Next("lander", Reaction.Success, ReactionSource.Tool, null).FirstToday);
    }

    [Fact]
    public void TheFirstOfTheDaySurvivesARestart()
    {
        var file = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "reactions-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var now = DateTimeOffset.Now.Date.AddHours(10);
        Assert.True(new ReactionLog(file, () => now).Next("pip", Reaction.Success, ReactionSource.Order, null).FirstToday);
        // K-Desktop restarted in the afternoon: not the first one any more, and the streak goes on.
        now = now.AddMinutes(10);
        var again = new ReactionLog(file, () => now).Next("pip", Reaction.Success, ReactionSource.Order, null);
        Assert.False(again.FirstToday);
        Assert.Equal(2, again.Streak);
        // The next day it is.
        now = now.AddDays(1);
        Assert.True(new ReactionLog(file, () => now).Next("pip", Reaction.Success, ReactionSource.Order, null).FirstToday);
        File.Delete(file);
    }

    private static ToolRan Ran(string tool, bool failed = false, bool reads = false) => new("pip", tool, reads, "ai", failed);

    [Fact]
    public void AnOrderIsJudgedByWhatItsToolsDid()
    {
        Assert.Equal((Reaction.Success, ReactionSource.Order, "run_command"), ModuleReactions.Decide("abre steam", [Ran("find_command", reads: true), Ran("run_command")]));
        Assert.Equal((Reaction.Success, ReactionSource.Tool, "set_timer"), ModuleReactions.Decide("pon un temporizador", [Ran("set_timer")]));
        // The AI got it wrong once and then right: forgiven.
        Assert.Equal(Reaction.Success, ModuleReactions.Decide("x", [Ran("run_command", failed: true), Ran("run_command")])!.Value.Reaction);
        // Something done and something else failed.
        Assert.Equal(Reaction.Warning, ModuleReactions.Decide("x", [Ran("set_timer"), Ran("send_telegram", failed: true)])!.Value.Reaction);
        // Nothing done: what failed, reading tools included.
        Assert.Equal((Reaction.Failure, ReactionSource.Tool, "weather"), ModuleReactions.Decide("¿qué tiempo hace?", [Ran("weather", failed: true, reads: true)]));
        Assert.Equal((Reaction.Failure, ReactionSource.Order, "control_app"), ModuleReactions.Decide("cierra steam", [Ran("control_app", failed: true)]));
        // Only reading and nothing failed: nothing to react to.
        Assert.Null(ModuleReactions.Decide("¿qué hora es?", [Ran("get_time", reads: true)]));
        // With no tool, what the user said about it.
        Assert.Equal((Reaction.Celebration, ReactionSource.User, "praise"), ModuleReactions.Decide("¡Gracias, Pip!", []));
        Assert.Equal((Reaction.Failure, ReactionSource.User, "wrong"), ModuleReactions.Decide("eso está mal", []));
        Assert.Null(ModuleReactions.Decide("cuéntame un chiste", []));
    }

    [Theory]
    [InlineData("gracias", true)]
    [InlineData("¡Muy bien, Lander!", true)]
    [InlineData("thank you so much", true)]
    [InlineData("gut gemacht", true)]
    [InlineData("obrigado", true)]
    [InlineData("no, gracias", false)]
    [InlineData("gracias pero eso está mal", false)]
    [InlineData("gracias, y ahora ábreme el correo y ponme una alarma para mañana a las ocho", false)]
    [InlineData("abre el bloc de notas", false)]
    public void PraiseIsShortAndInAnyLanguage(string said, bool praise) =>
        Assert.Equal(praise, UserSignals.IsPraise(said));

    [Fact]
    public async Task PipReactsToThanksAndToItsTimer()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "react-" + Guid.NewGuid().ToString("N")[..8]);
        var pipData = Path.Combine(data, "Assistants", "Data", "pip");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new TaskCompletionSource();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage) loaded.TrySetResult();
        };
        server.Start();

        var logFile = Path.Combine(data, "Logs", "assistant.log");
        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("pip", "1.0.0", PipFolder, pipData, LoadMessage.Startup)));
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.True(server.Send(new HeardMessage("¡Gracias!", "panel", "pip")));
            await Until(() => LogHas(logFile, "«pip» reacciona: Celebration (User praise, racha 1, primera del día)"));
            Assert.True(server.Send(new HeardMessage("pon un temporizador de 2 segundos", "panel", "pip")));
            await Until(() => LogHas(logFile, "«pip» reacciona: Success (Tool set_timer, racha 1)"));
            // It rings: the notice, the same kind in a row.
            await Until(() => LogHas(logFile, "«pip» reacciona: Success (Notice timer, racha 2)"));

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }
        Assert.True(File.Exists(Path.Combine(data, "Assistant", "reactions.json")));
    }

    private static bool LogHas(string file, string text)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd().Contains(text, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

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
