using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The active assistant's own cards in Panel → Assistant (stage 1.9): drawn from its panel.json and refreshed each time
/// it publishes. Nothing of the assistant's code runs here.
/// </summary>
public partial class AssistantPanelView : UserControl
{
    private readonly App _app = App.Current;
    private AssistantPanelModel? _model;

    public AssistantPanelView()
    {
        InitializeComponent();
        Loaded += (_, _) => _app.AssistantPanelChanged += OnPanelChanged;
        Unloaded += (_, _) => _app.AssistantPanelChanged -= OnPanelChanged;
    }

    /// <summary>Shows this assistant's panel; false (and nothing shown) when it has none.</summary>
    public bool Show(InstalledAssistant? assistant)
    {
        if (assistant is null)
        {
            _model = null;
            Blocks.ItemsSource = null;
            return false;
        }
        // The same assistant and version keeps its cards (and only refreshes them).
        if (_model is null || _model.Assistant.Id != assistant.Id || _model.Assistant.Folder != assistant.Folder)
        {
            _model = PanelLayout.Load(assistant) is { } layout ? new AssistantPanelModel(assistant, layout) : null;
            Blocks.ItemsSource = _model?.Blocks;
        }
        _model?.Refresh(_app.AssistantPanelData(assistant.Id));
        return _model is not null;
    }

    private void OnPanelChanged(string id)
    {
        if (_model?.Assistant.Id == id) _model.Refresh(_app.AssistantPanelData(id));
    }

    private void OnExtra(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PanelButtonModel button } && _model is { } model)
            _app.RunAssistantExtra(model.Assistant.Id, button.Extra);
    }
}
