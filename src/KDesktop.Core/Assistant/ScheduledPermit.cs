using KDesktop.Core.Model;

namespace KDesktop.Core.Assistant;

/// <summary>
/// What an order programmed by an assistant (the tool <c>schedule</c>) may do at its time, approved when it was asked for:
/// its tools and the user's commands. At that time nobody is asked: what was approved runs, whatever is risky and was not
/// approved does not (and the summary says so), what only reads or is safe runs as usual. K-Desktop keeps it for a while
/// after the event fires, under a key the order carries (<see cref="Pipe.HeardMessage.Scheduled"/>).
/// </summary>
public sealed record ScheduledPermit(string Key, string Assistant, IReadOnlySet<string> Tools, IReadOnlySet<string> Commands, DateTime ExpiresUtc)
{
    /// <summary>How long after firing its order may still run things (a slow model, a long chain of tools).</summary>
    public static readonly TimeSpan Lasts = TimeSpan.FromMinutes(30);

    /// <summary>The tools that do things to programs (<see cref="AssistantActs"/>): with one of them approved, those may run.</summary>
    public static readonly IReadOnlyList<string> ActTools = ["control_app", "end_process", "open_app"];

    public static ScheduledPermit Of(EventAsk ask, DateTime nowUtc) => new(
        Guid.NewGuid().ToString("N")[..16], ask.Assistant,
        new HashSet<string>(ask.Tools ?? [], StringComparer.Ordinal),
        new HashSet<string>(ask.Commands ?? [], StringComparer.Ordinal),
        nowUtc + Lasts);

    public bool IsValid(DateTime nowUtc) => nowUtc < ExpiresUtc;

    /// <summary>Whether a command that would ask may run: one approved by its id.</summary>
    public bool Allows(Command command) => Commands.Contains(command.Id);

    /// <summary>Whether something done to a program that would ask may run: one of the program tools was approved.</summary>
    public bool AllowsActs => ActTools.Any(Tools.Contains);
}

/// <summary>
/// In the assistants' process, the programmed order being answered now (<see cref="Pipe.HeardMessage.Scheduled"/>):
/// its key, which goes with every command it asks K-Desktop to run, and the tools approved for it. It flows with the
/// answer (<see cref="AsyncLocal{T}"/>) into every tool it calls.
/// </summary>
public sealed record ScheduledOrder(string Key, IReadOnlySet<string> Allowed)
{
    private static readonly AsyncLocal<ScheduledOrder?> Now = new();

    /// <summary>The programmed order being answered on this flow; null: an order like any other.</summary>
    public static ScheduledOrder? Current => Now.Value;

    /// <summary>Answers a programmed order from here on (until disposed).</summary>
    public static IDisposable Enter(string key, IEnumerable<string>? allowed)
    {
        var previous = Now.Value;
        Now.Value = new ScheduledOrder(key, new HashSet<string>(allowed ?? [], StringComparer.Ordinal));
        return new Leave(previous);
    }

    private sealed class Leave(ScheduledOrder? previous) : IDisposable
    {
        public void Dispose() => Now.Value = previous;
    }
}
