using System.Globalization;
using System.Security.Cryptography;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>
/// An order for the assistant from Telegram being answered (phase 4): its origin (what goes with it to the assistants'
/// process) and what K-Desktop applies itself to what it runs: the user's commands that person may run (by id; null: all)
/// and whether anything may be done at all (commands made for the occasion, events).
/// </summary>
public sealed record TelegramTurn(HeardOrigin Origin, TelegramIncoming Message, IReadOnlySet<string>? CommandIds, bool MayAct)
{
    public bool Owner => Origin.Owner;

    /// <summary>Whether that person may run this command of the user's.</summary>
    public bool Allows(Command command) => CommandIds is null || CommandIds.Contains(command.Id);
}

/// <summary>
/// From a message sorted by <see cref="TelegramInbox"/> to the order for the assistant (telegram, phase 4.1–4.3): the level
/// of who writes becomes what tools are offered and which commands may run; a group, a forwarded message or someone else's
/// file make it untrusted (what acts asks first). Pure, for the tests.
/// </summary>
public static class TelegramTalk
{
    /// <summary>How long a question in a Telegram turn waits: the PC first (30 s), then Telegram.</summary>
    public static int AskSeconds(TimeSpan confirmFor) => 30 + (int)confirmFor.TotalSeconds + 15;

    /// <param name="commands">The user's commands now (for the names of the chosen ones).</param>
    public static TelegramTurn TurnFor(TelegramIncoming m, TelegramVerdict v, TelegramSettings s, IReadOnlyList<Command> commands, TimeSpan confirmFor, bool withFile)
    {
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var owner = v.Route == TelegramRoute.Owner;
        var untrusted = m.ChatKind == TelegramChatKind.Group || m.Forwarded || (withFile && !owner);
        var seconds = AskSeconds(confirmFor);
        if (owner)
            return new TelegramTurn(new HeardOrigin(key, HeardOrigin.Telegram, m.From.Name, Owner: true, Untrusted: untrusted, By: Loc.T("telegram.by.owner"), AskSeconds: seconds),
                m, null, true);
        var contact = s.Contacts.FirstOrDefault(c => c.Id == m.From.Id);
        var chosen = v.Level == TelegramLevel.ChosenTools ? contact?.Commands.ToHashSet(StringComparer.Ordinal) ?? [] : [];
        var names = commands.Where(c => c.Enabled && chosen.Contains(c.Id)).Select(c => c.Name).ToList();
        var tools = v.Level == TelegramLevel.ChosenTools ? [.. contact?.Tools ?? [], .. names.Count > 0 ? ["run_command"] : Array.Empty<string>()] : (List<string>?)null;
        var (scope, ids, mayAct) = v.Level switch
        {
            TelegramLevel.LikeOwner => (HeardOrigin.ScopeAll, (IReadOnlySet<string>?)null, true),
            TelegramLevel.ChosenTools => (HeardOrigin.ScopeChosen, chosen, true),
            TelegramLevel.ReadOnlyTools => (HeardOrigin.ScopeRead, new HashSet<string>(), false),
            _ => (HeardOrigin.ScopeNone, new HashSet<string>(), false),
        };
        var origin = new HeardOrigin(key, HeardOrigin.Telegram, m.From.Name, false, scope, tools, scope == HeardOrigin.ScopeChosen ? names : null, untrusted,
            "telegram:" + m.From.Id.ToString(CultureInfo.InvariantCulture), Loc.T("telegram.by.other", m.From.Shown), seconds);
        return new TelegramTurn(origin, m, ids, mayAct);
    }

    /// <summary>Who answers someone: their own choice, else the one for everybody (null: the one on the screen).</summary>
    public static string? AssistantFor(TelegramSettings s, long from) =>
        s.Contacts.FirstOrDefault(c => c.Id == from)?.Assistant is { Length: > 0 } own ? own : s.Assistant is { Length: > 0 } all ? all : null;

    /// <summary>What it did, as lines under the answer («✓ Abrió Spotify», «✗ …»); empty when nothing.</summary>
    public static string DidLines(IEnumerable<(string Did, bool Ok)> did) =>
        string.Join("\n", did.Where(d => d.Did.Trim().Length > 0).Select(d => (d.Ok ? "✓ " : "✗ ") + d.Did.Trim()).Distinct(StringComparer.Ordinal).Take(10));

    /// <summary>The question for the owner about something asked by Telegram: as it is for their own order, with who asks for someone else's.</summary>
    public static string Question(TelegramTurn turn, string assistant, string text) =>
        turn.Owner ? $"{assistant}: {text}" : Loc.T("telegram.talk.ask", turn.Message.From.Shown, assistant, text);
}
