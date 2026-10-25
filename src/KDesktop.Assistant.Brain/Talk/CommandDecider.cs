using System.Text;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>What the model chose for an order nothing was clear about.</summary>
public enum CommandChoice { None, Run, Ask }

/// <param name="First">The command to run (<see cref="CommandChoice.Run"/>) or the first of the two to ask about.</param>
public sealed record CommandDecision(CommandChoice Choice, MatchedCandidate? First = null, MatchedCandidate? Second = null, double Probability = 0);

/// <summary>
/// The user's commands when the words and the meaning were not clear (5.0.200): the assistant's own model, already
/// loaded for the AI that would answer next, picks one of the closest by meaning (<see cref="MatchedMessage.Near"/>) with a
/// single token: «A»… or «Z» for none. Measured on 184 orders with the user's 117 commands (the report and
/// its harness are on the branch decision_nvidia), with this rule the assistant got right without the AI 123 of them instead of 98 (Bonsai
/// 27B: 122), with the same wrong runs as before and none turning the PC off, in some 0.1 s (Bonsai: 0.3 s) instead of a
/// whole turn of the AI. «None» leaves the order to the AI as before.
/// </summary>
public static class CommandDecider
{
    /// <summary>Run the model's choice from this probability (0.7 got more right but ran more wrong things than before).</summary>
    public const double Run = 0.9;

    /// <summary>Two commands that together reach <see cref="Run"/>, the second at least this likely, are asked about.</summary>
    public const double AskSecond = 0.25;

    /// <summary>Not worth asking when even the closest command is this far in meaning (small talk, questions).</summary>
    public const double Floor = 0.55;

    /// <summary>Orders longer than this are the AI's: a sentence may ask for several things.</summary>
    public const int MaxWords = 10;

    private const string Letters = "ABCDEFGHIJ";

    private const char None = 'Z';

    /// <summary>The candidates worth asking about, best first (none: do not ask).</summary>
    public static IReadOnlyList<MatchedCandidate> Options(MatchedMessage matched, string said) =>
        matched.Near is { Count: > 0 } near && near[0].Score >= Floor && said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= MaxWords
            ? [.. near.Take(Letters.Length)]
            : [];

    /// <summary>The question: the order, the commands by name with a letter each, and «Z» for none of them.</summary>
    public static IReadOnlyList<ChatMessage> Prompt(string said, IReadOnlyList<MatchedCandidate> options)
    {
        var list = new StringBuilder();
        for (var i = 0; i < options.Count; i++) list.Append(Letters[i]).Append(") ").Append(options[i].Name).Append('\n');
        list.Append(None).Append(") None of these: something else (a question, another device, small talk) or no command fits\n");
        return
        [
            ChatMessage.System("You route orders said to K-Desktop, the user's PC launcher, to one of their commands. Answer with one letter only."),
            ChatMessage.User($"Order: «{said}»\n\nWhich of the user's K-Desktop commands does this order ask to run? Pick «{None}» when it asks for " +
                             $"something else: a question, a device that is not this PC (TV, console, lights…), small talk, or when no command fits.\n\n" +
                             $"{list}\nAnswer with the letter."),
        ];
    }

    /// <summary>The letters the answer may start with, for <paramref name="options"/>.</summary>
    public static string LettersFor(IReadOnlyList<MatchedCandidate> options) => Letters[..options.Count] + None;

    /// <summary>
    /// What to do with the model's probabilities: run its choice from <see cref="Run"/>; ask about the two best commands
    /// when together they reach it; otherwise nothing (the AI). What turns off the PC is never run from here: it needs
    /// the meaning's own bar (<see cref="SemanticMatcher.AcceptPower"/>), as by voice.
    /// </summary>
    public static CommandDecision Decide(IReadOnlyDictionary<char, double> probabilities, IReadOnlyList<MatchedCandidate> options)
    {
        var ranked = probabilities.Where(p => p.Key == None || Letters.IndexOf(p.Key, StringComparison.Ordinal) is var i && i >= 0 && i < options.Count)
            .OrderByDescending(p => p.Value).ToList();
        if (ranked.Count == 0 || ranked[0].Key == None) return new CommandDecision(CommandChoice.None, Probability: ranked.FirstOrDefault().Value);
        var first = options[Letters.IndexOf(ranked[0].Key, StringComparison.Ordinal)];
        var p = ranked[0].Value;
        if (first.Power && first.Score < SemanticMatcher.AcceptPower) return new CommandDecision(CommandChoice.None, first, Probability: p);
        if (p >= Run) return new CommandDecision(CommandChoice.Run, first, Probability: p);
        if (ranked.Count > 1 && ranked[1].Key != None && ranked[1].Value >= AskSecond && p + ranked[1].Value >= Run)
        {
            var second = options[Letters.IndexOf(ranked[1].Key, StringComparison.Ordinal)];
            if (!(second.Power && second.Score < SemanticMatcher.AcceptPower)) return new CommandDecision(CommandChoice.Ask, first, second, p);
        }
        return new CommandDecision(CommandChoice.None, first, Probability: p);
    }
}
