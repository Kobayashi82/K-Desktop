using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using KDesktop.Services;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Assistant;

/// <summary>Base of the models the XAML binds to: change notification.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>
/// One of a group of pills (the page tabs, a choice shown as buttons): a radio button whose check the model keeps, so
/// only one is on.
/// </summary>
public abstract class PillModel(Action<PillModel> choose) : Observable
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected) choose(this);
            else if (!value && _isSelected) Notify(); // a checked pill stays checked: clicking it again changes nothing
        }
    }

    /// <summary>Set by the group, without asking it again.</summary>
    internal void Mark(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        Notify(nameof(IsSelected));
    }
}

/// <summary>A page of an assistant's settings (a tab when it has several).</summary>
public sealed class SettingsPageModel(string title, SymbolRegular? symbol, IReadOnlyList<SettingsSectionModel> sections, Action<PillModel> choose)
    : PillModel(choose)
{
    public string Title { get; } = title;
    public SymbolRegular Symbol { get; } = symbol ?? SymbolRegular.Settings24;
    public IReadOnlyList<SettingsSectionModel> Sections { get; } = sections;

    public override string ToString() => Title;
}

/// <summary>A section of a page: one card, with or without a title.</summary>
public sealed class SettingsSectionModel(string? title, IReadOnlyList<SettingModel> items)
{
    public string? Title { get; } = title;
    public bool HasTitle => !string.IsNullOrEmpty(Title);
    public IReadOnlyList<SettingModel> Items { get; } = items;
}

/// <summary>One control of an assistant's settings; each type of settings.json has its own model and template.</summary>
public abstract class SettingModel : Observable
{
    private bool _isEnabled = true;

    protected SettingModel(AssistantSettingsModel owner, SettingItem item)
    {
        Owner = owner;
        Item = item;
        Label = AssistantLook.Text(item.Label);
        Description = AssistantLook.Text(item.Description);
    }

    public AssistantSettingsModel Owner { get; }

    public SettingItem Item { get; }

    public string Label { get; }

    public string Description { get; }

    public bool HasDescription => Description.Length > 0;

    /// <summary>Its name for screen readers (the item of the list that holds it).</summary>
    public override string ToString() => Label;

    /// <summary>Off while its «enabledWhen» toggle says so.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            Notify();
        }
    }

    /// <summary>Reads its value (at the start and after «Reset»).</summary>
    public virtual void Load(JsonObject values) { }

    protected JsonNode? ValueIn(JsonObject values) => Item.Key is { } key ? values[key] : null;

    protected void Save(JsonNode? value) => Owner.Set(Item, value);

    /// <summary>The model of an item, or null for a type this K-Desktop does not know (a newer package).</summary>
    public static SettingModel? Create(AssistantSettingsModel owner, SettingItem item) => item.Type switch
    {
        "toggle" => new ToggleSetting(owner, item),
        "slider" => new SliderSetting(owner, item),
        "number" => new NumberSetting(owner, item),
        "choice" => new ChoiceSetting(owner, item),
        "text" => new TextSetting(owner, item),
        "color" => new ColorSetting(owner, item),
        "gallery" => new GallerySetting(owner, item),
        "apps" => new AppsSetting(owner, item),
        "hotkey" => new HotkeySetting(owner, item),
        "button" => new ButtonSetting(owner, item),
        "info" => new InfoSetting(owner, item),
        "keys" => new KeysSetting(owner, item),
        _ => null,
    };
}

public sealed class ToggleSetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item)
{
    private bool _isOn;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            Notify();
            Save(JsonValue.Create(value));
        }
    }

    public override void Load(JsonObject values)
    {
        _isOn = ValueIn(values) is JsonValue v && v.TryGetValue<bool>(out var on) && on;
        Notify(nameof(IsOn));
    }
}

/// <summary>A number between limits (the «slider» and «number» types).</summary>
public abstract class NumericSetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item)
{
    private double _value;

    public double Minimum => Math.Min(Item.Min ?? 0, Item.Max ?? 0);
    public double Maximum => Math.Max(Item.Min ?? 0, Item.Max ?? 0);
    public double Step => Item.Step is > 0 and var s ? s : 1;

    public double Value
    {
        get => _value;
        set
        {
            var snapped = AssistantUi.Snap(value, Minimum, Maximum, Item.Step);
            if (snapped.Equals(_value)) return;
            _value = snapped;
            Notify();
            Notify(nameof(ValueText));
            Save(AssistantUi.NumberNode(snapped, Minimum, Item.Step));
        }
    }

    public string ValueText => AssistantUi.FormatNumber(Item.Format, _value, Loc.Instance.Culture);

    public override void Load(JsonObject values)
    {
        _value = AssistantUi.Snap(AssistantUi.Number(ValueIn(values)) ?? Minimum, Minimum, Maximum, Item.Step);
        Notify(nameof(Value));
        Notify(nameof(ValueText));
    }
}

public sealed class SliderSetting(AssistantSettingsModel owner, SettingItem item) : NumericSetting(owner, item)
{
    public string MinLabel { get; } = AssistantLook.Text(item.MinLabel);
    public string MaxLabel { get; } = AssistantLook.Text(item.MaxLabel);
    public bool HasEndLabels => MinLabel.Length > 0 || MaxLabel.Length > 0;

    /// <summary>With words at the ends («Calm — Active») the number says little, unless the package gives it a format.</summary>
    public bool ShowValue => !HasEndLabels || Item.Format is not null;
}

public sealed class NumberSetting(AssistantSettingsModel owner, SettingItem item) : NumericSetting(owner, item)
{
    /// <summary>Decimals the box keeps: those of the step.</summary>
    public int Decimals => Step.ToString(CultureInfo.InvariantCulture) is var s && s.IndexOf('.') is var dot and >= 0 ? s.Length - dot - 1 : 0;

    /// <summary>The box works with double?; an emptied box keeps the value it had.</summary>
    public double? BoxValue
    {
        get => Value;
        set
        {
            if (value is { } v) Value = v;
            Notify();
        }
    }

    public override void Load(JsonObject values)
    {
        base.Load(values);
        Notify(nameof(BoxValue));
    }
}

/// <summary>An option of a choice. Its text is its label (what screen readers and type-to-find use).</summary>
public sealed class ChoiceOption(JsonNode value, string label, Action<PillModel> choose) : PillModel(choose)
{
    public JsonNode Value { get; } = value;

    public string Label { get; } = label;

    public override string ToString() => Label;
}

public sealed class ChoiceSetting : SettingModel
{
    private ChoiceOption? _selected;

    public ChoiceSetting(AssistantSettingsModel owner, SettingItem item) : base(owner, item) =>
        Options = [.. item.Options.Select(o => new ChoiceOption(o.Value, AssistantLook.Text(o.Label), p => Selected = (ChoiceOption)p))];

    public IReadOnlyList<ChoiceOption> Options { get; }

    /// <summary>Buttons side by side, or a drop-down list (by default, for more than four options).</summary>
    public bool AsButtons => Item.Style switch
    {
        "buttons" => true,
        "list" => false,
        _ => Options.Count <= 4,
    };

    public ChoiceOption? Selected
    {
        get => _selected;
        set
        {
            if (value is null || ReferenceEquals(_selected, value)) return;
            _selected = value;
            Mark();
            Save(value.Value.DeepClone());
        }
    }

    public override void Load(JsonObject values)
    {
        var current = ValueIn(values);
        _selected = Options.FirstOrDefault(o => JsonNode.DeepEquals(o.Value, current))
                    ?? Options.FirstOrDefault(o => AssistantUi.Plain(o.Value) == AssistantUi.Plain(current));
        Mark();
    }

    private void Mark()
    {
        foreach (var option in Options) option.Mark(ReferenceEquals(option, _selected));
        Notify(nameof(Selected));
    }
}

public sealed class TextSetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item)
{
    private string _text = string.Empty;

    public string Placeholder { get; } = AssistantLook.Text(item.Placeholder);

    public int MaxLength => Item.MaxLength ?? 0;

    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;
            _text = value;
            Notify();
            Save(JsonValue.Create(value));
        }
    }

    public override void Load(JsonObject values)
    {
        _text = AssistantUi.Plain(ValueIn(values));
        Notify(nameof(Text));
    }
}

public sealed class ColorSetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item)
{
    private string _hex = "#FFFFFF";
    private string _typed = "#FFFFFF";

    /// <summary>Ready-made colours of the drop-down: K-Desktop's palette and some greys.</summary>
    public static IReadOnlyList<string> Presets { get; } =
    [
        .. KdIcons.Palette.Select(p => $"#{p.Top.R:X2}{p.Top.G:X2}{p.Top.B:X2}"),
        .. KdIcons.Palette.Select(p => $"#{p.Bottom.R:X2}{p.Bottom.G:X2}{p.Bottom.B:X2}"),
        "#FFFFFF", "#D0D0D0", "#808080", "#404040", "#000000",
    ];

    public Brush Brush => Parse(_hex) is { } c ? Frozen(c) : Brushes.Transparent;

    /// <summary>The «#RRGGBB» box: a valid colour is saved at once; anything else waits until it is one.</summary>
    public string Typed
    {
        get => _typed;
        set
        {
            _typed = value ?? string.Empty;
            Notify();
            var text = _typed.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (Parse(text) is null || text.Equals(_hex, StringComparison.OrdinalIgnoreCase)) return;
            _hex = text.ToUpperInvariant();
            Notify(nameof(Brush));
            Save(JsonValue.Create(_hex));
        }
    }

    public void Pick(string hex)
    {
        Typed = hex;
        _typed = _hex;
        Notify(nameof(Typed));
    }

    public override void Load(JsonObject values)
    {
        var text = AssistantUi.Plain(ValueIn(values));
        _hex = Parse(text) is null ? "#FFFFFF" : text.ToUpperInvariant();
        _typed = _hex;
        NotifyAll();
    }

    private static Color? Parse(string text) =>
        text.Length == 7 && text[0] == '#' && uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}

/// <summary>An image of a gallery as a tile.</summary>
public sealed class GalleryTile(string id, string name, ImageSource? image, bool locked, string lockText) : Observable
{
    private bool _isSelected;

    public string Id { get; } = id;
    public string Name { get; } = name;
    public ImageSource? Image { get; } = image;
    public bool NoImage => Image is null;
    public bool Locked { get; } = locked;
    public bool Unlocked => !Locked;
    public string? LockText { get; } = lockText.Length > 0 ? lockText : null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            Notify();
        }
    }
}

/// <summary>
/// A gallery: fixed images of the package, or the ones the assistant publishes under its «source» (with the locked
/// ones and how to unlock them), which are only known while it is on the desktop.
/// </summary>
public sealed class GallerySetting : SettingModel
{
    private string? _selected;

    public GallerySetting(AssistantSettingsModel owner, SettingItem item) : base(owner, item)
    {
        if (item.Source is null) Fill(item.GalleryItems);
        else Fill(GalleryEntry.List(AssistantUi.Resolve(owner.PanelData, item.Source)));
    }

    public ObservableCollection<GalleryTile> Tiles { get; } = [];

    public bool IsEmpty => Tiles.Count == 0;

    public string EmptyText => Loc.T("assistantSettings.galleryEmpty", Owner.Assistant.Manifest.Name);

    /// <summary>New data from the assistant: the tiles of a gallery with «source» are made again.</summary>
    public void PanelChanged()
    {
        if (Item.Source is null) return;
        Fill(GalleryEntry.List(AssistantUi.Resolve(Owner.PanelData, Item.Source)));
    }

    private void Fill(IReadOnlyList<GalleryEntry> entries)
    {
        var lang = Loc.Instance.Language;
        Tiles.Clear();
        foreach (var e in entries)
            Tiles.Add(new GalleryTile(e.Id, e.Name.In(lang), AssistantLook.Image(Owner.Assistant, e.Image, 160), e.Locked, e.LockText?.In(lang) ?? string.Empty)
            {
                IsSelected = e.Id == _selected,
            });
        Notify(nameof(IsEmpty));
    }

    public void Choose(GalleryTile tile)
    {
        if (tile.Locked || tile.Id == _selected) return;
        _selected = tile.Id;
        foreach (var t in Tiles) t.IsSelected = t.Id == _selected;
        Save(JsonValue.Create(tile.Id));
    }

    public override void Load(JsonObject values)
    {
        _selected = ValueIn(values) is { } v ? AssistantUi.Plain(v) : null;
        foreach (var t in Tiles) t.IsSelected = t.Id == _selected;
    }
}

/// <summary>A list of applications (process names), chosen as everywhere in K-Desktop.</summary>
public sealed class AppsSetting : SettingModel
{
    private bool _loading;

    public AppsSetting(AssistantSettingsModel owner, SettingItem item) : base(owner, item) =>
        Apps.CollectionChanged += (_, _) =>
        {
            Notify(nameof(IsEmpty));
            if (!_loading) Save(new JsonArray([.. Apps.Select(a => (JsonNode)JsonValue.Create(a))]));
        };

    public ObservableCollection<string> Apps { get; } = [];

    public bool IsEmpty => Apps.Count == 0;

    public override void Load(JsonObject values)
    {
        _loading = true;
        Apps.Clear();
        if (ValueIn(values) is JsonArray list)
            foreach (var name in list.Select(AssistantUi.Plain).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                Apps.Add(name);
        _loading = false;
    }
}

/// <summary>A hotkey for one of its extras: it will create the K-Desktop command («Assistant: action», stage 2.6).</summary>
public sealed class HotkeySetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item);

public sealed class ButtonSetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item)
{
    public SymbolRegular Symbol => AssistantLook.Symbol(Item.Icon) ?? (Item.ResetData ? SymbolRegular.Delete24 : SymbolRegular.Play24);
}

public sealed class InfoSetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item)
{
    public string Text { get; } = AssistantLook.Text(item.Text);
}

public sealed record KeyRowModel(IReadOnlyList<string> Keys, string Text);

public sealed class KeysSetting(AssistantSettingsModel owner, SettingItem item) : SettingModel(owner, item)
{
    public IReadOnlyList<KeyRowModel> Rows { get; } = [.. item.KeyRows.Select(r => new KeyRowModel(r.Keys, AssistantLook.Text(r.Text)))];
}

/// <summary>
/// The settings of one installed assistant while they are shown: its pages, the values (defaults under what the user
/// changed) and saving. Changes are saved and sent to the assistant a moment later, at most a few times a second
/// while a slider is dragged.
/// </summary>
public sealed class AssistantSettingsModel : Observable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(120);

    private readonly App _app = App.Current;
    private readonly JsonObject _defaults;
    private readonly HashSet<string> _keys;
    private readonly List<SettingModel> _all = [];
    private readonly System.Windows.Threading.DispatcherTimer _save;
    private JsonObject _saved;
    private JsonObject _values;
    private SettingsPageModel? _page;

    public AssistantSettingsModel(InstalledAssistant assistant, SettingsLayout layout)
    {
        Assistant = assistant;
        _defaults = layout.Defaults();
        _keys = [.. layout.Items.Select(i => i.Key).OfType<string>()];
        _values = AssistantSettingsValues.Read(assistant.Folder, assistant.Manifest);
        // Only the keys this version still has (an older one may have left others).
        _saved = AssistantSettingsValues.ReadSaved(assistant.Id);
        foreach (var gone in _saved.Select(p => p.Key).Where(k => !_keys.Contains(k)).ToList()) _saved.Remove(gone);

        Pages =
        [
            .. layout.Pages.Select(p => new SettingsPageModel(
                AssistantLook.Text(p.Title),
                AssistantLook.Symbol(p.Icon),
                [.. p.Sections.Select(s => new SettingsSectionModel(
                    s.Title is null ? null : AssistantLook.Text(s.Title),
                    [.. s.Items.Select(i => SettingModel.Create(this, i)).OfType<SettingModel>()]))],
                page => CurrentPage = (SettingsPageModel)page))
        ];
        _all.AddRange(Pages.SelectMany(p => p.Sections).SelectMany(s => s.Items));
        foreach (var item in _all) item.Load(_values);
        UpdateEnabled();
        if (Pages.Count > 0) CurrentPage = Pages[0];

        _save = new System.Windows.Threading.DispatcherTimer { Interval = SaveDelay };
        _save.Tick += (_, _) => Flush();
    }

    public InstalledAssistant Assistant { get; }

    public IReadOnlyList<SettingsPageModel> Pages { get; }

    public bool HasTabs => Pages.Count > 1;

    public SettingsPageModel? CurrentPage
    {
        get => _page;
        set
        {
            if (value is null || ReferenceEquals(_page, value)) return;
            _page = value;
            foreach (var page in Pages) page.Mark(ReferenceEquals(page, value));
            Notify();
        }
    }

    /// <summary>Whether the user changed anything (what «Reset» would undo).</summary>
    public bool HasChanges => _saved.Count > 0;

    /// <summary>What it published for its panel (a gallery with «source» shows it).</summary>
    public JsonObject? PanelData => _app.AssistantPanelData(Assistant.Id);


    /// <summary>A control changed: remember it (only if it differs from the default) and save soon.</summary>
    public void Set(SettingItem item, JsonNode? value)
    {
        if (item.Key is not { } key) return;
        _values[key] = value?.DeepClone();
        if (JsonNode.DeepEquals(value, _defaults[key])) _saved.Remove(key);
        else _saved[key] = value?.DeepClone();
        if (_all.Any(i => i.Item.EnabledWhen is { } when && when.TrimStart('!') == key)) UpdateEnabled();
        Notify(nameof(HasChanges));
        _save.Stop();
        _save.Start();
    }

    /// <summary>Saves now what is still waiting (when the page closes).</summary>
    public void Flush()
    {
        if (!_save.IsEnabled) return;
        _save.Stop();
        _app.SaveAssistantSettings(Assistant, _saved, _values);
    }

    /// <summary>Back to the package's defaults.</summary>
    public void Reset()
    {
        _saved = [];
        _values = (JsonObject)_defaults.DeepClone();
        foreach (var item in _all) item.Load(_values);
        UpdateEnabled();
        Notify(nameof(HasChanges));
        _save.Stop();
        _app.SaveAssistantSettings(Assistant, _saved, _values);
    }

    public void PanelChanged()
    {
        foreach (var gallery in _all.OfType<GallerySetting>()) gallery.PanelChanged();
    }

    private void UpdateEnabled()
    {
        foreach (var item in _all) item.IsEnabled = AssistantUi.IsEnabled(item.Item.EnabledWhen, _values);
    }
}
