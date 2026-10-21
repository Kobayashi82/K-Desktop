using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>Records what the engine would show; runs "UI" work inline.</summary>
internal sealed class FakeUi : IUiServices
{
    public List<(string Text, OsdKind Kind)> Osd { get; } = [];

    public void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null)
    {
        lock (Osd) Osd.Add((text, kind));
    }

    public IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null)
    {
        ShowOsd(text);
        return new Nothing();
    }

    public IVoiceOsd ShowVoiceOsd(string text) => throw new NotSupportedException();
    public void ShowQuickMenu(string menuId, ForegroundWindow previousForeground) { }
    public void ShowMainWindow() { }
    public void RunOnUi(Action action) => action();
    public void RequestExit() { }

    /// <summary>The extras «Asistente: acción» asked for (assistant, extra).</summary>
    public List<(string Assistant, string Extra)> Extras { get; } = [];

    public void RunAssistantExtra(string assistant, string extra)
    {
        lock (Extras) Extras.Add((assistant, extra));
    }

    /// <summary>The orders «Asistente: preguntar» gave (assistant, text).</summary>
    public List<(string Assistant, string Text)> Asked { get; } = [];

    public void AskAssistant(string assistant, string text)
    {
        lock (Asked) Asked.Add((assistant, text));
    }

    /// <summary>What «Asistente: decir» said (assistant, text).</summary>
    public List<(string Assistant, string Text)> Said { get; } = [];

    public void AssistantSay(string assistant, string text)
    {
        lock (Said) Said.Add((assistant, text));
    }

    public List<AssistantBrainOp> Brain { get; } = [];

    public void AssistantBrain(AssistantBrainOp op)
    {
        lock (Brain) Brain.Add(op);
    }

    private sealed class Nothing : IDisposable { public void Dispose() { } }
}
