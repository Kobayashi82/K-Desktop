using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>What an element of a panel shows. New ones always at the end (the config keeps them by name).</summary>
public enum PanelItemKind
{
    NetUp, NetDown, Cpu, Ram, Gpu, Vram, GpuTemp,

    /// <summary>Nothing: an empty cell, to leave a gap (so the columns line up as one wants).</summary>
    Spacer,
}

/// <summary>How an element shows its value: the figure, the figure with a thin bar under it, or a small graph.</summary>
public enum PanelItemLook
{
    Text, Bar, Graph,
}

/// <summary>
/// «Paneles» (5.0.452): a small window on the desktop with figures of the PC (network, processor, memory, graphics card…).
/// It stays over the desktop and under the other windows, is dragged anywhere and, locked, can let the clicks through.
/// </summary>
public partial class InfoPanel : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private DateTime _created = DateTime.Now;

    /// <summary>Shown on the desktop.</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>It can't be dragged (the right-click menu still opens).</summary>
    [ObservableProperty] private bool _locked;

    /// <summary>Locked, the clicks go to what is under it (it is unlocked from K-Desktop or with the action).</summary>
    [ObservableProperty] private bool _clickThrough;

    /// <summary>
    /// Over every window (also over the taskbar), instead of only over the desktop; it hides while something fills its
    /// monitor (a game, a film).
    /// </summary>
    [ObservableProperty] private bool _topmost;

    /// <summary>The elements go in columns of this many (1: all in a row; as many as elements: a list).</summary>
    [ObservableProperty] private int _rows = 2;

    [ObservableProperty] private int _fontSize = 12;

    /// <summary>The colour of the names («CPU», «↑»…); null: the accent of K-Desktop.</summary>
    [ObservableProperty] private string? _labelColor;

    /// <summary>The colour of the figures; null: white, or black with the light theme.</summary>
    [ObservableProperty] private string? _valueColor;

    /// <summary>The background, «#RRGGBB» (null: that of K-Desktop's menus); how much of it is seen (0: none, only the text).</summary>
    [ObservableProperty] private string? _background;
    [ObservableProperty] private int _backgroundOpacity = 60;

    /// <summary>Every how many milliseconds the figures change.</summary>
    [ObservableProperty] private int _interval = 1000;

    /// <summary>Where it is: the monitor (its GDI name) and its corner in fractions of it (null: not placed yet).</summary>
    [ObservableProperty] private string? _monitor;
    [ObservableProperty] private double? _x;
    [ObservableProperty] private double? _y;

    public ObservableCollection<InfoPanelItem> Items
    {
        get;
        set
        {
            if (field is not null)
            {
                field.CollectionChanged -= OnItemsChanged;
                foreach (var i in field) i.PropertyChanged -= OnItemChanged;
            }
            field = value ?? [];
            field.CollectionChanged += OnItemsChanged;
            foreach (var i in field) i.PropertyChanged += OnItemChanged;
        }
    } = [];

    public InfoPanel() => Items.CollectionChanged += OnItemsChanged;

    /// <summary>A new panel: as the usual one on a desktop, the network, the processor and the graphics card in two rows.</summary>
    public static InfoPanel CreateDefault(string name) => new()
    {
        Name = name,
        Items = [.. new[] { PanelItemKind.NetUp, PanelItemKind.NetDown, PanelItemKind.Cpu, PanelItemKind.Gpu, PanelItemKind.Ram, PanelItemKind.Vram, PanelItemKind.GpuTemp }
            .Select(k => new InfoPanelItem { Kind = k })],
    };

    /// <summary>«↑ ↓ CPU RAM…», for the list.</summary>
    [JsonIgnore]
    public string Summary => Items.All(i => i.Kind == PanelItemKind.Spacer)
        ? Loc.T("panels.noItems")
        : string.Join("  ", Items.Where(i => i.Kind != PanelItemKind.Spacer).Select(i => i.ShownLabel));

    /// <summary>Re-evaluates the localised texts (after a language change).</summary>
    public void RefreshTexts() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));

    /// <summary>Raised whenever it changes (autosave).</summary>
    public event EventHandler? Modified;

    /// <summary>Whether it has been placed somewhere already.</summary>
    [JsonIgnore]
    public bool IsPlaced => X is not null && Y is not null;

    public InfoPanel Duplicate() => new()
    {
        Name = Name,
        Notes = Notes,
        Enabled = Enabled,
        Locked = Locked,
        ClickThrough = ClickThrough,
        Topmost = Topmost,
        Rows = Rows,
        FontSize = FontSize,
        LabelColor = LabelColor,
        ValueColor = ValueColor,
        Background = Background,
        BackgroundOpacity = BackgroundOpacity,
        Interval = Interval,
        Items = [.. Items.Select(i => i.Clone())],
    };

    /// <summary>Values out of range (a config edited by hand) back to something it can show.</summary>
    public bool Mend()
    {
        var changed = false;
        if (Rows is < 1 or > 12) (Rows, changed) = (Math.Clamp(Rows, 1, 12), true);
        if (FontSize is < 8 or > 48) (FontSize, changed) = (Math.Clamp(FontSize, 8, 48), true);
        if (BackgroundOpacity is < 0 or > 100) (BackgroundOpacity, changed) = (Math.Clamp(BackgroundOpacity, 0, 100), true);
        if (Interval is < 250 or > 60000) (Interval, changed) = (Math.Clamp(Interval, 250, 60000), true);
        return changed;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (InfoPanelItem i in e.OldItems) i.PropertyChanged -= OnItemChanged;
        if (e.NewItems is not null)
            foreach (InfoPanelItem i in e.NewItems) i.PropertyChanged += OnItemChanged;
        OnItemChanged(sender, new PropertyChangedEventArgs(nameof(Items)));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        Modified?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>One figure of a panel.</summary>
public partial class InfoPanelItem : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShownLabel), nameof(IsFigure))] private PanelItemKind _kind;
    [ObservableProperty] private PanelItemLook _look;

    /// <summary>Its own name instead of the usual one («CPU», «↑»…); null: the usual one.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShownLabel))] private string? _label;

    /// <summary>The name it shows (a gap shows nothing).</summary>
    [JsonIgnore]
    public string ShownLabel => Kind == PanelItemKind.Spacer ? string.Empty : string.IsNullOrWhiteSpace(Label) ? DefaultLabel(Kind) : Label;

    /// <summary>Whether it is a figure (not a gap): only figures have a name and a look to choose.</summary>
    [JsonIgnore]
    public bool IsFigure => Kind != PanelItemKind.Spacer;

    /// <summary>The usual short name of each kind.</summary>
    public static string DefaultLabel(PanelItemKind kind) => kind switch
    {
        PanelItemKind.NetUp => "↑",
        PanelItemKind.NetDown => "↓",
        PanelItemKind.Cpu => "CPU",
        PanelItemKind.Ram => "RAM",
        PanelItemKind.Gpu => "GPU",
        PanelItemKind.Vram => "VRAM",
        PanelItemKind.Spacer => string.Empty,
        _ => Loc.T("panels.label.temp"),
    };

    /// <summary>Whether it is a share of something (it can have a bar).</summary>
    public static bool IsPercent(PanelItemKind kind) => kind is PanelItemKind.Cpu or PanelItemKind.Ram or PanelItemKind.Gpu or PanelItemKind.Vram;

    public InfoPanelItem Clone() => new() { Kind = Kind, Look = Look, Label = Label };
}

public enum PanelActionMode { Toggle, Show, Hide, Lock, Unlock }

/// <summary>«Panel»: shows, hides, locks or unlocks a panel of «Paneles» (or all of them, with no panel chosen).</summary>
public partial class PanelAction : CommandAction
{
    /// <summary>The panel; empty: all of them.</summary>
    [ObservableProperty] private string _panelId = string.Empty;
    [ObservableProperty] private PanelActionMode _mode = PanelActionMode.Toggle;
    [ObservableProperty] private bool _showOsd = true;

    [ObservableProperty]
    [property: JsonIgnore] private string? _panelName;

    public override ActionKind Kind => ActionKind.Panel;

    public override string Summary => string.IsNullOrEmpty(PanelId)
        ? Loc.T("summary.panels." + Mode)
        : Loc.T("summary.panel." + Mode, string.IsNullOrWhiteSpace(PanelName) ? "?" : PanelName);
}
