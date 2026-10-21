using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace KDesktop.Core.Assistant.Pipe;

/// <summary>
/// A message of the pipe between K-Desktop and the assistant process (docs/assistant/protocolo.md). On the wire:
/// one compact JSON object per line, its type in <c>t</c>, camelCase fields.
/// </summary>
public abstract record PipeMessage
{
    /// <summary>The <c>t</c> of the message.</summary>
    [JsonIgnore]
    public abstract string Type { get; }
}

/// <summary>A message this side does not know (a newer protocol): ignored, kept only for the log.</summary>
public sealed record UnknownMessage(string UnknownType, JsonObject Body) : PipeMessage
{
    public override string Type => UnknownType;
}

// ── connection ──────────────────────────────────────────────────────────

public sealed record HelloMessage(int Protocol, string Sdk, int Pid) : PipeMessage
{
    public override string Type => "hello";
}

public sealed record WelcomeMessage(string Lang, string Theme, bool Paused, JsonObject? Config = null) : PipeMessage
{
    public override string Type => "welcome";
}

/// <param name="Reason"><c>protocol</c>, <c>client</c> or <c>busy</c>.</param>
public sealed record RejectMessage(string Reason) : PipeMessage
{
    public override string Type => "reject";
}

public sealed record PingMessage : PipeMessage
{
    public override string Type => "ping";
}

public sealed record PongMessage : PipeMessage
{
    public override string Type => "pong";
}

/// <param name="Reason"><c>disable</c>, <c>exit</c> or <c>restart</c>.</param>
public sealed record ByeMessage(string Reason) : PipeMessage
{
    public override string Type => "bye";
}

public sealed record ErrorMessage(string Code, string Message, string? Id = null) : PipeMessage
{
    public override string Type => "error";
}

// ── configuration and state of K-Desktop ────────────────────────────────

/// <param name="Assistant">Whose settings, for the section <c>assistantSettings</c> (the others are the module's).</param>
public sealed record ConfigMessage(string Section, JsonNode? Value = null, string? Assistant = null) : PipeMessage
{
    public override string Type => "config";
}

public sealed record PauseMessage(bool Paused) : PipeMessage
{
    public override string Type => "pause";
}

/// <summary>Puts an assistant on the desktop, next to those already there; one that is there already (another version) is replaced.</summary>
/// <param name="Entrance"><c>startup</c> or <c>switched</c>.</param>
public sealed record LoadMessage(string Assistant, string Version, string Path, string Data, string Entrance) : PipeMessage
{
    public override string Type => "load";
}

/// <summary>Takes one assistant off the desktop (with its exit); the others stay.</summary>
/// <param name="Reason"><c>disabled</c>, <c>uninstalling</c>, <c>hiding</c> or <c>reloading</c> (it comes back at once).</param>
public sealed record UnloadMessage(string Assistant, string Reason) : PipeMessage
{
    public override string Type => "unload";
}

public sealed record LoadedMessage(string Assistant, bool Ok, string? Error = null) : PipeMessage
{
    public override string Type => "loaded";
}

/// <summary>«Assistant: action», or a button of its settings or its panel: run one of its extras.</summary>
public sealed record ExtraMessage(string Assistant, string Action) : PipeMessage
{
    public override string Type => "extra";
}

/// <summary>
/// The hotkeys of the commands that run an assistant's extras, as K-Desktop shows them (extra id → «CTRL + ALT + L»;
/// extras without one are left out). Sent after its <c>load</c> and whenever the commands change.
/// </summary>
public sealed record ExtraKeysMessage(string Assistant, IReadOnlyDictionary<string, string> Keys) : PipeMessage
{
    public override string Type => "extraKeys";
}

// ── keyboard and wheel (K-Desktop's hook, which also works over elevated windows) ──

/// <summary>
/// K-Desktop swallows these keys (WPF <c>Key</c> names: «Up», «A», «Escape»…) while pressed without Ctrl, Alt or Win,
/// and sends their changes to that assistant (the Lander's game). Empty: let them go.
/// </summary>
public sealed record KeysMessage(string Assistant, IReadOnlyList<string> Capture) : PipeMessage
{
    public override string Type => "keys";
}

/// <summary>A captured key went down or up.</summary>
public sealed record KeyMessage(string Assistant, string Key, bool Down) : PipeMessage
{
    public override string Type => "key";
}

/// <summary>Whether the process wants every turn of the mouse wheel (<see cref="WheelMessage"/>): only while an assistant asks.</summary>
public sealed record WatchWheelMessage(bool Watch) : PipeMessage
{
    public override string Type => "watchWheel";
}

/// <summary>
/// The wheel turned: where (screen pixels), how much (120 per notch; positive is up or to the right, as Windows says)
/// and over which top-level window.
/// </summary>
public sealed record WheelMessage(int X, int Y, int Delta, bool Horizontal, long Hwnd) : PipeMessage
{
    public override string Type => "wheel";
}

/// <summary>The user scrolled hard (always sent: it is rare). <paramref name="Strength"/> 0…1, positive downwards.</summary>
public sealed record FastScrollMessage(double Strength, int X, int Y) : PipeMessage
{
    public override string Type => "fastScroll";
}

// ── from the assistant to K-Desktop's panel ─────────────────────────────

/// <param name="Assistant">Whose state; null: the process's (the model, the memory).</param>
public sealed record StatusMessage(
    string State,
    string? Model = null,
    bool? ModelLoaded = null,
    bool? Busy = null,
    int? MemoryMb = null,
    string? LastError = null,
    string? Assistant = null) : PipeMessage
{
    public override string Type => "status";
}

/// <param name="Assistant">Who says it (filled in by the host).</param>
public sealed record NotifyMessage(string Title, string Text, string? Assistant = null) : PipeMessage
{
    public override string Type => "notify";
}

/// <param name="Assistant">The assistant the page is about (its panel, its settings), if any.</param>
public sealed record OpenMessage(string Page, string? Assistant = null) : PipeMessage
{
    public override string Type => "open";
}

/// <summary>
/// Data for the assistant's own panel (the binds of its panel.json), merged with what it published before (objects key
/// by key; anything else replaces). <paramref name="Assistant"/> is whose it is, filled in by the host.
/// </summary>
public sealed record PanelMessage(JsonObject Data, string? Assistant = null) : PipeMessage
{
    public override string Type => "panel";
}

/// <summary>
/// Right click on an assistant: K-Desktop shows its menu (panel, settings, its extras, hide) at that point, in screen
/// pixels, like the rest of its menus.
/// </summary>
public sealed record MenuMessage(string Assistant, int X, int Y) : PipeMessage
{
    public override string Type => "menu";
}

/// <summary>
/// Middle click on an assistant (stage 3.6): K-Desktop opens the box to write to it above that point (where its bubble
/// points), in screen pixels. What is written comes back as <c>heard</c> with source <c>bubble</c>.
/// </summary>
public sealed record WriteMessage(string Assistant, int X, int Y) : PipeMessage
{
    public override string Type => "write";
}

/// <summary>
/// An assistant shows, greys out or hides one of its extras in its menu and its panel buttons (all are shown after each
/// <c>load</c>). <paramref name="State"/>: <c>shown</c>, <c>disabled</c> or <c>hidden</c>.
/// </summary>
public sealed record ExtraStateMessage(string Assistant, string Extra, string State) : PipeMessage
{
    public override string Type => "extraState";
}

/// <summary>Right-click menu → «Hide»: K-Desktop takes that assistant off the desktop (the others stay).</summary>
public sealed record HideMessage(string Assistant) : PipeMessage
{
    public override string Type => "hide";
}

// ── the brain (Options → Assistant → Brain) ─────────────────────────────

/// <summary>
/// Options → Assistant → Brain asks the process for something: <c>check</c> (look at the server now), <c>scan</c> (look in
/// the usual places of this PC), <c>installOllama</c> (with winget, as the user), <c>startOllama</c>,
/// <c>downloadPage</c> (Ollama's, in the user's browser), <c>pull</c> (download <paramref name="Model"/>), <c>cancel</c>
/// (the download) or <c>unload</c> (free the graphics card). Answered with <see cref="BrainStateMessage"/>.
/// </summary>
public sealed record BrainMessage(string Op, string? Model = null) : PipeMessage
{
    public override string Type => "brain";
}

/// <summary>A model the server has.</summary>
/// <param name="Billions">Its size in billions of parameters (0: unknown).</param>
public sealed record BrainModel(string Name, double Billions, long SizeBytes, bool Tools);

/// <summary>A download or an installation going on, or how it ended.</summary>
/// <param name="Kind"><c>pull</c> or <c>install</c>.</param>
/// <param name="State"><c>running</c>, <c>done</c>, <c>failed</c> or <c>cancelled</c>.</param>
public sealed record BrainJob(string Kind, string State, string? Model = null, long Completed = 0, long Total = 0, string? Error = null);

/// <summary>A model to download when there is none (by the video memory).</summary>
public sealed record BrainSuggestion(string Name, double DownloadGb);

/// <summary>A server that answered in a usual place of this PC.</summary>
public sealed record BrainFound(string Provider, string Endpoint, int Models);

/// <summary>Ollama on this PC.</summary>
public sealed record BrainOllama(bool Installed, bool CanInstall);

/// <summary>
/// How the brain is (process → K-Desktop), after each change and each <see cref="BrainMessage"/>.
/// <paramref name="State"/>: <c>off</c> (no AI chosen), <c>checking</c>, <c>ready</c>, <c>unreachable</c> (no server
/// there), <c>noModels</c>, <c>modelMissing</c> (the chosen one is not there) or <c>error</c>.
/// </summary>
/// <param name="Model">The model in use: the chosen one or, with <paramref name="Automatic"/>, the one picked.</param>
/// <param name="Loaded">Models in memory now (when the server says).</param>
/// <param name="Message">What went wrong, in the user's language.</param>
public sealed record BrainStateMessage(
    string State,
    string Provider,
    string Endpoint,
    string? Model = null,
    bool Automatic = false,
    IReadOnlyList<BrainModel>? Models = null,
    IReadOnlyList<string>? Loaded = null,
    string? Message = null,
    BrainOllama? Ollama = null,
    BrainJob? Job = null,
    BrainSuggestion? Suggested = null,
    IReadOnlyList<BrainFound>? Found = null,
    double? VideoMemoryGb = null) : PipeMessage
{
    public override string Type => "brainState";
}

// ── tools: activity, undo and asking (stage 3.3) ────────────────────────

/// <summary>One line of the activity (Panel → Assistant → Activity).</summary>
/// <param name="Id">Unique in the process run, so a later line can refer to it.</param>
/// <param name="Heard">What it was asked (empty: it did it by itself, a timer that rang…).</param>
/// <param name="Route"><c>rule</c>, <c>kdesktop</c>, <c>ai</c>, <c>undo</c> or <c>timer</c>.</param>
/// <param name="Did">What it did, in words for people.</param>
/// <param name="Undo">Id to undo it with (<see cref="UndoMessage"/>), when it can be undone.</param>
/// <param name="Undid">For <c>undo</c>: the <see cref="Undo"/> id it undid.</param>
public sealed record ActivityEntry(string Id, DateTimeOffset At, string Heard, string Route, string Did, bool Ok, string? Undo = null, string? Undid = null);

/// <param name="Assistant">Who did it (null: the module without a body).</param>
public sealed record LogMessage(ActivityEntry Entry, string? Assistant = null) : PipeMessage
{
    public override string Type => "log";
}

/// <summary>Panel → Assistant → Activity → «Undo»: undo that line (answered with a <c>log</c> of route <c>undo</c>).</summary>
public sealed record UndoMessage(string Undo, string? Assistant = null) : PipeMessage
{
    public override string Type => "undo";
}

/// <summary>
/// A tool needs the user's permission (plan §8): K-Desktop asks («¿Lo hago?») and answers with <see cref="AnswerMessage"/>.
/// <paramref name="CanTrust"/>: «yes, and don't ask again» may be offered (a tool that asks only the first time).
/// </summary>
public sealed record AskMessage(string Id, string Tool, string Text, bool CanTrust, int Seconds, string? Assistant = null) : PipeMessage
{
    public override string Type => "ask";
}

/// <param name="Choice"><c>yes</c>, <c>no</c>, <c>always</c> (yes, and that tool no longer asks) or <c>timeout</c>.</param>
public sealed record AnswerMessage(string Id, string Choice) : PipeMessage
{
    public override string Type => "answer";
}

// ── talking to an assistant (stage 3.4) ─────────────────────────────────

/// <summary>
/// An order for an assistant: what the user said or wrote. <paramref name="Source"/>: <c>voice</c>, <c>panel</c> (the chat
/// of the panel), <c>bubble</c> (the box of the character), <c>phone</c> or <c>command</c> («Asistente: preguntar»).
/// </summary>
/// <param name="Assistant">Who it is for; null: the module without a body.</param>
public sealed record HeardMessage(string Text, string Source, string? Assistant = null) : PipeMessage
{
    public override string Type => "heard";
}

/// <summary>
/// K-Desktop is listening to the user for this assistant (its word was heard, its talk key or a long click on it) or no
/// longer is (stage 5.2): the character shows it. It comes before the <c>heard</c> with what was said.
/// </summary>
/// <param name="Assistant">Who is listened to; null: the module without a body.</param>
public sealed record ListeningMessage(bool Active, string? Assistant = null) : PipeMessage
{
    public override string Type => "listening";
}

/// <summary>A long click on the character starts recording the user, and letting go stops it (stage 5.2).</summary>
public sealed record TalkMessage(string Assistant, bool Active) : PipeMessage
{
    public override string Type => "talk";
}

/// <summary>«Lander, para», Escape or the panel: stop what it is doing (the answer it is thinking, the order it is carrying out).</summary>
/// <param name="Source"><c>voice</c>, <c>escape</c> or <c>panel</c>.</param>
public sealed record CancelMessage(string Source, string? Assistant = null) : PipeMessage
{
    public override string Type => "cancel";
}

/// <summary>
/// A line of the conversation, for the chat of the panel: what was heard (<c>user</c>) and what it answers
/// (<c>assistant</c>). An answer being written comes several times with the same <paramref name="Turn"/>, each with all
/// the text so far, the last one with <paramref name="Final"/>.
/// </summary>
/// <param name="Role"><c>user</c> or <c>assistant</c>.</param>
/// <param name="Assistant">Whose conversation (filled in by the host; null: the module without a body).</param>
public sealed record ChatLineMessage(string Role, string Text, string Turn, bool Final = true, string? Assistant = null) : PipeMessage
{
    public override string Type => "chat";
}

// ── meaning (stage 3.5) ─────────────────────────────────────────────────

/// <summary>
/// Vectors of meaning for some texts, from K-Desktop's EmbeddingGemma (it already has it loaded for the voice): to
/// choose the tools that fit an order. Answered with <see cref="EmbeddedMessage"/>, or an <c>error</c>
/// <c>noModel</c> with this id when it is not downloaded.
/// </summary>
/// <param name="Texts">At most <see cref="MaxTexts"/>.</param>
/// <param name="Keep">Texts asked for again and again (the example phrases of the tools): K-Desktop keeps their vectors on disk.</param>
public sealed record EmbedMessage(string Id, IReadOnlyList<string> Texts, bool? Keep = null) : PipeMessage
{
    public const int MaxTexts = 64;

    public override string Type => "embed";
}

/// <param name="Vectors">One unit-length vector per text, in the same order.</param>
/// <param name="Model">Which model made them: vectors of different models do not compare.</param>
public sealed record EmbeddedMessage(string Id, IReadOnlyList<float[]> Vectors, string Model) : PipeMessage
{
    public override string Type => "embedded";
}

// ── what the assistants know about the user (stage 3.7) ──────────────────

/// <summary>Something the assistants know about the user («Su jefe se llama Luis»).</summary>
/// <param name="Kind"><c>fact</c>, <c>like</c> or <c>event</c>.</param>
/// <param name="Importance">0…1: the important ones come to mind first and are forgotten last.</param>
/// <param name="Source"><c>said</c> (asked to remember it), <c>learned</c> (from a conversation) or <c>added</c> (Options).</param>
public sealed record MemoryEntry(string Id, string Text, string Kind, double Importance, DateTimeOffset CreatedAt, string Source,
    DateTimeOffset? LastRecalled = null, int RecallCount = 0);

/// <summary>Options → Assistant → Memory: <c>list</c>, <c>add</c> (<paramref name="Text"/>), <c>edit</c> (<paramref name="Id"/>, <paramref name="Text"/>), <c>delete</c> (<paramref name="Id"/>) or <c>clear</c>.</summary>
public sealed record MemoryMessage(string Op, string? Id = null, string? Text = null) : PipeMessage
{
    public override string Type => "memory";
}

/// <summary>All that is remembered, newest first: after every <c>memory</c> and whenever it changes.</summary>
public sealed record MemoryStateMessage(IReadOnlyList<MemoryEntry> Items) : PipeMessage
{
    public override string Type => "memoryState";
}

// ── K-Desktop's commands (phase 4) ───────────────────────────────────────

/// <summary>«Asistente: decir»: the assistant says this (bubble and chat), without the AI. Null assistant: the module without a body.</summary>
public sealed record SayMessage(string Text, string? Assistant = null) : PipeMessage
{
    public override string Type => "say";
}

/// <summary>Which commands (or built-in media orders) an order names, without running anything (<c>IntentMatcher.Plan</c>).</summary>
public sealed record MatchMessage(string Id, string Text) : PipeMessage
{
    public override string Type => "match";
}

/// <param name="Command">The command's id; null for a built-in media order.</param>
/// <param name="Media">The media order (<c>volumeDown</c>…), with its <paramref name="Value"/> when it has one (a volume).</param>
/// (Every parameter has a default: a media order has no <c>command</c>, which is left out of the line and must read back.)
public sealed record MatchedCandidate(string? Command = null, string Name = "", double Score = 0, string? Media = null, int? Value = null);

/// <param name="Sure">The first candidate is a clear winner (it would run by voice).</param>
public sealed record MatchedPart(string Text, bool Sure, IReadOnlyList<MatchedCandidate> Candidates);

/// <summary>
/// What an order names: the orders it is made of, in order, each with its candidates (best first). Nothing clear: one
/// part, the whole text, not <c>sure</c>, with what came closest.
/// </summary>
/// <param name="Unmatched">Pieces of a multiple order that named nothing.</param>
public sealed record MatchedMessage(string Id, double Score, IReadOnlyList<string> Unmatched, IReadOnlyList<MatchedPart> Parts) : PipeMessage
{
    public override string Type => "matched";
}

/// <summary>Runs a command for an assistant (without choreography until phase 6). Answered with <see cref="EndMessage"/>.</summary>
public sealed record RunMessage(string Assistant, string Run, string Command, bool Choreography = false) : PipeMessage
{
    public override string Type => "run";
}

/// <param name="Status"><c>ok</c>, <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="Messages">What the command said (its messages, warnings and errors), in the user's language.</param>
public sealed record EndMessage(string Run, string Status, IReadOnlyList<string> Messages) : PipeMessage
{
    public override string Type => "end";
}

/// <summary>What a command does, in words (the tool <c>describe_command</c>): answered with <see cref="DescribedMessage"/>.</summary>
public sealed record DescribeMessage(string Id, string Command) : PipeMessage
{
    public override string Type => "describe";
}

/// <param name="Found">The command exists.</param>
/// <param name="Actions">What each of its enabled actions does («Abrir Chrome», «Volumen al 30 %»…), in order.</param>
/// <param name="Hotkeys">Its hotkeys, as K-Desktop shows them.</param>
/// <param name="Asks">Whether an assistant has to ask before running it (<c>no</c>, <c>once</c> or <c>always</c>).</param>
public sealed record DescribedMessage(string Id, bool Found, string Name, IReadOnlyList<string> Actions, IReadOnlyList<string> Hotkeys, string Asks) : PipeMessage
{
    public override string Type => "described";
}

/// <summary>Runs the commands of one of K-Desktop's events, found by its name. Answered with <see cref="EndMessage"/>.</summary>
public sealed record RunEventMessage(string Assistant, string Run, string Event) : PipeMessage
{
    public override string Type => "runEvent";
}

/// <summary>A built-in media order of K-Desktop (out of a <c>matched</c>); with <paramref name="Run"/>, answered with <see cref="EndMessage"/>.</summary>
/// <param name="Order">The kind, camelCase (<c>volumeUp</c>, <c>pause</c>…).</param>
public sealed record MediaMessage(string Order, int? Value = null, string? Run = null) : PipeMessage
{
    public override string Type => "media";
}

// ── the test bench (stage 3.9) ───────────────────────────────────────────

/// <summary>Options → Brain → Test the model: <c>start</c> (with <paramref name="Model"/>), <c>cancel</c> or <c>state</c>.</summary>
public sealed record BenchMessage(string Op, string? Model = null) : PipeMessage
{
    public override string Type => "bench";
}

/// <summary>One order of the bench and what happened.</summary>
/// <param name="Route"><c>rule</c> or <c>ai</c>.</param>
/// <param name="Tools">The tools it used (they did not really act).</param>
/// <param name="Commands">The user's commands it ran (they did not really run), by name.</param>
public sealed record BenchCaseResult(string Say, string Expected, bool Passed, string Route, IReadOnlyList<string> Tools, string Answer, int Ms,
    IReadOnlyList<string>? Commands = null);

/// <summary>A model's last result on the bench.</summary>
public sealed record BenchScore(string Model, int Passed, int Total, int MeanMs, DateTimeOffset At, bool Own);

/// <summary>How the bench goes: after each order while it runs, and its result; with the last score of each model.</summary>
/// <param name="State"><c>idle</c>, <c>running</c>, <c>done</c>, <c>cancelled</c> or <c>failed</c>.</param>
/// <param name="Own">It is the user's bench (<c>Assistant\bench.json</c>), not the built-in one.</param>
/// <param name="Failures">The orders that did not do what was expected (when it is over).</param>
/// <param name="Model">The model it runs or ran with (empty: none yet).</param>
public sealed record BenchStateMessage(string State, string Model, int Done, int Total, int Passed, int MeanMs, bool Own,
    IReadOnlyList<BenchCaseResult> Failures, IReadOnlyList<BenchScore> History, string? Message = null) : PipeMessage
{
    public override string Type => "benchState";
}

/// <summary>Reading and writing messages as lines.</summary>
public static class PipeJson
{
    /// <summary>Longest line accepted (protocolo.md): a longer one closes the connection.</summary>
    public const int MaxLine = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        // A missing field, or null where the message says it cannot be: a bad request, not a half-filled message.
        // (Optional fields are the parameters with a default.)
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipType } },
    };

    private static readonly Dictionary<string, Type> Types = new(StringComparer.Ordinal)
    {
        ["hello"] = typeof(HelloMessage),
        ["welcome"] = typeof(WelcomeMessage),
        ["reject"] = typeof(RejectMessage),
        ["ping"] = typeof(PingMessage),
        ["pong"] = typeof(PongMessage),
        ["bye"] = typeof(ByeMessage),
        ["error"] = typeof(ErrorMessage),
        ["config"] = typeof(ConfigMessage),
        ["pause"] = typeof(PauseMessage),
        ["load"] = typeof(LoadMessage),
        ["unload"] = typeof(UnloadMessage),
        ["loaded"] = typeof(LoadedMessage),
        ["status"] = typeof(StatusMessage),
        ["notify"] = typeof(NotifyMessage),
        ["open"] = typeof(OpenMessage),
        ["panel"] = typeof(PanelMessage),
        ["hide"] = typeof(HideMessage),
        ["extra"] = typeof(ExtraMessage),
        ["extraKeys"] = typeof(ExtraKeysMessage),
        ["extraState"] = typeof(ExtraStateMessage),
        ["menu"] = typeof(MenuMessage),
        ["write"] = typeof(WriteMessage),
        ["keys"] = typeof(KeysMessage),
        ["key"] = typeof(KeyMessage),
        ["watchWheel"] = typeof(WatchWheelMessage),
        ["wheel"] = typeof(WheelMessage),
        ["fastScroll"] = typeof(FastScrollMessage),
        ["brain"] = typeof(BrainMessage),
        ["brainState"] = typeof(BrainStateMessage),
        ["log"] = typeof(LogMessage),
        ["undo"] = typeof(UndoMessage),
        ["ask"] = typeof(AskMessage),
        ["answer"] = typeof(AnswerMessage),
        ["heard"] = typeof(HeardMessage),
        ["listening"] = typeof(ListeningMessage),
        ["talk"] = typeof(TalkMessage),
        ["cancel"] = typeof(CancelMessage),
        ["chat"] = typeof(ChatLineMessage),
        ["embed"] = typeof(EmbedMessage),
        ["embedded"] = typeof(EmbeddedMessage),
        ["memory"] = typeof(MemoryMessage),
        ["memoryState"] = typeof(MemoryStateMessage),
        ["say"] = typeof(SayMessage),
        ["match"] = typeof(MatchMessage),
        ["matched"] = typeof(MatchedMessage),
        ["run"] = typeof(RunMessage),
        ["end"] = typeof(EndMessage),
        ["media"] = typeof(MediaMessage),
        ["describe"] = typeof(DescribeMessage),
        ["described"] = typeof(DescribedMessage),
        ["runEvent"] = typeof(RunEventMessage),
        ["bench"] = typeof(BenchMessage),
        ["benchState"] = typeof(BenchStateMessage),
    };

    /// <summary>[JsonIgnore] on the abstract Type is not inherited by the overrides: drop it here.</summary>
    private static void SkipType(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || !typeof(PipeMessage).IsAssignableFrom(info.Type)) return;
        for (var i = info.Properties.Count - 1; i >= 0; i--)
            if (info.Properties[i].Name == "type") info.Properties.RemoveAt(i);
    }

    /// <summary>The message types this build understands.</summary>
    public static IReadOnlyCollection<string> KnownTypes => Types.Keys;

    /// <summary>One line, without the final newline.</summary>
    public static string Serialize(PipeMessage message)
    {
        var body = message is UnknownMessage unknown
            ? (JsonObject)unknown.Body.DeepClone()
            : JsonSerializer.SerializeToNode(message, message.GetType(), Options)!.AsObject();
        var line = new JsonObject { ["t"] = message.Type };
        foreach (var (key, value) in body.ToList())
        {
            if (key == "t") continue;
            body.Remove(key);
            line[key] = value;
        }
        return line.ToJsonString(Options);
    }

    /// <summary>
    /// Parses one line. Throws <see cref="JsonException"/> when it is not a JSON object with a <c>t</c>, or when a
    /// known message has fields of the wrong type; unknown messages come back as <see cref="UnknownMessage"/>.
    /// </summary>
    public static PipeMessage Parse(string line)
    {
        var node = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("No es un objeto JSON");
        var type = node["t"] is JsonValue t && t.TryGetValue<string>(out var name) ? name : throw new JsonException("Falta «t»");
        if (!Types.TryGetValue(type, out var clr)) return new UnknownMessage(type, node);
        node.Remove("t");
        try
        {
            return (PipeMessage)(node.Deserialize(clr, Options) ?? throw new JsonException($"«{type}» vacío"));
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            throw new JsonException($"«{type}» no válido: {ex.Message}", ex);
        }
    }
}
