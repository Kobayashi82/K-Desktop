using System.Windows.Controls;
using H.NotifyIcon;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Services;

namespace KDesktop;

/// <summary>
/// «Mantener despierto» (5.0.342): while the PC is kept awake a cup shows in the tray, its own icon (also with
/// K-Desktop's hidden), with «30 minutos más» and «Dejar que se suspenda».
/// </summary>
public partial class App
{
    private static readonly TimeSpan AwakeMore = TimeSpan.FromMinutes(30);

    private TaskbarIcon? _awakeTray;

    private void UpdateAwakeTray()
    {
        if (_exiting) return;
        var awake = Engine.Awake;
        if (!awake.IsOn)
        {
            _awakeTray?.Dispose();
            _awakeTray = null;
            return;
        }
        if (_awakeTray is null)
        {
            var menu = new ContextMenu();
            var more = TrayItem(Loc.T("tray.awakeMore"), (_, _) => Engine.Awake.Extend(AwakeMore));
            menu.Items.Add(more);
            menu.Items.Add(TrayItem(Loc.T("tray.awakeStop"), (_, _) => Engine.Awake.Stop()));
            _awakeTray = new TaskbarIcon { ContextMenu = menu, NoLeftClickDelay = true, MenuActivation = H.NotifyIcon.Core.PopupActivationMode.LeftOrRightClick };
            // Its own id (another icon of the same exe), per data folder like the main one.
            _awakeTray.CustomName = "KDesktopAwake" + Core.AppPaths.InstanceSuffix;
            if (TrayIcons.Custom("kd:DrinkCoffee24:orange") is { } cup) _awakeTray.Icon = cup;
            else _awakeTray.IconSource = TrayIcons.Default();
            try { _awakeTray.ForceCreate(false); }
            catch (InvalidOperationException ex)
            {
                Core.Log.Warn($"No se pudo crear el icono de «Mantener despierto» ({ex.Message})");
            }
        }
        _awakeTray.ToolTipText = $"{Loc.T("action.KeepAwake.name")}\n{CommandEngine.DescribeAwake(awake)}";
    }
}
