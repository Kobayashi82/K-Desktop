using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «Resume la última grabación», an audio dropped on it (D5, 5.0.373): what is said in an audio file, by K-Desktop's
/// Parakeet (<c>transcribe</c>; the voice lives there), for the model to summarize it or answer about it. Without a file
/// (nor an audio given): the newest recording of «Grabar audio». A long one gives the parts that answer the question, or
/// parts across it, like read_file.
/// </summary>
public sealed class TranscribeAudioTool(ICommandBridge bridge, ITextMeaning? meaning = null) : ITool
{
    public string Name => "transcribe_audio";

    public string Description => "Turns an audio file into text (a recording, a voice message, a meeting) to summarize it or answer about it. Without a path: the user's latest recording.";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{"
        + "\"path\":{\"type\":\"string\",\"description\":\"The full path of the audio file (the one the user gave you, when they gave one); empty: their latest recording\"}"
        + ",\"question\":{\"type\":\"string\",\"description\":\"What the user wants to know from it; empty to summarize it\"}}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) =>
        arguments.GetString("path") is { Length: > 0 } path ? Loc.T("tool.transcribe_audio.intent", GivenFiles.NameOf(path)) : Loc.T("tool.transcribe_audio.intentLatest");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        // A file given that is not audio (a document dropped before) is not what «the latest recording» means.
        var path = GivenFiles.Resolve(arguments, context) is { } given && (arguments.GetString("path") is { Length: > 0 } || IsAudio(given)) ? given : string.Empty;
        var done = await bridge.TranscribeAsync(path, cancellationToken).ConfigureAwait(false);
        if (done is null) return ToolResult.Fail("K-Desktop did not answer.");
        var name = done.File.Length > 0 ? GivenFiles.NameOf(done.File) : Loc.T("tool.transcribe_audio.latest");
        if (!done.Ok)
        {
            var (said, why) = done.Reason switch
            {
                "notFound" when path.Length == 0 => ("There is no recording (the folder of «Record audio» has no audio files). Say so.", Loc.T("tool.transcribe_audio.noRecording")),
                "notFound" => ($"{path} is not there. Say so.", Loc.T("tool.transcribe_audio.cannot", name)),
                "notAudio" => ($"{name} is not an audio file. For a document, use read_file.", Loc.T("tool.transcribe_audio.cannot", name)),
                "noModels" => ("The voice models are missing (Options › Voice), so audio cannot be turned into text. Say so.", Loc.T("voice.noModels")),
                _ => ($"{name} could not be read as audio. Say so.", Loc.T("tool.transcribe_audio.cannot", name)),
            };
            return new ToolResult(said) { Done = why, Reply = why, Failed = true };
        }
        var text = done.Text.Trim();
        if (text.Length == 0)
        {
            var none = Loc.T("tool.transcribe_audio.noSpeech", name);
            return new ToolResult($"Nobody speaks in {name} (music, silence…). Say so.") { Done = none, Reply = none };
        }
        Log.Info($"Asistente: {name} transcrito ({text.Length} letras)");
        var which = path.Length == 0 ? $"The user's latest recording, {name} ({done.File})" : name;
        if (text.Length <= ReadFileTool.MaxText)
            return new ToolResult($"What is said in {which}, transcribed:\n---\n{text}\n---\nAnswer the user from this, in their language.") { Done = Loc.T("tool.transcribe_audio.done", name) };

        // Too long for the context: as read_file does with a long document.
        var question = arguments.GetString("question")?.Trim() ?? string.Empty;
        var chunks = LongText.Chunks(new FileContent(text, [0]));
        var parts = question.Length > 0
            ? await LongText.BestAsync(chunks, question, meaning, done.File + "|" + text.Length, ReadFileTool.MaxText, cancellationToken).ConfigureAwait(false)
            : LongText.Spread(chunks, ReadFileTool.MaxText);
        var about = question.Length > 0
            ? $"Only the parts that matter for «{question}» are here"
            : "Only parts spread across it are here (for a summary); to look for something in it, call transcribe_audio again with a question";
        return new ToolResult($"What is said in {which} is long ({text.Length} characters). {about}:\n---\n{LongText.Describe(parts, false)}\n---\nAnswer the user from this, in their language.")
        {
            Done = Loc.T("tool.transcribe_audio.done", name),
        };
    }

    private static bool IsAudio(string path) => AudioTranscriber.Extensions.Contains(Path.GetExtension(path));
}
