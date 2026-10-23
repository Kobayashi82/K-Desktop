using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «Libera el cerebro», «descarga el modelo, que voy a jugar»: the AI model leaves the graphics card. The AI calls it while
/// it still needs the model for its answer, so the host frees it once the answer is over (an order with more to do, or
/// asked through the shared tools while nothing is being answered: at once). The fixed rule <c>freeBrain</c> does the
/// same without the AI; this is for the ways it does not know and for orders of several parts.
/// </summary>
/// <param name="free">Frees the brain of that assistant as soon as nothing is being answered; false: there is no brain.</param>
public sealed class FreeBrainTool(Func<string?, bool> free) : ITool
{
    public string Name => "free_brain";

    public string Description => "Takes the AI model (your own brain) out of the graphics card's memory, to leave the GPU free for a game or another program. "
        + "It happens right after you finish this answer; the next time the user talks to you it loads again by itself. Use it when they ask to free, "
        + "unload, take out or switch off the brain, the model, the AI, the GPU or the video memory: «descargar el modelo» said about the brain means this, "
        + "never downloading anything nor one of their commands.";

    public string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => false;

    public string Intent(JsonElement arguments) => Loc.T("tool.free_brain.intent");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(free(context.Assistant)
            ? new ToolResult("Done: the model leaves the graphics card as soon as you finish this answer.")
            {
                Done = Loc.T("tool.free_brain.done"),
                Reply = Loc.T("tool.free_brain.reply"),
                Acted = true,
            }
            : ToolResult.Fail("There is no AI model of yours to free here."));
}
