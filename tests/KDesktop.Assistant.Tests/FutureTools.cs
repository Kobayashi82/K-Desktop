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
        new FakeTool("transform_text", "Translates, corrects, summarises or rewrites the selected or copied text.",
            "tradúceme esto al inglés|corrige la ortografía de este texto|resúmelo|ponlo más formal|hazlo una lista|translate this into English|fix the spelling of this text|summarise it|make it more formal"),
        new FakeTool("notify_when", "Tells the user when something happens on the PC: a download ends, a program closes, the CPU goes down, a window appears.",
            "avísame cuando termine la descarga|dime cuando se cierre Blender|avísame cuando baje la CPU|avísame cuando aparezca una ventana de Teams|tell me when the download finishes|let me know when Blender closes|tell me when the CPU goes down"),
        new FakeTool("windows", "Lists, brings, moves to another monitor, maximises, minimises or closes windows.",
            "trae el Spotify|pon el Word en el otro monitor|maximiza el navegador|minimiza todas las ventanas|cierra la ventana del explorador|qué ventanas tengo abiertas|bring Spotify to the front|move Word to the other monitor|minimise all the windows"),
        new FakeTool("pc_status", "Tells how the PC is doing: CPU, memory, graphics card, temperature, disks, battery, network and what uses most.",
            "cuánto disco me queda|qué está consumiendo tanto|cómo va la memoria|a qué temperatura está la gráfica|cuánta batería queda|how much disk space is left|what is using so much CPU|how much battery is left"),
        new FakeTool("kill_process", "Ends a program that does not respond: first politely, then by force.",
            "cierra lo que se ha colgado|mata el proceso de Chrome|fuerza el cierre de Photoshop|el juego se ha quedado congelado, ciérralo|kill the frozen program|force Photoshop to close"),
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
