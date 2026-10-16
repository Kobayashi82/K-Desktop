using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

/// <summary>
/// Collects what a command "says" while it runs — its on-screen messages and the warnings/errors it logs — so a
/// remote caller (the web app) can show the result. It flows with the execution through AsyncLocal: only the run
/// started inside <see cref="Begin"/> feeds it.
/// </summary>
public sealed class ExecutionFeedback
{
    private static readonly AsyncLocal<ExecutionFeedback?> Ambient = new();
    private readonly Lock _lock = new();
    private readonly List<string> _messages = [];

    public static ExecutionFeedback? Current => Ambient.Value;

    public bool HasError { get; private set; }
    public bool HasWarning { get; private set; }

    public IReadOnlyList<string> Messages
    {
        get { lock (_lock) return _messages.ToList(); }
    }

    /// <summary>Makes this collector the current one until disposed; start the command inside.</summary>
    public IDisposable Begin()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new Restore(previous);
    }

    public void Add(string text, OsdKind kind = OsdKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock)
        {
            if (kind == OsdKind.Warning) HasWarning = true;
            if (_messages.Count < 20 && !_messages.Contains(text)) _messages.Add(text);
        }
    }

    public void AddError(string text)
    {
        HasError = true;
        Add(text);
    }

    /// <summary>Log lines written during the run (on its flow) become warnings/errors of the result.</summary>
    internal static void OnLog(string level, string message)
    {
        if (Current is not { } feedback) return;
        // The first line is enough: exception dumps are long.
        var text = message.Split('\n')[0].Trim();
        if (level == "ERR") feedback.AddError(text);
        else if (level == "WRN")
        {
            feedback.HasWarning = true;
            feedback.Add(text);
        }
    }

    private sealed class Restore(ExecutionFeedback? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

/// <summary>Passes everything to the real UI and copies the messages to the current <see cref="ExecutionFeedback"/>.</summary>
internal sealed class FeedbackUiServices(IUiServices inner) : IUiServices
{
    public void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null)
    {
        ExecutionFeedback.Current?.Add(text, kind);
        inner.ShowOsd(text, kind, duration);
    }

    public IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null)
    {
        ExecutionFeedback.Current?.Add(text);
        return inner.ShowPersistentOsd(text, countdownTo);
    }

    public IVoiceOsd ShowVoiceOsd(string text) => inner.ShowVoiceOsd(text);
    public void ShowQuickMenu(string menuId, ForegroundWindow previousForeground) => inner.ShowQuickMenu(menuId, previousForeground);
    public void ShowMainWindow() => inner.ShowMainWindow();
    public void RunOnUi(Action action) => inner.RunOnUi(action);
    public void RequestExit() => inner.RequestExit();
    public bool SetClipboardText(string text) => inner.SetClipboardText(text);
}
