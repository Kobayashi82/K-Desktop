using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The showcase (stage 13.9, SDK 1.18): what each assistant can show — the host's entrances, exits, states, reactions and
/// gestures, and its own animations — and «do this one now», with the real KDesktop.exe in assistant mode and Pip (an
/// assistant without code, whose own ones are the animations of its character.json). Pip shows above the taskbar for a
/// few seconds.
/// </summary>
[Collection(OnTheDesktop.Name)]
public class ShowcaseTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(ShowcaseTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string PipFolder => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package"));

    [Fact]
    public void EveryItemOfTheHostHasANameAndAGroup()
    {
        Assert.Equal(ModuleShowcase.Standard.Count, ModuleShowcase.Standard.Select(s => s.Id).Distinct().Count());
        foreach (var (id, group) in ModuleShowcase.Standard)
        {
            Assert.NotEqual("showcase.item." + id, Loc.T("showcase.item." + id));
            Assert.NotEqual("showcase.group." + group, Loc.T("showcase.group." + group));
        }
        foreach (var group in Enum.GetValues<ShowcaseGroup>())
            Assert.NotEqual("showcase.group." + ModuleShowcase.GroupOf(group), Loc.T("showcase.group." + ModuleShowcase.GroupOf(group)));
        // The animations of the assistants without code, which bring no names.
        foreach (var animation in new[] { "walk", "sleep", "surprised", "click", "catch" })
            Assert.NotEqual("showcase.own." + animation, Loc.T("showcase.own." + animation));
    }

    [Fact]
    public async Task ItListsAndPlaysWhatItCanShow()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "showcase-" + Guid.NewGuid().ToString("N")[..8]);
        var pipData = Path.Combine(data, "Assistants", "Data", "pip");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new ConcurrentQueue<LoadedMessage>();
        var lists = new ConcurrentQueue<ShowcaseListMessage>();
        var done = new ConcurrentQueue<ShowcaseDoneMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            switch (m)
            {
                case LoadedMessage l: loaded.Enqueue(l); break;
                case ShowcaseListMessage list: lists.Enqueue(list); break;
                case ShowcaseDoneMessage d: done.Enqueue(d); break;
            }
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("pip", "1.0.0", PipFolder, pipData, LoadMessage.Startup)));
            await Until(() => !loaded.IsEmpty);

            // The host's ones first, then the animations its character.json has (without names: K-Desktop names them).
            Assert.True(server.Send(new ShowcaseMessage("pip", ShowcaseMessage.List)));
            await Until(() => !lists.IsEmpty);
            lists.TryDequeue(out var list);
            Assert.Equal(ModuleShowcase.Standard.Select(s => s.Id), list!.Items.Where(i => !i.Own).Select(i => i.Id));
            Assert.Equal(["walk", "sleep", "surprised", "click", "catch"], list.Items.Where(i => i.Own).Select(i => i.Id));
            Assert.Equal("moving", list.Items.Single(i => i.Own && i.Id == "walk").Group);
            Assert.All(list.Items.Where(i => i.Own), i => Assert.Null(i.Names));

            async Task<string> Play(string item, bool own = false)
            {
                var before = done.Count;
                Assert.True(server.Send(new ShowcaseMessage("pip", ShowcaseMessage.Play, item, own)));
                await Until(() => done.Skip(before).Any(d => d.Item == item));
                return done.Skip(before).First(d => d.Item == item).Result;
            }

            Assert.Equal(ShowcaseDoneMessage.Done, await Play("react.success"));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("surprised", own: true));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("gesture.announce"));
            // What it does not know how to do (Pip has no way of pointing), or does not have.
            Assert.Equal(ShowcaseDoneMessage.NotPerformed, await Play("gesture.pointAt"));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("click", own: true));
            Assert.Equal(ShowcaseDoneMessage.Unknown, await Play("dance", own: true));
            Assert.Equal(ShowcaseDoneMessage.Unknown, await Play("state.nothing"));

            // Another one stops the one before.
            var count = done.Count;
            Assert.True(server.Send(new ShowcaseMessage("pip", ShowcaseMessage.Play, "state.thinking")));
            await Task.Delay(300);
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("react.failure"));
            Assert.Equal(ShowcaseDoneMessage.Stopped, done.Skip(count).First(d => d.Item == "state.thinking").Result);

            // An exit: it leaves as dismissed and comes back quietly; it is still there for K-Desktop.
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("exit.dismissed"));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("react.celebration"));
            Assert.Single(loaded);

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
        Assert.Contains("«pip» descargado (Dismissed)", log);
        Assert.Contains("«pip» cargado (1 en el escritorio, Reloaded)", log);
        Assert.Contains("escaparate de «pip»: suyo surprised → done", log);
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
