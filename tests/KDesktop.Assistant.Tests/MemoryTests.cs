using System.Runtime.CompilerServices;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 3.7: what the assistants know about the user — it is remembered (said or learned), it comes to mind when it
/// fits and goes into the instructions, and it can be edited, undone and deleted.
/// </summary>
public sealed class MemoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-memory-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private MemoryStore Store() => new(Path.Combine(_folder, "memory.json"));

    [Fact]
    public void ItRemembersUpdatesAndForgets()
    {
        var store = Store();
        var first = store.Remember("Su jefe se llama Luis.", "fact", 0.6, "said")!.Value;
        Assert.True(first.IsNew);
        Assert.Equal("Su jefe se llama Luis", first.Entry.Text);
        // Nearly the same: the new text replaces it.
        var again = store.Remember("Su jefe se llama Luis Pérez", "fact", 0.7, "learned")!.Value;
        Assert.False(again.IsNew);
        Assert.Equal("Su jefe se llama Luis", again.Previous);
        var only = Assert.Single(store.All);
        Assert.Equal("said", only.Source);
        Assert.Equal(0.7, only.Importance);

        Assert.True(store.Edit(only.Id, "Su jefa se llama Luisa"));
        // It is on disk: a new store reads it.
        Assert.Equal("Su jefa se llama Luisa", Assert.Single(Store().All).Text);
        Assert.True(store.Delete(only.Id));
        Assert.Empty(store.All);
        Assert.Null(store.Remember("ok", "fact", 1, "said"));
    }

    [Fact]
    public void WhatAFullerOneSaysIsKnown()
    {
        var store = Store();
        store.Remember("le gusta el café solo y salir a correr por el río", "like", 0.5, "said");
        Assert.Equal("Le gusta el café solo y salir a correr por el río", Assert.Single(store.All).Text);
        // Said already by the fuller one: known, nothing new.
        var known = store.Remember("Le gusta el café solo", "like", 0.6, "learned")!.Value;
        Assert.False(known.IsNew);
        Assert.Null(known.Previous);
        Assert.Contains("correr", Assert.Single(store.All).Text, StringComparison.Ordinal);
        // A fuller one of something known takes its place.
        store.Remember("Su hermana se llama Marta", "fact", 0.6, "said");
        var fuller = store.Remember("Su hermana se llama Marta y vive en Valencia con sus hijos", "fact", 0.6, "learned")!.Value;
        Assert.False(fuller.IsNew);
        Assert.Equal("Su hermana se llama Marta", fuller.Previous);
        Assert.Equal(2, store.All.Count);
    }

    [Fact]
    public void PastItsCapacityTheLeastValuableGoes()
    {
        var store = Store();
        store.Remember("Se llama Víctor", "fact", 0.9, "said");
        // Words that never repeat, so none is taken for another.
        static string Word() => "x" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < MemoryStore.Capacity + 5; i++) store.Remember($"{Word()} {Word()} {Word()}", "like", 0.2, "learned");
        Assert.Equal(MemoryStore.Capacity, store.All.Count);
        Assert.Contains(store.All, m => m.Text == "Se llama Víctor");
    }

    [Fact]
    public void WhatFitsComesToMind()
    {
        var store = Store();
        store.Remember("Es alérgico al marisco", "fact", 0.6, "said");
        store.Remember("Le encanta el café solo", "like", 0.5, "learned");
        store.Remember("Su hija se llama Ana", "fact", 0.6, "said");
        store.Remember("Se llama Víctor", "fact", 0.9, "said");

        // By words: «marisco»; and his name, an important fact, always.
        var recalled = store.Recall("¿puedo comer marisco esta noche?", 4);
        Assert.Contains(recalled, m => m.Text.Contains("marisco", StringComparison.Ordinal));
        Assert.Contains(recalled, m => m.Text == "Se llama Víctor");
        Assert.DoesNotContain(recalled, m => m.Text.Contains("café", StringComparison.Ordinal));
        Assert.Equal(1, store.All.Single(m => m.Text.Contains("marisco", StringComparison.Ordinal)).RecallCount);

        // By meaning, without a word in common: what stands out.
        recalled = store.Recall("¿qué me pido para desayunar?", 4, text => text.Contains("café", StringComparison.Ordinal) ? 0.8 : 0.3);
        Assert.Contains(recalled, m => m.Text.Contains("café", StringComparison.Ordinal));
        Assert.DoesNotContain(recalled, m => m.Text.Contains("Ana", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheToolRemembersAndIsUndone()
    {
        var store = Store();
        var tool = new RememberTool(store);
        var result = await tool.RunAsync(ToolArguments.Parse("""{"fact":"Trabaja de noche"}"""), new ToolContext("lander"), default);
        Assert.False(result.Failed);
        Assert.NotNull(result.Undo);
        Assert.Single(store.All);
        Assert.True(await tool.UndoAsync(result.Undo!, default));
        Assert.Empty(store.All);

        // Undoing an update brings the old text back.
        store.Remember("Su perro se llama Toby", "fact", 0.5, "said");
        result = await tool.RunAsync(ToolArguments.Parse("""{"fact":"Su perro se llama Toby y es un beagle"}"""), new ToolContext(null), default);
        Assert.True(await tool.UndoAsync(result.Undo!, default));
        Assert.Equal("Su perro se llama Toby", Assert.Single(store.All).Text);
    }

    [Fact]
    public void TheModelsProposalsAreReadWithTolerance()
    {
        var found = TalkMemory.Parse("Claro: {\"memories\":[{\"text\":\"Le gusta correr\",\"kind\":\"like\",\"importance\":0.6},\"Vive en Sevilla\"]} ¡listo!");
        Assert.Equal(2, found.Count);
        Assert.Equal(new MemoryCandidate("Le gusta correr", "like", 0.6), found[0]);
        Assert.Equal("fact", found[1].Kind);
        Assert.Empty(TalkMemory.Parse("no hay nada"));
        Assert.Empty(TalkMemory.Parse("{\"memories\":[]}"));
    }

    [Fact]
    public async Task ItComesToMindAndItLearnsAfterTheAi()
    {
        var builtin = new BuiltinTools(_folder);
        using var _ = builtin;
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        var log = new List<LogMessage>();
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), m => { lock (log) log.Add(m); }, Path.Combine(_folder, "undo.json"));
        builtin.Memory.Remember("Es alérgico al marisco", "fact", 0.6, "said");
        var model = new ScriptedModel();
        model.Answers.Enqueue("Cuidado, que eres alérgico al marisco.");
        model.Answers.Enqueue("""{"memories":[{"text":"Esta noche cena en un restaurante","kind":"event","importance":0.4}]}""");
        var memory = new TalkMemory(builtin.Memory, runner, null);
        var talk = new TalkService(_ => (model, "fake:4b"), runner, _ => AssistantPersona.Module(), new SilentOutput(), memory: memory);

        await talk.HandleAsync(new HeardMessage("esta noche ceno fuera y pediré marisco", "panel"));
        // What it knows went into the instructions.
        Assert.Contains("Es alérgico al marisco", model.Requests[0].Messages[0].Content, StringComparison.Ordinal);
        // And it learned something new, in the background, through the tool (in the activity, undoable).
        for (var i = 0; i < 100 && builtin.Memory.All.Count < 2; i++) await Task.Delay(20);
        Assert.Contains(builtin.Memory.All, m => m is { Text: "Esta noche cena en un restaurante", Source: "learned" });
        Assert.Contains(log, l => l.Entry is { Route: "memory", Ok: true, Undo: not null });
        Assert.Empty(model.Requests[1].Tools);
    }

    [Fact]
    public async Task ItDoesNotLearnWhenTurnedOff()
    {
        var builtin = new BuiltinTools(_folder);
        using var _ = builtin;
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), _ => { }, Path.Combine(_folder, "undo.json"));
        var model = new ScriptedModel();
        model.Answers.Enqueue("Muy bien, gracias por contármelo.");
        var talk = new TalkService(_ => (model, "fake:4b"), runner, _ => AssistantPersona.Module(), new SilentOutput(),
            memory: new TalkMemory(builtin.Memory, runner, null), learn: () => false);
        await talk.HandleAsync(new HeardMessage("mi hermano se llama Pablo y vive en Lisboa", "panel"));
        await Task.Delay(200);
        Assert.Single(model.Requests);
        Assert.Empty(builtin.Memory.All);
    }

    private sealed class SilentOutput : ITalkOutput
    {
        public void Chat(ChatLineMessage line) { }

        public void State(string? assistant, string state) { }

        public Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Answers with the texts of a queue, in order, and keeps every request.</summary>
    private sealed class ScriptedModel : IChatModel
    {
        public Queue<string> Answers { get; } = new();

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string answer;
            lock (Requests)
            {
                Requests.Add(request);
                answer = Answers.TryDequeue(out var next) ? next : throw new InvalidOperationException("Nadie esperaba otra petición al modelo");
            }
            await Task.Yield();
            yield return new ChatChunk(answer);
            yield return new ChatChunk("", Done: true);
        }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task PreloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }
}
