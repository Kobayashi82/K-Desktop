using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using Microsoft.Win32;

namespace KDesktop.Views;

/// <summary>
/// List of the sounds (built-in names, or a .wav file chosen with Browse) with Play and Browse buttons. Nothing is
/// typed: a file shows its name, and its whole path in the tooltip. Picking a sound in the list (mouse or arrow keys)
/// plays it, so they can be compared quickly.
/// </summary>
public partial class SoundPicker : UserControl
{
    public static readonly DependencyProperty SoundProperty = DependencyProperty.Register(
        nameof(Sound), typeof(string), typeof(SoundPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) =>
        {
            if (d is SoundPicker { _showing: false } picker) picker.ShowSound();
        }));

    public static readonly DependencyProperty ShowBrowseProperty = DependencyProperty.Register(
        nameof(ShowBrowse), typeof(bool), typeof(SoundPicker),
        new PropertyMetadata(true, (d, e) => ((SoundPicker)d).BrowseButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    /// <summary>An entry of the list: <see cref="Value"/> is what is saved (null = no sound), its text what is seen.</summary>
    public sealed record Item(string? Value, string Text)
    {
        // What UI Automation (and so screen readers) read for the entry.
        public override string ToString() => Text;
    }

    public SoundPicker()
    {
        InitializeComponent();
        Box.AddHandler(PreviewMouseMoveEvent, new MouseEventHandler(OnBoxMouseMove), handledEventsToo: true);
        ShowSound();
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

    /// <summary>Selecting the item of <see cref="Sound"/> from code: it is not the user picking (no sound, no saving).</summary>
    private bool _showing;

    /// <summary>
    /// The list again with the current sound selected: «no sound» and the built-in sounds. Anything else (a file chosen
    /// with Browse, or a name that no longer exists, kept so opening the options doesn't lose it) is not in the list:
    /// nothing is selected and its name shows over the combo, with the whole path in the tooltip.
    /// </summary>
    private void ShowSound()
    {
        var sound = string.IsNullOrWhiteSpace(Sound) ? null : Sound;
        var items = new List<Item> { new(null, Loc.T("sound.none")) };
        items.AddRange(Sounds.Available().Select(n => new Item(n, n)));
        var match = items.FirstOrDefault(i => string.Equals(i.Value, sound, StringComparison.OrdinalIgnoreCase));
        _showing = true;
        try
        {
            Box.ItemsSource = items;
            Box.SelectedItem = match;
        }
        finally { _showing = false; }
        ShowFile(match is null ? sound : null);
    }

    private void ShowFile(string? sound)
    {
        FileName.Text = sound is null ? string.Empty : Path.IsPathRooted(sound) ? Path.GetFileNameWithoutExtension(sound) : sound;
        FileName.Visibility = sound is null ? Visibility.Collapsed : Visibility.Visible;
        Box.ToolTip = sound is not null && Path.IsPathRooted(sound) ? sound : null;
    }

    /// <summary>Last sound previewed from the open list, so moving over the same item does not restart it.</summary>
    private string? _previewed;

    // Mouse over the current item: no sound until the mouse moves to another one.
    private void OnDropDownOpened(object? sender, EventArgs e) => _previewed = (Box.SelectedItem as Item)?.Value;

    /// <summary>
    /// With the list open, each sound plays as the mouse goes over it. The open combo captures the mouse, so the
    /// items get no MouseEnter: follow the moves on the combo (the list's events reach it) and find the item below.
    /// </summary>
    private void OnBoxMouseMove(object sender, MouseEventArgs e)
    {
        if (!Box.IsDropDownOpen) return;
        var item = e.OriginalSource as DependencyObject;
        while (item is not null and not ComboBoxItem) item = item is Visual or Visual3D ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
        if (item is ComboBoxItem { Content: Item { Value: { } sound } }) Preview(sound);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || e.AddedItems is not [Item picked, ..]) return;
        _showing = true;
        try { Sound = picked.Value; }
        finally { _showing = false; }
        ShowFile(null);
        // Choosing always plays it, even right after hovering over it.
        _previewed = picked.Value;
        if (picked.Value is not null) Sounds.Preview(picked.Value);
    }

    private void Preview(string sound)
    {
        if (sound == _previewed) return;
        _previewed = sound;
        Sounds.Preview(sound);
    }

    private void OnDropDownClosed(object? sender, EventArgs e) => _previewed = null;

    private void OnPlay(object sender, RoutedEventArgs e) => Sounds.Preview(Sound);

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.T("editor.chooseSound"), Filter = "WAV|*.wav" };
        if (Sound is { Length: > 0 } current && Path.IsPathRooted(current) && Path.GetDirectoryName(current) is { } dir && Directory.Exists(dir))
            dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        Sound = dlg.FileName;
        Sounds.Preview(Sound);
    }
}
