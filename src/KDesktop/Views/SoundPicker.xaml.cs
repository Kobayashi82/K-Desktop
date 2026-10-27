using System.IO;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Views.Dialogs;
using Wpf.Ui.Controls;

namespace KDesktop.Views;

/// <summary>
/// The sound chosen (a built-in or user sound by name, a Windows sound or another audio file by its path) shown as a
/// field: a click opens «Elegir sonido» (<see cref="SoundPickerDialog"/>), where every sound is by kind and plays on a
/// click; the button beside it plays the chosen one. A file shows its name, and its whole path in the tooltip.
/// </summary>
public partial class SoundPicker : UserControl
{
    public static readonly DependencyProperty SoundProperty = DependencyProperty.Register(
        nameof(Sound), typeof(string), typeof(SoundPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SoundPicker)d).ShowSound()));

    /// <summary>Whether «Elegir sonido» offers any other file too (on by default).</summary>
    public static readonly DependencyProperty ShowBrowseProperty = DependencyProperty.Register(
        nameof(ShowBrowse), typeof(bool), typeof(SoundPicker), new PropertyMetadata(true));

    /// <summary>
    /// With a text: a first choice «Global» (<see cref="ShowNotificationAction.GlobalSound"/>), the sound of Options ›
    /// Notificaciones, whichever it is then.
    /// </summary>
    public static readonly DependencyProperty GlobalTextProperty = DependencyProperty.Register(
        nameof(GlobalText), typeof(string), typeof(SoundPicker), new PropertyMetadata(null, (d, _) => ((SoundPicker)d).ShowSound()));

    public string? GlobalText
    {
        get => (string?)GetValue(GlobalTextProperty);
        set => SetValue(GlobalTextProperty, value);
    }

    /// <summary>Built-in or user sound name, or path of an audio file; null for none.</summary>
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

    public SoundPicker()
    {
        InitializeComponent();
        ShowSound();
    }

    /// <summary>What plays for a choice («Global»: the sound of Options).</summary>
    private static string? Playable(string? sound) =>
        sound == ShowNotificationAction.GlobalSound ? App.Current.Config.Settings.NotifySound : sound;

    private void ShowSound()
    {
        var sound = string.IsNullOrWhiteSpace(Sound) ? null : Sound;
        FieldText.Text = sound switch
        {
            null => Loc.T("sound.none"),
            ShowNotificationAction.GlobalSound => GlobalText ?? Loc.T("sound.none"),
            _ => SoundLibrary.NameOf(sound),
        };
        FieldIcon.Symbol = sound is null ? SymbolRegular.SpeakerMute24 : sound == ShowNotificationAction.GlobalSound ? SymbolRegular.Globe24 : SymbolRegular.MusicNote224;
        Field.ToolTip = sound is not null && Path.IsPathRooted(sound) ? sound : null;
        System.Windows.Automation.AutomationProperties.SetName(Field, FieldText.Text);
        PlayButton.IsEnabled = Playable(sound) is not null;
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (SoundPickerDialog.Pick(Window.GetWindow(this), Sound, GlobalText, ShowBrowse, out var chosen)) Sound = chosen;
    }

    private void OnPlay(object sender, RoutedEventArgs e) => Sounds.Preview(Playable(Sound));
}
