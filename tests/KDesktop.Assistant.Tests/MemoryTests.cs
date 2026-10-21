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

    [Fact]
    public void NumbersCountWhenComparing()
    {
        var store = Store();
        store.Remember("Kobayashi tiene 44 años", "fact", 0.6, "said");
        // «44» used to be dropped (two letters), leaving one word: the same fact was kept twice.
        var again = store.Remember("Tiene 44 años", "fact", 0.8, "learned")!.Value;
        Assert.False(again.IsNew);
        Assert.Single(store.All);
        Assert.True(store.Remember("Tiene 45 años de experiencia en el banco", "fact", 0.5, "said")!.Value.IsNew);
    }

    [Fact]
    public async Task TheForgetToolForgetsAndIsUndone()
    {
        var store = Store();
        store.Remember("Cuando diga «la vuelta», quiere que se abra ejemplo.com", "fact", 0.9, "said");
        store.Remember("Se llama Kobayashi", "fact", 0.9, "said");
        var tool = new ForgetTool(store);

        var result = await tool.RunAsync(ToolArguments.Parse("""{"fact":"la vuelta"}"""), new ToolContext(null), default);
        Assert.False(result.Failed);
        Assert.Equal("Se llama Kobayashi", Assert.Single(store.All).Text);
        Assert.True(await tool.UndoAsync(result.Undo!, default));
        Assert.Equal(2, store.All.Count);

        var none = await tool.RunAsync(ToolArguments.Parse("""{"fact":"mi coche rojo"}"""), new ToolContext(null), default);
        Assert.True(none.Failed);
        Assert.Equal(2, store.All.Count);
    }

    [Theory]
    [InlineData("añade a la memoria que mido 1,65", QuickRuleKind.Remember, "mido 1,65")]
    [InlineData("añada la memoria que mido 1.65.", QuickRuleKind.Remember, "mido 1.65")]
    [InlineData("a la memoria que me llamo Kobayashi y tengo 44 años.", QuickRuleKind.Remember, "me llamo Kobayashi y tengo 44 años")]
    [InlineData("Lander, recuerda que mi jefe se llama Luis", QuickRuleKind.Remember, "mi jefe se llama Luis")]
    [InlineData("quiero que recuerdes que trabajo de noche", QuickRuleKind.Remember, "trabajo de noche")]
    [InlineData("remember that I'm allergic to seafood", QuickRuleKind.Remember, "I'm allergic to seafood")]
    [InlineData("ricorda che mia figlia si chiama Ana", QuickRuleKind.Remember, "mia figlia si chiama Ana")]
    [InlineData("¿Qué tienes en la memoria?", QuickRuleKind.Recall, null)]
    [InlineData("que tienes en la memoria.", QuickRuleKind.Recall, null)]
    [InlineData("¿qué sabes de mí?", QuickRuleKind.Recall, null)]
    [InlineData("what do you know about me?", QuickRuleKind.Recall, null)]
    [InlineData("olvida lo de la vuelta", QuickRuleKind.Forget, "la vuelta")]
    [InlineData("olvida que tengo 44 años", QuickRuleKind.Forget, "tengo 44 años")]
    [InlineData("borra de tu memoria lo de mi jefe", QuickRuleKind.Forget, "lo de mi jefe")]
    // The rules that were there still win.
    [InlineData("apunta que tengo que comprar pilas", QuickRuleKind.Note, "tengo que comprar pilas")]
    [InlineData("olvídalo", QuickRuleKind.Cancel, null)]
    public void TheMemoryRulesUnderstand(string said, QuickRuleKind kind, string? text)
    {
        var rule = QuickRules.Match(said, ["Lander"]);
        Assert.NotNull(rule);
        Assert.Equal(kind, rule.Kind);
        Assert.Equal(text, rule.Text);
    }

    [Fact]
    public async Task WhatItIsAskedToRememberIsKeptToldAndForgotten()
    {
        var (talk, builtin, model, chat) = MakeTalk();
        using var _ = builtin;
        model.Answers.Enqueue("""{"memories":[{"text":"Se llama Kobayashi","kind":"fact"},{"text":"Tiene 44 años","kind":"fact"}]}""");

        await talk.HandleAsync(new HeardMessage("añade a la memoria que me llamo Kobayashi y tengo 44 años", "panel"));
        // The model only rewrote it: no tools, nothing else asked.
        var rewrite = Assert.Single(model.Requests);
        Assert.Empty(rewrite.Tools);
        Assert.Equal(["Se llama Kobayashi", "Tiene 44 años"], builtin.Memory.All.Select(m => m.Text).Order());
        Assert.All(builtin.Memory.All, m => Assert.Equal("said", m.Source));
        Assert.Contains("Lo recordaré: Se llama Kobayashi. Tiene 44 años.", chat.Last(), StringComparison.Ordinal);

        // Told without the AI.
        await talk.HandleAsync(new HeardMessage("¿qué sabes de mí?", "panel"));
        Assert.Single(model.Requests);
        Assert.Contains("Se llama Kobayashi", chat.Last(), StringComparison.Ordinal);
        Assert.Contains("Tiene 44 años", chat.Last(), StringComparison.Ordinal);

        await talk.HandleAsync(new HeardMessage("olvida que tengo 44 años", "panel"));
        Assert.Equal("Se llama Kobayashi", Assert.Single(builtin.Memory.All).Text);
        // No notes were taken on the way.
        Assert.Empty(builtin.Notes.All);
    }

    [Theory]
    // What qwen3-4b-instruct gave for «mi perra se llama Kira y odio el cilantro»: the examples of its instructions too.
    [InlineData("Su perra se llama Kira", "mi perra se llama Kira y odio el cilantro", true)]
    [InlineData("Odia el cilantro", "mi perra se llama Kira y odio el cilantro", true)]
    [InlineData("Tiene 44 años", "mi perra se llama Kira y odio el cilantro", false)]
    [InlineData("Cuando diga «modo cine», quiere que se abra netflix.com", "mi perra se llama Kira y odio el cilantro", false)]
    [InlineData("Mide 1,65 metros", "mido 1,65", true)]
    [InlineData("Se llama Kobayashi", "me llamo Kobayashi y tengo 44 años", true)]
    [InlineData("Cuando diga «la vuelta», quiere que se abra pornhub.com", "cuando te diga la vuelta es que abras la página de Pornhub.com", true)]
    public void OnlyFactsFromWhatWasSaidAreKept(string fact, string said, bool kept) => Assert.Equal(kept, TalkMemory.Grounded(fact, said));

    [Fact]
    public async Task WithoutTheAiItIsKeptAsSaid()
    {
        var (talk, builtin, model, _) = MakeTalk(brain: false);
        using var _ = builtin;
        await talk.HandleAsync(new HeardMessage("añade a la memoria que mido 1,65", "panel"));
        Assert.Empty(model.Requests);
        Assert.Equal("Pidió recordar: «mido 1,65»", Assert.Single(builtin.Memory.All).Text);
    }

    [Fact]
    public async Task TalkingAboutTheMemoryOffersItsToolsAndNotTheNotes()
    {
        var (talk, builtin, model, _) = MakeTalk();
        using var _ = builtin;
        model.Answers.Enqueue("Vale.");
        await talk.HandleAsync(new HeardMessage("y eso añádelo también a la memoria", "panel"));
        var offered = Assert.Single(model.Requests).Tools.Select(t => t.Name).ToList();
        Assert.Contains("remember", offered);
        Assert.Contains("forget", offered);
        Assert.DoesNotContain("save_note", offered);
        Assert.DoesNotContain("find_notes", offered);
        // And it does not learn from talk about the memory itself.
        await Task.Delay(200);
        Assert.Single(model.Requests);
    }

    private (TalkService Talk, BuiltinTools Builtin, ScriptedModel Model, List<string> Chat) MakeTalk(bool brain = true)
    {
        var builtin = new BuiltinTools(_folder);
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), _ => { }, Path.Combine(_folder, "undo.json"));
        var model = new ScriptedModel();
        var output = new SilentOutput();
        var talk = new TalkService(_ => brain ? (model, "fake:4b") : (null, null), runner, _ => AssistantPersona.Module(), output,
            memory: new TalkMemory(builtin.Memory, runner, null));
        return (talk, builtin, model, output.Said);
    }

    private sealed class SilentOutput : ITalkOutput
    {
        /// <summary>The final answers, in order.</summary>
        public List<string> Said { get; } = [];

        public void Chat(ChatLineMessage line)
        {
            if (line is { Role: "assistant", Final: true }) Said.Add(line.Text);
        }

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
