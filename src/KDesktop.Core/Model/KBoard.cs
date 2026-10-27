using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>What a remapped key or button does instead.</summary>
public enum RemapKind { Keys, Text, Disable, Command }

/// <summary>
/// A K-Board profile: a set of key / mouse button remappings that applies everywhere or only in some applications,
/// and that can be switched on and off (by hand, from a command, or from the tray).
/// </summary>
public partial class KBoardProfile : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>On / off. Commands can toggle it with the "K-Board" action.</summary>
    [ObservableProperty] private bool _active = true;

    /// <summary>
    /// «Ejecutar siempre»: also in the applications of «Desactivar en» (a game, say), where the hotkeys of the commands
    /// stay off but the keys are still changed. A profile limited to some applications always works in them.
    /// </summary>
    [ObservableProperty] private bool _ignoreDisabledApps = true;

    [ObservableProperty] private string? _notes;

    /// <summary>When not empty, the profile only works while one of these applications is in the foreground.</summary>
    public ObservableCollection<string> Processes { get; set => Rewire(ref field, value); } = [];

    public ObservableCollection<KeyRemap> Remaps { get; set => Rewire(ref field, value); } = [];

    public KBoardProfile()
    {
        Processes.CollectionChanged += OnCollectionChanged;
        Remaps.CollectionChanged += OnCollectionChanged;
    }

    /// <summary>Raised whenever the profile or any remap changes (autosave, index rebuild).</summary>
    public event EventHandler? Modified;

    [JsonIgnore] public string Summary =>
        Loc.T("kboard.summary", Remaps.Count(r => r.Enabled)) + "  ·  " +
        (Processes.Count == 0 ? Loc.T("kboard.everywhere") : string.Join(", ", Processes));

    /// <summary>For the list, on two lines: «3 reasignaciones», then the first application (or «en todas») and +N.</summary>
    [JsonIgnore] public string RemapsText => Loc.T("kboard.summary", Remaps.Count(r => r.Enabled));

    [JsonIgnore] public string FirstApp => Processes.Count == 0 ? Loc.T("kboard.everywhere") : Processes[0];

    /// <summary>«+2» when there are more applications; empty otherwise.</summary>
    [JsonIgnore] public string MoreAppsText => Processes.Count > 1 ? "+" + (Processes.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>All of them, one per line (the tooltip of +N).</summary>
    [JsonIgnore] public string AppsList => string.Join(Environment.NewLine, Processes);

    private void NotifyTexts()
    {
        foreach (var name in new[] { nameof(Summary), nameof(RemapsText), nameof(FirstApp), nameof(MoreAppsText), nameof(AppsList) })
            base.OnPropertyChanged(new PropertyChangedEventArgs(name));
    }

    public KeyRemap? Find(string from) => Remaps.FirstOrDefault(r => Hotkey.Comparer.Equals(r.From, from));

    public KBoardProfile Duplicate()
    {
        var copy = Json.Clone(this);
        copy.Id = Ids.New();
        return copy;
    }

    public void RefreshTexts()
    {
        NotifyTexts();
        foreach (var r in Remaps) r.RefreshTexts();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void Rewire<T>(ref ObservableCollection<T> field, ObservableCollection<T> value)
    {
        if (field is not null)
        {
            field.CollectionChanged -= OnCollectionChanged;
            foreach (var r in field.OfType<KeyRemap>()) r.PropertyChanged -= OnRemapChanged;
        }
        field = value ?? [];
        field.CollectionChanged += OnCollectionChanged;
        foreach (var r in field.OfType<KeyRemap>()) r.PropertyChanged += OnRemapChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (var r in e.OldItems.OfType<KeyRemap>()) r.PropertyChanged -= OnRemapChanged;
        if (e.NewItems is not null) foreach (var r in e.NewItems.OfType<KeyRemap>()) r.PropertyChanged += OnRemapChanged;
        NotifyTexts();
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeyRemap.Description) or nameof(KeyRemap.TargetText)) return;
        if (e.PropertyName is nameof(KeyRemap.Enabled)) NotifyTexts();
        Modified?.Invoke(this, EventArgs.Empty);
    }

    public override string ToString() => Name;
}

/// <summary>
/// One remapping: <see cref="From"/> (a key, button or wheel direction, optionally with modifiers: "CapsLock",
/// "CTRL + J", "Mouse 4", "Wheel Down") becomes <see cref="To"/> ("LCtrl", "CTRL + C", "Mouse 1"…), some text,
/// nothing at all, or a K-Desktop command.
/// </summary>
public partial class KeyRemap : ObservableObject
{
    [ObservableProperty] private string _from = string.Empty;
    [ObservableProperty] private RemapKind _kind = RemapKind.Keys;

    /// <summary>Keys: the key or combination to send. Text: the text to type.</summary>
    [ObservableProperty] private string _to = string.Empty;

    [ObservableProperty] private string? _commandId;
    [ObservableProperty] private bool _enabled = true;

    /// <summary>Display-only name of the command (resolved by the UI).</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _commandName;

    /// <summary>What the key does, for lists and tooltips.</summary>
    [JsonIgnore] public string TargetText => Kind switch
    {
        RemapKind.Disable => Loc.T("kboard.target.disabled"),
        RemapKind.Text => $"\"{To}\"",
        RemapKind.Command => Loc.T("kboard.target.command", string.IsNullOrWhiteSpace(CommandName) ? "?" : CommandName),
        _ => string.IsNullOrWhiteSpace(To) ? "?" : To,
    };

    [JsonIgnore] public string Description => $"{From}  →  {TargetText}";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Kind) or nameof(To) or nameof(CommandName) or nameof(From))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(TargetText)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Description)));
        }
    }

    public void RefreshTexts()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(TargetText)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Description)));
    }

    public override string ToString() => Description;

    public KeyRemap Clone() => new() { From = From, Kind = Kind, To = To, CommandId = CommandId, CommandName = CommandName, Enabled = Enabled };
}

public enum KBoardActionMode { Toggle, Activate, Deactivate }

/// <summary>Switches a K-Board profile on or off.</summary>
public partial class KBoardAction : CommandAction
{
    [ObservableProperty] private string _profileId = string.Empty;
    [ObservableProperty] private KBoardActionMode _mode = KBoardActionMode.Toggle;
    [ObservableProperty] private bool _showOsd = true;

    [ObservableProperty]
    [property: JsonIgnore] private string? _profileName;

    public override ActionKind Kind => ActionKind.KBoard;
    public override string Summary => Loc.T($"summary.kboard.{Mode}", string.IsNullOrWhiteSpace(ProfileName) ? "?" : ProfileName);
}
