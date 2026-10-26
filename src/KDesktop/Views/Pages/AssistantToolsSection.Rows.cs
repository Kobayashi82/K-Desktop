using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Views.Assistant;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>The rows of Options → Tools: a tool with its switch, when it asks, its test; and the pieces they are made of.</summary>
public partial class AssistantToolsSection
{
    /// <summary>The tool whose test is open.</summary>
    private string? _testing;

    /// <summary>What was written in each tool's test form (tool → field → text), and the JSON edited by hand.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _forms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rawJson = new(StringComparer.Ordinal);
    private readonly HashSet<string> _showJson = new(StringComparer.Ordinal);

    /// <summary>A tool's name for people: the built-in ones in the user's language, the others from their name («Get joke»).</summary>
    internal static string TitleOf(ToolInfo tool)
    {
        if (tool.Group == "builtin" && Loc.T("tool." + tool.Name + ".title") is var own && own != "tool." + tool.Name + ".title") return own;
        var name = tool.Name;
        // «mcp_jokes_get_joke»: the server is said apart.
        if (tool.Group.StartsWith("mcp:", StringComparison.Ordinal) && name.StartsWith("mcp_" + tool.Group[4..] + "_", StringComparison.OrdinalIgnoreCase))
            name = name[(5 + tool.Group.Length - 4)..];
        return ToolTestForm.Humanize(name);
    }

    /// <param name="showWhere">In the search of the summary: where it comes from.</param>
    /// <param name="file">One of the user's JSON tools: its file.</param>
    private FrameworkElement ToolRow(ToolInfo tool, bool showWhere = false, ToolFileInfo? file = null)
    {
        var testing = _app.AssistantToolsTesting.Contains(tool.Name);
        var body = new StackPanel();
        var heading = new WrapPanel();
        var title = Title(TitleOf(tool));
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 8, 0);
        heading.Children.Add(title);
        var technical = new TextBlock { Text = tool.Name, FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        technical.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        heading.Children.Add(technical);
        body.Children.Add(heading);
        // A phrase that asks for it says it best, in the user's language; the description is for the model (in English).
        var about = tool.Example is { Length: > 0 } example ? Loc.T("toolsOptions.example", example) : OneLine(tool.Description);
        // The description is long for some (an MCP server's say how to use them): as wide as the row, and whole in its tooltip.
        var wide = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var caption = Caption(Shorten(about, 300));
        if (about.Length > 300) caption.ToolTip = new System.Windows.Controls.ToolTip { Content = new TextBlock { Text = about, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 } };
        wide.Children.Add(caption);
        var chips = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        if (showWhere) chips.Children.Add(Chip(Where(tool)));
        if (file is not null) chips.Children.Add(Chip(System.IO.Path.GetFileName(file.File) + (file.Type is { } type ? " · " + Loc.T("toolsOptions.type." + type) : string.Empty)));
        if (tool.ReadsOnly) chips.Children.Add(Chip(Loc.T("toolsOptions.readsOnly")));
        if (!tool.Enabled) chips.Children.Add(StatusChip(Loc.T("toolsOptions.toolOff"), "TextFillColorTertiaryBrush"));
        if (chips.Children.Count > 0) wide.Children.Add(chips);
        body.VerticalAlignment = VerticalAlignment.Center;

        var buttons = Buttons();
        buttons.Children.Add(RiskChip(tool));
        var test = new Button
        {
            Content = Loc.T(_testing == tool.Name ? "toolsOptions.closeTest" : "toolsOptions.test"),
            Icon = new SymbolIcon { Symbol = _testing == tool.Name ? SymbolRegular.ChevronUp24 : SymbolRegular.Play24 },
            Appearance = ControlAppearance.Secondary,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(6, 0, 6, 0),
            IsEnabled = Running && tool.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(test, Loc.T("toolsOptions.test") + " " + tool.Name);
        test.Click += (_, _) =>
        {
            // Closed, its result goes with it (it stayed under the row).
            if (_testing == tool.Name) _app.ForgetToolTest(tool.Name);
            _testing = _testing == tool.Name ? null : tool.Name;
            Refresh();
        };
        buttons.Children.Add(test);
        buttons.Children.Add(Toggle(tool.Enabled, Loc.T("toolsOptions.toolSwitch", tool.Name), on => _app.SetToolEnabled(tool.Name, on)));

        // The result is only seen with the test open.
        var below = _testing == tool.Name && tool.Enabled ? TestPanel(tool, testing) : null;
        var icon = tool.Group == "builtin" ? SubjectIcon(SubjectOf(tool.Name))
            : tool.Group == "json" ? TypeIcon(file?.Type)
            : SymbolRegular.PlugConnected24;
        return Row(icon, body, buttons, tool.Name, dim: !tool.Enabled, below: below, wide: wide);
    }

    private static string Where(ToolInfo tool) => tool.Group switch
    {
        "builtin" => Loc.T("settings.nav.toolBuiltin") + " · " + Loc.T("toolsOptions.subject." + SubjectOf(tool.Name)),
        "json" => Loc.T("settings.nav.toolFiles"),
        _ => Loc.T("toolsOptions.groupMcp", tool.Group[4..]),
    };

    /// <summary>When it asks before doing it: a chip of its colour that opens the three choices.</summary>
    private FrameworkElement RiskChip(ToolInfo tool)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, RiskBrush(tool.Risk));
        content.Children.Add(dot);
        content.Children.Add(new TextBlock { Text = Loc.T("toolsOptions.risk." + tool.Risk), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        content.Children.Add(new SymbolIcon { Symbol = SymbolRegular.ChevronDown24, FontSize = 11, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var chip = new Button
        {
            Content = content,
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(10, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("toolsOptions.riskTip"),
        };
        chip.SetResourceReference(Control.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        chip.BorderThickness = new Thickness(1);
        AutomationProperties.SetName(chip, Loc.T("toolsOptions.risk", tool.Name));
        chip.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = chip, Placement = PlacementMode.Bottom };
            foreach (var risk in new[] { "safe", "askOnce", "askAlways" })
            {
                var mark = new Ellipse { Width = 8, Height = 8 };
                mark.SetResourceReference(Shape.FillProperty, RiskBrush(risk));
                var item = new MenuItem
                {
                    Header = Loc.T("toolsOptions.risk." + risk) + (risk == tool.DefaultRisk ? " " + Loc.T("toolsOptions.riskDefault") : string.Empty),
                    Icon = mark,
                    FontWeight = risk == tool.Risk ? FontWeights.SemiBold : FontWeights.Normal,
                };
                item.Click += (_, _) =>
                {
                    if (risk != tool.Risk) _app.SetToolRisk(tool.Name, risk == tool.DefaultRisk ? null : risk);
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return chip;
    }

    private static string RiskBrush(string risk) => risk switch
    {
        "askAlways" => "Kd.Danger",
        "askOnce" => "Kd.Warning",
        _ => "Kd.Success",
    };

    /// <summary>The test of a tool: a field for each of its parameters (or its JSON), run, and what it gave back.</summary>
    private Border TestPanel(ToolInfo tool, bool testing)
    {
        if (!_forms.TryGetValue(tool.Name, out var values)) _forms[tool.Name] = values = new Dictionary<string, string>(StringComparer.Ordinal);
        var form = new ToolTestForm(tool.Schema, values);
        var asJson = _showJson.Contains(tool.Name) || !form.HasFields && tool.Schema is null;
        var stack = new StackPanel();
        var intro = Caption(Loc.T(form.HasFields ? "toolsOptions.testIntro" : "toolsOptions.testNoFields"));
        intro.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(intro);

        TextBox? raw = null;
        if (asJson)
        {
            raw = new TextBox
            {
                Text = _rawJson.GetValueOrDefault(tool.Name) ?? form.ToJson(out _) ?? "{}",
                FontFamily = new FontFamily("Consolas"),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinLines = 3,
                ClearButtonEnabled = false,
                Tag = "json:" + tool.Name,
            };
            AutomationProperties.SetName(raw, Loc.T("toolsOptions.arguments"));
            raw.TextChanged += (_, _) => _rawJson[tool.Name] = raw.Text;
            stack.Children.Add(raw);
        }
        else if (form.HasFields) stack.Children.Add(form.View);

        var error = Caption(string.Empty);
        error.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
        error.Visibility = Visibility.Collapsed;
        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var run = new Button
        {
            Content = Loc.T(testing ? "toolsOptions.testing" : "toolsOptions.run"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 },
            Appearance = ControlAppearance.Primary,
            IsEnabled = !testing,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(run, Loc.T("toolsOptions.run") + " " + tool.Name);
        run.Click += (_, _) =>
        {
            string? arguments;
            string? why = null;
            if (raw is not null)
            {
                arguments = raw.Text;
                try { System.Text.Json.Nodes.JsonNode.Parse(arguments.Length == 0 ? "{}" : arguments); }
                catch (System.Text.Json.JsonException) { why = Loc.T("toolsOptions.form.badJson"); }
            }
            else arguments = form.ToJson(out why);
            if (why is not null || arguments is null)
            {
                error.Text = why ?? string.Empty;
                error.Visibility = Visibility.Visible;
                return;
            }
            _app.TestAssistantTool(tool.Name, arguments);
        };
        actions.Children.Add(run);
        if (form.HasFields)
        {
            var swap = new Button
            {
                Content = Loc.T(asJson ? "toolsOptions.form.asForm" : "toolsOptions.form.asJson"),
                Icon = new SymbolIcon { Symbol = asJson ? SymbolRegular.Form24 : SymbolRegular.Code24 },
                Appearance = ControlAppearance.Transparent,
            };
            swap.Click += (_, _) =>
            {
                if (asJson)
                {
                    _showJson.Remove(tool.Name);
                    _rawJson.Remove(tool.Name);
                }
                else
                {
                    _showJson.Add(tool.Name);
                    _rawJson[tool.Name] = form.ToJson(out _) ?? "{}";
                }
                _shown = null;
                Refresh();
            };
            actions.Children.Add(swap);
        }
        stack.Children.Add(actions);
        stack.Children.Add(error);
        var warning = Caption(Loc.T("toolsOptions.testReal"));
        warning.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(warning);
        if (_app.AssistantToolTests.TryGetValue(tool.Name, out var last) && !testing) stack.Children.Add(Result(last));

        var panel = new Border { Child = stack, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 10, 0, 2), BorderThickness = new Thickness(1) };
        panel.SetResourceReference(Border.BackgroundProperty, "Kd.Subtle");
        panel.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var focus = raw ?? form.First;
        if (focus is not null && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
            Dispatcher.BeginInvoke(() => focus.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return panel;
    }

    /// <summary>What a test gave back: a coloured stripe, whether it worked and how long it took, and the whole text (it can be copied).</summary>
    private static FrameworkElement Result(ToolTestMessage test)
    {
        var brush = test.Ok ? "Kd.Success" : "Kd.Danger";
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var stripe = new Border { CornerRadius = new CornerRadius(2) };
        stripe.SetResourceReference(Border.BackgroundProperty, brush);
        grid.Children.Add(stripe);
        var stack = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        var head = new TextBlock { Text = (test.Ok ? "✓ " : "✗ ") + Loc.T(test.Ok ? "toolsOptions.result.ok" : "toolsOptions.result.failed", test.Ms), FontWeight = FontWeights.SemiBold, FontSize = 12 };
        head.SetResourceReference(TextBlock.ForegroundProperty, brush);
        stack.Children.Add(head);
        var text = new System.Windows.Controls.TextBox
        {
            // Plain (WPF-UI's draws a line under it): text to read and copy, not a box to write in.
            Style = new Style(typeof(System.Windows.Controls.TextBox)),
            Text = test.Text.Length > 6000 ? test.Text[..6000] + "…" : test.Text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
        };
        text.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        AutomationProperties.SetName(text, Loc.T("toolsOptions.result"));
        stack.Children.Add(text);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        return grid;
    }

    // ── pieces ───────────────────────────────────────────────────────────

    /// <summary>A card being made: its border and where its content goes.</summary>
    private sealed class CardBuilder(Border border, StackPanel content)
    {
        public Border Border { get; } = border;

        public void Add(UIElement element) => content.Children.Add(element);
    }

    private CardBuilder Card(string title, string? intro, SymbolRegular? icon = null)
    {
        var stack = new StackPanel();
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is { } symbol)
        {
            var glyph = new SymbolIcon { Symbol = symbol, FontSize = 18 };
            glyph.SetResourceReference(ForegroundProperty, "Kd.AccentText");
            var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Child = glyph, Margin = new Thickness(0, 0, 12, 12), VerticalAlignment = VerticalAlignment.Top };
            tile.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
            heading.Children.Add(tile);
        }
        var name = new TextBlock { Style = (Style)FindResource("CardTitle"), Text = title, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) name.Margin = new Thickness(0, 0, 0, 12);
        heading.Children.Add(name);
        stack.Children.Add(heading);
        if (intro is { Length: > 0 })
        {
            var text = Caption(intro);
            text.Margin = new Thickness(0, icon is null ? -6 : -4, 0, 12);
            stack.Children.Add(text);
        }
        var card = new Border { Style = (Style)FindResource("Card"), Child = stack };
        return new CardBuilder(card, stack);
    }

    /// <summary>A tile of the summary: its picture, its name, how many and a word on them; a click goes to its page.</summary>
    private Button Tile(SymbolRegular icon, string title, int count, string? subtitle, string page, bool warn = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = new SymbolIcon { Symbol = icon, FontSize = 22 };
        glyph.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        var tile = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Child = glyph, Margin = new Thickness(0, 0, 14, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
        grid.Children.Add(tile);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        texts.Children.Add(name);
        if (subtitle is { Length: > 0 })
        {
            var line = new TextBlock { Text = subtitle, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetResourceReference(TextBlock.ForegroundProperty, warn ? "Kd.Danger" : "Kd.TextDim");
            texts.Children.Add(line);
        }
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var number = new TextBlock { Text = count.ToString(Loc.Instance.Culture), FontSize = 26, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
        number.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(number, 2);
        grid.Children.Add(number);
        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
        };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => Go(page);
        return button;
    }

    /// <summary>Rows one under the other on a panel of their own, a thin line between them.</summary>
    private static Border Rows(IEnumerable<UIElement> rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows)
        {
            if (stack.Children.Count > 0)
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 0) };
                line.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
                stack.Children.Add(line);
            }
            stack.Children.Add(row);
        }
        var panel = new Border { Style = (Style)Application.Current.FindResource("ListPanel"), Child = stack, Padding = new Thickness(14, 0, 14, 0) };
        return panel;
    }

    /// <param name="danger">Something is wrong with it: its picture in red.</param>
    /// <param name="dim">Switched off: dimmed (its buttons are not).</param>
    /// <param name="below">What goes under it, across (a test, its result).</param>
    /// <param name="wide">Under the name and the buttons, as wide as the row (a long description is not squeezed next to the buttons).</param>
    private static Border Row(SymbolRegular symbol, UIElement body, UIElement buttons, string name, bool danger = false, bool dim = false, UIElement? below = null,
        FrameworkElement? wide = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var glyph = new SymbolIcon { Symbol = symbol, FontSize = 18 };
        glyph.SetResourceReference(ForegroundProperty, danger ? "Kd.Danger" : "Kd.AccentText");
        var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Child = glyph, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        tile.SetResourceReference(Border.BackgroundProperty, danger ? "Kd.DangerSoft" : "Kd.AccentSoft");
        Grid.SetRowSpan(tile, 2);
        grid.Children.Add(tile);
        if (wide is not null)
        {
            Grid.SetRow(wide, 1);
            Grid.SetColumn(wide, 1);
            Grid.SetColumnSpan(wide, 2);
            grid.Children.Add(wide);
            if (dim) wide.Opacity = 0.6;
        }
        Grid.SetColumn((FrameworkElement)body, 1);
        grid.Children.Add(body);
        Grid.SetColumn((FrameworkElement)buttons, 2);
        grid.Children.Add(buttons);
        if (dim)
        {
            tile.Opacity = 0.5;
            ((FrameworkElement)body).Opacity = 0.6;
        }
        if (below is not null)
        {
            Grid.SetRow(below, 2);
            Grid.SetColumn(below, 1);
            Grid.SetColumnSpan(below, 2);
            grid.Children.Add(below);
        }
        var row = new Border { Child = grid, Padding = new Thickness(0, 12, 0, 12) };
        AutomationProperties.SetName(row, name);
        return row;
    }

    private static StackPanel Buttons() =>
        new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

    private static TextBlock Title(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return block;
    }

    private TextBlock Caption(string text) => new() { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap };

    /// <summary>A note inside a card (red when something is wrong).</summary>
    private Border Note(string text, bool danger = false)
    {
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, danger ? "Kd.Danger" : "TextFillColorPrimaryBrush");
        var box = new Border { Style = (Style)FindResource("InfoBox"), Child = line, Margin = new Thickness(0, 0, 0, 10) };
        if (danger) box.SetResourceReference(Border.BackgroundProperty, "Kd.DangerSoft");
        return box;
    }

    private static Border Chip(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var chip = new Border { Child = label, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 6, 4) };
        chip.SetResourceReference(Border.BackgroundProperty, "Kd.ChipBackground");
        return chip;
    }

    /// <summary>A chip with a dot of a colour: how something is (in use, stopped, missing…).</summary>
    private static Border StatusChip(string text, string brush)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, brush);
        content.Children.Add(dot);
        var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        content.Children.Add(label);
        var chip = new Border { Child = content, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 6, 4) };
        chip.SetResourceReference(Border.BackgroundProperty, "Kd.ChipBackground");
        return chip;
    }

    private static ToggleSwitch Toggle(bool on, string name, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, name);
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        return toggle;
    }

    /// <summary>A switch with a word next to it («Encendido»).</summary>
    private static FrameworkElement Labeled(ToggleSwitch toggle, string label)
    {
        toggle.Content = label;
        toggle.Margin = new Thickness(0, 0, 16, 6);
        return toggle;
    }

    /// <param name="target">What it acts on, for its accessible name («Probar jokes»).</param>
    private static Button Small(SymbolRegular symbol, string name, Action click, bool enabled, string? target = null)
    {
        var button = new Button
        {
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = name,
            IsEnabled = enabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, target is null ? name : name + " " + target);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A button with its word (the actions of a card).</summary>
    private static Button Wide(SymbolRegular symbol, string text, Action<FrameworkElement> click, bool primary = false, bool enabled = true)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 36,
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
            Margin = new Thickness(0, 0, 8, 6),
            IsEnabled = enabled,
        };
        button.Click += (_, _) => click(button);
        return button;
    }

    /// <summary>On one line, without runs of spaces (descriptions of servers come with their line breaks).</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Shorten(string text, int most) => text.Length <= most ? text : text[..most].TrimEnd() + "…";
}
