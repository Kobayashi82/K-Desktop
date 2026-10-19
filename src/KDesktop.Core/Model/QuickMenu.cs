using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KDesktop.Core.Model;

public enum QuickMenuItemKind
{
    /// <summary>Runs a command. May also have children (hover opens them, click runs the command).</summary>
    Command,
    /// <summary>Plain container that only opens its children.</summary>
    Submenu,
    /// <summary>Highlighted section header (may have children too).</summary>
    Title,
    Separator,
}

public partial class QuickMenu : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>Size preset 0 (compact) … 5 (large), same scale as K-Desktop 4.</summary>
    [ObservableProperty] private int _size = 1;

    public ObservableCollection<QuickMenuItem> Items { get; set { field = value ?? []; Hook(field); } } = [];

    public QuickMenu() => Hook(Items);

    public event EventHandler? Modified;

    private void Hook(ObservableCollection<QuickMenuItem> items) => QuickMenuItem.HookTree(items, () =>
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        Modified?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>«12 comandos» (in every submenu), under its name in the list.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => Localization.Loc.T("qm.summary", Descendants().Count(i => i.Kind == QuickMenuItemKind.Command));

    /// <summary>Re-evaluates the localised texts (after a language change).</summary>
    public void RefreshTexts() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Modified?.Invoke(this, EventArgs.Empty);
    }

    public IEnumerable<QuickMenuItem> Descendants() => Items.SelectMany(i => i.SelfAndDescendants());

    public override string ToString() => Name;
}

public partial class QuickMenuItem : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private QuickMenuItemKind _kind;
    [ObservableProperty] private string? _commandId;

    /// <summary>Custom label; when null a command item shows the command name.</summary>
    [ObservableProperty] private string? _label;
    [ObservableProperty] private string? _icon;

    public ObservableCollection<QuickMenuItem> Children { get; set { field = value ?? []; _childrenHooked = false; } } = [];

    private bool _childrenHooked;

    [JsonIgnore] public bool IsSeparator => Kind == QuickMenuItemKind.Separator;
    [JsonIgnore] public bool HasChildren => Children.Count > 0;

    public IEnumerable<QuickMenuItem> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }

    public QuickMenuItem DeepClone()
    {
        var copy = Json.Clone(this);
        foreach (var i in copy.SelfAndDescendants()) i.Id = Ids.New();
        return copy;
    }

    /// <summary>Subscribes <paramref name="changed"/> to any change inside the item tree, including items added later.</summary>
    internal static void HookTree(ObservableCollection<QuickMenuItem> items, Action changed)
    {
        void HookItem(QuickMenuItem item)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(IsSeparator) or nameof(HasChildren)) return;
                changed();
            };
            if (!item._childrenHooked)
            {
                item._childrenHooked = true;
                HookCollection(item.Children, item);
            }
        }

        void HookCollection(ObservableCollection<QuickMenuItem> col, QuickMenuItem? owner)
        {
            col.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is not null) foreach (QuickMenuItem n in e.NewItems) HookItem(n);
                owner?.OnPropertyChanged(nameof(HasChildren));
                changed();
            };
            foreach (var i in col) HookItem(i);
        }

        HookCollection(items, null);
    }

    public override string ToString() => Label ?? Kind.ToString();
}
