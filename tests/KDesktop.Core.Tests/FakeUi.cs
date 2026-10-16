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
    public void ShowLauncher(LauncherAction options, ForegroundWindow previousForeground) { }
    public void ShowMainWindow() { }
    public void RunOnUi(Action action) => action();
    public void RequestExit() { }

    private sealed class Nothing : IDisposable { public void Dispose() { } }
}
