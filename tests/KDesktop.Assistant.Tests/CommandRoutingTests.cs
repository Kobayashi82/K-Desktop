using System.Runtime.CompilerServices;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 4.4: an order goes rules → the user's commands → the AI. What clearly names commands runs in order without the
/// AI; what is left goes to it; two candidates nearly as close are asked about and answered without it.
/// </summary>
public sealed class CommandRoutingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-routing-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly FakeBridge _bridge = new();
    private readonly ScriptedModel _model = new();
    private readonly List<LogMessage> _log = [];
    private readonly List<ChatLineMessage> _chat = [];
    private readonly List<(string?, int)> _listens = [];

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private TalkService Make(BuiltinTools builtin)
    {
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        CommandTools.AddTo(registry, _bridge);
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), m => { lock (_log) _log.Add(m); }, Path.Combine(_folder, "undo.json"));
        return new TalkService(_ => (_model, "fake:4b"), runner, _ => AssistantPersona.Module(), new Output(_chat, _listens), commands: _bridge);
    }

    private string LastAnswer => _chat.Last(c => c is { Role: "assistant", Final: true }).Text;

    [Fact]
    public async Task WhatClearlyNamesCommandsRunsInOrderWithoutTheAi()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Sure("vacía la papelera y baja el volumen", ("bin", "Vaciar papelera", null), (null, "Bajar el volumen", "volumeDown"));

        await talk.HandleAsync(new HeardMessage("vacía la papelera y baja el volumen", "panel", "lander"));
        Assert.Equal(["run:bin:lander", "media:volumeDown"], _bridge.Done);
        Assert.Empty(_model.Requests);
        Assert.Contains("Vaciar papelera", LastAnswer, StringComparison.Ordinal);
        Assert.Contains("Bajar el volumen", LastAnswer, StringComparison.Ordinal);
        Assert.Equal(2, _log.Count(l => l.Entry is { Route: "kdesktop", Ok: true }));
    }

    [Fact]
    public async Task WhatIsLeftGoesToTheAi()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Matches["vacía la papelera y cuéntame un chiste"] = new MatchedMessage("x", 0.5, ["cuéntame un chiste"],
            [new MatchedPart("vacía la papelera", true, [new MatchedCandidate("bin", "Vaciar papelera", 0.97)])]);
        _bridge.Sure("Vaciar papelera", ("bin", "Vaciar papelera", null));
        _model.Answers.Enqueue("¿Por qué el libro de mates está triste? Porque tiene muchos problemas.");

        await talk.HandleAsync(new HeardMessage("vacía la papelera y cuéntame un chiste", "panel"));
        Assert.Equal(["run:bin:"], _bridge.Done);
        Assert.Equal("cuéntame un chiste", _model.Requests.Single().Messages[^1].Content);
        Assert.StartsWith(Core.Localization.Loc.T("tool.run_command.reply", "Vaciar papelera")[..5], LastAnswer.Split(' ', 2)[1], StringComparison.Ordinal);
        Assert.Contains("problemas", LastAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoNearlyAsCloseAreAskedAboutAndAnsweredWithoutTheAi()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Matches["pon chrome"] = new MatchedMessage("x", 0.4, [],
            [new MatchedPart("pon chrome", false, [new MatchedCandidate("left", "Chrome a la izquierda", 0.80), new MatchedCandidate("right", "Chrome a la derecha", 0.77)])]);
        _bridge.Sure("Chrome a la derecha", ("right", "Chrome a la derecha", null));

        await talk.HandleAsync(new HeardMessage("pon chrome", "panel"));
        Assert.Contains("Chrome a la izquierda", LastAnswer, StringComparison.Ordinal);
        Assert.Contains("Chrome a la derecha", LastAnswer, StringComparison.Ordinal);
        Assert.Empty(_bridge.Done);

        await talk.HandleAsync(new HeardMessage("la otra", "panel"));
        Assert.Equal(["run:right:"], _bridge.Done);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task AQuestionToAnOrderSaidAloudIsListenedFor()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Matches["pon chrome"] = new MatchedMessage("x", 0.4, [],
            [new MatchedPart("pon chrome", false, [new MatchedCandidate("left", "Chrome a la izquierda", 0.80), new MatchedCandidate("right", "Chrome a la derecha", 0.77)])]);
        _bridge.Sure("Chrome a la izquierda", ("left", "Chrome a la izquierda", null));

        // Written: nothing to listen for (to another one: its question stays with it).
        await talk.HandleAsync(new HeardMessage("pon chrome", "panel", "dot"));
        Assert.Empty(_listens);

        // Said aloud: K-Desktop listens for «el primero» without the wake word.
        await talk.HandleAsync(new HeardMessage("pon chrome", "voice", "lander"));
        Assert.Equal([("lander", TalkService.FollowUpSeconds)], _listens);
        await talk.HandleAsync(new HeardMessage("el primero", "voice", "lander"));
        Assert.Equal(["run:left:lander"], _bridge.Done);
        Assert.Single(_listens);

        // The AI asking something back too.
        _model.Answers.Enqueue("¿Te refieres al de casa o al del trabajo?");
        await talk.HandleAsync(new HeardMessage("abre el correo de siempre", "voice", "lander"));
        Assert.Equal(2, _listens.Count);
    }

    [Fact]
    public async Task TheAnswerCanBeTheName()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Matches["pon chrome"] = new MatchedMessage("x", 0.4, [],
            [new MatchedPart("pon chrome", false, [new MatchedCandidate("left", "Chrome a la izquierda", 0.80), new MatchedCandidate("right", "Chrome a la derecha", 0.77)])]);
        _bridge.Sure("Chrome a la izquierda", ("left", "Chrome a la izquierda", null));
        await talk.HandleAsync(new HeardMessage("pon chrome", "panel"));
        await talk.HandleAsync(new HeardMessage("a la izquierda", "panel"));
        Assert.Equal(["run:left:"], _bridge.Done);
    }

    [Fact]
    public async Task WhatTheUserDeclinesIsSaid()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Sure("apaga el equipo", ("off", "Apagar equipo", null));
        _bridge.Sure("Apagar equipo", ("off", "Apagar equipo", null));
        _bridge.Status = "declined";
        await talk.HandleAsync(new HeardMessage("apaga el equipo", "panel"));
        Assert.Equal(Core.Localization.Loc.T("tool.run_command.replyDeclined", "Apagar equipo"), LastAnswer);
        Assert.Contains(_log, l => l.Entry is { Route: "kdesktop", Ok: false });
    }

    [Fact]
    public async Task AQuestionAboutACommandDoesNotRunIt()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Sure("¿qué hace el comando modo juego?", ("game", "Modo juego", null));
        _model.Answers.Enqueue("Activa el modo juego.");
        await talk.HandleAsync(new HeardMessage("¿qué hace el comando modo juego?", "panel"));
        Assert.Empty(_bridge.Done);
        Assert.Single(_model.Requests);
    }

    [Fact]
    public async Task SayingSomethingNeedsNoAi()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        await talk.SayAsync("lander", "La copia de seguridad ha terminado.");
        Assert.Equal("La copia de seguridad ha terminado.", LastAnswer);
        Assert.Equal("lander", _chat[^1].Assistant);
        Assert.Empty(_model.Requests);
        // «Repite» says it again.
        await talk.HandleAsync(new HeardMessage("repite", "panel", "lander"));
        Assert.Equal("La copia de seguridad ha terminado.", LastAnswer);
    }

    [Fact]
    public async Task NothingClearGoesToTheAiWithTheCommandTools()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _model.Answers.Enqueue("Hola, ¿qué tal?");
        await talk.HandleAsync(new HeardMessage("hola, buenas tardes", "panel"));
        Assert.Single(_model.Requests);
        Assert.Empty(_bridge.Done);
    }

    // What went wrong talking to the Lander on the user's PC (21/10/2026), with the scores K-Desktop gave.

    [Fact]
    public async Task AnOrderFarCloserToOneCommandRunsItThoughNoPartIsSure()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        // Parakeet's «vacía la papelera».
        _bridge.Matches["Va ser la papelera."] = new MatchedMessage("x", 0.4, [],
            [new MatchedPart("Va ser la papelera.", false, [new MatchedCandidate("bin", "Vaciar Papelera", 0.82), new MatchedCandidate("open", "Abrir Papelera", 0.70)])]);
        await talk.HandleAsync(new HeardMessage("Va ser la papelera.", "voice", "lander"));
        Assert.Equal(["run:bin:lander"], _bridge.Done);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task TwoFarCandidatesAreNotAskedAboutAndTheAiHearsOfThem()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        // It asked «¿«Bajar el volumen» o «Ventana de Incognito»?».
        _bridge.Matches["minimiza la ventana actual"] = new MatchedMessage("x", 0.4, [],
            [new MatchedPart("minimiza la ventana actual", false, [new MatchedCandidate("down", "Bajar el volumen", 0.75), new MatchedCandidate("inc", "Ventana de Incognito", 0.74)])]);
        _model.Answers.Enqueue("No sé minimizar ventanas todavía.");
        await talk.HandleAsync(new HeardMessage("minimiza la ventana actual", "panel"));
        Assert.Empty(_bridge.Done);
        var request = Assert.Single(_model.Requests);
        Assert.Contains("«Bajar el volumen»", request.Messages[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Incognito", request.Messages[0].Content, StringComparison.Ordinal);
        Assert.Contains(request.Tools, t => t.Name == "run_command");
    }

    [Fact]
    public async Task TheHistoryHasWhatRanAndSayingSoWithoutRunningIsNotBelieved()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Sure("pausa la música", (null, "Pausar", "pause"));
        await talk.HandleAsync(new HeardMessage("pausa la música", "panel"));

        // It had seen «He ejecutado «Pausar».» and answered a screenshot with «He ejecutado «Captura de pantalla».».
        _model.Answers.Enqueue("Recibido. " + Core.Localization.Loc.T("tool.run_command.reply", "Captura de pantalla"));
        await talk.HandleAsync(new HeardMessage("haz una captura de pantalla", "panel"));
        var request = Assert.Single(_model.Requests);
        var call = request.Messages.Single(m => m.ToolCalls is { Count: > 0 });
        Assert.Equal("run_command", call.ToolCalls![0].Name);
        Assert.Contains(request.Messages, m => m.Role == ChatRole.Tool && m.ToolCallId == call.ToolCalls[0].Id && m.Content.StartsWith("Done", StringComparison.Ordinal));
        Assert.DoesNotContain("Captura", LastAnswer, StringComparison.Ordinal);
        Assert.Equal(["media:pause"], _bridge.Done);
    }

    private sealed class Output(List<ChatLineMessage> chat, List<(string?, int)> listens) : ITalkOutput
    {
        public void Chat(ChatLineMessage line)
        {
            lock (chat) chat.Add(line);
        }

        public void State(string? assistant, string state) { }

        public Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Listen(string? assistant, int seconds)
        {
            lock (listens) listens.Add((assistant, seconds));
        }
    }

    /// <summary>K-Desktop for the tests: scripted matches, and every run written down.</summary>
    private sealed class FakeBridge : ICommandBridge
    {
        public Dictionary<string, MatchedMessage> Matches { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Done { get; } = [];

        public string Status { get; set; } = "ok";

        /// <summary>A text that clearly names these, one part each.</summary>
        public void Sure(string text, params (string? Command, string Name, string? Media)[] parts) =>
            Matches[text] = new MatchedMessage("x", 0.95, [], [.. parts.Select(p => new MatchedPart(p.Name, true, [new MatchedCandidate(p.Command, p.Name, 0.95, p.Media)]))]);

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult<MatchedMessage?>(Matches.GetValueOrDefault(text) ?? new MatchedMessage("x", 0, [], [new MatchedPart(text, false, [])]));

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken)
        {
            Done.Add($"run:{command}:{assistant}");
            return Task.FromResult<EndMessage?>(new EndMessage("r", Status, []));
        }

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken)
        {
            Done.Add($"media:{order}");
            return Task.FromResult<EndMessage?>(new EndMessage("r", Status, []));
        }

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) =>
            Task.FromResult<DescribedMessage?>(new DescribedMessage("x", false, "", [], [], "no"));

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) =>
            Task.FromResult<EndMessage?>(new EndMessage("r", Status, []));
    }

    private sealed class ScriptedModel : IChatModel
    {
        public Queue<string> Answers { get; } = new();

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var answer = Answers.TryDequeue(out var next) ? next : throw new InvalidOperationException("Nadie esperaba otra petición al modelo");
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
