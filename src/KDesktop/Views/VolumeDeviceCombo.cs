using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Views;

/// <summary>A device of «Volumen»: the default one, or one of the speakers or microphones (with its name, kept).</summary>
public sealed record DeviceChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// <c>v:VolumeDeviceCombo.IsEnabled="True"</c> on the device combo of «Volumen»: lists the speakers or the microphones
/// (as the action's target says, again on opening, so one plugged in later shows up) and writes the chosen one's id and
/// name into the action. A chosen device that is not connected stays in the list.
/// </summary>
public static class VolumeDeviceCombo
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(VolumeDeviceCombo), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    /// <summary>The action the combo listens to (for its target), to stop listening when it changes.</summary>
    private static readonly DependencyProperty WatchedProperty = DependencyProperty.RegisterAttached(
        "Watched", typeof(VolumeAction), typeof(VolumeDeviceCombo));

    private static bool _filling;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox box || e.NewValue is not true) return;
        box.DisplayMemberPath = nameof(DeviceChoice.Name);
        box.Loaded += (_, _) => Watch(box);
        box.Unloaded += (_, _) => Unwatch(box);
        box.DataContextChanged += (_, _) =>
        {
            Unwatch(box);
            if (box.IsLoaded) Watch(box);
        };
        box.DropDownOpened += (_, _) => Fill(box);
        box.SelectionChanged += (_, _) =>
        {
            if (_filling || box.SelectedItem is not DeviceChoice choice || box.DataContext is not VolumeAction action || action.Device == choice.Id) return;
            action.Device = choice.Id;
            action.DeviceName = choice.Id.Length == 0 ? string.Empty : choice.Name;
        };
    }

    private static void Watch(ComboBox box)
    {
        if (box.DataContext is not VolumeAction action) return;
        box.SetValue(WatchedProperty, action);
        action.PropertyChanged += OnActionChanged;
        Fill(box);

        void OnActionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VolumeAction.Target) && box.GetValue(WatchedProperty) == action) Fill(box);
        }
        box.Unloaded += OnUnloaded;
        box.DataContextChanged += OnContextChanged;

        void OnUnloaded(object sender, RoutedEventArgs e) => Stop();
        void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Stop();
        void Stop()
        {
            action.PropertyChanged -= OnActionChanged;
            box.Unloaded -= OnUnloaded;
            box.DataContextChanged -= OnContextChanged;
        }
    }

    private static void Unwatch(ComboBox box) => box.ClearValue(WatchedProperty);

    private static void Fill(ComboBox box)
    {
        if (box.DataContext is not VolumeAction action) return;
        var devices = action.Target == VolumeTarget.Microphone ? AudioDevices.Recording() : AudioDevices.Playback();
        var list = new List<DeviceChoice> { new(string.Empty, Loc.T("editor.volumeDefaultDevice")) };
        list.AddRange(devices.Where(d => d.IsActive || d.Id == action.Device)
            .Select(d => new DeviceChoice(d.Id, d.IsActive ? d.Name : Loc.T("editor.volumeDeviceGone", d.Name))));
        if (action.Device.Length > 0 && list.All(c => c.Id != action.Device))
            list.Add(new DeviceChoice(action.Device, Loc.T("editor.volumeDeviceGone", action.DeviceName.Length > 0 ? action.DeviceName : "?")));
        _filling = true;
        try
        {
            box.ItemsSource = list;
            box.SelectedItem = list.First(c => c.Id == action.Device);
        }
        finally { _filling = false; }
    }
}
