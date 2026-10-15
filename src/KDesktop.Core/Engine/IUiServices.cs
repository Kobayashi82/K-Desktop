using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

public enum OsdKind { Info, Success, Warning }

/// <summary>Services the engine needs from the UI layer. Implementations must be thread-safe.</summary>
public interface IUiServices
{
    /// <summary>Shows a short on-screen message.</summary>
    void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null);

    /// <summary>
    /// Shows a message that stays until the returned handle is disposed.
    /// <paramref name="countdownTo"/> makes it display the remaining seconds.
    /// </summary>
    IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null);

    /// <summary>Opens a QuickMenu at the mouse position.</summary>
    void ShowQuickMenu(string menuId, ForegroundWindow previousForeground);

    /// <summary>Opens the launcher (search box) on the monitor of the mouse.</summary>
    void ShowLauncher(LauncherAction options, ForegroundWindow previousForeground);

    void ShowMainWindow();

    /// <summary>Runs <paramref name="action"/> on the UI thread (needed to modify bound collections).</summary>
    void RunOnUi(Action action);

    /// <summary>Runs exit commands and closes K-Desktop.</summary>
    void RequestExit();
}
