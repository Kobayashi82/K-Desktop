using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → General (stage 1.8): turn the module on and see how it is doing. Who is on the desktop is
/// chosen in Options → Assistants (several at once since stage 1.11). The word «for the assistant» (stage 5.1) calls
/// whoever answers what is not addressed to anyone, or the module without a body.
/// </summary>
public partial class AssistantGeneralSection : UserControl
{
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
        var (text, brush) = AssistantLook.Summary(_app);
        StateText.Text = text;
        StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        PanelButton.Visibility = _app.Assistant is null ? Visibility.Collapsed : Visibility.Visible;
        FillAnyWake();
    }

    /// <summary>The recorded words; one of an assistant says whose it is (choosing it here takes it from that one).</summary>
    private void FillAnyWake()
    {
        _filling = true;
        var owners = _app.Config.Settings.AssistantWakeWords;
        List<WakeOption> options = [new(null, Loc.T("assistantOptions.wakeNone"))];
        foreach (var word in _app.Voice.WakeWords.Words)
        {
            var owner = owners.FirstOrDefault(p => p.Value == word.Id).Key;
            options.Add(new(word.Id, owner is null or AssistantTargeting.AnyAssistant ? word.Name : Loc.T("assistantOptions.wakeOf", word.Name, App.AssistantName(owner))));
        }
        AnyWakeBox.ItemsSource = options;
        AnyWakeBox.SelectedItem = options.FirstOrDefault(o => o.Id == _app.AnyAssistantWakeWord) ?? options[0];
        _filling = false;
    }

    private void OnAnyWakeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || AnyWakeBox.SelectedItem is not WakeOption option || option.Id == _app.AnyAssistantWakeWord) return;
        // After this event: the box is filled again when it changes.
        Dispatcher.BeginInvoke(() => _app.SetAssistantWakeWord(AssistantTargeting.AnyAssistant, option.Id));
    }

    private void OnRecordAnyWake(object sender, RoutedEventArgs e)
    {
        if (!VoiceModels.AllInstalled || _app.Voice.Models.IsDownloading)
        {
            _app.Ui.ShowOsd(Loc.T("wake.needModels"), OsdKind.Info);
            return;
        }
        if (Dialogs.WakeWordDialog.Record(Window.GetWindow(this), suggestedName: Loc.T("assistantOptions.anyWakeName")) is { } word)
            _app.SetAssistantWakeWord(AssistantTargeting.AnyAssistant, word.Id);
    }

    private void OnOpenPanel(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("assistant");
}
