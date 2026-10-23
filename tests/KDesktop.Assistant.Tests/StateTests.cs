using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 13.4: the new states and what the assistant is busy with.</summary>
[Collection(OnTheDesktop.Name)]
public class StateTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(StateTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string PipFolder => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package"));

    [Fact]
    public void AnAssistantBuiltBeforeSeesOnlyTheStatesItKnows()
    {
        IAssistant old = new OldAssistant();
        old.SetState(AssistantState.ThinkingHard, StateDetail.None);
        old.SetState(AssistantState.LoadingBrain, new StateDetail(AssistantActivity.None));
        old.SetState(AssistantState.Busy, new StateDetail(AssistantActivity.SearchingWeb, "look_up_web", "Buscando en internet"));
        old.SetState(AssistantState.NoBrain, StateDetail.None);
        Assert.Equal([AssistantState.Thinking, AssistantState.Thinking, AssistantState.Busy, AssistantState.Thinking], ((OldAssistant)old).Seen);
    }

    [Fact]
    public void WhatWinsIsTheQuestionThenSpeakingThenATool()
    {
        var of = new ModuleStates.Of { Talk = "thinking" };
        Assert.Equal((AssistantState.Thinking, AssistantActivity.None, (string?)null), ModuleStates.Compose(of, brainLoading: false));
        // The model loads while it thinks.
        Assert.Equal(AssistantState.LoadingBrain, ModuleStates.Compose(of, brainLoading: true).Item1);
        // A tool runs: busy with it.
        of.Tools.Add(new ToolUse("pip", "look_up_web", AssistantActivity.SearchingWeb, ToolPhase.Running));
        Assert.Equal((AssistantState.Busy, AssistantActivity.SearchingWeb, "look_up_web"), ModuleStates.Compose(of, brainLoading: true));
        // Another one asks for permission meanwhile: that is what it shows.
        of.Tools.Add(new ToolUse("pip", "run_command", AssistantActivity.Command, ToolPhase.Asking));
        Assert.Equal((AssistantState.AwaitingConfirmation, AssistantActivity.Command, "run_command"), ModuleStates.Compose(of, false));
        of.Tools.RemoveAt(1);
        // Speaking goes over a tool still running (an answer that said ahead).
        of.Talk = "speaking";
        Assert.Equal((AssistantState.Speaking, AssistantActivity.None, (string?)null), ModuleStates.Compose(of, false));
        of.Tools.Clear();
        of.Talk = "thinkingHard";
        Assert.Equal(AssistantState.ThinkingHard, ModuleStates.Compose(of, false).Item1);
        Assert.Equal(AssistantState.LoadingBrain, ModuleStates.Compose(of, true).Item1);
        of.Talk = "noBrain";
        Assert.Equal(AssistantState.NoBrain, ModuleStates.Compose(of, true).Item1);
        // A dictation is listening, with that activity; at rest the brain loading for nobody shows nothing.
        of.Talk = "idle";
        Assert.Equal(AssistantState.Idle, ModuleStates.Compose(of, true).Item1);
        of.Dictating = true;
        Assert.Equal((AssistantState.Listening, AssistantActivity.Dictation, (string?)null), ModuleStates.Compose(of, false));
        Assert.Equal("awaitingConfirmation", ModuleStates.NameOf(AssistantState.AwaitingConfirmation));
        Assert.Equal("loadingBrain", ModuleStates.NameOf(AssistantState.LoadingBrain));
    }

    [Fact]
    public void EveryActivityHasItsWordsAndItsName()
    {
        foreach (var activity in Enum.GetValues<AssistantActivity>())
        {
            var id = ToolActivities.Id(activity);
            Assert.Equal(activity, ToolActivities.Parse(id));
            if (activity == AssistantActivity.None) Assert.Null(ToolActivities.Text(activity));
            else Assert.False(ToolActivities.Text(activity)!.StartsWith("assistant.", StringComparison.Ordinal), id);
        }
        Assert.Equal("searchingWeb", ToolActivities.Id(AssistantActivity.SearchingWeb));
        Assert.Equal(AssistantActivity.None, ToolActivities.Parse("flying"));
        Assert.Equal(AssistantActivity.SearchingWeb, ToolActivities.Of("look_up_web"));
        Assert.Equal(AssistantActivity.Other, ToolActivities.Of("mcp_calendar_list_events"));
        // A tool with no title of its own is named as it is.
        Assert.Contains("mcp_calendar_list_events", ToolActivities.Text(AssistantActivity.Other, "mcp_calendar_list_events"), StringComparison.Ordinal);
        Assert.Contains(Loc.T("tool.weather.title"), ToolActivities.Text(AssistantActivity.Other, "weather"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunnerSaysWhenAToolAsksRunsAndIsOver()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "states-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var registry = new ToolRegistry();
        registry.Add(new Named("weather", ToolRisk.Safe));
        registry.Add(new Named("risky", ToolRisk.AskAlways));
        var answer = "yes";
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult(answer), _ => { }, Path.Combine(folder, "undo.json"));
        var uses = new ConcurrentQueue<ToolUse>();
        runner.Using += uses.Enqueue;
        var args = JsonDocument.Parse("{}").RootElement;

        await runner.RunAsync("weather", args, new ToolContext("pip"), "¿qué tiempo hace?", "ai", default);
        Assert.Equal([ToolPhase.Running, ToolPhase.Over], uses.Select(u => u.Phase));
        Assert.All(uses, u => Assert.Equal(("pip", AssistantActivity.Weather), (u.Assistant!, u.Activity)));

        uses.Clear();
        answer = "no";
        await runner.RunAsync("risky", args, new ToolContext("pip"), "haz algo", "ai", default);
        Assert.Equal([ToolPhase.Asking, ToolPhase.Over], uses.Select(u => u.Phase));
        Assert.Equal(AssistantActivity.Other, uses.First().Activity);

        // One that is not there says nothing.
        uses.Clear();
        await runner.RunAsync("nothing", args, new ToolContext("pip"), "x", "ai", default);
        Assert.Empty(uses);
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task PipShowsItsTimerAndThatItHasNoBrain()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "states-" + Guid.NewGuid().ToString("N")[..8]);
        var pipData = Path.Combine(data, "Assistants", "Data", "pip");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new TaskCompletionSource();
        var states = new ConcurrentQueue<StatusMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage) loaded.TrySetResult();
            if (m is StatusMessage { Assistant: "pip" } status) states.Enqueue(status);
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("pip", "1.0.0", PipFolder, pipData, LoadMessage.Startup)));
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));

            // A rule with a tool: busy with the timer, then it says so, then at rest.
            Assert.True(server.Send(new HeardMessage("pon un temporizador de 10 minutos", "panel", "pip")));
            await Until(() => states.Any(s => s.State == "idle" && states.Any(b => b.State == "busy")));
            Assert.Contains(states, s => s is { State: "busy", Activity: "timer", Tool: "set_timer" });
            var order = states.Select(s => s.State).ToList();
            Assert.True(order.IndexOf("busy") < order.IndexOf("speaking"), string.Join(", ", order));

            // An order for the AI with no brain: it shows it right before saying so.
            states.Clear();
            Assert.True(server.Send(new HeardMessage("cuéntame un chiste de piratas", "panel", "pip")));
            await Until(() => states.Any(s => s.State == "idle"));
            order = [.. states.Select(s => s.State)];
            Assert.Contains("noBrain", order);
            Assert.True(order.IndexOf("noBrain") < order.IndexOf("speaking"), string.Join(", ", order));

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
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(100);
        }
    }

    /// <summary>An assistant of SDK 1.12: only the state without its detail.</summary>
    private sealed class OldAssistant : IAssistant
    {
        public List<AssistantState> Seen { get; } = [];
        public void SetState(AssistantState state) => Seen.Add(state);
        public System.Windows.Point? BubbleAnchor => null;
        public void Attach(IAssistantHost host) { }
        public void Enter(EntranceKind how) { }
        public Task ExitAsync(ExitKind why, CancellationToken ct) => Task.CompletedTask;
        public double Tick(TickInfo tick) => 1;
        public bool CanPerform(Gesture gesture) => false;
        public Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct) => Task.FromResult(GestureOutcome.NotPerformed);
        public void OnVoiceLevel(double level) { }
        public void OnPointer(PointerEvent e) { }
        public void OnSystemEvent(SystemEvent e) { }
        public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;
        public void OnSettingsChanged(JsonElement settings) { }
        public void Dispose() { }
    }

    private sealed class Named(string name, ToolRisk risk) : ITool
    {
        public string Name => name;
        public string Description => "A tool.";
        public string ParametersSchemaJson => """{"type":"object","properties":{}}""";
        public IReadOnlyList<string> Examples => [];
        public ToolRisk Risk => risk;
        public string Intent(JsonElement arguments) => "hacer algo";
        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) => Task.FromResult<ToolResult>("done");
    }
}
