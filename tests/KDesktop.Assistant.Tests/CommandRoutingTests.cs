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

    /// <param name="model">«fake-27B»: a big model (<see cref="TalkService.IsCapable"/>).</param>
    private TalkService Make(BuiltinTools builtin, bool memory = false, string model = "fake:4b")
    {
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        CommandTools.AddTo(registry, _bridge);
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), m => { lock (_log) _log.Add(m); }, Path.Combine(_folder, "undo.json"));
        return new TalkService(_ => (_model, model), runner, _ => AssistantPersona.Module(), new Output(_chat, _listens), commands: _bridge,
            memory: memory ? new TalkMemory(builtin.Memory, runner, null) : null, learn: () => false);
    }

    private const string Big = "fake-27B";

    // What went wrong with Bonsai 2 27B on the user's PC (21/10/2026, 5.0.101).

    [Fact]
    public async Task ABigModelGetsAllOfAnOrderOnlyPartOfWhichIsClear()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin, model: Big);
        // «la otra de equipo» (0.82) opened «Equipo», and the rest was done wrong.
        const string said = "la papelera del reciclaje, esa ventana muévela a la derecha y la otra de equipo";
        _bridge.Matches[said] = new MatchedMessage("x", 0.6, [],
            [new MatchedPart("la papelera del reciclaje", false, [new MatchedCandidate("open", "Abrir Papelera", 0.77)]),
             new MatchedPart("esa ventana muévela a la derecha", true, [new MatchedCandidate("top", "TopMost", 0.74)]),
             new MatchedPart("la otra de equipo", true, [new MatchedCandidate("pc", "Equipo", 0.82)])]);
        _model.Answers.Enqueue("Vale.");
        await talk.HandleAsync(new HeardMessage(said, "panel", "lander"));
        Assert.Empty(_bridge.Done);
        Assert.StartsWith(said, Assert.Single(_model.Requests).Messages[^1].Content, StringComparison.Ordinal);

        // All of it clear: at once, as always.
        _bridge.Sure("abre la papelera y abre equipo", ("open", "Abrir Papelera", null), ("pc", "Equipo", null));
        await talk.HandleAsync(new HeardMessage("abre la papelera y abre equipo", "panel", "lander"));
        Assert.Equal(["run:open:lander", "run:pc:lander"], _bridge.Done);
        Assert.Single(_model.Requests);
    }

    [Fact]
    public async Task WordsThatOnlySoundLikeACommandDoNotRunIt()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin, model: Big);
        // The model asked run_command for «la vuelta» (a web the user taught it) and «Anterior» ran, three times.
        _bridge.Matches["la vuelta"] = new MatchedMessage("x", 0.9, [], [new MatchedPart("la vuelta", true, [new MatchedCandidate("prev", "Anterior", 0.87)])]);
        _bridge.Sure("Anterior", ("prev", "Anterior", null));
        _model.Answers.Enqueue("call:run_command:la vuelta");
        _model.Answers.Enqueue("call:run_command:Anterior");
        _model.Answers.Enqueue("call:run_command:Anterior");
        _model.Answers.Enqueue("Hecho.");
        await talk.HandleAsync(new HeardMessage("haz lo de siempre", "panel", "lander"));
        // Not by its words; by its exact name, once.
        Assert.Equal(["run:prev:lander"], _bridge.Done);
        Assert.Contains(_model.Requests[1].Messages, m => m.Role == ChatRole.Tool && m.Content.Contains("\"Anterior\"", StringComparison.Ordinal));
        Assert.True(CommandTools.Names("vacía la papelera", "Vaciar Papelera"));
        Assert.False(CommandTools.Names("la vuelta", "Anterior"));
    }

    [Fact]
    public async Task GoingRoundInCirclesEndsInWords()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin, model: Big);
        // «tú sí que has fallado»: run_command «chiste» eight times, and its answer was «Ejecutar el comando «chiste»».
        _model.Answers.Enqueue("call:run_command:chiste");
        _model.Answers.Enqueue("call:run_command:chiste");
        _model.Answers.Enqueue("call:run_command:chiste");
        await talk.HandleAsync(new HeardMessage("tú sí que has fallado", "panel", "lander"));
        Assert.Equal(3, _model.Requests.Count);
        // The third without tools and told so; what it wrote as a call is not run, nor said.
        Assert.Empty(_model.Requests[2].Tools);
        Assert.Equal(Core.Localization.Loc.T("assistant.prompt.answerNow"), _model.Requests[2].Messages[^1].Content);
        Assert.Empty(_bridge.Done);
        Assert.DoesNotContain("chiste", LastAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatABigModelCanDoBetterThanARuleIsNotSaidBackAsHeard()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin, model: Big);
        _bridge.ActReason = "noWindow";
        _model.Answers.Enqueue("No veo Discord abierto.");
        await talk.HandleAsync(new HeardMessage("minimiza Discord", "panel"));
        Assert.Single(_model.Requests);
        Assert.Equal("No veo Discord abierto.", LastAnswer);
        // A sentence is not a program's name («No has abierto equipo. Tenías que mover… a la derecha»).
        Assert.NotEqual(QuickRuleKind.Act, QuickRules.Match("No has abierto equipo. Tenías que mover la ventana del explorador de la unidad D al otro monitor y la de la papelera a la derecha")?.Kind);
    }

    [Fact]
    public async Task WhatRanAndWhatTheAiDidSayOneAck()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Matches["vacía la papelera y luego la otra cosa"] = new MatchedMessage("x", 0.5, ["luego la otra cosa"],
            [new MatchedPart("vacía la papelera", true, [new MatchedCandidate("bin", "Vaciar papelera", 0.97)])]);
        _bridge.Sure("Abrir Papelera", ("open", "Abrir Papelera", null));
        _model.Answers.Enqueue("call:run_command:Abrir Papelera");
        _model.Answers.Enqueue("Recibido.");
        await talk.HandleAsync(new HeardMessage("vacía la papelera y luego la otra cosa", "panel"));
        Assert.Equal(["run:bin:", "run:open:"], _bridge.Done);
        var oks = Core.Localization.Loc.T("assistant.phrase.ok").Split('|');
        Assert.Equal(1, oks.Sum(ok => LastAnswer.Split(ok).Length - 1));
    }

    [Fact]
    public async Task APhraseTheUserTaughtWinsOverACommandThatSoundsLikeIt()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin, memory: true);
        builtin.Memory.Remember("Cuando diga «la vuelta», quiere que se abra ejemplo.com", "fact", 0.9, "said");
        // What happened (21/10): «la vuelta» named «Anterior» at 0.87 and ran it.
        _bridge.Matches["vamos a hacer la vuelta"] = new MatchedMessage("x", 0.9, [], [new MatchedPart("la vuelta", true, [new MatchedCandidate("prev", "Anterior", 0.87)])]);
        _model.Answers.Enqueue("Vale.");

        await talk.HandleAsync(new HeardMessage("vamos a hacer la vuelta", "panel", "lander"));
        Assert.Empty(_bridge.Done);
        var request = Assert.Single(_model.Requests);
        Assert.Contains("la vuelta", request.Messages[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Anterior", request.Messages[0].Content, StringComparison.Ordinal);
        Assert.Contains(request.Tools, t => t.Name == "open_web");
        Assert.Empty(builtin.Memory.Triggered("dame una vuelta por el parque"));
    }

    [Fact]
    public async Task TheSameCommandNamedTwiceRunsOnce()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Matches["la vuelta, es la vuelta"] = new MatchedMessage("x", 0.9, [],
            [new MatchedPart("la vuelta", true, [new MatchedCandidate("prev", "Anterior", 0.87)]), new MatchedPart("es la vuelta", true, [new MatchedCandidate("prev", "Anterior", 0.85)])]);

        await talk.HandleAsync(new HeardMessage("la vuelta, es la vuelta", "panel", "lander"));
        Assert.Equal(["run:prev:lander"], _bridge.Done);
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
        Assert.StartsWith("cuéntame un chiste\n", _model.Requests.Single().Messages[^1].Content, StringComparison.Ordinal);
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
        _bridge.Matches["pon algo para trabajar"] = new MatchedMessage("x", 0.4, [],
            [new MatchedPart("pon algo para trabajar", false, [new MatchedCandidate("down", "Bajar el volumen", 0.75), new MatchedCandidate("inc", "Ventana de Incognito", 0.74)])]);
        _model.Answers.Enqueue("No sé minimizar ventanas todavía.");
        await talk.HandleAsync(new HeardMessage("pon algo para trabajar", "panel"));
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

    [Fact]
    public async Task NamingACommandWithoutRunningItGetsOneMoreRound()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        // What happened (21/10): «Let me try opening it with the "Abrir Papelera" command», and nothing ran.
        _bridge.Matches["te pedí abrir la papelera y no la ha abierto"] = new MatchedMessage("x", 0.4, [],
            [new MatchedPart("te pedí abrir la papelera y no la ha abierto", true, [new MatchedCandidate("open", "Abrir Papelera", 0.77), new MatchedCandidate("bin", "Vaciar Papelera", 0.71)])]);
        _bridge.Sure("Abrir Papelera", ("open", "Abrir Papelera", null));
        _model.Answers.Enqueue("Voy a intentarlo con el comando «Abrir Papelera».");
        _model.Answers.Enqueue("call:run_command:Abrir Papelera");
        _model.Answers.Enqueue("Hecho.");

        await talk.HandleAsync(new HeardMessage("te pedí abrir la papelera y no la ha abierto", "panel", "lander"));
        Assert.Equal(["run:open:lander"], _bridge.Done);
        Assert.Equal(3, _model.Requests.Count);
        Assert.Contains("Abrir Papelera", _model.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        // Nothing of the answer taken back reached the chat.
        Assert.DoesNotContain(_chat, c => c.Text.Contains("Voy a intentarlo", StringComparison.Ordinal));
    }

    // Stage 8.1: what is done to a program, by the rules and after the user's own commands.

    [Theory]
    [InlineData("minimiza Steam", "act:minimize:Steam:lander")]
    [InlineData("por favor, cierra Chrome", "act:closeWindow:Chrome:lander")]
    [InlineData("sal de Chrome", "act:quit:Chrome:lander")]
    [InlineData("cierra Chrome del todo", "act:quit:Chrome:lander")]
    [InlineData("minimiza esto", "act:minimize::lander")]
    [InlineData("pasa la ventana al otro monitor", "act:otherMonitor::lander")]
    [InlineData("silencia Spotify", "act:mute:Spotify:lander")]
    [InlineData("quit Chrome", "act:quit:Chrome:lander")]
    [InlineData("mata Fall Guys", "act:kill:Fall Guys:lander")]
    [InlineData("cierra a la fuerza Photoshop", "act:kill:Photoshop:lander")]
    [InlineData("cierra lo que se ha colgado", "act:kill::lander")]
    [InlineData("kill the program that is not responding", "act:kill::lander")]
    public async Task WhatIsDoneToAProgramNeedsNoAi(string said, string done)
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        await talk.HandleAsync(new HeardMessage(said, "panel", "lander"));
        Assert.Equal([done], _bridge.Done);
        Assert.Empty(_model.Requests);
        Assert.Contains(_log, l => l.Entry is { Route: "rule", Ok: true });
    }

    [Fact]
    public void ClosingIsAWindowAndQuittingTheProgram()
    {
        Assert.Equal("closeWindow", QuickRules.Match("cierra Steam")?.Op);
        Assert.Equal("quit", QuickRules.Match("sal de Steam")?.Op);
        Assert.Equal("Steam", QuickRules.Match("cierra Steam del todo")?.Text);
        Assert.Null(QuickRules.Match("cierra esta ventana")?.Text);
    }

    [Fact]
    public async Task TheUsersCommandsComeFirstAndTheRestIsAProgram()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.Matches["abre YouTube y minimiza Steam"] = new MatchedMessage("x", 0.6, ["minimiza Steam"],
            [new MatchedPart("abre YouTube", true, [new MatchedCandidate("yt", "YouTube", 0.95)])]);
        await talk.HandleAsync(new HeardMessage("abre YouTube y minimiza Steam", "panel", "lander"));
        Assert.Equal(["run:yt:lander", "act:minimize:Steam:lander"], _bridge.Done);
        Assert.Empty(_model.Requests);
        Assert.Contains("YouTube", LastAnswer, StringComparison.Ordinal);
        Assert.Contains(Core.Localization.Loc.T("act.minimize.reply", "Program Steam"), LastAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartNoCommandIsSureOfIsNotLost()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        // «abre Steam y minimízalo»: K-Desktop resolves «minimiza steam», which is no command of the user.
        _bridge.Matches["abre Steam y minimízalo"] = new MatchedMessage("x", 0.6, [],
            [new MatchedPart("abre Steam", true, [new MatchedCandidate("steam", "Steam", 0.95)]),
             new MatchedPart("minimiza steam", false, [new MatchedCandidate("down", "Bajar el volumen", 0.75)])]);
        await talk.HandleAsync(new HeardMessage("abre Steam y minimízalo", "panel", "lander"));
        Assert.Equal(["run:steam:lander", "act:minimize:steam:lander"], _bridge.Done);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task WhatIsNotOpenIsSaid()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _bridge.ActReason = "noWindow";
        await talk.HandleAsync(new HeardMessage("minimiza Discord", "panel"));
        Assert.Equal(Core.Localization.Loc.T("act.reason.noWindow", "Discord"), LastAnswer);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task AQuestionAboutAProgramGoesToTheAi()
    {
        using var builtin = new BuiltinTools(_folder);
        var talk = Make(builtin);
        _model.Answers.Enqueue("Sí, puedo.");
        await talk.HandleAsync(new HeardMessage("¿cierras Chrome?", "panel"));
        Assert.Empty(_bridge.Done);
        Assert.Contains(_model.Requests.Single().Tools, t => t.Name == "control_app");
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

        /// <summary>The reason an act does not happen (null: it does), and the name K-Desktop finds.</summary>
        public string? ActReason { get; set; }

        public Task<EndMessage?> ActAsync(string op, string app, int? value, string? assistant, CancellationToken cancellationToken)
        {
            Done.Add($"act:{op}:{app}:{assistant}");
            return Task.FromResult<EndMessage?>(ActReason is { } reason
                ? new EndMessage("r", "failed", [], app, reason)
                : new EndMessage("r", Status, [], app.Length > 0 ? "Program " + app : null));
        }
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
            // «call:run_command:Abrir Papelera»: the model calls a tool with that one argument.
            if (answer.StartsWith("call:", StringComparison.Ordinal) && answer.Split(':', 3) is [_, var tool, var argument])
                yield return new ChatChunk("", [new ToolCall(tool, System.Text.Json.JsonSerializer.SerializeToElement(new { command = argument }), "call_1")]);
            else yield return new ChatChunk(answer);
            yield return new ChatChunk("", Done: true);
        }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task PreloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }
}
