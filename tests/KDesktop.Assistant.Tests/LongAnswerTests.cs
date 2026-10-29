using System.Runtime.CompilerServices;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 14.2: a long answer asked for («explícamelo en detalle») is written as a summary, «---» and the whole text; the
/// voice and the bubble get the summary, the chat line the whole text too. One that does not fit is written again in that
/// form, and one too long for a bubble that was not asked for is written down with its first sentences said.
/// </summary>
public sealed class LongAnswerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-long-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<ChatLineMessage> _chat = [];
    private readonly List<string> _said = [];
    private readonly List<string> _ahead = [];
    private readonly List<RepliedMessage> _replied = [];
    private readonly Model _model = new();

    private static readonly AssistantPersona Lander = new("Lander", "Eres un pequeño módulo lunar.",
        new Dictionary<string, IReadOnlyList<string>> { ["ok"] = ["Recibido."], ["longWritten"] = ["Te lo dejo por escrito."] });

    private TalkService Make()
    {
        var registry = new ToolRegistry();
        new BuiltinTools(_folder).AddTo(registry);
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), _ => { }, Path.Combine(_folder, "undo.json"));
        return new TalkService(_ => (_model, "fake:4b"), runner, _ => Lander, new Output(this));
    }

    private ChatLineMessage Final => _chat.Last(c => c is { Role: "assistant", Final: true });

    private const string Body = "# Cómo plantar tomates\n\n1. Elige un sitio con **mucho sol**.\n2. Prepara la tierra con abono.\n3. Planta en primavera, a medio metro.\n4. Riega a menudo sin mojar las hojas.\n\n> Truco: pon un tutor a cada planta.";

    [Theory]
    [InlineData("Explícamelo en detalle, por favor", true)]
    [InlineData("hazme una lista de cosas para la playa", true)]
    [InlineData("Resúmeme «factura.pdf».", true)]
    [InlineData("Explain step by step how to bake bread", true)]
    [InlineData("¿qué hora es en Tokio?", false)]
    [InlineData("abre Steam", false)]
    public void LongAnswersAreAskedForWithTheirWords(string said, bool asked) => Assert.Equal(asked, LongAnswers.Asked(said));

    [Fact]
    public void TheSummaryAndTheTextAreSplitAtTheirLine()
    {
        var (summary, text) = LongAnswers.Split("<think>hm</think>Para tomates, sol y agua.\n---\n" + Body);
        Assert.Equal("Para tomates, sol y agua.", summary);
        Assert.StartsWith("# Cómo plantar tomates", text, StringComparison.Ordinal);
        // The label a small model puts before the summary goes.
        Assert.Equal("Sol y agua.", LongAnswers.Split("Aquí tienes un resumen de una o dos frases para decir en voz alta: Sol y agua.\n---\n" + Body).Summary);
        Assert.Equal("Sol y agua.", LongAnswers.Split("**Resumen:** Sol y agua.\n---\n" + Body).Summary);
        Assert.Equal("Ojo: sol y agua.", LongAnswers.Split("Ojo: sol y agua.\n---\n" + Body).Summary);
        Assert.Equal("", LongAnswers.Split("Aquí tienes la guía para tu balcón:\n---\n" + Body).Summary);
        // Without the line, all of it is the text.
        Assert.Equal(("", Body), LongAnswers.Split(Body));
        // A rule further down, after a list, is part of the text.
        var (none, whole) = LongAnswers.Split(Body + "\n---\nFin.");
        Assert.Equal("", none);
        Assert.EndsWith("Fin.", whole, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsSaidAheadIsAlwaysTheStartOfWhatIsSaid()
    {
        Assert.Equal("Uno. Dos.", LongAnswers.Spoken("Uno. Dos. Tres."));
        Assert.Equal("Vale 3.5 kilos.", LongAnswers.Spoken("Vale 3.5 kilos."));
        var longOne = string.Join(", ", Enumerable.Repeat("una cosa más", 40));
        var cut = LongAnswers.Spoken(longOne);
        Assert.True(cut.Length <= LongAnswers.SpokenChars);
        Assert.EndsWith(",", cut, StringComparison.Ordinal);
        // Growing text: each spoken part starts where the one before did.
        const string answer = "Claro que sí. Te cuento cómo va todo esto, con calma. Primero el sol. Luego el agua.";
        var before = string.Empty;
        for (var i = 1; i <= answer.Length; i++)
        {
            var now = LongAnswers.Spoken(answer[..i]);
            Assert.StartsWith(before.TrimEnd(), now, StringComparison.Ordinal);
            if (now.EndsWith('.')) before = now;
        }
    }

    [Fact]
    public void ListsTablesAndCodeAreTooLongForABubble()
    {
        Assert.True(LongAnswers.TooLong(Body));
        Assert.True(LongAnswers.TooLong("Mira:\n```\nvar x = 1;\n```"));
        Assert.True(LongAnswers.TooLong("| a | b |\n|---|---|\n| 1 | 2 |"));
        Assert.False(LongAnswers.TooLong("Son las cinco y cuarto. Tienes tiempo."));
    }

    [Fact]
    public void ItIsReadAloudAsSentences()
    {
        var read = LongAnswers.Readable(Body + "\n\n```\ncódigo\n```\n| A | B |\n|---|---|\n| 1 | 2 |");
        Assert.Equal("Cómo plantar tomates. Elige un sitio con mucho sol. Prepara la tierra con abono. Planta en primavera, a medio metro. "
            + "Riega a menudo sin mojar las hojas. Truco: pon un tutor a cada planta. A, B. 1, 2.", read);
    }

    [Fact]
    public async Task WithAReplyItSaysNothingAndTheAnswerGoesBack()
    {
        // D1 (5.0.370): «Asistente: preguntar» that pastes, copies, shows or keeps the answer.
        var talk = Make();
        _model.Answers.Enqueue(_ => Chunks("Para tomates hacen falta sol, agua y paciencia.", "\n---\n", Body));
        await talk.HandleAsync(new HeardMessage("Explícame en detalle cómo plantar tomates", "command", "lander", Reply: "r-1"));

        Assert.Empty(_said);
        Assert.Empty(_ahead);
        var reply = Assert.Single(_replied);
        Assert.Equal(("r-1", true, "lander"), (reply.Id, reply.Ok, reply.Assistant));
        Assert.Equal("Para tomates hacen falta sol, agua y paciencia.", reply.Text);
        Assert.Equal(Body, reply.Written);
        // The chat has it, as always.
        Assert.Equal(Body, Final.Written);

        // Without an answer (the brain fails), it still goes back, not done.
        _replied.Clear();
        await talk.HandleAsync(new HeardMessage("Cuéntame algo de los volcanes de Marte", "command", "lander", Reply: "r-2"));
        Assert.Equal(("r-2", false), (Assert.Single(_replied).Id, _replied[0].Ok));
        Assert.Empty(_said);
    }

    [Fact]
    public async Task AskedForItWritesASummaryAndTheWholeText()
    {
        var talk = Make();
        _model.Answers.Enqueue(_ => Chunks("Para tomates hacen falta sol, agua y paciencia.", "\n---\n", Body));
        await talk.HandleAsync(new HeardMessage("Explícame en detalle cómo plantar tomates", "panel", "lander"));

        var request = Assert.Single(_model.Requests);
        Assert.True(request.MaxTokens >= TalkMind.LongTokens);
        Assert.Contains(Loc.T("assistant.prompt.long"), request.Messages[^1].Content, StringComparison.Ordinal);
        Assert.Equal("Para tomates hacen falta sol, agua y paciencia.", Final.Text);
        Assert.Equal(Body, Final.Written);
        Assert.Equal("Para tomates hacen falta sol, agua y paciencia.", Assert.Single(_said));
        // The voice ahead never had the whole text, and the lines while it wrote had it growing.
        Assert.All(_ahead, a => Assert.DoesNotContain("tierra", a, StringComparison.Ordinal));
        // The next order knows what it wrote.
        _model.Answers.Enqueue(_ => Chunks("El cuarto: regar sin mojar las hojas."));
        await talk.HandleAsync(new HeardMessage("¿y el cuarto paso?", "panel", "lander"));
        Assert.Contains(_model.Requests[1].Messages, m => m.Role == ChatRole.Assistant && m.Content.Contains("mojar las hojas", StringComparison.Ordinal));
        Assert.Null(Final.Written);
    }

    [Fact]
    public async Task AnAnswerThatDoesNotFitIsWrittenAgainLong()
    {
        var talk = Make();
        _model.Answers.Enqueue(_ => Cut("Bueno, hay muchas cosas que contar sobre los tomates y"));
        _model.Answers.Enqueue(_ => Chunks("Los tomates quieren sol y agua.", "\n---\n", Body));
        await talk.HandleAsync(new HeardMessage("¿Cómo se plantan los tomates?", "panel", "lander"));

        Assert.Equal(2, _model.Requests.Count);
        Assert.Contains(Loc.T("assistant.prompt.longer"), _model.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Equal("Los tomates quieren sol y agua.", Final.Text);
        Assert.Equal(Body, Final.Written);
        // What only steered it is not kept.
        _model.Answers.Enqueue(_ => Chunks("Vale."));
        await talk.HandleAsync(new HeardMessage("gracias", "panel", "lander"));
        Assert.DoesNotContain(_model.Requests[2].Messages, m => m.Content.Contains(Loc.T("assistant.prompt.longer"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task OneTooLongThatWasNotAskedForIsWrittenDown()
    {
        var talk = Make();
        _model.Answers.Enqueue(_ => Chunks("Aquí tienes los pasos. Son sencillos.\n\n", Body));
        await talk.HandleAsync(new HeardMessage("¿Cómo se plantan los tomates?", "panel", "lander"));

        Assert.Single(_model.Requests);
        Assert.Equal("Aquí tienes los pasos. Son sencillos. Te lo dejo por escrito.", Final.Text);
        Assert.StartsWith("Aquí tienes los pasos.", Final.Written, StringComparison.Ordinal);
        Assert.Contains("# Cómo plantar tomates", Final.Written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShortAnswerStaysAsItWas()
    {
        var talk = Make();
        _model.Answers.Enqueue(_ => Chunks("Hoy hace sol. Sal a pasear. Y lleva agua."));
        await talk.HandleAsync(new HeardMessage("¿qué hago hoy?", "panel", "lander"));
        Assert.Equal("Hoy hace sol. Sal a pasear. Y lleva agua.", Final.Text);
        Assert.Null(Final.Written);
        Assert.Equal("Hoy hace sol. Sal a pasear. Y lleva agua.", Assert.Single(_said));
    }

    private static IEnumerable<ChatChunk> Chunks(params string[] parts)
    {
        foreach (var part in parts)
            foreach (var piece in part.Chunk(7)) yield return new ChatChunk(new string(piece));
        yield return new ChatChunk("", Done: true);
    }

    private static IEnumerable<ChatChunk> Cut(string text)
    {
        yield return new ChatChunk(text);
        yield return new ChatChunk("", Done: true) { Cut = true };
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class Output(LongAnswerTests test) : ITalkOutput
    {
        public void Chat(ChatLineMessage line)
        {
            lock (test._chat) test._chat.Add(line);
        }

        public void State(string? assistant, string state)
        {
        }

        public void SpeakAhead(string? assistant, string turn, string textSoFar)
        {
            lock (test._ahead) test._ahead.Add(textSoFar);
        }

        public Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken)
        {
            lock (test._said) test._said.Add(text);
            return Task.CompletedTask;
        }

        public void Replied(RepliedMessage reply)
        {
            lock (test._replied) test._replied.Add(reply);
        }
    }

    private sealed class Model : IChatModel
    {
        public Queue<Func<ChatRequest, IEnumerable<ChatChunk>>> Answers { get; } = new();

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request with { Messages = [.. request.Messages] });
            if (!Answers.TryDequeue(out var answer)) throw new InvalidOperationException("Nadie esperaba otra petición al modelo");
            foreach (var chunk in answer(request))
            {
                yield return chunk;
                await Task.Yield();
            }
        }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task PreloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }
}
