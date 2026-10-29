using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Views.Assistant;

namespace KDesktop;

/// <summary>
/// The chat window (D3, 5.0.371): «Asistente: chat» opens, closes or toggles it; the reading sheets' «Seguir en el chat»
/// opens it with their assistant. One at a time. UI thread.
/// </summary>
public partial class App
{
    private AssistantChatWindow? _chatWindow;

    /// <summary>«Asistente: chat»: with <paramref name="id"/> (empty: the last one it had).</summary>
    private void ChatWindowFromCommand(string id, AssistantChatMode mode)
    {
        switch (mode)
        {
            case AssistantChatMode.Close:
                _chatWindow?.Close();
                return;
            case AssistantChatMode.Toggle when _chatWindow is not null:
                _chatWindow.Close();
                return;
        }
        if (_assistant is null)
        {
            Ui.ShowOsd(Loc.T("assistantExtra.moduleOff"), Core.Engine.OsdKind.Warning);
            return;
        }
        ShowChatWindow(id.Length > 0 ? id : null);
    }

    /// <summary>The chat window in front, talking to <paramref name="id"/> (null: the one it has, or the last one it had).</summary>
    internal void ShowChatWindow(string? id = null)
    {
        if (_exiting) return;
        id ??= _chatWindow?.Assistant ?? Config.Settings.AssistantChatWindowAssistant;
        if (id is null || !IsAssistant(id)) id = PickAssistant() ?? AssistantTargeting.KDesktop;
        if (_chatWindow is null)
        {
            var window = new AssistantChatWindow(id);
            window.Closed += (_, _) =>
            {
                if (_chatWindow == window) _chatWindow = null;
            };
            _chatWindow = window;
            Log.Info($"Asistente: ventana de chat con «{id}»");
            window.Show();
        }
        else if (_chatWindow.Assistant != id) _chatWindow.Talk(id);
        if (_chatWindow.WindowState == System.Windows.WindowState.Minimized) _chatWindow.WindowState = System.Windows.WindowState.Normal;
        _chatWindow.Activate();
        WindowTools.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_chatWindow).Handle);
    }
}
