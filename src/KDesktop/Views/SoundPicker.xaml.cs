using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using Microsoft.Win32;

namespace KDesktop.Views;

/// <summary>
/// Editable list of the sounds (built-in names or a .wav path) with Play and Browse buttons. Picking a sound in the
/// list (mouse or arrow keys) plays it, so they can be compared quickly.
/// </summary>
public partial class SoundPicker : UserControl
{
    public static readonly DependencyProperty SoundProperty = DependencyProperty.Register(
        nameof(Sound), typeof(string), typeof(SoundPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ShowBrowseProperty = DependencyProperty.Register(
        nameof(ShowBrowse), typeof(bool), typeof(SoundPicker),
        new PropertyMetadata(true, (d, e) => ((SoundPicker)d).BrowseButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public SoundPicker()
    {
        InitializeComponent();
        Box.ItemsSource = Sounds.Available();
        // Empty text → null (no sound).
        Box.SetBinding(ComboBox.TextProperty, new Binding(nameof(Sound))
        {
            Source = this,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            TargetNullValue = string.Empty,
        });
    }

    /// <summary>Built-in sound name or path of a .wav file; null for none.</summary>
    public string? Sound
    {
        get => (string?)GetValue(SoundProperty);
        set => SetValue(SoundProperty, value);
    }

    public bool ShowBrowse
    {
        get => (bool)GetValue(ShowBrowseProperty);
        set => SetValue(ShowBrowseProperty, value);
    }

    /// <summary>Last sound previewed from the open list, so moving over the same item does not restart it.</summary>
    private string? _previewed;

    private void OnDropDownOpened(object? sender, EventArgs e) => _previewed = null;

    /// <summary>With the list open, each sound plays as the mouse goes over it.</summary>
    private void OnItemMouseEnter(object sender, MouseEventArgs e) => Preview((sender as ComboBoxItem)?.Content as string);

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only what the user picks: the binding also selects the matching item when the editor loads.
        if (!Box.IsDropDownOpen && !Box.IsKeyboardFocusWithin) return;
        if (e.AddedItems is [string sound, ..]) Preview(sound);
        DeselectText();
    }

    private void Preview(string? sound)
    {
        if (string.IsNullOrEmpty(sound) || sound == _previewed) return;
        _previewed = sound;
        Sounds.Play(sound);
    }

    private void OnDropDownClosed(object? sender, EventArgs e)
    {
        _previewed = null;
        DeselectText();
    }

    /// <summary>
    /// The editable combo selects its whole text when an item is chosen or the list closes: leave the caret at the
    /// end instead (after WPF has done its selecting).
    /// </summary>
    private void DeselectText() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if (Box.Template?.FindName("PART_EditableTextBox", Box) is not TextBox text) return;
        text.Select(text.Text.Length, 0);
    });

    private void OnPlay(object sender, RoutedEventArgs e) => Sounds.Play(Sound);

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.T("editor.chooseSound"), Filter = "WAV|*.wav" };
        if (Sound is { Length: > 0 } current && Path.IsPathRooted(current) && Path.GetDirectoryName(current) is { } dir && Directory.Exists(dir))
            dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        Sound = dlg.FileName;
        Sounds.Play(Sound);
    }
}
