using System.Collections.Concurrent;
using System.Globalization;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The user's K-Desktop commands, from the assistant process (phase 4): which commands an order names (<c>match</c> →
/// <c>matched</c>) and running one or a built-in media order (<c>run</c> / <c>media</c> → <c>end</c>). Never waits
/// forever: no answer in time is no answer.
/// </summary>
internal sealed class ModuleCommands(KDesktopLink link)
{
    /// <summary>Matching is quick (the meaning model may have to load, ~1 s).</summary>
    private static readonly TimeSpan MatchWait = TimeSpan.FromSeconds(5);

    /// <summary>A command may count down or wait for a window; past this the assistant stops waiting (it keeps running).</summary>
    private static readonly TimeSpan RunWait = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<MatchedMessage?>> _matches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<EndMessage?>> _runs = new(StringComparer.Ordinal);
    private int _next;

    public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) =>
        AskAsync(_matches, id => new MatchMessage(id, text), MatchWait, cancellationToken);

    /// <param name="assistant">Who asks (empty: the module without a body), for K-Desktop's history.</param>
    public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) =>
        AskAsync(_runs, id => new RunMessage(assistant ?? string.Empty, id, command), RunWait, cancellationToken);

    public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) =>
        AskAsync(_runs, id => new MediaMessage(order, value, id), RunWait, cancellationToken);

    public void OnMatched(MatchedMessage matched)
    {
        if (_matches.TryRemove(matched.Id, out var waiting)) waiting.TrySetResult(matched);
    }

    public void OnEnd(EndMessage end)
    {
        if (_runs.TryRemove(end.Run, out var waiting)) waiting.TrySetResult(end);
    }

    private async Task<T?> AskAsync<T>(ConcurrentDictionary<string, TaskCompletionSource<T?>> pending, Func<string, PipeMessage> make, TimeSpan wait,
        CancellationToken cancellationToken) where T : class
    {
        var id = "c-" + Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture);
        var answer = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = answer;
        try
        {
            if (!link.Send(make(id))) return null;
            using var timeout = new CancellationTokenSource(wait);
            using (timeout.Token.Register(() => answer.TrySetResult(null)))
            using (cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken)))
            {
                var result = await answer.Task.ConfigureAwait(false);
                if (result is null && timeout.IsCancellationRequested) Log.Warn($"Asistente: K-Desktop no ha contestado a tiempo ({typeof(T).Name})");
                return result;
            }
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }
}
