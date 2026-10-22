using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 3.5: which tools reach the model. With twenty registered (the nine built-in and eleven to come), 3–5 and the
/// right one among them; by meaning with a fake model (words hashed into a vector, so related phrases point alike),
/// and by words without one. The real EmbeddingGemma was measured apart: 48 of 48 orders, 3.2 tools on average.
/// </summary>
public sealed class ToolChooserTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-chooser-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly BuiltinTools _builtin;
    private readonly IReadOnlyCollection<ITool> _tools;

    public ToolChooserTests()
    {
        _builtin = new BuiltinTools(_folder);
        var registry = new ToolRegistry();
        _builtin.AddTo(registry);
        foreach (var tool in FutureTools.All()) registry.Add(tool);
        _tools = registry.All;
    }

    public void Dispose()
    {
        _builtin.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("apunta que mañana viene el técnico", "save_note")]
    [InlineData("¿qué tenía que comprar?", "find_notes")]
    [InlineData("pon un temporizador de diez minutos para la pasta", "set_timer")]
    [InlineData("¿cuántos euros son 50 dólares?", "convert")]
    [InlineData("¿va a llover mañana en Madrid?", "weather")]
    [InlineData("abre la calculadora", "open_app")]
    [InlineData("busca recetas de gazpacho", "search_web")]
    [InlineData("avísame cuando termine la descarga", "notify_when")]
    [InlineData("¿cuánto disco me queda?", "pc_status")]
    public async Task TwentyToolsGiveThreeToFiveWithTheRightOne(string said, string expected)
    {
        foreach (var chooser in new[] { new ToolChooser(new FakeMeaning()), new ToolChooser(null) })
        {
            var choice = await chooser.ChooseAsync(_tools, said, [], default);
            Assert.True(_tools.Count >= 20);
            Assert.InRange(choice.Tools.Count, ToolChooser.Least, ToolChooser.Most);
            Assert.Contains(expected, choice.Tools.Select(t => t.Name));
        }
    }

    [Fact]
    public async Task ByMeaningWhenThereIsAModelAndByWordsWhenNot()
    {
        Assert.Equal("meaning", (await new ToolChooser(new FakeMeaning()).ChooseAsync(_tools, "abre youtube", [], default)).How);
        Assert.Equal("words", (await new ToolChooser(null).ChooseAsync(_tools, "abre youtube", [], default)).How);
        // K-Desktop without EmbeddingGemma (noModel), or no answer in time.
        Assert.Equal("words", (await new ToolChooser(new FakeMeaning { Missing = true }).ChooseAsync(_tools, "abre youtube", [], default)).How);
    }

    [Fact]
    public async Task FewToolsAreAllOffered()
    {
        var few = _tools.Take(ToolChooser.Most).ToList();
        var meaning = new FakeMeaning();
        var choice = await new ToolChooser(meaning).ChooseAsync(few, "hola", [], default);
        Assert.Equal("all", choice.How);
        Assert.Equal(few, choice.Tools);
        Assert.Empty(meaning.Asked);
    }

    [Fact]
    public async Task ThePhrasesAreAskedForOnceAndKept()
    {
        var meaning = new FakeMeaning();
        var chooser = new ToolChooser(meaning);
        await chooser.WarmAsync(_tools, default);
        var phrases = meaning.Asked.Where(a => a.Keep).Sum(a => a.Texts.Count);
        Assert.True(phrases > 100);
        Assert.All(meaning.Asked, a => Assert.InRange(a.Texts.Count, 1, EmbedMessage.MaxTexts));

        meaning.Asked.Clear();
        await chooser.ChooseAsync(_tools, "abre youtube", [], default);
        await chooser.ChooseAsync(_tools, "qué hora es", [], default);
        // Only the orders, without keep.
        Assert.Equal(2, meaning.Asked.Count);
        Assert.All(meaning.Asked, a => Assert.False(a.Keep));
    }

    [Fact]
    public async Task AnotherModelPreparesThePhrasesAgain()
    {
        var meaning = new FakeMeaning();
        var chooser = new ToolChooser(meaning);
        await chooser.WarmAsync(_tools, default);
        meaning.Model = "other";
        meaning.Asked.Clear();
        // The order's vector is of another model than the phrases: they are asked for again, at once.
        Assert.Equal("meaning", (await chooser.ChooseAsync(_tools, "abre youtube", [], default)).How);
        Assert.True(meaning.Asked.Where(a => a.Keep).Sum(a => a.Texts.Count) > 100);
        meaning.Asked.Clear();
        await chooser.ChooseAsync(_tools, "qué hora es", [], default);
        Assert.Single(meaning.Asked);
    }

    [Fact]
    public async Task TheToolsOfTheLastExchangeComeAgain()
    {
        var chooser = new ToolChooser(new FakeMeaning());
        var choice = await chooser.ChooseAsync(_tools, "¿y cuánto le queda?", ["set_timer", "wikipedia"], default);
        Assert.Contains("set_timer", choice.Tools.Select(t => t.Name));
        Assert.Contains("wikipedia", choice.Tools.Select(t => t.Name));
        Assert.InRange(choice.Tools.Count, ToolChooser.Least, ToolChooser.Most + 2);
        // One that no longer exists is ignored.
        choice = await chooser.ChooseAsync(_tools, "hola", ["gone_tool"], default);
        Assert.DoesNotContain(choice.Tools, t => t.Name == "gone_tool");
    }

    [Fact]
    public async Task WhatMustBeAtHandNeverPushesOutAnotherOfItsOwn()
    {
        // 22/10 (5.0.128): «¿y el mes que viene?» after the calendar. The tools used a moment before, the command tools of a
        // big model and open_web went in one by one into a full list, each pushing the last one out: list_events was lost.
        var chooser = new ToolChooser(new FakeMeaning());
        string[] pinned = ["save_note", "find_notes", "calculate", "open_web", "set_timer"];
        var choice = await chooser.ChooseAsync(_tools, "¿y el mes que viene?", pinned, default);
        var names = choice.Tools.Select(t => t.Name).ToList();
        Assert.All(pinned, p => Assert.Contains(p, names));
        Assert.True(names.Count >= ToolChooser.Least);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void TheExamplesComeInTheUsersLanguageAndInEnglish()
    {
        var timer = _tools.First(t => t.Name == "set_timer");
        Assert.Contains("pon un temporizador de diez minutos", timer.Examples);
        Assert.Contains("set a timer for ten minutes", timer.Examples);
        // Every built-in tool has its phrases.
        Assert.All(_tools.Take(9), t => Assert.True(t.Examples.Count >= 10, t.Name));
    }

    [Fact]
    public void KDesktopAnswersNoModelWithoutEmbeddingGemma()
    {
        // The test data folder has no models.
        using var embedder = new TextEmbedder();
        var meaning = new AssistantMeaning(embedder);
        var error = Assert.IsType<ErrorMessage>(meaning.Answer(new EmbedMessage("e-1", ["hola"])));
        Assert.Equal("noModel", error.Code);
        Assert.Equal("e-1", error.Id);
        var tooMany = Assert.IsType<ErrorMessage>(meaning.Answer(new EmbedMessage("e-2", [.. Enumerable.Repeat("x", EmbedMessage.MaxTexts + 1)])));
        Assert.Equal("badRequest", tooMany.Code);
    }

    /// <summary>
    /// A meaning model for tests: each word (folded, its first five letters) adds to one of 4096 places (a stable hash:
    /// the same in every run), so phrases that share words point alike, and a few everyday words add nothing.
    /// </summary>
    private sealed class FakeMeaning : IMeaning
    {
        private static readonly HashSet<string> Stop = ["que", "qu", "el", "la", "de", "en", "un", "una", "me", "the", "a", "to", "is", "what", "es", "y", "lo", "los", "las", "of", "for"];

        public List<(IReadOnlyList<string> Texts, bool Keep)> Asked { get; } = [];

        public string Model { get; set; } = "fake";

        public bool Missing { get; init; }

        public Task<MeaningVectors?> EmbedAsync(IReadOnlyList<string> texts, bool keep, CancellationToken cancellationToken)
        {
            lock (Asked) Asked.Add((texts, keep));
            return Task.FromResult(Missing ? null : new MeaningVectors(Model, [.. texts.Select(Vector)]));
        }

        private static float[] Vector(string text)
        {
            var v = new float[4096];
            foreach (var word in TextSimilarity.Words(text).Where(w => !Stop.Contains(w)))
            {
                var stem = word.Length > 5 ? word[..5] : word;
                var hash = 2166136261u;
                foreach (var c in stem) hash = (hash ^ c) * 16777619u;
                v[(int)(hash % 4095)] += 1;
            }
            v[4095] += 0.3f; // never all zeros
            var norm = (float)Math.Sqrt(v.Sum(x => x * x));
            return [.. v.Select(x => x / norm)];
        }
    }
}
