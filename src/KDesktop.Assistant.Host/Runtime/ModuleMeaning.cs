using System.Collections.Concurrent;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// Vectors of meaning from K-Desktop (stage 3.5): <c>embed</c> → <c>embedded</c>, made by the EmbeddingGemma it already
/// loads for the voice. Without the model (<c>error</c> <c>noModel</c>) or without an answer in time: null, and the
/// tools are chosen by their words. Never waits long: an order must not hang on it.
/// </summary>
internal sealed class ModuleMeaning(KDesktopLink link) : IMeaning
{
    /// <summary>For an order: K-Desktop may have to load the model first (~1 s) or finish other phrases.</summary>
    private static readonly TimeSpan OrderWait = TimeSpan.FromSeconds(5);

    /// <summary>For the example phrases: the first time K-Desktop computes them all (a few seconds).</summary>
    private static readonly TimeSpan KeptWait = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (TaskCompletionSource<MeaningVectors?> Answer, int Count)> _waiting = new(StringComparer.Ordinal);
    private int _next;

    public async Task<MeaningVectors?> EmbedAsync(IReadOnlyList<string> texts, bool keep, CancellationToken cancellationToken)
    {
        if (texts.Count == 0) return null;
        var id = "e-" + Interlocked.Increment(ref _next).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var answer = new TaskCompletionSource<MeaningVectors?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = (answer, texts.Count);
        try
        {
            if (!link.Send(new EmbedMessage(id, texts, keep ? true : null))) return null;
            using var timeout = new CancellationTokenSource(keep ? KeptWait : OrderWait);
            using (timeout.Token.Register(() => answer.TrySetResult(null)))
            using (cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken)))
            {
                var result = await answer.Task.ConfigureAwait(false);
                if (result is null && timeout.IsCancellationRequested) Log.Warn($"Asistente: K-Desktop no ha dado los significados a tiempo ({texts.Count} texto(s))");
                return result;
            }
        }
        finally
        {
            _waiting.TryRemove(id, out _);
        }
    }

    public void OnEmbedded(EmbeddedMessage embedded)
    {
        if (!_waiting.TryRemove(embedded.Id, out var waiting)) return;
        var whole = embedded.Vectors.Count == waiting.Count && embedded.Vectors.All(v => v.Length > 0 && v.Length == embedded.Vectors[0].Length);
        if (!whole) Log.Warn("Asistente: K-Desktop ha mandado unos significados incompletos");
        waiting.Answer.TrySetResult(whole ? new MeaningVectors(embedded.Model, embedded.Vectors) : null);
    }

    /// <summary>An <c>error</c>: true when it answered one of these requests.</summary>
    public bool OnError(ErrorMessage error)
    {
        if (error.Id is not { } id || !_waiting.TryRemove(id, out var waiting)) return false;
        if (error.Code != "noModel") Log.Warn($"Asistente: K-Desktop no da los significados ({error.Code}: {error.Message})");
        waiting.Answer.TrySetResult(null);
        return true;
    }
}
