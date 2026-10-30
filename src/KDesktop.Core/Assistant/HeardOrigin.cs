namespace KDesktop.Core.Assistant;

/// <summary>
/// Where an order comes from when it is not the user at the PC (telegram, phase 4.1; generic for any channel): the
/// channel, who wrote it, whether it is the owner, and what that person may use. It goes with the order
/// (<see cref="Pipe.HeardMessage.Origin"/>) and flows in the assistants' process with the answer (<see cref="TurnOrigin"/>):
/// the tools it may not use are not offered to the model nor run, and the confirmations go back to K-Desktop with
/// <see cref="Key"/>, which asks the owner.
/// </summary>
/// <param name="Key">The turn's key: what it runs goes back with it to K-Desktop (commands, questions).</param>
/// <param name="Channel"><see cref="Telegram"/>.</param>
/// <param name="Who">The name of who wrote it (for the prompt).</param>
/// <param name="Owner">The owner (the user themself): their memory and their conversation, and their «Antes de hacer algo».</param>
/// <param name="Scope">What tools may be used: <see cref="ScopeAll"/>, <see cref="ScopeRead"/> (only what reads),
/// <see cref="ScopeChosen"/> (<paramref name="Tools"/>) or <see cref="ScopeNone"/> (only talk).</param>
/// <param name="Tools">With <see cref="ScopeChosen"/>: the names of the tools.</param>
/// <param name="Commands">The names of the user's commands it may run (null: all of them); the model is told them.</param>
/// <param name="Untrusted">The turn brings text of third parties (a group, a forwarded message, someone else's file): what
/// acts asks first, even with «no preguntar».</param>
/// <param name="Conversation">Not the owner: its own conversation (never the owner's history), by this key.</param>
/// <param name="By">«pedido por Ana vía Telegram»: in the activity, after each thing it did.</param>
/// <param name="AskSeconds">How long a question waits (the owner may answer by Telegram).</param>
public sealed record HeardOrigin(string Key, string Channel, string Who, bool Owner = false, string Scope = HeardOrigin.ScopeAll,
    IReadOnlyList<string>? Tools = null, IReadOnlyList<string>? Commands = null, bool Untrusted = false, string? Conversation = null,
    string? By = null, int AskSeconds = 0)
{
    public const string Telegram = "telegram";

    public const string ScopeAll = "all";
    public const string ScopeRead = "read";
    public const string ScopeChosen = "chosen";
    public const string ScopeNone = "none";

    /// <summary>Whether a tool may be used in this turn.</summary>
    public bool Allows(string tool, bool readsOnly) => Scope switch
    {
        ScopeAll => true,
        ScopeRead => readsOnly,
        ScopeChosen => Tools?.Contains(tool, StringComparer.Ordinal) == true,
        _ => false,
    };

    /// <summary>Not everything: the rules and the user's commands that run without the AI are skipped (they could do more).</summary>
    public bool Restricted => Scope != ScopeAll;
}

/// <summary>
/// What K-Desktop applies to what an order from another channel runs (telegram, phase 4.2): which of the user's commands
/// that person may run, and whether whatever is not safe asks the owner whatever «Antes de hacer algo» says (someone
/// who is not the owner, or text of third parties in the turn).
/// </summary>
public sealed record RemoteLimits(Func<Model.Command, bool> Allows, bool AsksUnlessSafe);

/// <summary>The order from another channel being answered now on this flow (<see cref="System.Threading.AsyncLocal{T}"/>), like <see cref="ScheduledOrder"/>.</summary>
public static class TurnOrigin
{
    private static readonly AsyncLocal<HeardOrigin?> Now = new();

    /// <summary>Null: an order of the user at the PC.</summary>
    public static HeardOrigin? Current => Now.Value;

    /// <summary>Answers an order from <paramref name="origin"/> from here on (until disposed).</summary>
    public static IDisposable Enter(HeardOrigin origin)
    {
        var previous = Now.Value;
        Now.Value = origin;
        return new Leave(previous);
    }

    private sealed class Leave(HeardOrigin? previous) : IDisposable
    {
        public void Dispose() => Now.Value = previous;
    }
}
