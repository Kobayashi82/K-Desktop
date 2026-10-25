using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Catalog (phase 12): the assistants the catalogs publish, with their icon, version, author and what
/// they are (read from each package), and «Instalar…» / «Actualizar…» (download and its card); and the catalogs that are
/// read, K-Desktop's own first, with others the user adds. Made in code (the rows' buttons stay visible to UI Automation).
/// </summary>
public partial class AssistantCatalogSection : UserControl
{
    private readonly App _app = App.Current;
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.Ordinal);
    private string? _shown;
    private string _newCatalog = string.Empty;
    private string? _catalogError;

    public AssistantCatalogSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantCatalogChanged += Refresh;
            _app.AssistantChanged += Refresh;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantCatalogChanged -= Refresh;
            _app.AssistantChanged -= Refresh;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            _ = _app.LoadAssistantCatalogAsync(force: false);
            Refresh();
        };
    }

    private void Refresh()
    {
        if (!IsVisible) return;
        var result = _app.AssistantCatalogResult;
        var signature = string.Join("\n", new[]
        {
            _app.LoadingAssistantCatalog.ToString(), _catalogError ?? "", Loc.Instance.Language, string.Join(",", _app.Config.Settings.AssistantCatalogs),
            _app.AssistantCatalogRead?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        }.Concat((result?.Items ?? []).Select(i => $"{i.Id}|{i.State}|{i.Installed}|{_app.IsDownloadingFromCatalog(i.Id)}")));
        if (signature == _shown) return;
        _shown = signature;
        Body.Children.Clear();
        FillAssistants(result);
        FillCatalogs(result);
    }

    private void FillAssistants(CatalogResult? result)
    {
        var card = Card(Loc.T("assistantCatalog.title"), Loc.T("assistantCatalog.intro"), SymbolRegular.AppsAddIn24);
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var again = Wide(SymbolRegular.ArrowSync24, Loc.T(_app.LoadingAssistantCatalog ? "assistantCatalog.loading" : "assistantCatalog.refresh"),
            () => _ = _app.LoadAssistantCatalogAsync(force: true), enabled: !_app.LoadingAssistantCatalog);
        top.Children.Add(again);
        if (_app.AssistantCatalogRead is { } read && !_app.LoadingAssistantCatalog)
            top.Children.Add(new TextBlock { Style = (Style)FindResource("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 8),
                Text = Loc.T("assistantCatalog.readAt", read.ToString("t", Loc.Instance.Culture)) });
        card.Children.Add(top);

        var items = (result?.Items ?? []).OrderBy(i => i.State == CatalogState.Failed).ThenBy(i => i.Name, StringComparer.Create(Loc.Instance.Culture, true)).ToList();
        if (items.Count == 0)
            card.Children.Add(Caption(Loc.T(result is null || _app.LoadingAssistantCatalog ? "assistantCatalog.loadingList" : "assistantCatalog.empty")));
        foreach (var item in items) card.Children.Add(ItemRow(item));
        Body.Children.Add(Wrap(card));
    }

    private FrameworkElement ItemRow(CatalogItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBox = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
        iconBox.SetResourceReference(Border.BackgroundProperty, "Kd.ChipBackground");
        if (IconOf(item) is { } image) iconBox.Child = new Image { Source = image, Margin = new Thickness(4), Stretch = Stretch.Uniform };
        else
        {
            var symbol = new SymbolIcon { Symbol = SymbolRegular.Bot24, FontSize = 22 };
            symbol.SetResourceReference(ForegroundProperty, "Kd.AccentText");
            iconBox.Child = symbol;
        }
        grid.Children.Add(iconBox);

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = item.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        text.Children.Add(name);
        if (item.Manifest is { } manifest)
        {
            text.Children.Add(Caption(Loc.T("assistantCatalog.byline", manifest.Version, manifest.Author)));
            if (AssistantLook.Text(manifest.Description) is { Length: > 0 } about) text.Children.Add(Caption(about, margin: new Thickness(0, 3, 0, 0)));
        }
        var (stateText, brush) = StateLine(item);
        if (stateText.Length > 0)
        {
            var state = new TextBlock { Text = stateText, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            state.SetResourceReference(TextBlock.ForegroundProperty, brush);
            text.Children.Add(state);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var downloading = _app.IsDownloadingFromCatalog(item.Id);
        Button? action = item.State switch
        {
            CatalogState.Available => Wide(SymbolRegular.ArrowDownload24, Loc.T(downloading ? "assistantCatalog.downloading" : "assistantCatalog.install"),
                () => _ = _app.OpenFromCatalogAsync(item), primary: true, enabled: !downloading, target: item.Name),
            CatalogState.Update => Wide(SymbolRegular.ArrowSync24, Loc.T(downloading ? "assistantCatalog.downloading" : "assistantCatalog.updateButton"),
                () => _ = _app.OpenFromCatalogAsync(item), primary: true, enabled: !downloading, target: item.Name),
            _ => null,
        };
        // What it can do (clips), what it is and what it needs, before installing it: whenever its package could be read.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (item.Manifest is not null)
        {
            var details = Wide(SymbolRegular.Eye24, Loc.T("assistantCatalog.details"), () => _app.OpenCatalogDetail(item), target: item.Name);
            details.Margin = new Thickness(0);
            buttons.Children.Add(details);
        }
        if (action is not null)
        {
            action.Margin = new Thickness(buttons.Children.Count > 0 ? 8 : 0, 0, 0, 0);
            buttons.Children.Add(action);
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var row = new Border { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = grid };
        row.SetResourceReference(Border.BackgroundProperty, "Kd.Subtle");
        row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return row;
    }

    /// <summary>How it is here (installed, an update, it needs a newer K-Desktop, it failed), and the brush for it; empty when available.</summary>
    internal static (string Text, string Brush) StateLine(CatalogItem item) => item.State switch
    {
        CatalogState.Installed => (Loc.T("assistantCatalog.installed", item.Installed ?? ""), "Kd.Success"),
        CatalogState.Update => (Loc.T("assistantCatalog.update", item.Installed ?? "", item.Manifest?.Version ?? ""), "Kd.AccentText"),
        CatalogState.NeedsNewerKDesktop => (Loc.T("assistantCatalog.needsKDesktop"), "Kd.Warning"),
        CatalogState.Failed => (item.Error ?? Loc.T("assistantCatalog.failed"), "Kd.Danger"),
        _ => (string.Empty, string.Empty),
    };

    private ImageSource? IconOf(CatalogItem item)
    {
        if (item.Icon is not { } bytes) return null;
        var key = item.Id + "/" + item.Manifest?.Version;
        if (_icons.TryGetValue(key, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 88;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException) { }
        _icons[key] = image;
        return image;
    }

    private void FillCatalogs(CatalogResult? result)
    {
        var card = Card(Loc.T("assistantCatalog.catalogs"), Loc.T("assistantCatalog.catalogsIntro"), SymbolRegular.Globe24);
        foreach (var url in _app.Config.Settings.AssistantCatalogs)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var official = url.Equals(AssistantCatalog.Official, StringComparison.OrdinalIgnoreCase);
            var title = new TextBlock { Text = official ? Loc.T("assistantCatalog.official") : url, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            text.Children.Add(title);
            if (official) text.Children.Add(Caption(url));
            if (result?.Failed.TryGetValue(url, out var why) == true)
            {
                var error = new TextBlock { Text = Loc.T("assistantCatalog.catalogFailed", why), FontSize = 12, TextWrapping = TextWrapping.Wrap };
                error.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Warning");
                text.Children.Add(error);
            }
            grid.Children.Add(text);
            var remove = Wide(SymbolRegular.Delete24, Loc.T("assistantCatalog.remove"), () => _app.RemoveAssistantCatalog(url), target: url);
            remove.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(remove, 1);
            grid.Children.Add(remove);
            card.Children.Add(grid);
        }
        if (_app.Config.Settings.AssistantCatalogs.Count == 0) card.Children.Add(Caption(Loc.T("assistantCatalog.noCatalogs")));

        var add = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { Text = _newCatalog, PlaceholderText = "https://…/catalog.json" };
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T("assistantCatalog.addHint"));
        box.TextChanged += (_, _) => _newCatalog = box.Text;
        void Add()
        {
            if (_app.AddAssistantCatalog(_newCatalog, out var why))
            {
                _newCatalog = string.Empty;
                _catalogError = null;
            }
            else _catalogError = why;
            _shown = null;
            Refresh();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            Add();
        };
        add.Children.Add(box);
        var button = Wide(SymbolRegular.Add24, Loc.T("assistantCatalog.add"), Add);
        button.Margin = new Thickness(8, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Stretch; // as tall as the box beside it
        Grid.SetColumn(button, 1);
        add.Children.Add(button);
        card.Children.Add(add);
        if (_catalogError is not null)
        {
            var error = new TextBlock { Text = _catalogError, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            error.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
            card.Children.Add(error);
        }
        card.Children.Add(Caption(Loc.T("assistantCatalog.trust"), margin: new Thickness(0, 8, 0, 0)));
        Body.Children.Add(Wrap(card));
    }

    // ── pieces ───────────────────────────────────────────────────────────

    private StackPanel Card(string title, string? intro, SymbolRegular icon)
    {
        var content = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var symbol = new SymbolIcon { Symbol = icon, FontSize = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        symbol.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        head.Children.Add(symbol);
        head.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = title, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(head);
        if (intro is not null) content.Children.Add(Caption(intro, margin: new Thickness(0, 0, 0, 10)));
        return content;
    }

    private Border Wrap(StackPanel content) => new() { Style = (Style)FindResource("Card"), Child = content };

    private TextBlock Caption(string text, Thickness? margin = null) =>
        new() { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 1, 0, 0) };

    private static Button Wide(SymbolRegular symbol, string text, Action click, bool primary = false, bool enabled = true, string? target = null)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 36,
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
            IsEnabled = enabled,
            Margin = new Thickness(0, 0, 8, 8),
        };
        // The name says which one it acts on (several rows have the same buttons).
        System.Windows.Automation.AutomationProperties.SetName(button, target is null ? text : $"{text} {target}");
        button.Click += (_, _) => click();
        return button;
    }
}
