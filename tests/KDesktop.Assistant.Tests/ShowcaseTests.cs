using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The showcase (stages 13.9 and 16.1, SDK 1.18): what each assistant can show — the host's entrances, exits, states,
/// reactions, gestures and what it does to a window, and its own animations — and «do this one now», on its stage: the real
/// KDesktop.exe with <c>--assistant-showcase</c> and Tic (an assistant without code, whose own ones are the animations of its
/// character.json). Nothing shows on the screen: the stage is drawn off it and its pictures come through shared memory.
/// </summary>
public class ShowcaseTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(ShowcaseTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string TicFolder => TestCharacter.Folder;

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
        Process? process = null;
        var loaded = new ConcurrentQueue<LoadedMessage>();
        var lists = new ConcurrentQueue<ShowcaseListMessage>();
        var done = new ConcurrentQueue<ShowcaseDoneMessage>();
        StageFrameMessage? frame = null;
        await using var stage = new AssistantStage(TicFolder, () => new WelcomeMessage("es", "dark", false),
            (exe, arguments) => process = Process.Start(new ProcessStartInfo(exe, arguments + " --allow-elevated") { UseShellExecute = false }),
            Exe);
        var ended = new TaskCompletionSource<string>();
        stage.Ended += reason => ended.TrySetResult(reason);
        stage.Received += m =>
        {
            switch (m)
            {
                case LoadedMessage l: loaded.Enqueue(l); break;
                case ShowcaseListMessage list: lists.Enqueue(list); break;
                case ShowcaseDoneMessage d: done.Enqueue(d); break;
                case StageFrameMessage f: frame = f; break;
            }
        };
        stage.Start();
        var server = stage;
        try
        {
            await Until(() => !loaded.IsEmpty);
            Assert.True(loaded.Single().Ok);

            // Its pictures: a desktop with Tic on it, in the shared memory the stage made.
            await Until(() => frame is not null);
            Assert.Equal((720, 405), (frame!.Width, frame.Height));
            using (var map = MemoryMappedFile.OpenExisting(@"Local\" + frame.Map, MemoryMappedFileRights.Read))
            using (var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
            {
                var pixels = new byte[720 * 405 * 4];
                view.ReadArray((long)frame.Slot * pixels.Length, pixels, 0, pixels.Length);
                Assert.True(pixels.Chunk(4).Select(p => (p[0], p[1], p[2])).Distinct().Count() > 50, "el escenario está vacío");
            }

            // The host's ones first, then the animations its character.json has (without names: K-Desktop names them).
            Assert.True(server.Send(new ShowcaseMessage("tic", ShowcaseMessage.List)));
            await Until(() => !lists.IsEmpty);
            lists.TryDequeue(out var list);
            Assert.Equal(ModuleShowcase.Standard.Select(s => s.Id), list!.Items.Where(i => !i.Own).Select(i => i.Id));
            Assert.Equal(["walk", "sleep", "surprised", "click", "catch"], list.Items.Where(i => i.Own).Select(i => i.Id));
            Assert.Equal("moving", list.Items.Single(i => i.Own && i.Id == "walk").Group);
            Assert.All(list.Items.Where(i => i.Own), i => Assert.Null(i.Names));

            async Task<string> Play(string item, bool own = false)
            {
                var before = done.Count;
                Assert.True(server.Send(new ShowcaseMessage("tic", ShowcaseMessage.Play, item, own)));
                await Until(() => done.Skip(before).Any(d => d.Item == item));
                return done.Skip(before).First(d => d.Item == item).Result;
            }

            // On the stage's ground from the start (loaded with an empty world it fell off the stage and could not walk).
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("walk", own: true));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("react.success"));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("surprised", own: true));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("gesture.announce"));
            // What it does not know how to do (Tic has no way of pointing), or does not have.
            Assert.Equal(ShowcaseDoneMessage.NotPerformed, await Play("gesture.pointAt"));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("click", own: true));
            Assert.Equal(ShowcaseDoneMessage.Unknown, await Play("dance", own: true));
            Assert.Equal(ShowcaseDoneMessage.Unknown, await Play("state.nothing"));
            // On the stage's windows: Tic cannot press a button, so the stage does it while the bubble says it (as in use).
            Assert.Equal(ShowcaseDoneMessage.NotPerformed, await Play("window.minimize"));
            Assert.Equal(ShowcaseDoneMessage.NotPerformed, await Play("gesture.typing"));

            // Another one stops the one before.
            var count = done.Count;
            Assert.True(server.Send(new ShowcaseMessage("tic", ShowcaseMessage.Play, "state.thinking")));
            await Task.Delay(300);
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("react.failure"));
            Assert.Equal(ShowcaseDoneMessage.Stopped, done.Skip(count).First(d => d.Item == "state.thinking").Result);

            // An exit: it leaves as dismissed and comes back quietly; it is still there for K-Desktop.
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("exit.dismissed"));
            Assert.Equal(ShowcaseDoneMessage.Done, await Play("react.celebration"));
            Assert.Single(loaded);

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process!.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (process is { HasExited: false }) process.Kill();
            process?.Dispose();
        }

        var log = await File.ReadAllTextAsync(Path.Combine(KDesktop.Core.AppPaths.LogDir, "stage.log"));
        Assert.Contains("dibujo fuera de la pantalla", log);
        Assert.Contains("«tic» descargado (Dismissed)", log);
        Assert.Contains("«tic» cargado (1 en el escritorio, Reloaded)", log);
        Assert.Contains("escaparate de «tic»: suyo surprised → done", log);
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
