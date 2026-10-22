using System.Runtime.CompilerServices;
using System.Text.Json;
using KDesktop.Assistant.Brain.Bench;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Bench;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 3.9: the test bench reads its orders, runs them like real ones without acting, and scores them.</summary>
public sealed class BenchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-bench-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Fact]
    public void TheFormatIsReadWithItsThreeConditions()
    {
        var cases = BenchFile.Parse("""
            {
              // comments are allowed
              "cases": [
                { "say": "apunta pan", "expect": { "tool": "save_note" } },
                { "say": "abre wikipedia", "expect": { "tool": ["open_web", "search_web"] } },
                { "say": "¿qué tal?", "expect": { "noTool": true, "answerContains": ["bien", "genial"] } },
                { "say": "  " },
              ],
            }
            """);
        Assert.Equal(3, cases.Count);
        Assert.Equal(["save_note"], cases[0].Expect.Tools);
        Assert.Equal(["open_web", "search_web"], cases[1].Expect.Tools);
        Assert.True(cases[2].Expect.NoTool);
        Assert.Equal(["bien", "genial"], cases[2].Expect.AnswerContains);
        Assert.Throws<JsonException>(() => BenchFile.Parse("""{"orders":[]}"""));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("de")]
    public void TheBuiltInBenchOnlyAsksForToolsThatExist(string language)
    {
        using var builtin = new BuiltinTools(_folder);
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        CommandTools.AddTo(registry, new EndBridge());
        var cases = BenchFile.Parse(BenchFile.BuiltIn(language));
        Assert.True(cases.Count >= 30);
        Assert.All(cases.SelectMany(c => c.Expect.Tools), tool => Assert.NotNull(registry.Find(tool)));
    }

    [Fact]
    public void WhatItDidIsCheckedAgainstWhatWasExpected()
    {
        var expect = new BenchExpect(["calculate"], false, ["43,2", "43.2"]);
        Assert.True(BenchRunner.Check(expect, ["calculate"], "Son 43.2.", byRule: false));
        Assert.False(BenchRunner.Check(expect, ["calculate"], "Son 43.", byRule: false));
        Assert.False(BenchRunner.Check(expect, [], "Son 43,2.", byRule: false));
        // A rule has no answer to check.
        Assert.True(BenchRunner.Check(new BenchExpect(["get_time"], false, ["17"]), ["get_time"], "", byRule: true));
        // A voice says its numbers in words.
        Assert.True(BenchRunner.Check(new BenchExpect([], false, ["195"]), [], "Hay ciento noventa y cinco minutos.", byRule: false));
        Assert.False(BenchRunner.Check(new BenchExpect([], false, ["195"]), [], "Hay doscientos veinticinco minutos.", byRule: false));
        Assert.False(BenchRunner.Check(new BenchExpect([], true, []), ["search_web"], "", byRule: false));
    }

    [Fact]
    public async Task ItRunsLikeARealOrderWithoutActing()
    {
        using var builtin = new BuiltinTools(_folder);
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        var model = new ScriptedModel();
        // «guarda la idea…» → the AI calls save_note, then answers.
        model.Rounds.Enqueue(new ChatChunk("", [new ToolCall("save_note", ToolArguments.Parse("""{"note":"una app para regar"}"""))]));
        model.Rounds.Enqueue(new ChatChunk("Apuntado."));
        // «¿cuánto es 18 % de 240?» → calculate really runs (it only reads), and the answer says it.
        model.Rounds.Enqueue(new ChatChunk("", [new ToolCall("calculate", ToolArguments.Parse("""{"expression":"18%*240"}"""))]));
        model.Rounds.Enqueue(new ChatChunk("Son 43,2."));
        // «¿qué tal?» → it answers, with a tool it should not have used.
        model.Rounds.Enqueue(new ChatChunk("", [new ToolCall("search_web", ToolArguments.Parse("""{"query":"qué tal"}"""))]));
        model.Rounds.Enqueue(new ChatChunk("Bien."));
        BenchCase[] cases =
        [
            new("¿qué hora es?", new(["get_time"], false, [])),
            new("guárdame esta idea: una app para regar las plantas", new(["save_note"], false, [])),
            new("¿cuánto es el 18 por ciento de 240?", new(["calculate"], false, ["43,2"])),
            new("¿qué tal estás?", new([], true, [])),
        ];
        var seen = new List<string>();
        var results = await new BenchRunner(registry, new ToolChooser(null)).RunAsync(model, "fake:4b", cases, r => seen.Add(r.Say), default);

        Assert.Equal(cases.Select(c => c.Say), seen);
        Assert.Equal([true, true, true, false], results.Select(r => r.Passed));
        Assert.Equal("rule", results[0].Route);
        Assert.Equal(["save_note"], results[1].Tools);
        // The note was only pretended.
        Assert.Empty(builtin.Notes.All);
        Assert.Equal("Son 43,2.", results[2].Answer);
        Assert.Equal(["search_web"], results[3].Tools);
        // The rule did not reach the model; each AI order went with tools, then without history.
        Assert.Equal(6, model.Requests.Count);
        Assert.All(model.Requests, r => Assert.Equal(2, r.Messages.Count(m => m.Role != ChatRole.Tool && m.Role != ChatRole.Assistant)));
    }

    /// <summary>Answers each request with the next chunk of a queue, and keeps every request.</summary>
    private sealed class ScriptedModel : IChatModel
    {
        public Queue<ChatChunk> Rounds { get; } = new();

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var chunk = Rounds.TryDequeue(out var next) ? next : throw new InvalidOperationException("Nadie esperaba otra petición al modelo");
            await Task.Yield();
            yield return chunk;
            yield return new ChatChunk("", Done: true);
        }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task PreloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }
}
