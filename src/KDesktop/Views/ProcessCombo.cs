using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Input;

namespace KDesktop.Views;

/// <summary>
/// The editable "process" combos next to the process lists: choosing a running process from the drop-down adds it
/// to the list right away; typing a name still needs Enter or the Add button.
/// </summary>
public static class ProcessCombo
{
    /// <summary>
    /// <c>v:ProcessCombo.Running="True"</c>: the drop-down lists the programs open at the moment it opens (one opened
    /// after the editor shows up too).
    /// </summary>
    public static readonly DependencyProperty RunningProperty = DependencyProperty.RegisterAttached(
        "Running", typeof(bool), typeof(ProcessCombo), new PropertyMetadata(false, OnRunningChanged));

    public static bool GetRunning(DependencyObject d) => (bool)d.GetValue(RunningProperty);

    public static void SetRunning(DependencyObject d, bool value) => d.SetValue(RunningProperty, value);

    private static void OnRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox box || e.NewValue is not true) return;
        box.DropDownOpened += (_, _) =>
        {
            // Setting the list clears an editable box's text: keep what is typed.
            var text = box.Text;
            box.ItemsSource = Lookups.RunningProcesses();
            box.Text = text;
        };
    }

    /// <summary>Fills the drop-down with the running processes that are not in <paramref name="list"/> yet.</summary>
    public static void Fill(ComboBox box, ICollection<string>? list) =>
        box.ItemsSource = Lookups.RunningProcesses().Where(p => list is null || !list.Contains(p)).ToList();

    /// <summary>On DropDownClosed: adds the process chosen in the drop-down (if any) and empties the box.</summary>
    public static void AddPicked(ComboBox box, ICollection<string>? list)
    {
        if (list is null || box.SelectedItem is not string picked) return;
        Add(picked, list);
        Clear(box);
    }

    /// <summary>Adds what was typed in the box.</summary>
    public static void AddTyped(ComboBox box, ICollection<string>? list)
    {
        if (list is null) return;
        Add(box.Text, list);
        Clear(box);
    }

    private static void Add(string text, ICollection<string> list)
    {
        var name = ProcessNames.Normalize(text);
        if (name.Length > 0 && !list.Contains(name)) list.Add(name);
    }

    private static void Clear(ComboBox box)
    {
        box.SelectedItem = null;
        box.Text = string.Empty;
    }
}
