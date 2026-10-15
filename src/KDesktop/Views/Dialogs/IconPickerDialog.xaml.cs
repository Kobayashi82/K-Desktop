using System.IO;
using System.Windows.Media.Imaging;
using KDesktop.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;
using KDesktop.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>One icon in the picker grid.</summary>
public partial class IconChoice : ObservableObject
{
    public required string Reference { get; init; }
    public string Tip { get; init; } = string.Empty;
    public string? Caption { get; init; }
    public Visibility CaptionVisibility => Caption is null ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty] private ImageSource? _image;
}

/// <summary>
/// Icon picker: the K-Desktop 5 collection (any symbol, any colour, tile or plain symbol), the icons inside Windows
/// libraries and programs (shell32.dll, imageres.dll… with their index) or an image file.
/// </summary>
public partial class IconPickerDialog : FluentWindow
{
    private sealed record Category(string Key, string Name);

    private static readonly string[] Libraries =
    [
        "imageres.dll", "shell32.dll", "ddores.dll", "mmres.dll", "wmploc.dll", "netshell.dll", "setupapi.dll", "dsuiext.dll",
        "mmcndmgr.dll", "moricons.dll", "pifmgr.dll", "compstui.dll", "ieframe.dll", "accessibilitycpl.dll", "networkexplorer.dll",
        "wpdshext.dll", "comres.dll", "pnidui.dll", "sensorscpl.dll", "%SystemRoot%\\explorer.exe",
    ];

    private const string AutoColor = "auto";
    private readonly string _baseName;
    private string _color = AutoColor;
    private KdIconStyle _style = KdIconStyle.Tile;
    private string _category = "all";
    private string? _library;
    private CancellationTokenSource? _loading;
    private bool _ready;

    /// <summary>The chosen icon; empty string = back to the automatic icon.</summary>
    public string? Result { get; private set; }

    private IconPickerDialog(string? current, string baseName)
    {
        InitializeComponent();
        _baseName = baseName;

        CategoryList.ItemsSource = new[] { "all" }.Concat(KdIcons.Categories).Select(c => new Category(c, Loc.T($"icons.cat.{c}"))).ToList();
        BuildSwatches();
        LibraryBox.ItemsSource = Libraries.Where(l => File.Exists(IconProvider.ResolveLibrary(Environment.ExpandEnvironmentVariables(l)))).ToList();

        // Open where the current icon is.
        if (KdIconSpec.TryParse(current, out var kd))
        {
            _style = kd.Style;
            (_style == KdIconStyle.Glyph ? GlyphStyle : TileStyle).IsChecked = true;
            var entry = KdIcons.All.FirstOrDefault(e => e.Symbol == kd.Symbol);
            if (entry is null || !entry.Color.Equals(kd.Color, StringComparison.OrdinalIgnoreCase)) SelectSwatch(kd.Color);
            if (entry is not null) _category = entry.Category;
        }
        else if (IconProvider.TryParseLibrary(current, out var file, out _))
        {
            _library = file;
            WinTab.IsChecked = true;
        }

        CategoryList.SelectedItem = CategoryList.Items.OfType<Category>().First(c => c.Key == _category);
        _ready = true;
        ShowKd(select: current);
        if (_library is not null) LoadLibrary(_library, select: current);

        Loaded += (_, _) => SearchBox.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };
    }

    /// <summary>Shows the picker. Returns the icon reference, "" for "automatic" or null when cancelled.</summary>
    public static string? Pick(Window? owner, string? current, string baseName)
    {
        var dlg = new IconPickerDialog(current, baseName) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    // ── tabs ─────────────────────────────────────────────────────────────

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || KdPanel is null) return;
        var kd = KdTab.IsChecked == true;
        Show(kd ? KdPanel : WinPanel, kd ? WinPanel : KdPanel);
        SearchBox.Visibility = kd ? Visibility.Visible : Visibility.Collapsed;
        if (!kd && _library is null && LibraryBox.Items.Count > 0) LibraryBox.SelectedIndex = 0;
        UpdatePreview((kd ? KdList : WinList).SelectedItem as IconChoice);
    }

    private static void Show(FrameworkElement show, FrameworkElement hide)
    {
        hide.Visibility = Visibility.Collapsed;
        show.Visibility = Visibility.Visible;
        show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    // ── K-Desktop collection ─────────────────────────────────────────────

    private void BuildSwatches()
    {
        var auto = new RadioButton
        {
            Style = (Style)FindResource("Swatch"), GroupName = "color", IsChecked = true, Tag = AutoColor,
            ToolTip = Loc.T("icons.colorAuto"),
            Background = new LinearGradientBrush(KdIcons.Gradient("orange").Top, KdIcons.Gradient("blue").Bottom, 45),
        };
        auto.Checked += OnSwatch;
        Swatches.Children.Add(auto);
        foreach (var (key, top, bottom) in KdIcons.Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"), GroupName = "color", Tag = key,
                ToolTip = Loc.T($"icons.color.{key}"),
                Background = new LinearGradientBrush(top, bottom, 90),
            };
            swatch.Checked += OnSwatch;
            Swatches.Children.Add(swatch);
        }
    }

    private void SelectSwatch(string color)
    {
        foreach (var s in Swatches.Children.OfType<RadioButton>())
            if (string.Equals((string)s.Tag, color, StringComparison.OrdinalIgnoreCase)) s.IsChecked = true;
        _color = color;
    }

    private void OnSwatch(object sender, RoutedEventArgs e)
    {
        _color = (string)((RadioButton)sender).Tag;
        if (_ready) ShowKd(select: (KdList.SelectedItem as IconChoice)?.Reference);
    }

    private void OnStyle(object sender, RoutedEventArgs e)
    {
        _style = GlyphStyle?.IsChecked == true ? KdIconStyle.Glyph : KdIconStyle.Tile;
        if (_ready) ShowKd(select: (KdList.SelectedItem as IconChoice)?.Reference);
    }

    private void OnCategory(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not Category category) return;
        _category = category.Key;
        if (!_ready) return;
        if (SearchBox.Text.Length > 0) SearchBox.Text = string.Empty; // re-shows through OnSearch
        else ShowKd(select: null);
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        if (_ready) ShowKd(select: (KdList.SelectedItem as IconChoice)?.Reference);
    }

    private KdIconSpec SpecFor(string symbol, string defaultColor) =>
        new(symbol, _color == AutoColor ? defaultColor : _color, _style);

    private void ShowKd(string? select)
    {
        var query = SearchBox.Text.Trim();
        List<IconChoice> items;
        if (query.Length > 0)
        {
            var curated = KdIcons.All.Where(i => i.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                 Loc.T($"icons.cat.{i.Category}").Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            var known = curated.Select(i => i.Symbol).ToHashSet();
            var extra = KdIcons.SearchAllSymbols(query).Where(s => !known.Contains(s)).Take(400);
            items = curated.Select(i => Choice(SpecFor(i.Symbol, i.Color))).Concat(extra.Select(s => Choice(SpecFor(s, "blue")))).ToList();
            KdHint.Text = Loc.T("icons.searchResults", items.Count);
        }
        else
        {
            items = KdIcons.All.Where(i => _category == "all" || i.Category == _category).Select(i => Choice(SpecFor(i.Symbol, i.Color))).ToList();
            KdHint.Text = Loc.T("icons.searchHint");
        }
        KdList.ItemsSource = items;

        // Keep the same symbol selected when the colour or style changes.
        var symbol = KdIconSpec.TryParse(select, out var s) ? s.Symbol : null;
        var match = symbol is null ? null : items.FirstOrDefault(i => KdIconSpec.TryParse(i.Reference, out var x) && x.Symbol == symbol);
        if (match is not null)
        {
            KdList.SelectedItem = match;
            KdList.ScrollIntoView(match);
        }
        if (KdTab.IsChecked == true) UpdatePreview(match);
    }

    private static IconChoice Choice(KdIconSpec spec) => new()
    {
        Reference = spec.ToString(),
        Tip = KdIcons.Humanize(spec.Symbol),
        Image = KdIcons.Render(spec),
    };

    // ── Windows libraries ────────────────────────────────────────────────

    private void OnLibraryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibraryBox.SelectedItem is string lib) LoadLibrary(lib, select: null);
    }

    private void OnLibraryKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LibraryBox.Text.Trim().Length > 0) LoadLibrary(LibraryBox.Text.Trim(), select: null);
    }

    private void OnLibraryLostFocus(object sender, RoutedEventArgs e)
    {
        var text = LibraryBox.Text.Trim();
        if (text.Length > 0 && !string.Equals(text, _library, StringComparison.OrdinalIgnoreCase)) LoadLibrary(text, select: null);
    }

    private void OnBrowseLibrary(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("icons.chooseLibrary"),
            Filter = Loc.T("icons.filterLibraries") + "|*.dll;*.exe;*.cpl;*.icl;*.ocx;*.ico|" + Loc.T("editor.filterAll") + "|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
        };
        if (dlg.ShowDialog(this) == true) LoadLibrary(dlg.FileName, select: null);
    }

    private async void LoadLibrary(string library, string? select)
    {
        _library = library;
        if (!string.Equals(LibraryBox.Text, library, StringComparison.OrdinalIgnoreCase)) LibraryBox.Text = library;
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();

        var file = Environment.ExpandEnvironmentVariables(library);
        WinList.ItemsSource = null;
        WinEmpty.Visibility = Visibility.Collapsed;
        Loading.Visibility = Visibility.Visible;
        var count = await Task.Run(() => IconProvider.LibraryCount(file));
        if (cts.IsCancellationRequested) return;

        var items = Enumerable.Range(0, count).Select(i => new IconChoice
        {
            Reference = IconProvider.LibraryReference(file, i),
            Caption = i.ToString(),
            Tip = $"{Path.GetFileName(file)}, {i}",
        }).ToList();
        WinList.ItemsSource = items;
        LibraryCount.Text = Loc.T("icons.libraryCount", count);
        WinEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (select is not null && items.FirstOrDefault(i => i.Reference.Equals(select, StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            WinList.SelectedItem = match;
            WinList.ScrollIntoView(match);
            if (WinTab.IsChecked == true) UpdatePreview(match);
        }

        // Extract the images in the background, a batch at a time, so the grid fills progressively.
        foreach (var batch in items.Chunk(24))
        {
            var images = await Task.Run(() => batch.Select(i => IconProvider.TryParseLibrary(i.Reference, out var f, out var n) ? IconProvider.Library(f, n) : null).ToList());
            if (cts.IsCancellationRequested) return;
            for (var i = 0; i < batch.Length; i++) batch[i].Image = images[i];
            Loading.Visibility = Visibility.Collapsed;
        }
        Loading.Visibility = Visibility.Collapsed;
    }

    // ── selection and result ─────────────────────────────────────────────

    private void OnIconSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && ReferenceEquals(list, KdTab.IsChecked == true ? KdList : WinList)) UpdatePreview(list.SelectedItem as IconChoice);
    }

    private void UpdatePreview(IconChoice? choice)
    {
        AcceptButton.IsEnabled = choice is not null;
        Preview.Source = choice?.Image ?? (choice is not null ? IconProvider.FromConfigIcon(choice.Reference) : null);
        PreviewName.Text = choice is null ? Loc.T("icons.noSelection") : choice.Tip;
        PreviewRef.Text = choice?.Reference ?? string.Empty;
        if (choice is null) return;
        var pop = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        PreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void OnIconDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement((ListBox)sender, d) is ListBoxItem) OnAccept(sender, e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var list = KdPanel.Visibility == Visibility.Visible ? KdList : WinList;
        if (list.SelectedItem is not IconChoice choice) return;
        Result = choice.Reference;
        DialogResult = true;
    }

    private void OnFromFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("editor.chooseIcon"),
            Filter = Loc.T("editor.filterIcons") + "|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll;*.lnk|" + Loc.T("editor.filterAll") + "|*.*",
            DereferenceLinks = false,
        };
        if (dlg.ShowDialog(this) != true) return;
        if (IconPicker.Import(dlg.FileName, _baseName) is not { } file) return;
        Result = file;
        DialogResult = true;
    }

    private void OnAutomatic(object sender, RoutedEventArgs e)
    {
        Result = string.Empty;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        _loading?.Cancel();
        base.OnClosed(e);
    }
}

/// <summary>Icon choice for commands and QuickMenu items.</summary>
public static class IconPicker
{
    /// <summary>Opens the icon picker. Returns the icon reference, "" for the automatic icon, or null when cancelled.</summary>
    public static string? Pick(Window? owner, string? current, string baseName) => IconPickerDialog.Pick(owner, current, baseName);

    /// <summary>Copies an image (or the icon of an exe/lnk) into the icons folder and returns the file name.</summary>
    public static string? Import(string source, string baseName)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.IconsDir);
            var ext = Path.GetExtension(source).ToLowerInvariant();
            var stamp = DateTime.Now.ToString("yyMMddHHmmss");
            if (ext is ".ico" or ".png" or ".jpg" or ".jpeg" or ".bmp")
            {
                var file = $"{baseName}-{stamp}{ext}";
                File.Copy(source, Path.Combine(AppPaths.IconsDir, file), true);
                return file;
            }
            if (IconProvider.Shell(source) is BitmapSource bmp)
            {
                var file = $"{baseName}-{stamp}.png";
                using var stream = File.Create(Path.Combine(AppPaths.IconsDir, file));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(stream);
                return file;
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo importar el icono", ex);
        }
        return null;
    }
}
