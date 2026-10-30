using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// Panel → Assistant → Activity (stage 3.3, plan §8): what was asked, what was done and whether it worked, newest first, with
/// «Undo» where the tool can undo it. Since 5.0.289, only what the assistant shown did, or (a pill) everyone's. The rows
/// are made in code (an ItemsControl hides its buttons from UI Automation).
/// </summary>
public partial class ActivityView : UserControl
{
    /// <summary>Lines shown at first and added by «more».</summary>
    private const int Page = 12;

    private readonly App _app = App.Current;
    private int _shown = Page;
    /// <summary>The assistant whose activity it shows (null: not chosen yet, everyone's).</summary>
    private string? _assistant;
    private bool _all;

    public ActivityView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantActivityChanged += Fill;
            _app.AssistantChanged += Fill;
            Fill();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantActivityChanged -= Fill;
            _app.AssistantChanged -= Fill;
        };
    }

    /// <summary>Shows what <paramref name="assistant"/> did (its own at first; everyone's if that was chosen).</summary>
    public void Show(string assistant, string name)
    {
        if (assistant != _assistant) _shown = Page;
        _assistant = assistant;
        MineButton.Content = name;
        if (IsLoaded) Fill();
    }

    private void OnWhose(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || AllButton is null) return;
        _all = AllButton.IsChecked == true;
        _shown = Page;
        Fill();
    }

    private bool Everyone => _all || _assistant is null;

    private void Fill()
    {
        var all = Everyone ? _app.AssistantActivity : [.. _app.AssistantActivity.Where(item => App.IsActivityOf(item, _assistant!))];
        var canUndo = _app.Assistant is { IsRunning: true };
        Rows.Children.Clear();
        foreach (var item in all.Take(_shown)) Rows.Children.Add(Row(item, canUndo));
        EmptyText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Everyone ? Loc.T("assistantActivity.empty") : Loc.T("assistantActivity.emptyOf", App.AssistantName(_assistant));
        ClearButton.Visibility = all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        MoreButton.Visibility = all.Count > _shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border Row(ActivityItem item, bool canUndo)
    {
        var entry = item.Entry;
        var (symbol, brush) = entry.Route switch
        {
            "undo" => (SymbolRegular.ArrowUndo24, entry.Ok ? "TextFillColorSecondaryBrush" : "Kd.Warning"),
            "timer" => (SymbolRegular.Timer24, "Kd.AccentText"),
            "memory" when entry.Ok => (SymbolRegular.BrainCircuit24, "Kd.AccentText"),
            _ when !entry.Ok => (SymbolRegular.DismissCircle24, "Kd.Danger"),
            _ => (SymbolRegular.CheckmarkCircle24, "Kd.Success"),
        };
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 18, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(ForegroundProperty, brush);

        var text = new StackPanel();
        var did = new TextBlock { Text = entry.Did, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        did.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        if (item.Undone) did.TextDecorations = TextDecorations.Strikethrough;
        text.Children.Add(did);
        // Who did it, only when everyone's is shown.
        var line = entry.At.ToLocalTime().ToString("g", Loc.Instance.Culture) + (Everyone ? " · " + App.ActivityWho(item) : string.Empty);
        if (entry.Heard.Length > 0) line += " · «" + entry.Heard + "»";
        line += " · " + Loc.T("assistantActivity.route." + entry.Route);
        // Asked from another channel (telegram, phase 4.6): «pedido por Ana vía Telegram».
        if (entry.By is { Length: > 0 } by) line += " · " + by;
        text.Children.Add(new TextBlock { Text = line, Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 2, 0, 0) });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (entry.Undo is not null && !item.Undone)
        {
            var undo = new Button
            {
                Content = Loc.T("assistantActivity.undo"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowUndo24 },
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = canUndo,
            };
            AutomationProperties.SetName(undo, Loc.T("assistantActivity.undoThis", entry.Did));
            undo.Click += (_, _) => _app.UndoAssistantActivity(item);
            Grid.SetColumn(undo, 2);
            grid.Children.Add(undo);
        }

        var row = new Border { Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        row.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        return row;
    }

    private void OnMore(object sender, RoutedEventArgs e)
    {
        _shown += Page;
        Fill();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _shown = Page;
        _app.ClearAssistantActivity(Everyone ? null : _assistant);
    }
}
