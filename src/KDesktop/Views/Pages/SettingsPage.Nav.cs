using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace KDesktop.Views.Pages;

/// <summary>
/// The tree of Options (5.0.100): groups that fold —several open at once, remembered—, their sections, and the
/// installed assistants inside «Asistentes»; a search over titles, hints and the text of each page (Ctrl+F). The
/// section shown is <see cref="NavItem.IsCurrent"/>; the tree's own selection is only where the keyboard is.
/// </summary>
public partial class SettingsPage
{
    private sealed record SectionDef(string Key, string Icon, string Text);

    private sealed record GroupDef(string Key, string Title, SectionDef[] Sections);

    /// <summary>The groups and their sections, in order (<c>Text</c>: the key of its title; «.hint» is its hint).</summary>
    private static readonly GroupDef[] Groups =
    [
        new("general", "settings.nav.group.general",
        [
            new("appearance", "Color24", "settings.nav.appearance"),
            new("general", "Settings24", "settings.nav.general"),
            new("osd", "CommentNote24", "settings.nav.osd"),
            new("launcher", "Search24", "settings.nav.launcher"),
            new("mouse", "CursorClick24", "settings.nav.mouse"),
            new("disabledIn", "Prohibited24", "settings.nav.disabledIn"),
        ]),
        new("media", "settings.nav.group.media",
        [
            new("voice", "Mic24", "settings.nav.voice"),
            new("media", "MusicNote224", "settings.nav.media"),
        ]),
        new("connections", "settings.nav.group.connections",
        [
            new("remote", "PlugConnected24", "settings.nav.remote"),
            new("web", "Phone24", "settings.nav.web"),
            new("android", "PhoneUpdate24", "settings.nav.android"),
        ]),
        new("system", "settings.nav.group.system",
        [
            new("capture", "Screenshot24", "settings.nav.capture"),
            new("data", "Database24", "settings.nav.data"),
            new("updates", "ArrowSync24", "settings.nav.updates"),
        ]),
        new("assistant", "settings.nav.assistantGroup",
        [
            new("assistant", "Bot24", "settings.nav.assistant"),
            new("assistants", "PeopleCommunity24", "settings.nav.assistants"),
            new("brain", "BrainCircuit24", "settings.nav.brain"),
            new("tools", "Wrench24", "settings.nav.tools"),
            new("speech", "PersonVoice24", "settings.nav.assistantVoice"),
            new("memory", "BookInformation24", "settings.nav.memory"),
            new("notes", "Note24", "settings.nav.notes"),
        ]),
    ];

    /// <summary>The section shown last, for when the window opens again (saved when it closes); null: none yet.</summary>
    public static string? LastSection { get; private set; }

    private readonly List<NavItem> _groups = [];
    private NavItem? _current;
    private NavItem? _assistantsItem;
    private NavItem? _toolsItem;

    /// <summary>
    /// The pages inside «Herramientas» (stage 9.3; 5.0.133): the built-in tools, the user's JSON ones, the MCP servers —with
    /// one row per server inside— and the secrets. «Herramientas» itself is the summary.
    /// </summary>
    private static readonly SectionDef[] ToolParts =
    [
        new("tools/builtin", "Toolbox24", "settings.nav.toolBuiltin"),
        new("tools/files", "DocumentData24", "settings.nav.toolFiles"),
        new("tools/servers", "PlugConnected24", "settings.nav.toolServers"),
        new("tools/secrets", "Key24", "settings.nav.toolSecrets"),
    ];

    /// <summary>
    /// The pages of «Herramientas» that fold, with one row per thing inside (the subjects, the user's files, the MCP
    /// servers; 5.0.134), and the page that shows one of them.
    /// </summary>
    private sealed record ToolFolder(string Key, string Part);

    private static readonly ToolFolder[] ToolFolders =
    [
        new("tools/builtin", "tools/subject"),
        new("tools/files", "tools/file"),
        new("tools/servers", "tools/server"),
    ];

    private readonly Dictionary<string, NavItem> _toolFolders = new(StringComparer.Ordinal);
    private bool _building;
    private bool _byMouse;
    private bool _searching;
    private HashSet<string>? _openBeforeSearch;

    private void BuildNav()
    {
        // Before the first section is chosen (choosing it shows its page).
        NavTree.AddHandler(TreeViewItem.SelectedEvent, new RoutedEventHandler(OnNavSelected));
        NavTree.AddHandler(TreeViewItem.UnselectedEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is NavItem item) item.ShowFocus = false;
        }));
        _building = true;
        try
        {
            var open = _app.Config.Settings.SettingsNavOpen;
            foreach (var group in Groups)
            {
                var groupItem = new NavItem(NavKind.Group, group.Key) { Style = (Style)FindResource("NavItem") };
                groupItem.Header = GroupHeader(groupItem, group.Title);
                SpeakAs(groupItem, group.Title);
                if (_groups.Count > 0) groupItem.Margin = new Thickness(0, 6, 0, 0);
                foreach (var section in group.Sections)
                {
                    var item = new NavItem(NavKind.Section, section.Key) { Style = (Style)FindResource("NavItem") };
                    item.Header = SectionHeader(item, section.Icon, section.Text, folds: section.Key is "assistants" or "tools");
                    SpeakAs(item, section.Text);
                    groupItem.Items.Add(item);
                    if (section.Key == "assistants") _assistantsItem = item;
                    if (section.Key == "tools") _toolsItem = item;
                }
                groupItem.Expanded += OnNavExpanded;
                groupItem.Collapsed += OnNavExpanded;
                _groups.Add(groupItem);
                NavTree.Items.Add(groupItem);
            }
            _assistantsItem!.Expanded += OnNavExpanded;
            _assistantsItem.Collapsed += OnNavExpanded;
            FillAssistants();
            FillTools();
            _toolsItem!.Expanded += OnNavExpanded;
            _toolsItem.Collapsed += OnNavExpanded;
            foreach (var folder in _toolFolders.Values)
            {
                folder.Expanded += OnNavExpanded;
                folder.Collapsed += OnNavExpanded;
            }
            ApplyAvailability();

            // As it was left: the groups open (none the first time) and the section shown, even inside a folded group.
            var start = Find(LastSection ?? _app.Config.Settings.SettingsSection ?? "appearance") is { } last && IsShown(last) ? last : Find("appearance")!;
            foreach (var group in _groups) group.IsExpanded = open?.Contains(group.Key) == true;
            _assistantsItem.IsExpanded = open?.Contains("assistants") == true;
            _toolsItem.IsExpanded = open?.Contains("tools") == true;
            foreach (var (key, folder) in _toolFolders) folder.IsExpanded = open?.Contains(key) == true;
            foreach (var group in _groups) UpdateChevron(group, animate: false);
            UpdateChevron(_assistantsItem, animate: false);
            UpdateChevron(_toolsItem, animate: false);
            foreach (var folder in _toolFolders.Values) UpdateChevron(folder, animate: false);
            Select(start);
        }
        finally
        {
            _building = false;
        }

        AssistantsSection.Shown += OnAssistantsShown;
        Loc.Instance.Changed += () => _searchIndex = null;
        _app.Config.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Core.Model.AppSettings.AssistantEnabled)) Dispatcher.BeginInvoke(ApplyAvailability);
        };
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += OnAssistantsChanged;
            _app.AssistantToolsChanged += OnToolsChanged;
            App.McpServersChanged += OnToolsChanged;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= OnAssistantsChanged;
            _app.AssistantToolsChanged -= OnToolsChanged;
            App.McpServersChanged -= OnToolsChanged;
        };
    }

    private void OnAssistantsChanged() => Dispatcher.BeginInvoke(() =>
    {
        FillAssistants();
        ApplyAvailability();
    });

    // ── rows ─────────────────────────────────────────────────────────────

    private static void SpeakAs(NavItem item, string textKey) =>
        BindingOperations.SetBinding(item, AutomationProperties.NameProperty, new Binding($"[{textKey}]") { Source = Loc.Instance, Mode = BindingMode.OneWay });

    private static TextBlock Text(string key, double size, bool bold, string brush)
    {
        var text = new TextBlock { FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (bold) text.FontWeight = FontWeights.SemiBold;
        text.SetBinding(TextBlock.TextProperty, new Binding($"[{key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay });
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }

    private static FrameworkElement Chevron(double size, string brush)
    {
        var icon = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24, FontSize = size, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
        icon.SetResourceReference(ForegroundProperty, brush);
        return icon;
    }

    private static FrameworkElement GroupHeader(NavItem item, string titleKey)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chevron = Chevron(11, "TextFillColorTertiaryBrush");
        chevron.Margin = new Thickness(0, 0, 10, 0);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        item.Chevron = chevron;
        var title = Text(titleKey, 13, true, "TextFillColorSecondaryBrush");
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        var count = new TextBlock { FontSize = 11, Margin = new Thickness(7, 1, 7, 1) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        var chip = new Border { CornerRadius = new CornerRadius(8), Child = count, VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BackgroundProperty, "Kd.ChipBackground");
        Grid.SetColumn(chip, 2);
        item.CountChip = chip;
        grid.Children.Add(chevron);
        grid.Children.Add(title);
        grid.Children.Add(chip);
        return grid;
    }

    private static FrameworkElement SectionHeader(NavItem item, string icon, string textKey, bool folds)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var symbol = new SymbolIcon { Symbol = Enum.Parse<SymbolRegular>(icon), FontSize = 18 };
        symbol.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        var tile = new Border { Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(8), Child = symbol };
        tile.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Text(textKey, 14, true, "TextFillColorPrimaryBrush"));
        texts.Children.Add(Text(textKey + ".hint", 12, false, "TextFillColorTertiaryBrush"));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(tile);
        grid.Children.Add(texts);
        if (folds)
        {
            // Its own target: clicking the chevron folds, clicking the rest opens the section.
            var chevron = Chevron(12, "TextFillColorSecondaryBrush");
            var target = new Border { Background = Brushes.Transparent, Padding = new Thickness(8), Margin = new Thickness(4, 0, -4, 0), CornerRadius = new CornerRadius(6), Child = chevron, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(target, 2);
            grid.Children.Add(target);
            item.Chevron = target;
        }
        return grid;
    }

    /// <summary>A page inside a section, with a symbol (not an assistant's picture); with <paramref name="folds"/>, its chevron.</summary>
    private static FrameworkElement SubHeader(string icon, string text, NavItem? folds = null)
    {
        var grid = new Grid { Margin = new Thickness(46, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var symbol = new SymbolIcon { Symbol = Enum.Parse<SymbolRegular>(icon), FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        symbol.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        grid.Children.Add(symbol);
        var name = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (folds is not null)
        {
            var target = new Border { Background = Brushes.Transparent, Padding = new Thickness(6), Margin = new Thickness(4, -4, -4, -4), CornerRadius = new CornerRadius(6), Child = Chevron(11, "TextFillColorSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(target, 2);
            grid.Children.Add(target);
            folds.Chevron = target;
        }
        return grid;
    }

    private static FrameworkElement SubHeader(ImageSource? icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(46, 0, 0, 0) };
        row.Children.Add(new Image
        {
            Source = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        var name = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        row.Children.Add(name);
        RenderOptions.SetBitmapScalingMode(row.Children[0], BitmapScalingMode.HighQuality);
        return row;
    }

    /// <summary>The assistants inside «Asistentes», K-Desktop first (each opens its own settings).</summary>
    private void FillAssistants()
    {
        var assistants = AssistantPackages.Installed().Select(a => (Id: a.Id, Name: a.Manifest.Name, Icon: AssistantLook.Icon(a)))
            .Prepend((Id: Core.Assistant.AssistantTargeting.KDesktop, Name: "K-Desktop", Icon: AssistantLook.SystemIcon)).ToList();
        var keys = assistants.Select(a => "assistants/" + a.Id).ToList();
        if (_assistantsItem!.Children.Select(c => c.Key).SequenceEqual(keys)) return;
        var current = _current?.Key;
        _assistantsItem.Items.Clear();
        foreach (var assistant in assistants)
        {
            var item = new NavItem(NavKind.Sub, "assistants/" + assistant.Id)
            {
                Style = (Style)FindResource("NavItem"),
                Header = SubHeader(assistant.Icon, assistant.Name),
                SearchText = Fold(assistant.Name + " " + assistant.Id),
            };
            AutomationProperties.SetName(item, assistant.Name);
            _assistantsItem.Items.Add(item);
            if (item.Key == current) MarkCurrent(item);
        }
        if (_current is { Kind: NavKind.Sub } gone && gone.Key.StartsWith("assistants/", StringComparison.Ordinal) && !keys.Contains(gone.Key)) MarkCurrent(_assistantsItem);
    }

    /// <summary>The pages inside «Herramientas».</summary>
    private void FillTools()
    {
        foreach (var part in ToolParts)
        {
            var item = new NavItem(NavKind.Sub, part.Key)
            {
                Style = (Style)FindResource("NavItem"),
                SearchText = Fold(Loc.T(part.Text) + " " + Loc.T(part.Text + ".hint")),
            };
            var folds = ToolFolders.Any(f => f.Key == part.Key);
            item.Header = SubHeader(part.Icon, Loc.T(part.Text), folds ? item : null);
            AutomationProperties.SetName(item, Loc.T(part.Text));
            _toolsItem!.Items.Add(item);
            if (folds) _toolFolders[part.Key] = item;
        }
        FillToolFolders();
    }

    private void FillToolFolders()
    {
        FillSubjects();
        FillToolFiles();
        FillServers();
    }

    /// <summary>A row inside one of those pages: its key, its name and what else finds it.</summary>
    private sealed record InnerRow(string Id, string Name, string Search);

    /// <summary>Puts those rows inside the page (only when they change); returns its rows, to give them their look.</summary>
    private List<NavItem> FillInner(NavItem folder, IReadOnlyList<InnerRow> rows)
    {
        var prefix = folder.Key + "/";
        var keys = rows.Select(r => prefix + r.Id).ToList();
        var current = _current?.Key;
        if (!folder.Children.Select(c => c.Key).SequenceEqual(keys))
        {
            folder.Items.Clear();
            foreach (var key in keys)
            {
                var item = new NavItem(NavKind.Sub, key) { Style = (Style)FindResource("NavItem") };
                folder.Items.Add(item);
                if (item.Key == current) MarkCurrent(item);
            }
            if (_current is { } gone && gone.Key.StartsWith(prefix, StringComparison.Ordinal) && !keys.Contains(gone.Key)) MarkCurrent(folder);
        }
        var items = folder.Children.ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].SearchText = Fold(rows[i].Name + " " + rows[i].Search);
            AutomationProperties.SetName(items[i], rows[i].Name);
        }
        if (folder.Chevron is { } chevron) chevron.Visibility = folder.Items.Count > 0 ? Visibility.Visible : Visibility.Hidden;
        return items;
    }

    /// <summary>The subjects inside «Incluidas», in alphabetical order (all of them until the process says which it has).</summary>
    private void FillSubjects()
    {
        var folder = _toolFolders["tools/builtin"];
        var tools = _app.AssistantTools?.Tools;
        if (tools is null && folder.Items.Count > 0) return;
        var subjects = tools is null ? AssistantToolsSection.AllSubjects
            : AssistantToolsSection.SubjectsIn(tools.Where(t => t.Group == "builtin").Select(t => t.Name));
        var rows = subjects.Select(s => new InnerRow(s, Loc.T("toolsOptions.subject." + s), Loc.T("settings.nav.toolBuiltin"))).ToList();
        var items = FillInner(folder, rows);
        for (var i = 0; i < items.Count; i++) items[i].Header = InnerHeader(AssistantToolsSection.SubjectIcon(rows[i].Id), "Kd.AccentText", rows[i].Name, off: false);
    }

    /// <summary>The user's files inside «Tus herramientas», by their tool's name (the files in the folder until the process says).</summary>
    private void FillToolFiles()
    {
        var folder = _toolFolders["tools/files"];
        var state = _app.AssistantTools;
        if (state is null && folder.Items.Count > 0) return;
        var tools = (state?.Tools ?? []).Where(t => t.Group == "json").ToList();
        var files = state?.Files ?? (System.IO.Directory.Exists(App.ToolsFolder)
            ? System.IO.Directory.GetFiles(App.ToolsFolder, "*.json").Select(f => new Core.Assistant.Pipe.ToolFileInfo(f, true, [], [])).ToList()
            : []);
        var shown = files.Select(f => (File: f, Name: AssistantToolsSection.FileTitle(f, tools)))
            .OrderBy(f => f.Name, AssistantToolsSection.Alphabetical).ToList();
        var items = FillInner(folder, shown.Select(f => new InnerRow(System.IO.Path.GetFileName(f.File.File), f.Name,
            System.IO.Path.GetFileName(f.File.File) + " " + Loc.T("settings.nav.toolFiles"))).ToList());
        for (var i = 0; i < items.Count; i++)
        {
            var (file, name) = shown[i];
            var tool = tools.FirstOrDefault(t => string.Equals(t.Source, file.File, StringComparison.OrdinalIgnoreCase));
            var broken = file.Errors.Count > 0;
            items[i].Header = InnerHeader(broken ? SymbolRegular.ErrorCircle24 : AssistantToolsSection.TypeIcon(file.Type), broken ? "Kd.Danger" : "Kd.AccentText",
                name, off: !broken && (!file.Enabled || tool is { Enabled: false }));
        }
    }

    /// <summary>One row per MCP server inside «Servidores MCP», with a dot of how it is (in use, stopped, failing, off).</summary>
    private void FillServers()
    {
        var servers = App.ReadMcpServers(out _).OrderBy(s => s.Id, AssistantToolsSection.Alphabetical).ToList();
        var states = (_app.AssistantTools?.Servers ?? []).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var items = FillInner(_toolFolders["tools/servers"], servers.Select(s => new InnerRow(s.Id, s.Id, "mcp " + Loc.T("settings.nav.toolServers"))).ToList());
        for (var i = 0; i < items.Count; i++)
        {
            var server = servers[i];
            var state = states.GetValueOrDefault(server.Id);
            var brush = server.Disabled ? "TextFillColorDisabledBrush" : state?.State switch
            {
                "running" => "Kd.Success",
                "failed" => "Kd.Danger",
                "starting" => "Kd.AccentText",
                _ => "TextFillColorTertiaryBrush",
            };
            items[i].Header = InnerHeader(null, brush, server.Id, server.Disabled);
        }
    }

    /// <summary>A row one level further in: a symbol (or, without one, a dot of its state) and its name.</summary>
    private static FrameworkElement InnerHeader(SymbolRegular? symbol, string brush, string name, bool off)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(symbol is null ? 76 : 74, 0, 0, 0) };
        if (symbol is { } glyph)
        {
            var icon = new SymbolIcon { Symbol = glyph, FontSize = 14, Width = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(ForegroundProperty, brush);
            if (off) icon.Opacity = 0.5;
            row.Children.Add(icon);
        }
        else
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
            row.Children.Add(dot);
        }
        var text = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, off ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush");
        row.Children.Add(text);
        return row;
    }

    private void OnToolsChanged() => Dispatcher.BeginInvoke(FillToolFolders);

    /// <summary>With the module off, its group only has «General» (where it is turned on); counts and chevrons follow.</summary>
    private void ApplyAvailability()
    {
        if (_searching) return;
        var on = _app.Config.Settings.AssistantEnabled;
        foreach (var group in _groups)
        {
            foreach (var section in group.Children)
                section.Visibility = group.Key != "assistant" || on || section.Key == "assistant" ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)((Border)group.CountChip!).Child).Text = group.Children.Count(s => s.Visibility == Visibility.Visible).ToString(CultureInfo.CurrentCulture);
        }
        _assistantsItem!.Chevron!.Visibility = _assistantsItem.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _toolsItem!.Chevron!.Visibility = Visibility.Visible;
    }

    // ── choosing ─────────────────────────────────────────────────────────

    private NavItem? Find(string key) => _groups.SelectMany(Everything).FirstOrDefault(i => i.Key == key);

    /// <summary>The rows inside a row, at every level (a server inside «Servidores MCP» inside «Herramientas»).</summary>
    private static IEnumerable<NavItem> Everything(NavItem item) => item.Children.SelectMany(c => Everything(c).Prepend(c));

    /// <summary>Neither it nor what holds it is hidden (the module's sections are, with the module off).</summary>
    private static bool IsShown(NavItem item)
    {
        for (NavItem? i = item; i is not null; i = i.Owner)
            if (i.Visibility != Visibility.Visible) return false;
        return true;
    }

    /// <summary>Opens what holds it (its group, «Asistentes» for an assistant).</summary>
    private static void Reveal(NavItem item)
    {
        for (var owner = item.Owner; owner is not null; owner = owner.Owner) owner.IsExpanded = true;
    }

    /// <summary>Selects it in the tree (which opens it), or opens it if it already was.</summary>
    private void Select(NavItem item)
    {
        _byMouse = true;
        if (item.IsSelected) Open(item);
        else item.IsSelected = true;
        _byMouse = false;
        // A TreeViewItem brought into view unfolds what holds it: only when it is already out.
        Dispatcher.BeginInvoke(() =>
        {
            if (IsUnfolded(item)) item.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static bool IsUnfolded(NavItem item)
    {
        for (var owner = item.Owner; owner is not null; owner = owner.Owner)
            if (!owner.IsExpanded) return false;
        return true;
    }

    private void OnNavSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not NavItem item) return;
        item.ShowFocus = !_byMouse;
        if (item.Kind != NavKind.Group) Open(item);
    }

    /// <summary>Shows a section's page (or an assistant's settings).</summary>
    private void Open(NavItem item)
    {
        var same = _current == item;
        MarkCurrent(item);
        if (same && item.Kind == NavKind.Section && item.Key != "assistants") return;
        // One subject, file or MCP server («Herramientas → Servidores MCP → ft42»): its page, showing that one.
        if (ToolFolders.FirstOrDefault(f => item.Key.StartsWith(f.Key + "/", StringComparison.Ordinal)) is { } folder)
        {
            var section = folder.Part switch
            {
                "tools/subject" => SubjectSection,
                "tools/file" => FileSection,
                _ => ServerSection,
            };
            section.ShowItem(item.Key[(folder.Key.Length + 1)..]);
            ShowCard(folder.Part, animate: !same);
            return;
        }
        // A page of its own inside a section («Herramientas → Secretos»).
        if (item.Kind == NavKind.Sub && !item.Key.StartsWith("assistants/", StringComparison.Ordinal))
        {
            ShowCard(item.Key, animate: !same);
            return;
        }
        ShowCard(item.Kind == NavKind.Sub ? "assistants" : item.Key, animate: !same);
        if (item.Kind == NavKind.Sub) AssistantsSection.ShowSettings(item.Key["assistants/".Length..]);
        else if (item.Key == "assistants") AssistantsSection.ShowList();
    }

    private void MarkCurrent(NavItem item)
    {
        if (_current is not null && _current != item) _current.IsCurrent = false;
        item.IsCurrent = true;
        _current = item;
        LastSection = item.Key;
    }

    /// <summary>«Ajustes» of a row, or back to the list, inside «Asistentes»: the tree follows.</summary>
    private void OnAssistantsShown(string? id)
    {
        if ((id is null ? _assistantsItem : Find("assistants/" + id)) is not { } item || item == _current) return;
        if (_current?.Key.StartsWith("assistants", StringComparison.Ordinal) != true) return;
        if (item.Kind == NavKind.Sub) Reveal(item);
        MarkCurrent(item);
        _byMouse = true;
        item.IsSelected = true;
        _byMouse = false;
    }

    // ── folding ──────────────────────────────────────────────────────────

    /// <summary>A click on a group's row folds or unfolds it (and unfolding shows its first section); on a chevron, the same.</summary>
    private void OnNavMouseDown(object sender, MouseButtonEventArgs e)
    {
        _byMouse = true;
        var item = ItemOf(e.OriginalSource as DependencyObject);
        if (item is null) return;
        var onChevron = item.Chevron is { } chevron && IsInside(e.OriginalSource as DependencyObject, chevron);
        if (item.Kind == NavKind.Group)
        {
            Toggle(item);
            e.Handled = true;
        }
        else if (onChevron && item.Items.Count > 0)
        {
            item.IsExpanded = !item.IsExpanded;
            e.Handled = true;
        }
    }

    private void OnNavKeyDown(object sender, KeyEventArgs e)
    {
        _byMouse = false;
        if (e.Key is Key.Enter or Key.Space && NavTree.SelectedItem is NavItem { Kind: NavKind.Group } group)
        {
            Toggle(group);
            e.Handled = true;
        }
    }

    private void Toggle(NavItem group)
    {
        group.IsExpanded = !group.IsExpanded;
        // Unfolded: its first section, unless the one shown is already in it.
        if (group.IsExpanded && !_searching && !Everything(group).Contains(_current)
            && group.Children.FirstOrDefault(s => s.Visibility == Visibility.Visible) is { } first)
            Select(first);
    }

    private void OnNavExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender || sender is not NavItem item) return;
        UpdateChevron(item, animate: !_building);
        if (_building || _searching) return;
        var open = _groups.Where(g => g.IsExpanded).Select(g => g.Key).ToList();
        if (_assistantsItem!.IsExpanded) open.Add("assistants");
        if (_toolsItem!.IsExpanded) open.Add("tools");
        open.AddRange(_toolFolders.Where(f => f.Value.IsExpanded).Select(f => f.Key));
        _app.Config.Settings.SettingsNavOpen = open;
    }

    private static readonly IEasingFunction ChevronEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static void UpdateChevron(NavItem item, bool animate)
    {
        var icon = item.Chevron is Border { Child: FrameworkElement inner } ? inner : item.Chevron;
        if (icon?.RenderTransform is RotateTransform rotate)
        {
            var to = item.IsExpanded ? 90 : 0;
            if (animate) rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(200)) { EasingFunction = ChevronEase });
            else
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                rotate.Angle = to;
            }
        }
        if (item.CountChip is { } chip)
        {
            if (animate) chip.BeginAnimation(OpacityProperty, new DoubleAnimation(item.IsExpanded ? 0 : 1, TimeSpan.FromMilliseconds(160)));
            else chip.Opacity = item.IsExpanded ? 0 : 1;
        }
    }

    private static NavItem? ItemOf(DependencyObject? element)
    {
        for (; element is not null; element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is NavItem item) return item;
        }
        return null;
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        for (; element is not null; element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element == container) return true;
            if (element is NavItem) return false;
        }
        return false;
    }

    // ── search ───────────────────────────────────────────────────────────

    /// <summary>The words of each section's page (the texts on its cards), built when first searched.</summary>
    private Dictionary<string, string>? _searchIndex;

    private Dictionary<string, string> SearchIndex()
    {
        if (_searchIndex is { } known) return known;
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in Groups)
            foreach (var section in group.Sections)
            {
                var words = new StringBuilder();
                words.Append(Loc.T(section.Text)).Append(' ').Append(Loc.T(section.Text + ".hint")).Append(' ').Append(Loc.T(group.Title)).Append(' ');
                foreach (var card in Sections.Children.OfType<FrameworkElement>().Where(c => c.Tag as string == section.Key)) Collect(card, words);
                index[section.Key] = Fold(words.ToString());
            }
        return _searchIndex = index;
    }

    /// <summary>Every text of a page: titles, hints, labels, the options of its switches and buttons.</summary>
    private static void Collect(DependencyObject element, StringBuilder words)
    {
        switch (element)
        {
            case TextBlock { Text: { Length: > 0 } text }:
                words.Append(text).Append(' ');
                break;
            case ContentControl { Content: string content }:
                words.Append(content).Append(' ');
                break;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>()) Collect(child, words);
    }

    /// <summary>Lower case and without accents («Gráfica» finds «grafica»).</summary>
    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Filter(SearchBox.Text);

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _searching)
        {
            // The first match.
            if (_groups.Where(g => g.Visibility == Visibility.Visible).SelectMany(g => g.Children)
                .SelectMany(s => s.Children.Where(c => c.Visibility == Visibility.Visible).Prepend(s))
                .FirstOrDefault(i => i.Visibility == Visibility.Visible) is { } first)
                Select(first);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            (NavTree.SelectedItem as NavItem ?? _current)?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+F anywhere in Options: the search.</summary>
    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void Filter(string query)
    {
        var words = Fold(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            EndSearch();
            return;
        }
        if (!_searching)
        {
            _openBeforeSearch = [.. _groups.Where(g => g.IsExpanded).Select(g => g.Key)];
            if (_assistantsItem!.IsExpanded) _openBeforeSearch.Add("assistants");
            if (_toolsItem!.IsExpanded) _openBeforeSearch.Add("tools");
            _openBeforeSearch.UnionWith(_toolFolders.Where(f => f.Value.IsExpanded).Select(f => f.Key));
            _searching = true;
        }
        var index = SearchIndex();
        var moduleOn = _app.Config.Settings.AssistantEnabled;
        var any = false;
        foreach (var group in _groups)
        {
            var groupHas = false;
            foreach (var section in group.Children)
            {
                var available = group.Key != "assistant" || moduleOn || section.Key == "assistant";
                var subsHit = false;
                foreach (var sub in section.Children)
                {
                    // A server inside «Servidores MCP».
                    var innerHit = false;
                    foreach (var inner in sub.Children)
                    {
                        var found = available && words.All(inner.SearchText.Contains);
                        inner.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
                        innerHit |= found;
                    }
                    var hit = available && (words.All(sub.SearchText.Contains) || innerHit);
                    sub.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
                    if (sub.Items.Count > 0) sub.IsExpanded = innerHit;
                    subsHit |= hit;
                }
                var sectionHit = available && (words.All(index.GetValueOrDefault(section.Key, string.Empty).Contains) || subsHit);
                section.Visibility = sectionHit ? Visibility.Visible : Visibility.Collapsed;
                if (section.Items.Count > 0) section.IsExpanded = subsHit;
                groupHas |= sectionHit;
            }
            group.Visibility = groupHas ? Visibility.Visible : Visibility.Collapsed;
            group.IsExpanded = groupHas;
            any |= groupHas;
        }
        NoResults.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EndSearch()
    {
        if (!_searching) return;
        _searching = false;
        NoResults.Visibility = Visibility.Collapsed;
        foreach (var group in _groups)
        {
            group.Visibility = Visibility.Visible;
            foreach (var item in Everything(group)) item.Visibility = Visibility.Visible;
            group.IsExpanded = _openBeforeSearch?.Contains(group.Key) == true;
        }
        _assistantsItem!.IsExpanded = _openBeforeSearch?.Contains("assistants") == true;
        _toolsItem!.IsExpanded = _openBeforeSearch?.Contains("tools") == true;
        foreach (var (key, folder) in _toolFolders) folder.IsExpanded = _openBeforeSearch?.Contains(key) == true;
        ApplyAvailability();
        if (_current is { } current) Reveal(current);
    }
}
