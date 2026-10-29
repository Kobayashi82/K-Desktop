using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>D5 (5.0.373): transcribe_audio asks K-Desktop (a K-Desktop of the test) for what is said in an audio.</summary>
public sealed class TranscribeAudioTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>K-Desktop's side of <c>transcribe</c>: scripted answers, and every file asked for.</summary>
    private sealed class Desk : ICommandBridge
    {
        public List<string> Asked { get; } = [];

        public Func<string, TranscribedMessage> Answer { get; set; } = file => new TranscribedMessage("r", true, "Hola, esto es la reunión.", file.Length > 0 ? file : @"C:\Música\Audio 1.opus");

        public Task<TranscribedMessage?> TranscribeAsync(string file, CancellationToken cancellationToken)
        {
            Asked.Add(file);
            return Task.FromResult<TranscribedMessage?>(Answer(file));
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }

    [Fact]
    public async Task WithoutAFileItIsTheLatestRecording()
    {
        var desk = new Desk();
        var tool = new TranscribeAudioTool(desk);

        var result = await tool.RunAsync(Json("{}"), new ToolContext("dot"), default);

        Assert.Equal(string.Empty, Assert.Single(desk.Asked));
        Assert.Contains("latest recording", result.Text, StringComparison.Ordinal);
        Assert.Contains("Hola, esto es la reunión.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAudioGivenIsTheOneButADocumentGivenIsNot()
    {
        var desk = new Desk();
        var tool = new TranscribeAudioTool(desk);

        await tool.RunAsync(Json("{}"), new ToolContext("dot") { Files = [@"C:\nota de voz.m4a"] }, default);
        await tool.RunAsync(Json("{}"), new ToolContext("dot") { Files = [@"C:\factura.pdf"] }, default);

        Assert.Equal([@"C:\nota de voz.m4a", string.Empty], desk.Asked);
    }

    [Fact]
    public async Task ALongOneGivesPartsAndNothingSaidIsSaid()
    {
        var long_ = string.Join(' ', Enumerable.Range(1, 2000).Select(i => $"Frase {i} de la reunión."));
        var desk = new Desk { Answer = file => new TranscribedMessage("r", true, long_, @"C:\Música\larga.opus") };
        var tool = new TranscribeAudioTool(desk);

        var result = await tool.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.Contains("is long", result.Text, StringComparison.Ordinal);
        Assert.True(result.Text.Length < ReadFileTool.MaxText + 1000, result.Text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

        desk.Answer = file => new TranscribedMessage("r", true, File: @"C:\Música\música.mp3", Reason: "noSpeech");
        var silent = await tool.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.False(silent.Failed);
        Assert.Contains("Nobody speaks", silent.Text, StringComparison.Ordinal);

        desk.Answer = file => new TranscribedMessage("r", false, Reason: "notFound");
        var none = await tool.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.True(none.Failed);
        Assert.Contains("no recording", none.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFileSendsAnAudioToTranscribeAudio()
    {
        var folder = Directory.CreateTempSubdirectory("kd-audio-").FullName;
        try
        {
            var audio = Path.Combine(folder, "nota.mp3");
            await File.WriteAllBytesAsync(audio, [1, 2, 3]);
            var result = await new ReadFileTool().RunAsync(Json(JsonSerializer.Serialize(new { path = audio })), new ToolContext("dot"), default);
            Assert.True(result.Failed);
            Assert.Contains("transcribe_audio", result.Text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(folder, true); }
    }
}
