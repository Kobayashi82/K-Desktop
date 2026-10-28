using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Views;

/// <summary>
/// «Apagar pantallas» of a «Pantalla» action (its DataContext): a box per monitor that is on; the ticked ones go into
/// <see cref="DisplayAction.OffMonitors"/>. None or all ticked = Windows turns them all off; some = those are covered
/// in black. The text below says which of the two it will be. A chosen monitor that is not on stays listed.
/// </summary>
public sealed class MonitorChecks : StackPanel
{
    private readonly WrapPanel _boxes = new();
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<CheckBox> _checks = [];

    public MonitorChecks()
    {
        _note.SetResourceReference(StyleProperty, "Caption");
        Children.Add(_boxes);
        Children.Add(_note);
        DataContextChanged += (_, _) => Fill();
    }

    private void Fill()
    {
        _boxes.Children.Clear();
        _checks.Clear();
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
                ShowNote();
            };
            box.Unchecked += (_, _) =>
            {
                action.OffMonitors.Remove(device);
                ShowNote();
            };
            _checks.Add(box);
            _boxes.Children.Add(box);
        }
        ShowNote();
    }

    private void ShowNote() =>
        _note.Text = Loc.T(_checks.Any(c => c.IsChecked == true) && _checks.Any(c => c.IsChecked != true) ? "editor.monitorsOffHint" : "editor.monitorOffHint");
}
