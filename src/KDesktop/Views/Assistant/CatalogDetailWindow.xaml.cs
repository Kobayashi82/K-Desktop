using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// An assistant of a catalog, before installing it (stage 16.3): the clips of its showcase, one at a time (a pill per clip,
/// grouped as in the «Probar» tab), and what it is, what is new, its voice, the model it suggests and how big it is. The
/// package is opened at its address and only what is shown is downloaded (<see cref="RemotePackage"/>: its directory and
/// manifest first, then each clip when it is chosen). Installing it is the catalog's usual way (download and its card).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The player, the package and the token sources are let go when the window closes.")]
public partial class CatalogDetailWindow : FluentWindow
{
    /// <summary>The host's groups, in the order the «Probar» tab shows them.</summary>
    private static readonly string[] HostGroups = ["entrances", "exits", "states", "reactions", "gestures", "windows"];

    /// <summary>The largest clip it shows (as a package may have).</summary>
    private const long MaxClip = 8L << 20;

    /// <summary>Clips kept after they were downloaded, so going back to one does not download it again.</summary>
    private const long KeptClips = 48L << 20;

    private readonly App _app = App.Current;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _closing = new();
    private readonly Dictionary<string, byte[]> _kept = new(StringComparer.Ordinal);
    private readonly List<string> _keptOrder = [];
    private readonly Dictionary<string, RadioButton> _pills = new(StringComparer.Ordinal);
    private readonly Image _picture;
    private readonly TextBlock _cover;
    private readonly TextBlock _clipName;
    private readonly ClipPlayer _player;
    private CatalogItem _item;
    private RemotePackage? _package;
    private VoiceProfile? _voice;
    private string? _opening;
    private string? _current;
    private CancellationTokenSource? _choosing;

    public CatalogDetailWindow(CatalogItem item, HttpClient http)
    {
        InitializeComponent();
        _item = item;
        _http = http;
        Title = item.Name;
        Bar.Title = item.Name;

        _picture = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        _cover = Caption(Loc.T("catalogDetail.opening"));
        _cover.HorizontalAlignment = HorizontalAlignment.Center;
        _cover.VerticalAlignment = VerticalAlignment.Center;
        _clipName = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        _clipName.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _player = new ClipPlayer(_picture);
        _player.Failed += () => ShowCover(Loc.T("catalogDetail.clipFailed"));

        FillHeader();
        FillAbout();
        FillClips();
        Loaded += async (_, _) => await OpenAsync();
        _app.AssistantCatalogChanged += OnCatalogChanged;
        Closed += (_, _) =>
        {
            _app.AssistantCatalogChanged -= OnCatalogChanged;
            _closing.Cancel();
            _choosing?.Cancel();
            _player.Dispose();
            _package?.Dispose();
            _closing.Dispose();
        };
    }

    public string Id => _item.Id;

    /// <summary>Opens its package at its address (directory, manifest, voice).</summary>
    private async Task OpenAsync()
    {
        _opening = Loc.T("catalogDetail.opening");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            timeout.CancelAfter(AssistantCatalog.Timeout);
            _package = await RemotePackage.OpenAsync(_item.Package, _http, timeout.Token);
            _voice = await _package.ReadVoiceAsync(timeout.Token);
            Log.Info($"Asistentes: catálogo, ficha de «{_item.Id}» abierta ({_package.Fetched / 1024.0:0.#} KB leídos de {_package.Size / 1024.0:0} KB)");
            _opening = null;
        }
        catch (Exception ex) when (!_closing.IsCancellationRequested)
        {
            Log.Warn($"Asistentes: catálogo, no se ha podido abrir «{_item.Id}» en {_item.Package} ({ex.Message})");
            _opening = Loc.T("catalogDetail.failed", ex is OperationCanceledException ? Loc.T("updates.timeout") : ex is HttpRequestException ? Loc.T("updates.noConnection") : ex.Message);
        }
        catch (OperationCanceledException) { return; }
        FillAbout();
        FillClips();
    }

    // ── header ─────────────────────────────────────────────────────────────

    private void OnCatalogChanged()
    {
        if (_app.AssistantCatalogResult?.Items.FirstOrDefault(i => i.Id.Equals(_item.Id, StringComparison.OrdinalIgnoreCase)) is { } now) _item = now;
        FillHeader();
    }

    private void FillHeader()
    {
        Header.Children.Clear();
        Header.ColumnDefinitions.Clear();
        Header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBox = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top };
        iconBox.SetResourceReference(Border.BackgroundProperty, "Kd.ChipBackground");
        if (IconOf(_item.Icon) is { } icon) iconBox.Child = new Image { Source = icon, Margin = new Thickness(6), Stretch = Stretch.Uniform };
        else
        {
            var symbol = new SymbolIcon { Symbol = SymbolRegular.Bot24, FontSize = 28 };
            symbol.SetResourceReference(ForegroundProperty, "Kd.AccentText");
            iconBox.Child = symbol;
        }
        Header.Children.Add(iconBox);

        var text = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = _item.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        text.Children.Add(name);
        if (_item.Manifest is { } manifest) text.Children.Add(Caption(Loc.T("assistantCatalog.byline", manifest.Version, manifest.Author)));
        var (stateText, brush) = AssistantCatalogSection.StateLine(_item);
        if (stateText.Length > 0)
        {
            var state = new TextBlock { Text = stateText, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            state.SetResourceReference(TextBlock.ForegroundProperty, brush);
            text.Children.Add(state);
        }
        Grid.SetColumn(text, 1);
        Header.Children.Add(text);

        var downloading = _app.IsDownloadingFromCatalog(_item.Id);
        var (symbolOf, label) = _item.State switch
        {
            CatalogState.Available => (SymbolRegular.ArrowDownload24, "assistantCatalog.install"),
            CatalogState.Update => (SymbolRegular.ArrowSync24, "assistantCatalog.updateButton"),
            _ => (SymbolRegular.Empty, null),
        };
        if (label is null) return;
        var action = new Button
        {
            Content = Loc.T(downloading ? "assistantCatalog.downloading" : label),
            Icon = new SymbolIcon { Symbol = symbolOf },
            Appearance = ControlAppearance.Primary,
            IsEnabled = !downloading,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(action, $"{action.Content} {_item.Name}");
        action.Click += (_, _) => _ = _app.OpenFromCatalogAsync(_item);
        Grid.SetColumn(action, 2);
        Header.Children.Add(action);
    }

    private static BitmapImage? IconOf(byte[]? bytes)
    {
        if (bytes is null) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 120;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException) { return null; }
    }

    // ── about ──────────────────────────────────────────────────────────────

    private void FillAbout()
    {
        About.Children.Clear();
        var manifest = _package?.Manifest ?? _item.Manifest;
        if (manifest is null) return;
        if (AssistantLook.Text(manifest.Description) is { Length: > 0 } description) Section(Loc.T("catalogDetail.about"), description);
        if (AssistantLook.Text(manifest.Changes) is { Length: > 0 } changes) Section(Loc.T("catalogDetail.changes", manifest.Version), changes);

        var voice = _package is null && _opening is not null ? "…"
            : _voice is { } profile ? Loc.T("catalogDetail.voiceOf", profile.Name, EngineName(profile.Engine))
            : Loc.T("catalogDetail.noVoice");
        Section(Loc.T("catalogDetail.voice"), voice);
        if (manifest.WakeWord is { Length: > 0 } wake) Section(Loc.T("catalogDetail.wakeWord"), wake);

        var models = manifest.Model?.Models ?? [];
        Section(Loc.T("catalogDetail.model"), models.Count > 0 ? string.Join("\n", models.Select(m => m.ToString())) : Loc.T("catalogDetail.noModel"));
        if (manifest.Model?.ThinkModels is { Count: > 0 } think) Section(Loc.T("catalogDetail.think"), string.Join("\n", think.Select(m => m.ToString())));

        if (_package is { } package) Section(Loc.T("catalogDetail.size"), Loc.T("catalogDetail.megabytes", (package.Size / 1048576.0).ToString("0.#", Loc.Instance.Culture)));
        if (manifest.License is { Length: > 0 } license) Section(Loc.T("catalogDetail.license"), license);
        if (manifest.Homepage is { } homepage && Uri.TryCreate(homepage, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            var link = new Hyperlink(new Run(homepage)) { NavigateUri = uri };
            link.RequestNavigate += (_, e) =>
            {
                ProcessLauncher.Launch(new LaunchSpec(e.Uri.ToString()), asAdmin: false);
                e.Handled = true;
            };
            var line = new TextBlock(link) { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12) };
            About.Children.Add(Heading(Loc.T("catalogDetail.homepage")));
            About.Children.Add(line);
        }
    }

    private static string EngineName(string engine)
    {
        var key = "catalogDetail.engine." + engine;
        var text = Loc.T(key);
        return text == key ? engine : text;
    }

    private void Section(string title, string text)
    {
        About.Children.Add(Heading(title));
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        About.Children.Add(body);
    }

    private TextBlock Heading(string text) => new() { Style = (Style)FindResource("Caption"), Text = text, FontWeight = FontWeights.SemiBold };

    // ── clips ──────────────────────────────────────────────────────────────

    private void FillClips()
    {
        Clips.Children.Clear();
        Player.Children.Clear();
        _pills.Clear();
        // The picture, the line over it and the clip's name are kept from one fill to the next.
        if (_picture.Parent is Border oldBorder) oldBorder.Child = null;
        if (_cover.Parent is Panel old) old.Children.Remove(_cover);
        _picture.MaxHeight = 380;
        var frame = new Grid { MinHeight = 200 };
        var border = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, BorderThickness = new Thickness(1), Child = _picture };
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        // Room for a clip of the stage (16:9) before it comes.
        border.MinHeight = 200;
        frame.Children.Add(border);
        frame.Children.Add(_cover);
        Player.Children.Add(frame);
        Player.Children.Add(_clipName);

        if (_package is not { } package)
        {
            ShowCover(_opening ?? Loc.T("catalogDetail.opening"));
            return;
        }
        var clips = package.Manifest.Showcase.Where(c => package.Has(c.Clip)).ToList();
        if (clips.Count == 0)
        {
            _picture.Source = null;
            ShowCover(Loc.T("catalogDetail.noClips", _item.Name));
            return;
        }

        var entries = clips.Select(c => (Clip: c, Entry: EntryOf(c))).ToList();
        var own = entries.Where(e => e.Entry.Own).GroupBy(e => e.Entry.Group).Select(g => (g.Key, g.ToList())).ToList();
        var host = HostGroups.Select(g => (g, entries.Where(e => !e.Entry.Own && e.Entry.Group == g).ToList())).ToList();
        Groups(Loc.T("assistantTry.own"), own);
        Groups(Loc.T("assistantTry.host"), host);

        // The first of its own (what makes it different), or else the first one.
        var first = own.SelectMany(g => g.Item2).Concat(entries).First();
        Choose(first.Clip, first.Entry);
    }

    private static ShowcaseEntry EntryOf(ManifestClip clip)
    {
        var standard = ShowcaseItems.Standard.FirstOrDefault(s => s.Id == clip.Item);
        return standard.Id is not null
            ? new ShowcaseEntry(clip.Item, standard.Group)
            : new ShowcaseEntry(clip.Item, clip.Group ?? "other", true, clip.Name?.Texts.Where(t => t.Key.Length > 0).ToDictionary(t => t.Key, t => t.Value) is { Count: > 0 } names
                ? names
                : clip.Name is { } plain ? new Dictionary<string, string> { ["en"] = plain.In("en") } : null);
    }

    private void Groups(string title, IReadOnlyList<(string Group, List<(ManifestClip Clip, ShowcaseEntry Entry)> Entries)> groups)
    {
        if (groups.All(g => g.Entries.Count == 0)) return;
        var heading = new TextBlock { Style = (Style)FindResource("CardTitle"), Text = title, Margin = new Thickness(0, Clips.Children.Count == 0 ? 6 : 16, 0, 0) };
        Clips.Children.Add(heading);
        foreach (var (group, entries) in groups)
        {
            if (entries.Count == 0) continue;
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Caption(App.ShowcaseGroupName(group));
            label.Margin = new Thickness(0, 6, 10, 0);
            row.Children.Add(label);
            var wrap = new WrapPanel();
            foreach (var (clip, entry) in entries) wrap.Children.Add(PillFor(clip, entry));
            Grid.SetColumn(wrap, 1);
            row.Children.Add(wrap);
            Clips.Children.Add(row);
        }
    }

    private RadioButton PillFor(ManifestClip clip, ShowcaseEntry entry)
    {
        var name = App.ShowcaseName(entry);
        var pill = new RadioButton { Style = (Style)FindResource("Pill"), Content = name, GroupName = "clips" };
        AutomationProperties.SetName(pill, name);
        pill.Checked += (_, _) =>
        {
            if (_current != clip.Clip) Choose(clip, entry);
        };
        _pills[clip.Clip] = pill;
        return pill;
    }

    /// <summary>Shows a clip: from what was kept, or downloaded now (the one chosen before stops downloading).</summary>
    private async void Choose(ManifestClip clip, ShowcaseEntry entry)
    {
        if (_package is not { } package) return;
        _current = clip.Clip;
        if (_pills.TryGetValue(clip.Clip, out var pill)) pill.IsChecked = true;
        _clipName.Text = App.ShowcaseName(entry);
        _choosing?.Cancel();
        var choosing = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _choosing = choosing;
        try
        {
            if (!_kept.TryGetValue(clip.Clip, out var bytes))
            {
                _player.Stop();
                ShowCover(Loc.T("catalogDetail.clipLoading"));
                bytes = await package.ReadAsync(clip.Clip, MaxClip, choosing.Token);
                if (choosing.IsCancellationRequested) return;
                if (bytes is null)
                {
                    _picture.Source = null;
                    ShowCover(Loc.T("catalogDetail.clipFailed"));
                    return;
                }
                Keep(clip.Clip, bytes);
            }
            ShowCover(null);
            _player.Play(bytes);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or ObjectDisposedException)
        {
            if (choosing.IsCancellationRequested) return;
            Log.Warn($"Asistentes: catálogo, no se ha podido bajar el clip {clip.Clip} de «{_item.Id}» ({ex.Message})");
            _picture.Source = null;
            ShowCover(Loc.T("catalogDetail.clipFailed"));
        }
    }

    private void Keep(string clip, byte[] bytes)
    {
        _kept[clip] = bytes;
        _keptOrder.Remove(clip);
        _keptOrder.Add(clip);
        while (_keptOrder.Count > 1 && _kept.Values.Sum(b => (long)b.Length) > KeptClips)
        {
            _kept.Remove(_keptOrder[0]);
            _keptOrder.RemoveAt(0);
        }
    }

    private void ShowCover(string? text)
    {
        _cover.Text = text ?? "";
        _cover.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        _picture.Opacity = text is null ? 1 : 0.35;
    }

    private TextBlock Caption(string text) => new() { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap };
}
