using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Memory (stage 3.7): what the assistants know about the user, newest first, where it came from
/// (said, learned, written here) and when; each one can be edited in place or deleted, new ones written, and all of it
/// cleared. Whether they learn by themselves from what is talked about. The rows are made in code (an ItemsControl
/// hides its buttons from UI Automation).
/// </summary>
public partial class AssistantMemorySection : UserControl
{
    private readonly App _app = App.Current;
    /// <summary>The one being edited (its text box shows instead of its text).</summary>
    private string? _editing;

    public AssistantMemorySection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantMemoryChanged += Fill;
            _app.AssistantChanged += OnAssistantChanged;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantMemoryChanged -= Fill;
            _app.AssistantChanged -= OnAssistantChanged;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            _app.RequestAssistantMemory();
            Fill();
        };
    }

    private void OnAssistantChanged()
    {
        if (!IsVisible) return;
        // The process came back: it has the memory.
        if (_app.AssistantMemory is null) _app.RequestAssistantMemory();
        Fill();
    }

    private void Fill()
    {
        var running = _app.Assistant is { IsRunning: true };
        OffBox.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        OffText.Text = Loc.T(_app.Assistant is null ? "brainOptions.moduleOff" : "memoryOptions.notRunning");
        GoGeneral.Visibility = _app.Assistant is null ? Visibility.Visible : Visibility.Collapsed;
        AddRow.IsEnabled = running;
        var items = _app.AssistantMemory ?? [];
        Rows.Children.Clear();
        foreach (var item in items) Rows.Children.Add(Row(item, running));
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T(_app.AssistantMemory is null && running ? "memoryOptions.loading" : "memoryOptions.empty");
        ClearButton.Visibility = items.Count > 0 && running ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border Row(MemoryEntry item, bool running)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon
        {
            Symbol = item.Source switch { "learned" => SymbolRegular.BrainCircuit24, "added" => SymbolRegular.Edit24, _ => SymbolRegular.PersonVoice24 },
            FontSize = 18,
            Margin = new Thickness(0, 1, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        icon.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        grid.Children.Add(icon);

        var caption = new TextBlock
        {
            Style = (Style)FindResource("Caption"),
            Text = Loc.T("memoryOptions.source." + item.Source) + " · " + item.CreatedAt.ToLocalTime().ToString("g", Loc.Instance.Culture),
        };
        var body = new StackPanel();
        Grid.SetColumn(body, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(buttons, 2);
        if (_editing == item.Id)
        {
            var box = new TextBox { Text = item.Text, MaxLength = 200, ClearButtonEnabled = false };
            AutomationProperties.SetName(box, Loc.T("memoryOptions.edit"));
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Save(item, box.Text);
                else if (e.Key == Key.Escape) StopEditing();
                else return;
                e.Handled = true;
            };
            body.Children.Add(box);
            body.Children.Add(caption);
            buttons.Children.Add(Small(SymbolRegular.Checkmark24, Loc.T("memoryOptions.save"), () => Save(item, box.Text), running));
            buttons.Children.Add(Small(SymbolRegular.Dismiss24, Loc.T("common.cancel"), StopEditing, true));
            Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }
        else
        {
            var text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            body.Children.Add(text);
            body.Children.Add(caption);
            buttons.Children.Add(Small(SymbolRegular.Edit24, Loc.T("memoryOptions.edit"), () => { _editing = item.Id; Fill(); }, running));
            buttons.Children.Add(Small(SymbolRegular.Delete24, Loc.T("memoryOptions.delete"), () => _app.DeleteAssistantMemory(item.Id), running));
        }
        grid.Children.Add(body);
        grid.Children.Add(buttons);
        var row = new Border { Child = grid, Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Border.BorderBrushProperty, "Kd.Subtle");
        AutomationProperties.SetName(row, item.Text);
        return row;
    }

    private static Button Small(SymbolRegular symbol, string name, Action click, bool enabled)
    {
        var button = new Button
        {
            Icon = ButtonIcons.For(symbol),
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = name,
            IsEnabled = enabled,
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private void Save(MemoryEntry item, string text)
    {
        if (text.Trim().Length >= 3 && text.Trim() != item.Text) _app.EditAssistantMemory(item.Id, text.Trim());
        StopEditing();
    }

    private void StopEditing()
    {
        _editing = null;
        Fill();
    }

    private void OnAddKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Add();
    }

    private void OnAdd(object sender, RoutedEventArgs e) => Add();

    private void Add()
    {
        var text = AddBox.Text.Trim();
        if (text.Length < 3 || !_app.AddAssistantMemory(text)) return;
        AddBox.Text = string.Empty;
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("memoryOptions.clearTitle"),
            Width = 480,
            MaxWidth = 480,
            Content = Loc.T("memoryOptions.clearQuestion"),
            PrimaryButtonText = Loc.T("memoryOptions.clear"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) _app.ClearAssistantMemory();
    }

    private void OnGoGeneral(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistant");
}
