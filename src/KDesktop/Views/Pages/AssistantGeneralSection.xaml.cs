using System.Windows;
using System.Windows.Controls;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → General (stage 1.8): turn the module on and see how it is doing. Who is on the desktop is
/// chosen in Options → Assistants (several at once since stage 1.11). The talk key and the choreography arrive with
/// the voice and the brain.
/// </summary>
public partial class AssistantGeneralSection : UserControl
{
    private readonly App _app = App.Current;

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
        var (text, brush) = AssistantLook.Summary(_app);
        StateText.Text = text;
        StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        PanelButton.Visibility = _app.Assistant is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenPanel(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("assistant");
}
