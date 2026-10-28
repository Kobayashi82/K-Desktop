using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Core.Assistant;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Tools that do not exist yet but will (plan §6.3), with example phrases like the real ones: with the built-in nine
/// they make the twenty of stage 3.5's «done when».
/// </summary>
internal static class FutureTools
{
    public static IEnumerable<ITool> All() =>
    [
        new FakeTool("now_playing", "Tells which song or video is playing on the PC.",
            "qué canción es esta|qué está sonando|cómo se llama esta canción|de quién es esta canción|what song is this|what is playing right now|who sings this song"),
        new FakeTool("weather", "Gives the weather forecast for a place and a day.",
            "va a llover mañana|qué tiempo hace|qué temperatura habrá el sábado en Madrid|necesito paraguas hoy|is it going to rain tomorrow|what's the weather like|how cold will it be on Saturday"),
        new FakeTool("convert", "Converts amounts between units or currencies.",
            "cuántos euros son 50 dólares|pasa 10 millas a kilómetros|cuántos gramos son tres onzas|convierte 30 grados Fahrenheit a Celsius|how many euros is 50 dollars|convert 10 miles to kilometres|how many grams are three ounces"),
        new FakeTool("wikipedia", "Gives a short summary of a topic, a person or a place from Wikipedia.",
            "quién era Hedy Lamarr|qué es la fotosíntesis|háblame de la Alhambra|cuándo se fundó Roma|who was Hedy Lamarr|what is photosynthesis|tell me about the Alhambra"),
    ];

    private sealed class FakeTool(string name, string description, string examples) : ITool
    {
        public string Name => name;

        public string Description => description;

        public string ParametersSchemaJson => """{"type":"object","properties":{}}""";

        public IReadOnlyList<string> Examples { get; } = examples.Split('|');

        public ToolRisk Risk => ToolRisk.Safe;

        public string Intent(JsonElement arguments) => name;

        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) => Task.FromResult<ToolResult>("ok");
    }
}
