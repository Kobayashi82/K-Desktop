using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KDesktop.Core.Model;

/// <summary>A named sequence of actions that can be triggered by hotkeys, the QuickMenu, UDP, etc.</summary>
public partial class Command : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _icon;
    [ObservableProperty] private bool _enabled = true;

    /// <summary>Can be executed remotely through the UDP server.</summary>
    [ObservableProperty] private bool _remote;

    /// <summary>Swallow the hotkey so the foreground application never receives it.</summary>
    [ObservableProperty] private bool _blockKey = true;

    [ObservableProperty] private bool _runOnStartup;
    [ObservableProperty] private bool _runOnExit;

    /// <summary>Runs even in applications where K-Desktop is disabled ("Desactivar en").</summary>
    [ObservableProperty] private bool _ignoreDisabledApps;

    /// <summary>Hidden from the main list unless "show hidden" is on.</summary>
    [ObservableProperty] private bool _hidden;

    /// <summary>Sound played when the command runs (name of a bundled sound or path to a .wav).</summary>
    [ObservableProperty] private string? _sound;

    [ObservableProperty] private string? _notes;

    public ObservableCollection<string> Hotkeys { get; set => Rewire(ref field, value); } = [];

    /// <summary>When not empty, the command only runs while one of these processes is in the foreground.</summary>
    public ObservableCollection<string> Processes { get; set => Rewire(ref field, value); } = [];

    public ObservableCollection<CommandAction> Actions { get; set => Rewire(ref field, value); } = [];

    public Command()
    {
        Hotkeys.CollectionChanged += OnChildCollectionChanged;
        Processes.CollectionChanged += OnChildCollectionChanged;
        Actions.CollectionChanged += OnChildCollectionChanged;
    }

    /// <summary>Raised whenever the command or anything inside it changes (used for autosave and index rebuilds).</summary>
    public event EventHandler? Modified;

    [JsonIgnore] public string HotkeysText => Hotkeys.Count == 0 ? string.Empty : string.Join("   ·   ", Hotkeys);

    [JsonIgnore] public CommandAction? PrimaryAction => Actions.FirstOrDefault(a => a.Enabled) ?? Actions.FirstOrDefault();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void Rewire<T>(ref ObservableCollection<T> field, ObservableCollection<T> value)
    {
        if (field is not null)
        {
            field.CollectionChanged -= OnChildCollectionChanged;
            if (typeof(T) == typeof(CommandAction))
                foreach (var a in field.OfType<CommandAction>()) a.PropertyChanged -= OnActionChanged;
        }
        field = value ?? [];
        field.CollectionChanged += OnChildCollectionChanged;
        if (typeof(T) == typeof(CommandAction))
            foreach (var a in field.OfType<CommandAction>()) a.PropertyChanged += OnActionChanged;
    }

    private void OnChildCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (var a in e.OldItems.OfType<CommandAction>()) a.PropertyChanged -= OnActionChanged;
        if (e.NewItems is not null)
            foreach (var a in e.NewItems.OfType<CommandAction>()) a.PropertyChanged += OnActionChanged;

        if (ReferenceEquals(sender, Hotkeys)) OnPropertyChanged(nameof(HotkeysText));
        if (ReferenceEquals(sender, Actions)) OnPropertyChanged(nameof(PrimaryAction));
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandAction.Summary)) return;
        Modified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deep copy with a new id.</summary>
    public Command Duplicate()
    {
        var copy = Json.Clone(this);
        copy.Id = Ids.New();
        return copy;
    }

    public override string ToString() => Name;
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
