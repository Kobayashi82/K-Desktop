using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>A card of the sound chooser: <see cref="Value"/> is what is saved (null: no sound).</summary>
public sealed class SoundChoice(string? value, string name, string caption, SymbolRegular symbol, SoundKind? kind, SoundSource? source)
    : INotifyPropertyChanged
{
    public string? Value { get; } = value;
    public string Name { get; } = name;
    public string Caption { get; } = caption;
    public SymbolRegular Symbol { get; } = symbol;
    public SoundKind? Kind { get; } = kind;
    public SoundSource? Source { get; } = source;

    /// <summary>A file shows its whole path.</summary>
    public string? Tip => Value is not null && Path.IsPathRooted(Value) ? Value : null;

    public bool IsSelected
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A kind of sound (or «Todos», «Tuyos»), with the sounds it shows now (after the search).</summary>
public sealed class SoundGroup(string name, SymbolRegular symbol, Func<SoundChoice, bool> holds)
{
    public string Name { get; } = name;
    public SymbolRegular Symbol { get; } = symbol;
    public Func<SoundChoice, bool> Holds { get; } = holds;
    public IReadOnlyList<SoundChoice> All { get; set; } = [];
    public IReadOnlyList<SoundChoice> Shown { get; set; } = [];
    public int? Count => All.Count > 0 ? All.Count : null;
}

/// <summary>
/// «Elegir sonido»: the built-in sounds, Windows' (C:\Windows\Media) and the user's, by kind and with a search. A click
/// plays a sound and marks it; a double click, Enter or «Elegir» keeps it. Another file can be any of the formats that
/// play (.wav, .mp3, .wma, .m4a, .aac, .flac).
/// </summary>
public partial class SoundPickerDialog : FluentWindow
{
    private readonly List<SoundChoice> _sounds;
    private readonly List<SoundGroup> _kinds;
    private readonly SoundGroup _all, _yours;
    private SoundChoice? _selected;
    private static int _lastKind;

    /// <summary>What was chosen (null: no sound).</summary>
    public string? Chosen { get; private set; }

    private SoundPickerDialog(string? current, string? globalText, bool allowFiles)
    {
        InitializeComponent();
        FileButton.Visibility = allowFiles ? Visibility.Visible : Visibility.Collapsed;

        var specials = new List<SoundChoice> { new(null, Loc.T("sound.none"), "", SymbolRegular.SpeakerMute24, null, null) };
        if (globalText is { Length: > 0 })
        {
            // Under «Global», the sound it is now.
            var notify = App.Current.Config.Settings.NotifySound;
            var now = string.IsNullOrWhiteSpace(notify) ? Loc.T("sound.none") : SoundLibrary.NameOf(notify);
            specials.Insert(0, new SoundChoice(ShowNotificationAction.GlobalSound, globalText, now, SymbolRegular.Globe24, null, null));
        }
        Specials.ItemsSource = specials;

        _sounds = SoundLibrary.All().Select(Choice).ToList();
        // A file chosen before (or a name that is no longer there): among «Tuyos», so it can be seen and kept.
        if (current is { Length: > 0 } && current != ShowNotificationAction.GlobalSound &&
            !_sounds.Any(s => string.Equals(s.Value, current, StringComparison.OrdinalIgnoreCase)))
            _sounds.Insert(0, Choice(new SoundEntry(current, SoundLibrary.NameOf(current), SoundLibrary.KindOf(SoundLibrary.NameOf(current)), SoundSource.Yours)));

        _all = new SoundGroup(Loc.T("sound.all"), SymbolRegular.Grid24, _ => true);
        _yours = new SoundGroup(Loc.T("sound.yours"), SymbolRegular.Person24, s => s.Source == SoundSource.Yours);
        _kinds =
        [
            new(Loc.T("sound.kind.alert"), SymbolRegular.Alert24, s => s.Kind == SoundKind.Alert),
            new(Loc.T("sound.kind.alarm"), SymbolRegular.Clock24, s => s.Kind == SoundKind.Alarm),
            new(Loc.T("sound.kind.ring"), SymbolRegular.Call24, s => s.Kind == SoundKind.Ring),
            new(Loc.T("sound.kind.system"), SymbolRegular.Desktop24, s => s.Kind == SoundKind.System),
        ];
        foreach (var g in _kinds.Append(_all).Append(_yours)) g.All = _sounds.Where(g.Holds).ToList();
        Kinds.ItemsSource = new[] { _all }.Concat(_kinds).Append(_yours).ToList();

        var selected = specials.Concat(_sounds).FirstOrDefault(s => string.Equals(s.Value, string.IsNullOrWhiteSpace(current) ? null : current, StringComparison.OrdinalIgnoreCase));
        Select(selected, play: false);
        Kinds.SelectedIndex = Math.Clamp(_lastKind, 0, _kinds.Count + 1);
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            BringSelectedIntoView();
        };
        Closed += (_, _) => Sounds.Preview(null);
    }

    /// <summary>Lets the user choose a sound; false when closed without choosing.</summary>
    public static bool Pick(Window? owner, string? current, string? globalText, bool allowFiles, out string? chosen)
    {
        var dlg = new SoundPickerDialog(current, globalText, allowFiles) { Owner = owner };
        var ok = dlg.ShowDialog() == true;
        chosen = ok ? dlg.Chosen : current;
        return ok;
    }

    private static SoundChoice Choice(SoundEntry e) => new(e.Value, e.Name, e.Source switch
    {
        SoundSource.Windows => "Windows",
        SoundSource.Yours => Loc.T("sound.source.yours"),
        _ => "K-Desktop",
    }, e.Kind switch
    {
        SoundKind.Alarm => SymbolRegular.Clock24,
        SoundKind.Ring => SymbolRegular.Call24,
        SoundKind.System => SymbolRegular.Desktop24,
        _ => SymbolRegular.Alert24,
    }, e.Kind, e.Source);

    private void OnKind(object sender, SelectionChangedEventArgs e)
    {
        _lastKind = Kinds.SelectedIndex;
        Refresh();
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Refresh();

    /// <summary>The chosen kind (all of them grouped for «Todos» or while searching) with the sounds that match the search.</summary>
    private void Refresh()
    {
        if (Kinds.SelectedItem is not SoundGroup chosen) return;
        var tokens = FuzzyMatch.Tokens(SearchBox.Text);
        var searching = tokens.Length > 0;
        IReadOnlyList<SoundGroup> groups = searching || chosen == _all ? _kinds : [chosen];
        foreach (var g in groups)
            g.Shown = searching ? g.All.Where(s => FuzzyMatch.Score(tokens, new SearchKey(s.Name)) > 0 || s.Caption.Contains(SearchBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList() : g.All;
        var visible = groups.Where(g => g.Shown.Count > 0).ToList();
        Groups.ItemsSource = null;
        Groups.ItemsSource = visible;
        Specials.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        NothingFound.Visibility = visible.Count == 0 && (searching || chosen != _yours) ? Visibility.Visible : Visibility.Collapsed;
        YoursHint.Visibility = chosen == _yours && !searching ? Visibility.Visible : Visibility.Collapsed;
        Scroll.ScrollToTop();
    }

    private void Select(SoundChoice? choice, bool play)
    {
        if (_selected is not null) _selected.IsSelected = false;
        _selected = choice;
        if (choice is not null) choice.IsSelected = true;
        ChooseButton.IsEnabled = choice is not null;
        if (!play) return;
        var playable = choice?.Value == ShowNotificationAction.GlobalSound ? App.Current.Config.Settings.NotifySound : choice?.Value;
        Sounds.Preview(playable);
    }

    private void BringSelectedIntoView()
    {
        if (_selected is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var card in FindCards(this))
                if (card.Tag == _selected)
                {
                    card.BringIntoView();
                    return;
                }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static IEnumerable<System.Windows.Controls.Button> FindCards(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Button { Tag: SoundChoice } card) yield return card;
            foreach (var inner in FindCards(child)) yield return inner;
        }
    }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SoundChoice choice }) Select(choice, play: true);
    }

    private void OnPickTwice(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SoundChoice choice }) return;
        e.Handled = true;
        Choose(choice);
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // The marked one; while searching, the first one found.
                var first = Groups.ItemsSource is IEnumerable<SoundGroup> groups ? groups.SelectMany(g => g.Shown).FirstOrDefault() : null;
                if (SearchBox.Text.Trim().Length > 0 && first is not null) Choose(first);
                else if (_selected is not null) Choose(_selected);
                e.Handled = true;
                break;
            case Key.Escape:
                DialogResult = false;
                break;
        }
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) Choose(_selected);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Choose(SoundChoice choice)
    {
        Chosen = choice.Value;
        DialogResult = true;
    }

    private void OnFile(object sender, RoutedEventArgs e)
    {
        var patterns = string.Join(";", SoundLibrary.Extensions.Select(x => "*" + x));
        var dlg = new OpenFileDialog { Title = Loc.T("editor.chooseSound"), Filter = $"{Loc.T("sound.audio")}|{patterns}" };
        if (_selected?.Value is { Length: > 0 } current && Path.IsPathRooted(current) && Path.GetDirectoryName(current) is { } dir && Directory.Exists(dir))
            dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) != true) return;
        Chosen = dlg.FileName;
        DialogResult = true;
    }

    private void OnFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SoundsDir);
            ProcessLauncher.Launch(new LaunchSpec("explorer.exe", $"\"{AppPaths.SoundsDir}\""), asAdmin: false);
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo abrir la carpeta de sonidos: {ex.Message}");
        }
    }
}
