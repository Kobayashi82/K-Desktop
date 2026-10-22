using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 8 (<c>transformar_texto</c>): the text taken, rewritten by the model, and put back where it belongs.</summary>
public class TransformTextTests
{
    private static Task<ToolResult> Transform(TextBridge bridge, Writer writer, string json) =>
        new TransformTextTool(bridge, writer).RunAsync(ToolArguments.Parse(json), new ToolContext("lander"), default);

    [Fact]
    public async Task TheSelectionIsReplaced()
    {
        var bridge = new TextBridge(new TextDoneMessage("r", true, "hola qe tal", "selection", 100, "Documento1 - Word"));
        var writer = new Writer("Hola, ¿qué tal?");
        var result = await Transform(bridge, writer, """{"action":"correct"}""");
        Assert.Equal("Correct its spelling, grammar and punctuation; change nothing else.", writer.Instruction);
        Assert.Equal("hola qe tal", writer.Text);
        Assert.Equal("put:paste:100:Hola, ¿qué tal?", bridge.Asked[^1]);
        Assert.Equal("Listo, ya está cambiado.", result.Reply);
        Assert.Equal("Corregir el texto en Documento1 - Word", result.Done);
        Assert.True(result.Acted);
    }

    [Fact]
    public async Task WhatWasCopiedIsLeftCopied()
    {
        var bridge = new TextBridge(new TextDoneMessage("r", true, "Good morning", "clipboard"), new TextDoneMessage("r", true, From: "copied"));
        var result = await Transform(bridge, new Writer("Buenos días"), """{"action":"translate","language":"Spanish"}""");
        Assert.Equal("put:copy:0:Buenos días", bridge.Asked[^1]);
        Assert.Equal("Te lo he dejado copiado.", result.Reply);
    }

    [Fact]
    public async Task ASummaryIsSaid()
    {
        var bridge = new TextBridge(new TextDoneMessage("r", true, "un texto muy largo", "selection", 100), new TextDoneMessage("r", true, From: "copied"));
        var result = await Transform(bridge, new Writer("Es un texto largo."), """{"action":"summarize"}""");
        Assert.Equal("put:copy:100:Es un texto largo.", bridge.Asked[^1]);
        Assert.Equal("Es un texto largo.", result.Reply);
    }

    [Theory]
    [InlineData("empty", "No tienes nada seleccionado ni copiado.")]
    [InlineData("tooLong", "Es demasiado texto de una vez.")]
    public async Task NoTextIsSaid(string reason, string reply)
    {
        var writer = new Writer("x");
        var result = await Transform(new TextBridge(new TextDoneMessage("r", false, Reason: reason)), writer, """{"action":"list"}""");
        Assert.True(result.Failed);
        Assert.Equal(reply, result.Reply);
        Assert.Null(writer.Text);
    }

    [Fact]
    public async Task WithoutAModelNothingIsTaken()
    {
        var bridge = new TextBridge();
        var result = await Transform(bridge, new Writer("x") { Able = false }, """{"action":"formal"}""");
        Assert.True(result.Failed);
        Assert.Empty(bridge.Asked);
    }

    [Theory]
    [InlineData("<think>hmm</think>\nHola, ¿qué tal?", "hola qe tal", "Hola, ¿qué tal?")]
    [InlineData("```\nHola\n```", "hola", "Hola")]
    [InlineData("\"Hola\"", "hola", "Hola")]
    [InlineData("«Hola»", "«hola»", "«Hola»")]
    [InlineData("<text>\nHola\n</text>", "hola", "Hola")]
    public void OnlyTheTextComesBack(string answer, string original, string expected) => Assert.Equal(expected, ModelWriter.Clean(answer, original));

    /// <summary>K-Desktop taking and putting texts: the answers given, in order (then «pasted» in the window).</summary>
    private sealed class TextBridge(params TextDoneMessage[] answers) : ICommandBridge
    {
        private readonly Queue<TextDoneMessage> _answers = new(answers);

        public List<string> Asked { get; } = [];

        public Task<TextDoneMessage?> TextAsync(string op, string? assistant, string source, string text, string mode, long hwnd, CancellationToken cancellationToken)
        {
            Asked.Add(op == "grab" ? "grab:" + source : $"put:{mode}:{hwnd}:{text}");
            return Task.FromResult<TextDoneMessage?>(_answers.TryDequeue(out var next) ? next : new TextDoneMessage("r", true, From: mode == "paste" ? "pasted" : "copied"));
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }

    private sealed class Writer(string result) : IWriter
    {
        public bool Able { get; init; } = true;
        public string? Instruction { get; private set; }
        public string? Text { get; private set; }

        public bool CanWrite(string? assistant) => Able;

        public Task<string?> RewriteAsync(string? assistant, string instruction, string text, CancellationToken cancellationToken)
        {
            (Instruction, Text) = (instruction, text);
            return Task.FromResult<string?>(result);
        }
    }
}
