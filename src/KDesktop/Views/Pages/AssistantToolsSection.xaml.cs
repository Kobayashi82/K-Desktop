using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Views.Assistant;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Tools (stage 9.3; by pages since 5.0.133). Each instance is one page (<see cref="Part"/>): the
/// summary with a search over every tool (<c>tools</c>), the built-in ones by subject (<c>tools/builtin</c>), the user's
/// JSON ones with their files (<c>tools/files</c>), the MCP servers (<c>tools/servers</c>), one server with its tools
/// (<c>tools/server</c>, <see cref="ShowItem"/>) and the secrets (<c>tools/secrets</c>). Only the page that shows is
/// made, and only when what it shows changed (it was all of them, 230 rows each time, and every click took a second).
/// The rows are made in code (an ItemsControl hides its buttons from UI Automation).
/// </summary>
public partial class AssistantToolsSection : UserControl
{
    /// <summary>The page about tools of the documentation website, in Spanish or else in English.</summary>
    private static string GuideUrl => Loc.Instance.Language == "es"
        ? "https://kobayashi82.github.io/K-Desktop/es/assistant/tools.html#tus-herramientas-json"
        : "https://kobayashi82.github.io/K-Desktop/en/assistant/tools.html#your-own-tools-json";

    private readonly App _app = App.Current;
    /// <summary>What the page showed last time it was made (it is not made again for the same).</summary>
    private string? _shown;

    public AssistantToolsSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            FitWidth();
            _app.AssistantToolsChanged += Refresh;
            _app.AssistantChanged += OnAssistantChanged;
            App.McpServersChanged += Refresh;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantToolsChanged -= Refresh;
            _app.AssistantChanged -= OnAssistantChanged;
            App.McpServersChanged -= Refresh;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _savedSecret = null;
                return;
            }
            if (_app.AssistantTools is null) _app.RequestAssistantTools();
            Refresh();
        };
    }

    /// <summary>
    /// Which page it is: <c>tools</c>, <c>tools/builtin</c>, <c>tools/subject</c>, <c>tools/files</c>, <c>tools/file</c>,
    /// <c>tools/servers</c>, <c>tools/server</c> or <c>tools/secrets</c>.
    /// </summary>
    public string Part { get; set; } = "tools";

    /// <summary>What <c>tools/subject</c>, <c>tools/file</c> or <c>tools/server</c> shows: a subject, a file name, a server.</summary>
    public string? ItemId { get; private set; }

    /// <summary>Shows that subject, file or MCP server (5.0.134: the built-in tools and the user's are one page each too).</summary>
    public void ShowItem(string id)
    {
        ItemId = id;
        _shown = null;
        Refresh();
    }

    /// <summary>In alphabetical order, in the user's language.</summary>
    internal static StringComparer Alphabetical => StringComparer.Create(Loc.Instance.Culture, ignoreCase: true);

    private ScrollViewer? _scroll;

    /// <summary>
    /// As wide as the page lets it (the sections are as wide as their content, up to their most): its pages have short
    /// texts and each one came out of another width.
    /// </summary>
    private void FitWidth()
    {
        if (_scroll is not null) return;
        DependencyObject? at = this;
        while (at is not null and not ScrollViewer) at = System.Windows.Media.VisualTreeHelper.GetParent(at);
        if (at is not ScrollViewer scroll || Parent is not FrameworkElement holder) return;
        _scroll = scroll;
        void Fit() => Width = Math.Max(0, Math.Min(double.IsInfinity(holder.MaxWidth) ? double.MaxValue : holder.MaxWidth, scroll.ViewportWidth));
        scroll.SizeChanged += (_, _) => Fit();
        scroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0) Fit(); };
        Fit();
    }

    private void OnAssistantChanged()
    {
        if (!IsVisible) return;
        if (_app.AssistantTools is null) _app.RequestAssistantTools();
        Refresh();
    }

    private bool Running => _app.Assistant is { IsRunning: true };

    private ToolsStateMessage? State => _app.AssistantTools;

    /// <summary>Makes the page again, if it shows and what it shows changed.</summary>
    private void Refresh()
    {
        if (!IsVisible) return;
        var signature = Signature();
        if (signature == _shown) return;
        _shown = signature;
        var focused = Keyboard.FocusedElement is FrameworkElement { Tag: string tag } ? tag : null;
        OffBox.Visibility = Running ? Visibility.Collapsed : Visibility.Visible;
        OffText.Text = Loc.T(_app.Assistant is null ? "toolsOptions.moduleOff" : "toolsOptions.notRunning");
        GoGeneral.Visibility = _app.Assistant is null ? Visibility.Visible : Visibility.Collapsed;
        Body.Children.Clear();
        switch (Part)
        {
            case "tools/builtin": FillBuiltin(); break;
            case "tools/subject": FillSubject(); break;
            case "tools/files": FillFiles(); break;
            case "tools/file": FillFile(); break;
            case "tools/servers": FillServers(); break;
            case "tools/server": FillServer(); break;
            case "tools/secrets": FillSecrets(); break;
            case "tools/share": FillShare(); break;
            default: FillSummary(); break;
        }
        if (focused is not null && FindTagged(this, focused) is { } again)
            Dispatcher.BeginInvoke(() =>
            {
                again.Focus();
                if (again is System.Windows.Controls.TextBox box) box.CaretIndex = box.Text.Length;
            }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Everything the page shows, in a few words: the same, and nothing is made again (focus and caret stay).</summary>
    private string Signature()
    {
        var state = State;
        var parts = new List<string> { Part, ItemId ?? "", Running.ToString(), (_app.Assistant is null).ToString(), _testing ?? "", _changingSecret ?? "", _savedSecret ?? "", _search, Loc.Instance.Language };
        IEnumerable<ToolInfo> tools = Part switch
        {
            "tools/builtin" => ToolsOf("builtin"),
            "tools/subject" => ToolsOf("builtin").Where(t => SubjectOf(t.Name) == ItemId),
            "tools/files" or "tools/file" => ToolsOf("json"),
            "tools/server" => ToolsOf("mcp:" + ItemId),
            "tools" => _search.Length > 0 ? state?.Tools ?? [] : [],
            _ => [],
        };
        foreach (var tool in tools) parts.Add($"{tool.Name}|{tool.Enabled}|{tool.Risk}|{_app.AssistantToolsTesting.Contains(tool.Name)}|{(_app.AssistantToolTests.TryGetValue(tool.Name, out var t) ? t.Ms + t.Text.Length : -1)}");
        if (Part is "tools" or "tools/servers" or "tools/server")
            foreach (var server in state?.Servers ?? []) parts.Add($"{server.Id}|{server.State}|{server.Since}|{server.Disabled}|{server.Tools}|{server.Error}|{_app.AssistantToolsTesting.Contains(server.Id)}|{(_app.AssistantToolTests.TryGetValue(server.Id, out var t) ? t.Ms : -1)}");
        if (Part is "tools" or "tools/servers" or "tools/server") parts.Add(string.Join(",", App.ReadMcpServers(out _).Select(s => s.Id + s.Disabled + s.Summary)));
        if (Part is "tools" or "tools/files" or "tools/file") foreach (var file in state?.Files ?? []) parts.Add($"{file.File}|{file.Enabled}|{string.Join(";", file.Errors)}");
        if (Part is "tools") parts.Add(string.Join(",", (state?.Tools ?? []).Select(t => t.Enabled ? "1" : "0")));
        if (Part is "tools/share") parts.Add(ShareSignature());
        if (Part is "tools" or "tools/secrets") parts.Add(string.Join(",", _app.ToolSecretList().Select(s => s.Name + s.Kept + string.Join("+", s.UsedBy))));
        return string.Join("\n", parts);
    }

    private IEnumerable<ToolInfo> ToolsOf(string group) => (State?.Tools ?? []).Where(t => t.Group == group);

    private static FrameworkElement? FindTagged(DependencyObject parent, string tag)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Tag: string t } element && t == tag && element.Focusable) return element;
            if (FindTagged(child, tag) is { } found) return found;
        }
        return null;
    }

    private void Go(string page) => (Window.GetWindow(this) as MainWindow)?.Navigate("settings/" + page);

    // ── summary ──────────────────────────────────────────────────────────

    private string _search = string.Empty;

    /// <summary>Most rows the search shows.</summary>
    private const int SearchShown = 40;

    private void FillSummary()
    {
        var state = State;
        var tools = state?.Tools ?? [];
        var builtin = tools.Where(t => t.Group == "builtin").ToList();
        var own = tools.Where(t => t.Group == "json").ToList();
        var mcp = tools.Where(t => t.Group.StartsWith("mcp:", StringComparison.Ordinal)).ToList();
        var servers = App.ReadMcpServers(out _);
        var running = (state?.Servers ?? []).Count(s => s.State == "running");
        var broken = (state?.Files ?? []).Count(f => f.Errors.Count > 0);
        var secrets = _app.ToolSecretList();
        var missing = secrets.Count(s => !s.Kept && s.UsedBy.Count > 0);

        var card = Card(Loc.T("toolsOptions.summary"), Loc.T("toolsOptions.summaryIntro"));
        var tiles = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 4, -4, 0) };
        tiles.Children.Add(Tile(SymbolRegular.Toolbox24, Loc.T("settings.nav.toolBuiltin"), builtin.Count, Off(builtin), null, "tools/builtin"));
        tiles.Children.Add(Tile(SymbolRegular.DocumentData24, Loc.T("settings.nav.toolFiles"), own.Count,
            Off(own), broken > 0 ? Loc.T("toolsOptions.tileBroken", broken) : null, "tools/files", broken > 0));
        tiles.Children.Add(Tile(SymbolRegular.PlugConnected24, Loc.T("settings.nav.toolServers"), servers.Count,
            Loc.T("toolsOptions.tileTools", mcp.Count), Loc.T("toolsOptions.tileRunning", running), "tools/servers"));
        tiles.Children.Add(Tile(SymbolRegular.Key24, Loc.T("settings.nav.toolSecrets"), secrets.Count,
            missing > 0 ? Loc.T("toolsOptions.tileMissing", missing) : Loc.T("toolsOptions.tileAllKept"), null, "tools/secrets", missing > 0));
        card.Add(tiles);
        Body.Children.Add(card.Border);

        // What is in use: only the MCP servers are programs that start and stop; the built-in tools and the user's are
        // always at hand and take nothing.
        var live = (state?.Servers ?? []).Where(s => s.State is "running" or "starting").OrderBy(s => s.Id, Alphabetical).ToList();
        var inUse = Card(Loc.T("toolsOptions.inUse"), Loc.T("toolsOptions.inUseIntro"), SymbolRegular.Pulse24);
        if (live.Count == 0) inUse.Add(Caption(Loc.T(Running ? "toolsOptions.noneInUse" : "toolsOptions.noToolsOff")));
        else
            inUse.Add(Rows(live.Select(s =>
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                chips.Children.Add(s.State == "running" ? StatusChip(Loc.T("toolsOptions.serverRunning"), "Kd.Success") : StatusChip(Loc.T("toolsOptions.serverStarting"), "Kd.AccentText"));
                chips.Children.Add(Chip(Loc.T("toolsOptions.serverTools", s.Tools)));
                var since = s.Since is { } at ? Loc.T("toolsOptions.runningSince", at.ToLocalTime().ToString("t", Loc.Instance.Culture)) : null;
                return (UIElement)OpenableRow(SymbolRegular.PlugConnected24, s.Id, since, chips, "tools/servers/" + s.Id,
                    extra: Small(SymbolRegular.Stop24, Loc.T("toolsOptions.stop"), () => _app.StopMcpServer(s.Id), true, s.Id));
            })));
        Body.Children.Add(inUse.Border);

        var search = Card(Loc.T("toolsOptions.searchTitle"), Loc.T("toolsOptions.searchIntro"));
        var box = new TextBox
        {
            Text = _search,
            PlaceholderText = Loc.T("toolsOptions.filter"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 },
            Tag = "search",
            Margin = new Thickness(0, 0, 0, 4),
        };
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T("toolsOptions.filter"));
        box.TextChanged += (_, _) =>
        {
            _search = box.Text.Trim();
            Refresh();
        };
        search.Add(box);
        if (_search.Length > 0)
        {
            var found = tools.Where(t => Matches(t, _search)).OrderBy(TitleOf, Alphabetical).ToList();
            if (found.Count == 0) search.Add(Caption(Loc.T(state is null ? (Running ? "memoryOptions.loading" : "toolsOptions.noToolsOff") : "toolsOptions.noTools")));
            else
            {
                search.Add(Rows(found.Take(SearchShown).Select(t => ToolRow(t, showWhere: true))));
                if (found.Count > SearchShown) search.Add(Caption(Loc.T("toolsOptions.searchMore", found.Count - SearchShown)));
            }
        }
        Body.Children.Add(search.Border);
    }

    private static string? Off(IReadOnlyCollection<ToolInfo> tools) =>
        tools.Count(t => !t.Enabled) is var off and > 0 ? Loc.T("toolsOptions.groupOff", off) : Loc.T("toolsOptions.allOn");

    private static bool Matches(ToolInfo tool, string search) =>
        tool.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || TitleOf(tool).Contains(search, StringComparison.OrdinalIgnoreCase)
        || tool.Description.Contains(search, StringComparison.OrdinalIgnoreCase) || (tool.Example?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    // ── built-in ─────────────────────────────────────────────────────────

    /// <summary>The subjects, each a row that opens its own page (5.0.134: it was one long page with all of them).</summary>
    private void FillBuiltin()
    {
        var tools = ToolsOf("builtin").ToList();
        var card = Card(Loc.T("settings.nav.toolBuiltin"), Loc.T("toolsOptions.builtinIntro"));
        if (tools.Count == 0) card.Add(Caption(Loc.T(Running ? "memoryOptions.loading" : "toolsOptions.noToolsOff")));
        else
            card.Add(Rows(SubjectsIn(tools.Select(t => t.Name)).Select(subject =>
            {
                var these = tools.Where(t => SubjectOf(t.Name) == subject).OrderBy(TitleOf, Alphabetical).ToList();
                var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                chips.Children.Add(Chip(Loc.T("toolsOptions.serverTools", these.Count)));
                if (these.Count(t => !t.Enabled) is var off and > 0) chips.Children.Add(StatusChip(Loc.T("toolsOptions.groupOff", off), "TextFillColorTertiaryBrush"));
                var name = Loc.T("toolsOptions.subject." + subject);
                return OpenableRow(SubjectIcon(subject), name, string.Join(", ", these.Select(TitleOf)), chips, "tools/builtin/" + subject);
            })));
        Body.Children.Add(card.Border);
    }

    /// <summary>One subject of the built-in tools, in alphabetical order.</summary>
    private void FillSubject()
    {
        var subject = ItemId ?? "other";
        var tools = ToolsOf("builtin").Where(t => SubjectOf(t.Name) == subject).OrderBy(TitleOf, Alphabetical).ToList();
        var card = Card(Loc.T("toolsOptions.subject." + subject), null, SubjectIcon(subject));
        if (tools.Count == 0)
        {
            card.Add(Caption(Loc.T(State is null ? (Running ? "memoryOptions.loading" : "toolsOptions.noToolsOff") : "toolsOptions.subjectGone")));
            if (State is not null) card.Add(Wide(SymbolRegular.ArrowLeft24, Loc.T("toolsOptions.backToBuiltin"), _ => Go("tools/builtin")));
        }
        else
        {
            if (tools.Count > 1) card.Add(Bulk(tools));
            card.Add(Rows(tools.Select(t => ToolRow(t))));
        }
        Body.Children.Add(card.Border);
    }

    /// <summary>«Encender todas» and «Apagar todas».</summary>
    private WrapPanel Bulk(List<ToolInfo> tools)
    {
        var bulk = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        bulk.Children.Add(Wide(SymbolRegular.CheckmarkCircle24, Loc.T("toolsOptions.allOnButton"), _ => { foreach (var t in tools) _app.SetToolEnabled(t.Name, true); }, enabled: tools.Any(t => !t.Enabled)));
        bulk.Children.Add(Wide(SymbolRegular.DismissCircle24, Loc.T("toolsOptions.allOffButton"), _ => { foreach (var t in tools) _app.SetToolEnabled(t.Name, false); }, enabled: tools.Any(t => t.Enabled)));
        return bulk;
    }

    /// <summary>The subjects those tools are in, in alphabetical order (the user's language), «Otras» last.</summary>
    internal static IEnumerable<string> SubjectsIn(IEnumerable<string> tools) =>
        tools.Select(SubjectOf).Distinct().OrderBy(s => s == "other").ThenBy(s => Loc.T("toolsOptions.subject." + s), Alphabetical);

    /// <summary>Every subject (before the process said which tools it has).</summary>
    internal static IEnumerable<string> AllSubjects => SubjectsIn(SubjectOfTool.Keys);

    private static readonly Dictionary<string, string> SubjectOfTool = new(StringComparer.Ordinal)
    {
        ["run_command"] = "commands", ["find_command"] = "commands", ["describe_command"] = "commands", ["diagnose_command"] = "commands", ["query_log"] = "commands", ["usage_stats"] = "commands", ["add_voice_phrase"] = "commands", ["run_event"] = "commands", ["create_event"] = "commands", ["stop_countdown"] = "commands",
        ["get_time"] = "time", ["set_timer"] = "time", ["cancel_timer"] = "time", ["schedule"] = "time", ["list_scheduled"] = "time", ["cancel_scheduled"] = "time", ["notify_when"] = "time", ["stop_watching"] = "time", ["date_math"] = "time", ["calculate"] = "time",
        ["mail_list"] = "mail", ["mail_read"] = "mail", ["mail_draft"] = "mail", ["mail_send"] = "mail", ["mail_move"] = "mail", ["mail_invitation"] = "mail",
        ["telegram_send"] = "telegram", ["telegram_send_file"] = "telegram", ["telegram_read"] = "telegram",
        ["calendar_list"] = "calendar", ["calendar_add"] = "calendar", ["calendar_change"] = "calendar", ["calendar_delete"] = "calendar",
        ["save_note"] = "notes", ["find_notes"] = "notes", ["edit_note"] = "notes", ["delete_note"] = "notes", ["remember"] = "notes", ["forget"] = "notes",
        ["find_files"] = "files", ["open_file"] = "files", ["read_file"] = "files", ["transcribe_audio"] = "files", ["delete_file"] = "files", ["move_file"] = "files", ["write_file"] = "files", ["copy_file"] = "files", ["create_folder"] = "files", ["zip_files"] = "files", ["unzip"] = "files", ["search_knowledge"] = "files",
        ["open_web"] = "web", ["search_web"] = "web", ["look_up_web"] = "web", ["read_web"] = "web", ["network_check"] = "web", ["send_to_phone"] = "web",
        ["look_at_screen"] = "screen", ["transform_text"] = "screen", ["read_clipboard"] = "screen", ["clipboard_history"] = "screen", ["set_clipboard"] = "screen",
        ["open_app"] = "pc", ["control_app"] = "pc", ["list_windows"] = "pc", ["arrange_windows"] = "pc", ["set_volume"] = "pc", ["audio_device"] = "pc", ["display"] = "pc", ["windows_theme"] = "pc", ["toggle_radio"] = "pc", ["lock_pc"] = "pc", ["power_pc"] = "pc", ["take_screenshot"] = "pc", ["power_plan"] = "pc", ["keep_awake"] = "pc", ["end_process"] = "pc", ["pc_status"] = "pc", ["free_brain"] = "pc",
        ["whats_playing"] = "media", ["control_media"] = "media",
        ["weather"] = "weather", ["convert"] = "weather",
    };

    private static string SubjectOf(string tool) => SubjectOfTool.GetValueOrDefault(tool, "other");

    /// <summary>The subject a built-in tool is listed under («time», «files»…; «other» when none).</summary>
    internal static string SubjectOfName(string tool) => SubjectOf(tool);

    internal static SymbolRegular SubjectIcon(string subject) => subject switch
    {
        "commands" => SymbolRegular.Flash24,
        "time" => SymbolRegular.Clock24,
        "notes" => SymbolRegular.Note24,
        "files" => SymbolRegular.Folder24,
        "web" => SymbolRegular.Globe24,
        "screen" => SymbolRegular.Eye24,
        "pc" => SymbolRegular.Desktop24,
        "media" => SymbolRegular.MusicNote224,
        "weather" => SymbolRegular.WeatherPartlyCloudyDay24,
        "calendar" => SymbolRegular.CalendarLtr24,
        "mail" => SymbolRegular.Mail24,
        "telegram" => SymbolRegular.Send24,
        _ => SymbolRegular.Toolbox24,
    };

    // ── the user's JSON tools ────────────────────────────────────────────

    private void FillFiles()
    {
        var files = State?.Files ?? [];
        var tools = ToolsOf("json").ToList();
        var card = Card(Loc.T("toolsOptions.files"), null);
        card.Add(IntroWithGuide());
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        buttons.Children.Add(Wide(SymbolRegular.Add24, Loc.T("toolsOptions.newTool"), NewTool, primary: true));
        buttons.Children.Add(Wide(SymbolRegular.FolderOpen24, Loc.T("toolsOptions.openFolder"), _ => App.OpenToolsFolder()));
        card.Add(buttons);
        if (files.Count == 0) card.Add(Caption(Loc.T(State is null && Running ? "memoryOptions.loading" : "toolsOptions.noFiles")));
        // One row per file that opens its own page (5.0.134), in alphabetical order.
        var rows = files.OrderBy(f => FileTitle(f, tools), Alphabetical).Select(file =>
        {
            var tool = ToolOfFile(file, tools);
            var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            chips.Children.Add(Chip(Path.GetFileName(file.File) + (file.Type is { } type ? " · " + Loc.T("toolsOptions.type." + type) : string.Empty)));
            if (file.Errors.Count > 0) chips.Children.Add(StatusChip(Loc.T("toolsOptions.fileBroken"), "Kd.Danger"));
            else if (!file.Enabled) chips.Children.Add(StatusChip(Loc.T("toolsOptions.fileOff"), "TextFillColorTertiaryBrush"));
            else if (tool is { Enabled: false }) chips.Children.Add(StatusChip(Loc.T("toolsOptions.toolOff"), "TextFillColorTertiaryBrush"));
            var about = tool?.Example is { Length: > 0 } example ? Loc.T("toolsOptions.example", example) : tool is null ? null : OneLine(tool.Description);
            return (UIElement)OpenableRow(file.Errors.Count > 0 ? SymbolRegular.ErrorCircle24 : TypeIcon(file.Type), FileTitle(file, tools), about, chips,
                "tools/files/" + Path.GetFileName(file.File), danger: file.Errors.Count > 0, dim: file.Errors.Count == 0 && (!file.Enabled || tool is { Enabled: false }));
        }).ToList();
        if (rows.Count > 0) card.Add(Rows(rows));
        Body.Children.Add(card.Border);
    }

    /// <summary>What they are, with «Cómo se escriben» linking to the guide (like the link of «Control remoto»).</summary>
    private TextBlock IntroWithGuide()
    {
        var intro = Caption(string.Empty);
        intro.Margin = new Thickness(0, 0, 0, 10);
        var parts = Loc.T("toolsOptions.filesIntro").Split("{0}");
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Loc.T("toolsOptions.guideLink"))) { ToolTip = GuideUrl };
        link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Kd.AccentText");
        link.Click += (_, _) => ProcessLauncher.Launch(new LaunchSpec(GuideUrl), asAdmin: false);
        intro.Inlines.Add(parts[0]);
        intro.Inlines.Add(link);
        if (parts.Length > 1) intro.Inlines.Add(parts[1]);
        return intro;
    }

    /// <summary>One of the user's files: its tool, or what is wrong with it.</summary>
    private void FillFile()
    {
        var tools = ToolsOf("json").ToList();
        var file = (State?.Files ?? []).FirstOrDefault(f => string.Equals(Path.GetFileName(f.File), ItemId, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            var gone = Card(ItemId ?? string.Empty, Loc.T(State is null ? (Running ? "memoryOptions.loading" : "toolsOptions.noToolsOff") : "toolsOptions.fileGone"));
            if (State is not null) gone.Add(Wide(SymbolRegular.ArrowLeft24, Loc.T("toolsOptions.backToFiles"), _ => Go("tools/files")));
            Body.Children.Add(gone.Border);
            return;
        }
        var card = Card(FileTitle(file, tools), null, file.Errors.Count > 0 ? SymbolRegular.ErrorCircle24 : TypeIcon(file.Type));
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        buttons.Children.Add(Wide(SymbolRegular.Edit24, Loc.T("toolsOptions.editFile"), _ => App.OpenToolFile(file.File)));
        buttons.Children.Add(Wide(SymbolRegular.FolderOpen24, Loc.T("toolsOptions.openFolder"), _ => App.OpenToolsFolder()));
        card.Add(buttons);
        card.Add(Rows([ToolOfFile(file, tools) is { } tool && file.Errors.Count == 0 ? ToolRow(tool, file: file) : BrokenFileRow(file)]));
        Body.Children.Add(card.Border);
    }

    private static ToolInfo? ToolOfFile(ToolFileInfo file, IEnumerable<ToolInfo> tools) =>
        tools.FirstOrDefault(t => string.Equals(t.Source, file.File, StringComparison.OrdinalIgnoreCase));

    /// <summary>A file's name for people: its tool's, else the name it gives, else the file's.</summary>
    internal static string FileTitle(ToolFileInfo file, IEnumerable<ToolInfo> tools) =>
        ToolOfFile(file, tools) is { } tool ? TitleOf(tool) : ToolTestForm.Humanize(file.Name ?? Path.GetFileNameWithoutExtension(file.File));

    private FrameworkElement BrokenFileRow(ToolFileInfo file)
    {
        var body = new StackPanel();
        body.Children.Add(Title(file.Name ?? Path.GetFileName(file.File)));
        body.Children.Add(Caption(Path.GetFileName(file.File) + (file.Errors.Count == 0 ? " · " + Loc.T("toolsOptions.fileOff") : string.Empty)));
        foreach (var error in file.Errors.Take(3))
        {
            var line = Caption(error);
            line.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
            body.Children.Add(line);
        }
        var buttons = Buttons();
        buttons.Children.Add(Small(SymbolRegular.Edit24, Loc.T("toolsOptions.editFile"), () => App.OpenToolFile(file.File), true, Path.GetFileName(file.File)));
        return Row(file.Errors.Count > 0 ? SymbolRegular.ErrorCircle24 : SymbolRegular.DocumentData24, body, buttons, file.File, danger: file.Errors.Count > 0);
    }

    private void NewTool(FrameworkElement from)
    {
        var menu = new ContextMenu { PlacementTarget = from, Placement = PlacementMode.Bottom };
        foreach (var type in new[] { "http", "kdesktop", "routine", "script" })
        {
            var item = new MenuItem { Header = Loc.T("toolsOptions.type." + type), Icon = new SymbolIcon { Symbol = TypeIcon(type) } };
            item.Click += (_, _) => App.NewToolFromTemplate(type);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    internal static SymbolRegular TypeIcon(string? type) => type switch
    {
        "http" => SymbolRegular.Globe24,
        "kdesktop" => SymbolRegular.Flash24,
        "routine" => SymbolRegular.TextBulletListLtr24,
        "script" => SymbolRegular.Code24,
        _ => SymbolRegular.DocumentData24,
    };

    // ── MCP servers ──────────────────────────────────────────────────────

    private void FillServers()
    {
        var servers = App.ReadMcpServers(out var errors);
        var card = Card(Loc.T("toolsOptions.servers"), Loc.T("toolsOptions.serversIntro"));
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        buttons.Children.Add(Wide(SymbolRegular.Add24, Loc.T("toolsOptions.addServer"), _ => EditServer(null), primary: true));
        buttons.Children.Add(Wide(SymbolRegular.ArrowDownload24, Loc.T("toolsOptions.import"), _ => Import()));
        buttons.Children.Add(Wide(SymbolRegular.ClipboardPaste24, Loc.T("toolsOptions.paste"), _ => Paste()));
        card.Add(buttons);
        if (errors.Count > 0) card.Add(Note(Loc.T("toolsOptions.mcpBroken", errors[0]), danger: true));
        if (servers.Count == 0) card.Add(Caption(Loc.T("toolsOptions.noServers")));
        var states = (State?.Servers ?? []).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        if (servers.Count > 0) card.Add(Rows(servers.OrderBy(s => s.Id, Alphabetical).Select(s => ServerListRow(s, states.GetValueOrDefault(s.Id)))));
        Body.Children.Add(card.Border);
    }

    /// <summary>A server in the list: its name, what it is and how it is; a click opens its page.</summary>
    private FrameworkElement ServerListRow(McpServerEntry server, McpServerInfo? state)
    {
        var (status, brush) = StatusOf(server, state);
        var body = new StackPanel();
        body.Children.Add(Title(server.Id));
        body.Children.Add(Caption(Shorten(server.Summary, 90)));
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        chips.Children.Add(StatusChip(status, brush));
        if (state is not null && !server.Disabled) chips.Children.Add(Chip(Loc.T("toolsOptions.serverTools", state.Tools)));
        body.Children.Add(chips);
        // The name and its state open the server's page (the keyboard has the chevron button).
        var open = new Border { Child = body, Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        open.MouseLeftButtonUp += (_, _) => Go("tools/servers/" + server.Id);
        var buttons = Buttons();
        buttons.Children.Add(Toggle(!server.Disabled, Loc.T("toolsOptions.serverSwitch", server.Id), on => App.SetMcpServerDisabled(server.Id, !on)));
        buttons.Children.Add(Small(SymbolRegular.ChevronRight24, Loc.T("toolsOptions.openServer", server.Id), () => Go("tools/servers/" + server.Id), true));
        return Row(SymbolRegular.PlugConnected24, open, buttons, server.Id, dim: server.Disabled);
    }

    /// <summary>A row that opens a page of its own: a click on it, or its chevron (for the keyboard).</summary>
    /// <param name="extra">A button before the chevron (stop a server).</param>
    private FrameworkElement OpenableRow(SymbolRegular icon, string title, string? about, WrapPanel? chips, string page, bool danger = false, bool dim = false, UIElement? extra = null)
    {
        var body = new StackPanel();
        var name = Title(title);
        name.FontWeight = FontWeights.SemiBold;
        body.Children.Add(name);
        if (about is { Length: > 0 })
        {
            var caption = Caption(Shorten(about, 220));
            caption.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(caption);
        }
        if (chips is { Children.Count: > 0 }) body.Children.Add(chips);
        var open = new Border { Child = body, Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        open.MouseLeftButtonUp += (_, _) => Go(page);
        var buttons = Buttons();
        if (extra is not null) buttons.Children.Add(extra);
        buttons.Children.Add(Small(SymbolRegular.ChevronRight24, Loc.T("toolsOptions.openServer", title), () => Go(page), true));
        return Row(icon, open, buttons, title, danger: danger, dim: dim);
    }

    private (string Text, string Brush) StatusOf(McpServerEntry server, McpServerInfo? state)
    {
        var testing = _app.AssistantToolsTesting.Contains(server.Id);
        return server.Disabled ? (Loc.T("toolsOptions.serverOff"), "TextFillColorTertiaryBrush")
            : testing || state?.State == "starting" ? (Loc.T("toolsOptions.serverStarting"), "Kd.AccentText")
            : state?.State == "failed" ? (Loc.T("toolsOptions.serverFailedShort"), "Kd.Danger")
            : state?.State == "running" ? (Loc.T("toolsOptions.serverRunning"), "Kd.Success")
            : (Loc.T("toolsOptions.serverStopped"), "TextFillColorSecondaryBrush");
    }

    /// <summary>One server: what it is, how it is, its buttons, and its tools.</summary>
    private void FillServer()
    {
        var server = App.ReadMcpServers(out _).FirstOrDefault(s => string.Equals(s.Id, ItemId, StringComparison.OrdinalIgnoreCase));
        if (server is null)
        {
            var gone = Card(ItemId ?? string.Empty, Loc.T("toolsOptions.serverGone"));
            gone.Add(Wide(SymbolRegular.ArrowLeft24, Loc.T("toolsOptions.backToServers"), _ => Go("tools/servers")));
            Body.Children.Add(gone.Border);
            return;
        }
        var state = (State?.Servers ?? []).FirstOrDefault(s => string.Equals(s.Id, server.Id, StringComparison.OrdinalIgnoreCase));
        var (status, brush) = StatusOf(server, state);
        var running = state?.State == "running";
        var testing = _app.AssistantToolsTesting.Contains(server.Id);

        var head = Card(server.Id, null, SymbolRegular.PlugConnected24);
        var line = new TextBlock { Text = server.Summary, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -6, 0, 8) };
        line.SetResourceReference(TextBlock.ForegroundProperty, "Kd.TextDim");
        head.Add(line);
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        chips.Children.Add(StatusChip(status, brush));
        chips.Children.Add(Chip(Loc.T("toolsOptions.transport." + server.Transport)));
        if (state is not null) chips.Children.Add(Chip(Loc.T("toolsOptions.serverTools", state.Tools)));
        foreach (var secret in server.SecretNames) chips.Children.Add(Chip("🔑 " + secret));
        head.Add(chips);
        if (state?.State == "failed" && !string.IsNullOrEmpty(state.Error)) head.Add(Note(Loc.T("toolsOptions.serverFailed", state.Error), danger: true));
        if (server.Disabled) head.Add(Note(Loc.T("toolsOptions.serverOffNote")));
        var actions = new WrapPanel();
        actions.Children.Add(Labeled(Toggle(!server.Disabled, Loc.T("toolsOptions.serverSwitch", server.Id), on => App.SetMcpServerDisabled(server.Id, !on)), Loc.T("toolsOptions.on")));
        if (running) actions.Children.Add(Wide(SymbolRegular.Stop24, Loc.T("toolsOptions.stop"), _ => _app.StopMcpServer(server.Id), enabled: Running));
        else actions.Children.Add(Wide(SymbolRegular.Play24, Loc.T(testing ? "toolsOptions.serverStarting" : "toolsOptions.testServer"), _ => _app.StartMcpServer(server.Id), primary: true,
            enabled: Running && !server.Disabled && !testing));
        actions.Children.Add(Wide(SymbolRegular.Edit24, Loc.T("toolsOptions.edit"), _ => EditServer(server)));
        actions.Children.Add(Wide(SymbolRegular.Delete24, Loc.T("toolsOptions.remove"), _ => RemoveServer(server.Id)));
        head.Add(actions);
        if (_app.AssistantToolTests.TryGetValue(server.Id, out var test) && !testing) head.Add(Result(test));
        Body.Children.Add(head.Border);

        var tools = ToolsOf("mcp:" + server.Id).OrderBy(TitleOf, Alphabetical).ToList();
        var list = Card(Loc.T("toolsOptions.serverToolsTitle", tools.Count), Loc.T(tools.Count == 0 ? "toolsOptions.serverNoTools" : "toolsOptions.serverToolsIntro"));
        if (tools.Count > 1) list.Add(Bulk(tools));
        if (tools.Count > 0) list.Add(Rows(tools.Select(t => ToolRow(t))));
        Body.Children.Add(list.Border);
    }

    /// <summary>The servers of another program's file (it opens where it looked last, Documents the first time).</summary>
    private void Import()
    {
        var settings = _app.Config.Settings;
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("toolsOptions.importTitle"),
            Filter = Loc.T("toolsOptions.importFilter") + "|*.json",
            InitialDirectory = Directory.Exists(settings.AssistantMcpImportFolder) ? settings.AssistantMcpImportFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        settings.AssistantMcpImportFolder = Path.GetDirectoryName(dialog.FileName);
        string json;
        try { json = File.ReadAllText(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = Say(Loc.T("toolsOptions.importTitle"), ex.Message); return; }
        ImportFrom(json, Path.GetFileName(dialog.FileName), "toolsOptions.importNone");
    }

    /// <summary>What was copied from a README or another program's settings (5.0.137).</summary>
    private void Paste()
    {
        string json;
        try { json = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty; }
        catch (System.Runtime.InteropServices.ExternalException) { json = string.Empty; }
        ImportFrom(json, Loc.T("toolsOptions.clipboard"), "toolsOptions.pasteNone");
    }

    private void ImportFrom(string json, string source, string noneKey)
    {
        var existing = App.ReadMcpServers(out _).Select(s => s.Id).ToList();
        var found = McpConfig.Import(json, existing);
        if (found.Count == 0)
        {
            _ = Say(Loc.T("toolsOptions.importTitle"), Loc.T(noneKey));
            return;
        }
        var window = new McpImportWindow(found, source) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true && window.Chosen.Count > 0) _app.ImportMcpServers(window.Chosen);
    }

    private void EditServer(McpServerEntry? server)
    {
        var existing = App.ReadMcpServers(out _).Select(s => s.Id).Where(id => !string.Equals(id, server?.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var window = new McpServerWindow(server, existing, _app.ToolSecrets) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.Result is not { } result) return;
        _app.SaveMcpServer(result, server?.Id, window.Secrets);
        if (server is not null && !string.Equals(server.Id, result.Id, StringComparison.OrdinalIgnoreCase)) Go("tools/servers/" + result.Id);
    }

    private async void RemoveServer(string id)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("toolsOptions.removeTitle"),
            Width = 480,
            MaxWidth = 480,
            Content = Loc.T("toolsOptions.removeQuestion", id),
            PrimaryButtonText = Loc.T("toolsOptions.remove"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        _app.RemoveMcpServer(id);
        if (Part == "tools/server") Go("tools/servers");
    }

    // ── secrets ──────────────────────────────────────────────────────────

    /// <summary>The secret being changed (its box shows).</summary>
    private string? _changingSecret;

    /// <summary>The secret just added or changed from «Añadir un secreto» (a line says so; the boxes are emptied).</summary>
    private string? _savedSecret;

    private void FillSecrets()
    {
        var secrets = _app.ToolSecretList();
        var card = Card(Loc.T("toolsOptions.secrets"), Loc.T("toolsOptions.secretsIntro"));
        if (secrets.Count == 0) card.Add(Caption(Loc.T("toolsOptions.noSecrets")));
        else card.Add(Rows(secrets.OrderBy(s => s.Name, Alphabetical).Select(s => SecretRow(s.Name, s.Kept, s.UsedBy))));
        Body.Children.Add(card.Border);

        var add = Card(Loc.T("toolsOptions.addSecretTitle"), Loc.T("toolsOptions.addSecretIntro"), SymbolRegular.Add24);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBox { PlaceholderText = Loc.T("toolsOptions.secretName"), ClearButtonEnabled = false, Tag = "newSecretName", FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        System.Windows.Automation.AutomationProperties.SetName(name, Loc.T("toolsOptions.secretName"));
        var value = new PasswordBox { PlaceholderText = Loc.T("toolsOptions.secretValue"), Margin = new Thickness(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(value, Loc.T("toolsOptions.secretValue"));
        var save = new Button { Content = Loc.T("toolsOptions.addSecret"), Icon = new SymbolIcon { Symbol = SymbolRegular.Add24 }, Appearance = ControlAppearance.Primary, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch };
        save.Click += async (_, _) =>
        {
            var text = name.Text.Trim();
            if (!ToolSecrets.IsName(text) || value.Password.Length == 0)
            {
                _ = Say(Loc.T("toolsOptions.secrets"), Loc.T("toolsOptions.secretBadName"));
                return;
            }
            // One that is already kept: its value is changed only if they say so (it was, without a word).
            if (secrets.FirstOrDefault(s => string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase)) is { Kept: true } kept)
            {
                var box = new Wpf.Ui.Controls.MessageBox
                {
                    Title = Loc.T("toolsOptions.secrets"),
                    Width = 480,
                    MaxWidth = 480,
                    Content = Loc.T("toolsOptions.secretExists", kept.Name),
                    PrimaryButtonText = Loc.T("toolsOptions.secretChange"),
                    CloseButtonText = Loc.T("common.cancel"),
                };
                if (await box.ShowCenteredAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
                text = kept.Name;
            }
            _savedSecret = text;
            _shown = null;
            _app.SetToolSecret(text, value.Password);
        };
        Grid.SetColumn(value, 1);
        Grid.SetColumn(save, 2);
        grid.Children.Add(name);
        grid.Children.Add(value);
        grid.Children.Add(save);
        add.Add(grid);
        if (_savedSecret is { } saved)
        {
            var done = Caption("✓ " + Loc.T("toolsOptions.secretSaved", saved));
            done.Margin = new Thickness(0, 8, 0, 0);
            done.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Success");
            add.Add(done);
        }
        Body.Children.Add(add.Border);
    }

    private FrameworkElement SecretRow(string name, bool kept, IReadOnlyList<string> usedBy)
    {
        var body = new StackPanel();
        var title = Title(name);
        title.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        body.Children.Add(title);
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        chips.Children.Add(StatusChip(Loc.T(kept ? "toolsOptions.secretKept" : "toolsOptions.secretMissing"), kept ? "Kd.Success" : "Kd.Danger"));
        chips.Children.Add(Chip(usedBy.Count > 0 ? Loc.T("toolsOptions.secretUsedBy", string.Join(", ", usedBy)) : Loc.T("toolsOptions.secretUnused")));
        body.Children.Add(chips);
        var buttons = Buttons();
        if (_changingSecret == name)
        {
            var box = new PasswordBox { Width = 240, PlaceholderText = Loc.T("toolsOptions.secretValue"), Tag = "secret:" + name };
            System.Windows.Automation.AutomationProperties.SetName(box, Loc.T("toolsOptions.secretValue"));
            void Save()
            {
                _changingSecret = null;
                if (box.Password.Length > 0) _app.SetToolSecret(name, box.Password);
                else Refresh();
            }
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Save();
                else if (e.Key == Key.Escape) { _changingSecret = null; Refresh(); }
                else return;
                e.Handled = true;
            };
            buttons.Children.Add(box);
            buttons.Children.Add(Small(SymbolRegular.Checkmark24, Loc.T("memoryOptions.save"), Save, true, name));
            buttons.Children.Add(Small(SymbolRegular.Dismiss24, Loc.T("common.cancel"), () => { _changingSecret = null; Refresh(); }, true, name));
            Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            if (kept) buttons.Children.Add(Small(SymbolRegular.Edit24, Loc.T("toolsOptions.secretChange"), () => { _changingSecret = name; Refresh(); }, true, name));
            else
            {
                var set = new Button { Content = Loc.T("toolsOptions.secretSet"), Margin = new Thickness(4, 0, 0, 0), MinHeight = 36, Appearance = ControlAppearance.Primary };
                System.Windows.Automation.AutomationProperties.SetName(set, Loc.T("toolsOptions.secretSet") + " " + name);
                set.Click += (_, _) => { _changingSecret = name; Refresh(); };
                buttons.Children.Add(set);
            }
            if (kept) buttons.Children.Add(Small(SymbolRegular.Delete24, Loc.T("toolsOptions.secretDelete"), () => _app.SetToolSecret(name, null), true, name));
        }
        return Row(SymbolRegular.Key24, body, buttons, name, danger: !kept && usedBy.Count > 0);
    }

    private static Task Say(string title, string text) => new Wpf.Ui.Controls.MessageBox
    {
        Title = title,
        Width = 480,
        MaxWidth = 480,
        Content = text,
        CloseButtonText = Loc.T("common.close"),
    }.ShowCenteredAsync();

    private void OnGoGeneral(object sender, RoutedEventArgs e) => Go("assistant");
}
