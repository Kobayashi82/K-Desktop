using System.Windows;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;

namespace KDesktop.Views;

/// <summary>"Elegir ventana": the next click on any window returns its process name (Esc cancels).</summary>
public static class WindowPicker
{
    public static void Pick(Action<string> onProcess)
    {
        var app = App.Current;
        if (!app.Router.IsRunning)
        {
            app.Ui.ShowOsd(Loc.T("picker.unavailable"), OsdKind.Warning);
            return;
        }
        var osd = app.Ui.ShowPersistentOsd(Loc.T("picker.prompt"));
        app.Router.PickWindow(window => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            osd.Dispose();
            if (window is null) return;
            if (window.ProcessName.Length == 0 || window.ProcessId == (uint)Environment.ProcessId)
            {
                app.Ui.ShowOsd(Loc.T("picker.invalid"), OsdKind.Warning);
                return;
            }
            onProcess(window.ProcessName);
            app.Ui.ShowOsd(window.ProcessName, OsdKind.Success);
        }));
    }
}
