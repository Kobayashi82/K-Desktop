using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → General (stage 1.8): turn the module on, choose the assistant on the desktop and see how it
/// is doing. The wake word, the talk key and the choreography arrive with the voice and the brain.
/// </summary>
public partial class AssistantGeneralSection : UserControl
{
    private sealed record ActiveOption(string? Id, string Name);

    private readonly App _app = App.Current;
    private bool _filling;

    public AssistantGeneralSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => _app.AssistantChanged -= Refresh;
    }

    private void Refresh()
    {
        _filling = true;
        var active = _app.Config.Settings.AssistantActive;
        var options = new List<ActiveOption> { new(null, Loc.T("assistantOptions.none")) };
        options.AddRange(AssistantLook.Rows(active).Select(r => new ActiveOption(r.Id, r.Name)));
        ActiveBox.ItemsSource = options;
        ActiveBox.SelectedItem = options.FirstOrDefault(o => o.Id == active) ?? options[0];
        ActiveBox.IsEnabled = options.Count > 1;
        _filling = false;

        var (text, brush) = AssistantLook.Summary(_app);
        StateText.Text = text;
        StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        PanelButton.Visibility = _app.Assistant is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnActiveChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ActiveBox.SelectedItem is not ActiveOption option) return;
        _app.ActivateAssistant(option.Id);
    }

    private void OnOpenPanel(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("assistant");
}
