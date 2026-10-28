using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Views;

/// <summary>
/// «Apagar pantallas» of a «Pantalla» action (its DataContext): a box per monitor that is on; the ticked ones go into
/// <see cref="DisplayAction.OffMonitors"/>. None ticked = all of them. A chosen monitor that is not on stays listed.
/// </summary>
public sealed class MonitorChecks : WrapPanel
{
    public MonitorChecks() => DataContextChanged += (_, _) => Fill();

    private void Fill()
    {
        Children.Clear();
        if (DataContext is not DisplayAction action) return;
        var monitors = DisplayTools.Monitors().Select(m => (m.DeviceName, m.Display)).ToList();
        foreach (var gone in action.OffMonitors.Where(d => monitors.All(m => m.DeviceName != d)).ToList())
            monitors.Add((gone, gone.Replace(@"\\.\DISPLAY", "", StringComparison.OrdinalIgnoreCase) + ". ?"));
        foreach (var (device, name) in monitors)
        {
            var box = new CheckBox { Content = name, IsChecked = action.OffMonitors.Contains(device), Margin = new Thickness(0, 0, 24, 6) };
            box.Checked += (_, _) =>
            {
                if (!action.OffMonitors.Contains(device)) action.OffMonitors.Add(device);
            };
            box.Unchecked += (_, _) => action.OffMonitors.Remove(device);
            Children.Add(box);
        }
    }
}
