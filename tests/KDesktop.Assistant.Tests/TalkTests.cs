using System.Runtime.CompilerServices;
using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 3.4: the fixed rules answer without the AI, everything else goes to it (with the persona, a short history and
/// at most three rounds of tools), and «cancela», «repite» and «deshaz» work on the conversation.
/// </summary>
public sealed class TalkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-talk-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<ChatLineMessage> _chat = [];
    private readonly List<string> _states = [];
    private readonly List<string> _said = [];
    private readonly List<LogMessage> _log = [];
    private readonly FakeModel _model = new();
    private bool _brain = true;

    private static readonly AssistantPersona Lander = new("Lander", "Eres un pequeño módulo lunar. Hablas como un piloto de la NASA.",
        new Dictionary<string, IReadOnlyList<string>> { ["ok"] = ["Recibido."], ["cancelled"] = ["Maniobra abortada."] });

    private (TalkService Talk, BuiltinTools Tools) Make()
    {
        var builtin = new BuiltinTools(_folder);
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), m => { lock (_log) _log.Add(m); }, Path.Combine(_folder, "undo.json"));
        var talk = new TalkService(_ => _brain ? (_model, "fake:4b") : (null, null), runner, who => who == "lander" ? Lander : AssistantPersona.Module(), new Output(this));
        return (talk, builtin);
    }

    private static readonly string[] Errors = ["Algo ha fallado.", "Vaya, no ha salido."];

    private static HeardMessage Heard(string text, string? who = "lander") => new(text, "panel", who);

    private string LastAnswer => _chat.Last(c => c.Role == "assistant" && c.Final).Text;

    [Theory]
    [InlineData("¿Qué hora es?", QuickRuleKind.Time, null, null)]
    [InlineData("Lander, ¿qué hora es, por favor?", QuickRuleKind.Time, null, null)]
    [InlineData("¿Qué día es hoy?", QuickRuleKind.Time, null, null)]
    [InlineData("avísame en diez minutos", QuickRuleKind.Timer, 600.0, null)]
    [InlineData("Avísame en 25 minutos", QuickRuleKind.Timer, 1500.0, null)]
    [InlineData("pon un temporizador de una hora y media para sacar la ropa", QuickRuleKind.Timer, 5400.0, "sacar la ropa")]
    [InlineData("¿Puedes poner un temporizador de cinco minutos?", QuickRuleKind.Timer, 300.0, null)]
    [InlineData("recuérdame que llame a mamá en media hora", QuickRuleKind.Timer, 1800.0, "llame a mamá")]
    [InlineData("avísame en una hora y diez minutos", QuickRuleKind.Timer, 4200.0, null)]
    [InlineData("apunta que tengo que comprar pilas", QuickRuleKind.Note, null, "tengo que comprar pilas")]
    [InlineData("Apunta: comprar leche.", QuickRuleKind.Note, null, "comprar leche")]
    [InlineData("quita el temporizador de la pizza", QuickRuleKind.CancelTimer, null, "la pizza")]
    [InlineData("cancela el temporizador", QuickRuleKind.CancelTimer, null, null)]
    [InlineData("Cancela.", QuickRuleKind.Cancel, null, null)]
    [InlineData("olvídalo", QuickRuleKind.Cancel, null, null)]
    [InlineData("¿Qué has dicho?", QuickRuleKind.Repeat, null, null)]
    [InlineData("deshaz eso", QuickRuleKind.Undo, null, null)]
    [InlineData("remind me in 20 minutes to call Bob", QuickRuleKind.Timer, 1200.0, "call Bob")]
    [InlineData("set a 10 minute timer", QuickRuleKind.Timer, 600.0, null)]
    [InlineData("What's the time?", QuickRuleKind.Time, null, null)]
    [InlineData("erinnere mich in fünfzehn Minuten", QuickRuleKind.Timer, 900.0, null)]
    [InlineData("Wie spät ist es?", QuickRuleKind.Time, null, null)]
    [InlineData("rappelle-moi dans une demi-heure", QuickRuleKind.Timer, 1800.0, null)]
    [InlineData("ricordami tra venti minuti di chiamare Luca", QuickRuleKind.Timer, 1200.0, "chiamare Luca")]
    [InlineData("me avisa daqui a dez minutos", QuickRuleKind.Timer, 600.0, null)]
    public void TheFixedRulesUnderstand(string said, QuickRuleKind kind, double? seconds, string? text)
    {
        var rule = QuickRules.Match(said, ["Lander"]);
        Assert.NotNull(rule);
        Assert.Equal(kind, rule.Kind);
        Assert.Equal(seconds, rule.Seconds);
        Assert.Equal(text, rule.Text);
    }

    [Theory]
    [InlineData("¿Qué tiempo hará mañana?")]
    [InlineData("cuéntame un chiste")]
    [InlineData("abre la calculadora")]
    [InlineData("¿para qué sirve esto?")]
    [InlineData("en diez minutos")]
    [InlineData("apunta")]
    [InlineData("hola")]
    public void AnythingElseGoesToTheAi(string said) => Assert.Null(QuickRules.Match(said, ["Lander"]));

    [Fact]
    public async Task ARuleAnswersWithoutTheAi()
    {
        var (talk, tools) = Make();
        using var _ = tools;
        await talk.HandleAsync(Heard("Lander, ¿qué hora es?"));

        Assert.Empty(_model.Requests);
        Assert.StartsWith("Son las ", LastAnswer, StringComparison.Ordinal);
        Assert.Equal(["user", "assistant"], _chat.Select(c => c.Role));
        Assert.All(_chat, c => Assert.Equal("lander", c.Assistant));
        Assert.Equal(_chat[0].Turn, _chat[1].Turn);
        Assert.Equal("rule", Assert.Single(_log).Entry.Route);
        Assert.Equal(["speaking", "idle"], _states);
        Assert.Equal(LastAnswer, Assert.Single(_said));

        // An action starts with the assistant's «ok».
        await talk.HandleAsync(Heard("avísame en diez minutos para sacar la pizza"));
        Assert.Empty(_model.Requests);
        Assert.StartsWith("Recibido. Te aviso a las ", LastAnswer, StringComparison.Ordinal);
        Assert.EndsWith(": sacar la pizza.", LastAnswer, StringComparison.Ordinal);
        Assert.Equal("sacar la pizza", Assert.Single(tools.Timers.Pending).Text);
    }

    [Fact]
    public async Task TheRestGoesToTheAiWithThePersonaAndAShortHistory()
    {
        var (talk, tools) = Make();
        using var _ = tools;
        _model.Answers.Enqueue(_ => Text("¡Aquí **Lander**, listo para despegar! 🚀"));
        await talk.HandleAsync(Heard("¿quién eres?"));

        var request = Assert.Single(_model.Requests);
        Assert.Equal("fake:4b", request.Model);
        var system = request.Messages[0];
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Contains("Te llamas Lander", system.Content, StringComparison.Ordinal);
        Assert.Contains(Lander.Prompt, system.Content, StringComparison.Ordinal);
        Assert.Contains("en español", system.Content, StringComparison.Ordinal);
        Assert.Equal("¿quién eres?", request.Messages[^1].Content);
        Assert.NotEmpty(request.Tools);
        // Cleaned for a bubble and a voice.
        Assert.Equal("¡Aquí Lander, listo para despegar!", LastAnswer);
        Assert.Equal(["thinking", "speaking", "idle"], _states);

        // The history goes with the next ones, three exchanges at most.
        for (var i = 0; i < 4; i++)
        {
            _model.Answers.Enqueue(_ => Text("Vale."));
            await talk.HandleAsync(Heard($"pregunta {i}"));
        }
        var last = _model.Requests[^1].Messages;
        Assert.Equal(1 + 2 * TalkHistory.Kept + 1, last.Count);
        Assert.Equal("pregunta 0", last[1].Content);
        Assert.Equal("pregunta 3", last[^1].Content);

        // Each assistant has its own conversation.
        _model.Answers.Enqueue(_ => Text("Hola."));
        await talk.HandleAsync(Heard("hola", who: null));
        var alone = _model.Requests[^1].Messages;
        Assert.Equal(2, alone.Count);
        Assert.Contains("Te llamas K-Desktop", alone[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAiUsesToolsAtMostThreeRounds()
    {
        var (talk, tools) = Make();
        using var _ = tools;
        _model.Answers.Enqueue(_ => Call("set_timer", """{"minutes":5,"reason":"el té"}"""));
        _model.Answers.Enqueue(_ => Text("Hecho, te aviso para el té."));
        await talk.HandleAsync(Heard("cuando pasen cinco minutos me dices lo del té"));

        Assert.Equal(2, _model.Requests.Count);
        var result = _model.Requests[1].Messages[^1];
        Assert.Equal(ChatRole.Tool, result.Role);
        Assert.Contains("Timer set", result.Content, StringComparison.Ordinal);
        Assert.Equal("el té", Assert.Single(tools.Timers.Pending).Text);
        Assert.Equal("ai", Assert.Single(_log).Entry.Route);
        // An action: what the tool says it did, not what the model makes of it.
        Assert.StartsWith("Recibido. Te aviso a las ", LastAnswer, StringComparison.Ordinal);
        Assert.EndsWith(": el té.", LastAnswer, StringComparison.Ordinal);

        // A question: the model's words.
        _model.Answers.Enqueue(_ => Call("calculate", """{"expression":"240*18/100"}"""));
        _model.Answers.Enqueue(_ => Text("Son 43,2."));
        await talk.HandleAsync(Heard("¿cuánto es el 18 % de 240?"));
        Assert.Equal("Son 43,2.", LastAnswer);

        // A model that keeps calling tools: three calls, then it is asked without tools; silent, the tool says what it did.
        _model.Requests.Clear();
        _log.Clear();
        tools.Timers.RemoveMatching(null);
        for (var i = 0; i < 3; i++) _model.Answers.Enqueue(_ => Call("save_note", $$"""{"note":"nota {{Guid.NewGuid():N}}"}"""));
        _model.Answers.Enqueue(_ => Text(""));
        await talk.HandleAsync(Heard("llena mis notas de cosas"));
        Assert.Equal(TalkMind.MaxToolRounds + 1, _model.Requests.Count);
        Assert.Empty(_model.Requests[^1].Tools);
        Assert.All(_model.Requests.Take(TalkMind.MaxToolRounds), r => Assert.NotEmpty(r.Tools));
        Assert.Equal(3, _log.Count);
        Assert.StartsWith("Recibido. Apuntado: «nota ", LastAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheAiItSaysSo()
    {
        var (talk, tools) = Make();
        using var _ = tools;
        _brain = false;
        await talk.HandleAsync(Heard("cuéntame un chiste"));
        Assert.Contains("Opciones → Cerebro", LastAnswer, StringComparison.Ordinal);

        // A failing server: the persona's error.
        _brain = true;
        _model.Answers.Enqueue(_ => throw new ChatModelException(ChatModelFailure.Unreachable, "no"));
        await talk.HandleAsync(Heard("cuéntame otro"));
        Assert.Contains(LastAnswer, Errors);
    }

    [Fact]
    public async Task CancelRepeatAndUndo()
    {
        var (talk, tools) = Make();
        using var _ = tools;

        // «Cancela» while the AI thinks: that answer never comes.
        var thinking = new TaskCompletionSource();
        _model.Answers.Enqueue(_ => Slow(thinking));
        var first = talk.HandleAsync(Heard("piensa algo muy largo"));
        await thinking.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await talk.HandleAsync(Heard("cancela"));
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Maniobra abortada.", LastAnswer);
        Assert.Single(_chat, c => c.Role == "assistant" && c.Final);
        await talk.HandleAsync(Heard("cancela"));
        Assert.Equal("No estaba haciendo nada.", LastAnswer);

        // «Repite» says the last answer again.
        await talk.HandleAsync(Heard("apunta comprar pilas"));
        var noted = LastAnswer;
        Assert.Equal("Recibido. Apuntado: «comprar pilas».", noted);
        await talk.HandleAsync(Heard("repite"));
        Assert.Equal(noted, LastAnswer);

        // «Deshaz» undoes the newest thing it did.
        Assert.Single(tools.Notes.Find(null, 10));
        await talk.HandleAsync(Heard("deshaz eso"));
        Assert.Equal("Deshecho: apuntó «comprar pilas».", LastAnswer);
        Assert.Empty(tools.Notes.Find(null, 10));
        await talk.HandleAsync(Heard("deshaz"));
        Assert.Equal("No hay nada reciente que deshacer.", LastAnswer);
        // Only «piensa algo muy largo» went to the AI.
        Assert.Single(_model.Requests);
    }

    [Fact]
    public void RepliesAreCleanedForABubble()
    {
        Assert.Equal("Hola, soy Dot.", ReplyText.Clean("<think>mmm</think>**Hola**, soy `Dot`. 😀"));
        Assert.Equal("Uno dos", ReplyText.Clean("- Uno\n- dos"));
        Assert.Equal("Abre la web", ReplyText.Clean("Abre [la web](https://x.y)"));
        Assert.Equal("Lo dejo", ReplyText.Clean("Lo dejo<think>sin cerrar"));
        Assert.Equal("set_timer y 20 °C", ReplyText.Clean("set_timer y 20 °C"));
        Assert.Equal("Dicho", ReplyText.Clean("«Dicho»"));
        Assert.Equal("Tienes pilas, piloto.", ReplyText.Clean("<bocadillo>Tienes pilas, piloto.</bocadillo>"));
        Assert.Equal("1 < 2 y 3 > 2", ReplyText.Clean("1 < 2 y 3 > 2"));
    }

    [Fact]
    public void ThePersonaComesFromThePackageInTheUsersLanguage()
    {
        var package = Path.Combine(_folder, "package");
        Directory.CreateDirectory(Path.Combine(package, "persona"));
        File.WriteAllText(Path.Combine(package, "persona", "en.json"), """{ "prompt": "You are a tiny lunar module.", "phrases": { "ok": ["Roger."] } }""");
        var manifest = AssistantManifest.Parse(File.ReadAllText(Path.Combine(Contracts.DocsDir, "ejemplos-paquete", "lander", "assistant.json")));

        // No Spanish one: English.
        var persona = AssistantPersona.Read(package, manifest, "es");
        Assert.Equal("Lander", persona.Name);
        Assert.Equal("You are a tiny lunar module.", persona.Prompt);
        Assert.Equal("Roger.", persona.Phrase("ok"));
        // A phrase it does not have is the module's.
        Assert.Equal("No estaba haciendo nada.", persona.Phrase("nothingToCancel"));

        File.WriteAllText(Path.Combine(package, "persona", "es.json"), File.ReadAllText(Path.Combine(Contracts.DocsDir, "ejemplos-paquete", "lander", "persona", "es.json")));
        persona = AssistantPersona.Read(package, manifest, "es");
        Assert.StartsWith("Eres un pequeño módulo lunar", persona.Prompt, StringComparison.Ordinal);
        Assert.Equal("¿«A» o «B»?", persona.Phrase("which", null, "A", "B"));

        // Nothing at all: the module's.
        persona = AssistantPersona.Read(Path.Combine(_folder, "empty"), manifest, "es");
        Assert.Equal(Core.Localization.Loc.T("assistant.persona.default"), persona.Prompt);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static IEnumerable<ChatChunk> Text(string text) => [new ChatChunk(text[..(text.Length / 2)]), new ChatChunk(text[(text.Length / 2)..]), new ChatChunk("", Done: true)];

    private static IEnumerable<ChatChunk> Call(string tool, string arguments) =>
        [new ChatChunk("", [new ToolCall(tool, ToolArguments.Parse(arguments))]), new ChatChunk("", Done: true)];

    /// <summary>Says it started, then never answers (until cancelled).</summary>
    private static IEnumerable<ChatChunk> Slow(TaskCompletionSource started)
    {
        started.TrySetResult();
        yield return new ChatChunk("Pensando");
        throw new InvalidOperationException("the fake waits in StreamAsync");
    }

    private sealed class Output(TalkTests test) : ITalkOutput
    {
        public void Chat(ChatLineMessage line)
        {
            lock (test._chat) test._chat.Add(line);
        }

        public void State(string? assistant, string state)
        {
            lock (test._states) test._states.Add(state);
        }

        public Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken)
        {
            lock (test._said) test._said.Add(text);
            return Task.CompletedTask;
        }
    }

    /// <summary>A model that answers from a queue of scripted rounds and keeps every request.</summary>
    private sealed class FakeModel : IChatModel
    {
        public Queue<Func<ChatRequest, IEnumerable<ChatChunk>>> Answers { get; } = new();

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request with { Messages = [.. request.Messages] });
            if (!Answers.TryDequeue(out var answer)) throw new InvalidOperationException("Nadie esperaba otra petición al modelo");
            var chunks = answer(request);
            using var e = chunks.GetEnumerator();
            while (true)
            {
                bool more;
                try { more = e.MoveNext(); }
                catch (InvalidOperationException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A slow answer: wait until the order is cancelled.
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    yield break;
                }
                if (!more) yield break;
                yield return e.Current;
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
