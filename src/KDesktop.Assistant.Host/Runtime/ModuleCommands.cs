using System.Collections.Concurrent;
using System.Globalization;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Sharing;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The user's K-Desktop commands, from the assistant process (phase 4): which commands an order names (<c>match</c> →
/// <c>matched</c>) and running one or a built-in media order (<c>run</c> / <c>media</c> → <c>end</c>). Never waits
/// forever: no answer in time is no answer.
/// </summary>
internal sealed class ModuleCommands(KDesktopLink link) : ICommandBridge
{
    /// <summary>Matching is quick (the meaning model may have to load, ~1 s).</summary>
    private static readonly TimeSpan MatchWait = TimeSpan.FromSeconds(5);

    /// <summary>A command may count down or wait for a window; past this the assistant stops waiting (it keeps running).</summary>
    private static readonly TimeSpan RunWait = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<MatchedMessage?>> _matches = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<EndMessage?>> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DescribedMessage?>> _descriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TextDoneMessage?>> _texts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PhraseDoneMessage?>> _phrases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ScheduleDoneMessage?>> _schedules = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<LogFoundMessage?>> _logs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<UsageDoneMessage?>> _usages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DiagnosedMessage?>> _diagnoses = new(StringComparer.Ordinal);
    private int _next;

    public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) =>
        AskAsync(_matches, id => new MatchMessage(id, text), MatchWait, cancellationToken);

    /// <param name="assistant">Who asks (empty: the module without a body), for K-Desktop's history.</param>
    public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken)
    {
        // One on the desktop shows each step (stage 6.3); without a body there is nothing to show.
        var gestures = assistant is { Length: > 0 } who && HasBody?.Invoke(who) == true && Choreography is not null;
        return AskAsync(_runs, id =>
        {
            if (gestures) Choreography!.Begin(id, assistant!, cancellationToken);
            return new RunMessage(assistant ?? string.Empty, id, command, gestures, ScheduledOrder.Current?.Key, McpShare.Caller);
        }, RunWait, cancellationToken);
    }

    public Task<EndMessage?> ActAsync(string op, string app, int? value, string? assistant, CancellationToken cancellationToken)
    {
        var gestures = assistant is { Length: > 0 } who && HasBody?.Invoke(who) == true && Choreography is not null;
        return AskAsync(_runs, id =>
        {
            if (gestures) Choreography!.Begin(id, assistant!, cancellationToken);
            return new ActMessage(assistant ?? string.Empty, id, op, app, value, gestures, ScheduledOrder.Current?.Key, McpShare.Caller);
        }, RunWait, cancellationToken);
    }

    public Task<EndMessage?> ArrangeAsync(IReadOnlyList<string> apps, string? layout, int? monitor, string? assistant, CancellationToken cancellationToken)
    {
        var gestures = assistant is { Length: > 0 } who && HasBody?.Invoke(who) == true && Choreography is not null;
        return AskAsync(_runs, id =>
        {
            if (gestures) Choreography!.Begin(id, assistant!, cancellationToken);
            return new ActMessage(assistant ?? string.Empty, id, AssistantActs.Arrange, string.Empty, monitor, gestures, ScheduledOrder.Current?.Key, McpShare.Caller, apps, layout);
        }, RunWait, cancellationToken);
    }

    /// <summary>The steps of the runs with gestures (stage 6.3).</summary>
    public ModuleChoreography? Choreography { get; set; }

    /// <summary>Whether an assistant is on the desktop (any thread).</summary>
    public Func<string, bool>? HasBody { get; set; }

    public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) =>
        AskAsync(_runs, id => new MediaMessage(order, value, id), RunWait, cancellationToken);

    public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) =>
        AskAsync(_descriptions, id => new DescribeMessage(id, command), MatchWait, cancellationToken);

    public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) =>
        AskAsync(_runs, id => new RunEventMessage(assistant ?? string.Empty, id, name, ScheduledOrder.Current?.Key, McpShare.Caller), RunWait, cancellationToken);

    /// <summary>Programs, lists or removes an order for later (schedule, list_scheduled, cancel_scheduled).</summary>
    public Task<ScheduleDoneMessage?> ScheduleAsync(ScheduleMessage request, CancellationToken cancellationToken) =>
        AskAsync(_schedules, id => request with { Id = id }, MatchWait, cancellationToken);

    public void OnScheduleDone(ScheduleDoneMessage done)
    {
        if (_schedules.TryRemove(done.Id, out var waiting)) waiting.TrySetResult(done);
    }

    /// <summary>The «Registro», the statistics and the checks of a command or event (B3): read only, quick.</summary>
    public Task<LogFoundMessage?> QueryLogAsync(LogQueryMessage request, CancellationToken cancellationToken) =>
        AskAsync(_logs, id => request with { Id = id }, MatchWait, cancellationToken);

    public Task<UsageDoneMessage?> UsageAsync(int days, CancellationToken cancellationToken) =>
        AskAsync(_usages, id => new UsageMessage(id, days), MatchWait, cancellationToken);

    public Task<DiagnosedMessage?> DiagnoseAsync(string what, string? kind, CancellationToken cancellationToken) =>
        AskAsync(_diagnoses, id => new DiagnoseMessage(id, what, kind), MatchWait, cancellationToken);

    public void OnLogFound(LogFoundMessage found)
    {
        if (_logs.TryRemove(found.Id, out var waiting)) waiting.TrySetResult(found);
    }

    public void OnUsageDone(UsageDoneMessage done)
    {
        if (_usages.TryRemove(done.Id, out var waiting)) waiting.TrySetResult(done);
    }

    public void OnDiagnosed(DiagnosedMessage diagnosed)
    {
        if (_diagnoses.TryRemove(diagnosed.Id, out var waiting)) waiting.TrySetResult(diagnosed);
    }

    /// <summary>Taking or putting a text (transform_text): Ctrl+C or Ctrl+V and the clipboard, quick.</summary>
    public Task<TextDoneMessage?> TextAsync(string op, string? assistant, string source, string text, string mode, long hwnd, CancellationToken cancellationToken) =>
        AskAsync(_texts, id => new TextMessage(assistant ?? string.Empty, id, op, source, text, mode, hwnd), MatchWait, cancellationToken);

    public void OnTextDone(TextDoneMessage done)
    {
        if (_texts.TryRemove(done.Run, out var waiting)) waiting.TrySetResult(done);
    }

    public void OnDescribed(DescribedMessage described)
    {
        if (_descriptions.TryRemove(described.Id, out var waiting)) waiting.TrySetResult(described);
    }

    /// <summary>A voice phrase taught to a command (add_voice_phrase), or taken out when undone.</summary>
    public Task<PhraseDoneMessage?> PhraseAsync(string command, string phrase, bool remove, CancellationToken cancellationToken) =>
        AskAsync(_phrases, id => new PhraseMessage(id, command, phrase, remove), MatchWait, cancellationToken);

    public void OnPhraseDone(PhraseDoneMessage done)
    {
        if (_phrases.TryRemove(done.Id, out var waiting)) waiting.TrySetResult(done);
    }

    public void OnMatched(MatchedMessage matched)
    {
        if (_matches.TryRemove(matched.Id, out var waiting)) waiting.TrySetResult(matched);
    }

    public void OnEnd(EndMessage end)
    {
        Choreography?.End(end.Run);
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
