using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>5.0.200: the one-token decision among the closest commands (docs/assistant/decision-model-pruebas.md).</summary>
public class CommandDeciderTests
{
    private static readonly MatchedCandidate[] Options =
    [
        new("a", "KeePassXC", 0.71), new("b", "Vault Open", 0.66), new("off", "Apagar Equipo", 0.62, Power: true),
    ];

    [Fact]
    public void RunsAsksOrLeavesItToTheAi()
    {
        Assert.Equal(CommandChoice.Run, CommandDecider.Decide(new Dictionary<char, double> { ['A'] = 0.92, ['Z'] = 0.08 }, Options).Choice);
        var ask = CommandDecider.Decide(new Dictionary<char, double> { ['A'] = 0.55, ['B'] = 0.37, ['Z'] = 0.08 }, Options);
        Assert.Equal((CommandChoice.Ask, "KeePassXC", "Vault Open"), (ask.Choice, ask.First!.Name, ask.Second!.Name));
        // Two that together do not reach it, or a second too unlikely: the AI.
        Assert.Equal(CommandChoice.None, CommandDecider.Decide(new Dictionary<char, double> { ['A'] = 0.4, ['B'] = 0.2, ['Z'] = 0.4 }, Options).Choice);
        Assert.Equal(CommandChoice.None, CommandDecider.Decide(new Dictionary<char, double> { ['A'] = 0.6, ['B'] = 0.15, ['Z'] = 0.25 }, Options).Choice);
        Assert.Equal(CommandChoice.None, CommandDecider.Decide(new Dictionary<char, double> { ['A'] = 0.1, ['Z'] = 0.9 }, Options).Choice);
    }

    [Fact]
    public void WhatTurnsThePcOffNeedsTheMeaningsOwnBar()
    {
        Assert.Equal(CommandChoice.None, CommandDecider.Decide(new Dictionary<char, double> { ['C'] = 0.99, ['Z'] = 0.01 }, Options).Choice);
        MatchedCandidate[] clear = [new("off", "Apagar Equipo", 0.95, Power: true)];
        Assert.Equal(CommandChoice.Run, CommandDecider.Decide(new Dictionary<char, double> { ['A'] = 0.99, ['Z'] = 0.01 }, clear).Choice);
    }

    [Fact]
    public void OnlyShortOrdersWithSomethingCloseAreAsked()
    {
        var near = new MatchedMessage("x", 0.4, [], [new MatchedPart("abre el gestor", false, [])], Options);
        Assert.Equal(3, CommandDecider.Options(near, "abre el gestor").Count);
        Assert.Empty(CommandDecider.Options(near with { Near = [new("a", "Steam", 0.5)] }, "hola"));
        Assert.Empty(CommandDecider.Options(near, "esto es una frase muy larga que habla de muchas cosas a la vez y no es una orden"));
        Assert.Empty(CommandDecider.Options(near with { Near = null }, "abre el gestor"));
        Assert.Equal("ABCZ", CommandDecider.LettersFor(Options));
    }

    /// <summary>What llama-server answers with <c>top_logprobs</c>: tokens with and without a space, other tokens left out.</summary>
    [Fact]
    public void TheLettersAreReadFromTheFirstToken()
    {
        const string json = """
            {"choices":[{"logprobs":{"content":[{"token":"A","logprob":-0.28,"top_logprobs":[
              {"token":"A","logprob":-0.28},{"token":"Z","logprob":-2.06},{"token":" B","logprob":-2.8},{"token":"**","logprob":-4},{"token":"Q","logprob":-5}]}]}}]}
            """;
        using var document = JsonDocument.Parse(json);
        var letters = OpenAICompatibleChatModel.ReadLetters(document.RootElement, "ABZ")!;
        Assert.Equal(1, letters.Values.Sum(), 6);
        Assert.True(letters['A'] > letters['Z'] && letters['Z'] > letters['B']);
        using var none = JsonDocument.Parse("""{"choices":[{"message":{"content":"A"}}]}""");
        Assert.Null(OpenAICompatibleChatModel.ReadLetters(none.RootElement, "ABZ"));
    }
}
