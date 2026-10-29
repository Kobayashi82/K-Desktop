using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>Records what the engine would show; runs "UI" work inline.</summary>
internal sealed class FakeUi : IUiServices
{
    public List<(string Text, OsdKind Kind)> Osd { get; } = [];

    public void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null, OsdLook? look = null)
    {
        lock (Osd) Osd.Add((text, kind));
    }

    public IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null, string? icon = null)
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

    public void RunAssistantExtra(string assistant, string extra, bool bringOut)
    {
        lock (Extras) Extras.Add((assistant, extra));
    }

    /// <summary>The orders «Asistente: preguntar» gave (assistant, text).</summary>
    public List<(string Assistant, string Text)> Asked { get; } = [];

    public void AskAssistant(string assistant, string text, bool bringOut = false)
    {
        lock (Asked) Asked.Add((assistant, text));
    }

    /// <summary>What «Asistente: preguntar» with an answer gets back (assistant, text → null: not done).</summary>
    public Func<string, string, Assistant.Pipe.RepliedMessage?> Reply { get; set; } = (_, _) => null;

    public Task<Assistant.Pipe.RepliedMessage?> AskAssistantForAsync(string assistant, string text, bool bringOut, CancellationToken token)
    {
        lock (Asked) Asked.Add((assistant, text));
        return Task.FromResult(Reply(assistant, text));
    }

    /// <summary>The tools «Usar una herramienta» used (tool, arguments, run by itself).</summary>
    public List<(string Tool, string Arguments, bool Unattended)> Tools { get; } = [];

    /// <summary>What a tool gives back (tool, arguments → null: not done).</summary>
    public Func<string, string, Assistant.Pipe.ToolTestMessage?> ToolAnswer { get; set; } = (_, _) => null;

    public Task<Assistant.Pipe.ToolTestMessage?> UseAssistantToolAsync(string tool, string arguments, string command, bool unattended, CancellationToken token)
    {
        lock (Tools) Tools.Add((tool, arguments, unattended));
        return Task.FromResult(ToolAnswer(tool, arguments));
    }

    /// <summary>What was put in the clipboard (nothing touches the real one).</summary>
    public List<string> Copied { get; } = [];

    public bool SetClipboardText(string text)
    {
        lock (Copied) Copied.Add(text);
        return true;
    }

    /// <summary>The answers shown on a reading sheet (assistant, text).</summary>
    public List<(string Assistant, string Text)> Sheets { get; } = [];

    public void ShowAnswerSheet(string assistant, string markdown)
    {
        lock (Sheets) Sheets.Add((assistant, markdown));
    }

    /// <summary>What «Asistente: decir» said (assistant, text).</summary>
    public List<(string Assistant, string Text)> Said { get; } = [];

    public void AssistantSay(string assistant, string text, bool catchEye, bool bringOut)
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
