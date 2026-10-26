using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → General (stage 1.8): turn the module on and see how it is doing, and what goes for all of them.
/// Who is on the desktop is chosen in Options → Assistants (several at once since stage 1.11); each one is called by its
/// own word or its own key (5.0.113: there is no word or key «for the assistant»).
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
        FillManoeuvres();
    }

    /// <summary>An option of a combo with its value (manoeuvres, long commands).</summary>
    private sealed record Choice<T>(T Value, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>The choices for «more than N actions: only the window ones».</summary>
    private static readonly int[] AnimateLimits = [3, 5, 8];

    /// <summary>The speed of the manoeuvres and the rule for long commands (stage 6.7).</summary>
    private void FillManoeuvres()
    {
        _filling = true;
        var s = _app.Config.Settings;
        List<Choice<string>> speeds =
        [
            new("showy", Loc.T("assistantOptions.manoeuvresShowy")),
            new("normal", Loc.T("assistantOptions.manoeuvresNormal")),
            new("fast", Loc.T("assistantOptions.manoeuvresFast")),
        ];
        ManoeuvresBox.ItemsSource = speeds;
        ManoeuvresBox.SelectedItem = speeds.FirstOrDefault(o => o.Value == s.AssistantManoeuvres) ?? speeds[1];

        List<Choice<string>> asks =
        [
            new(AskPolicy.Never, Loc.T("assistantOptions.askNever")),
            new(AskPolicy.Risky, Loc.T("assistantOptions.askRisky")),
            new(AskPolicy.First, Loc.T("assistantOptions.askFirst")),
        ];
        AskBeforeBox.ItemsSource = asks;
        AskBeforeBox.SelectedItem = asks.FirstOrDefault(o => o.Value == s.AssistantAskBefore) ?? asks[1];

        List<Choice<int>> limits = [new(0, Loc.T("assistantOptions.animateAll")), .. AnimateLimits.Select(n => new Choice<int>(n, Loc.T("assistantOptions.animateMore", n)))];
        if (limits.All(o => o.Value != s.AssistantAnimateMax)) limits.Add(new(s.AssistantAnimateMax, Loc.T("assistantOptions.animateMore", s.AssistantAnimateMax)));
        AnimateBox.ItemsSource = limits;
        AnimateBox.SelectedItem = limits.First(o => o.Value == s.AssistantAnimateMax);

        _filling = false;
    }

    private void OnManoeuvresChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && ManoeuvresBox.SelectedItem is Choice<string> choice) _app.Config.Settings.AssistantManoeuvres = choice.Value;
    }

    private void OnAskBeforeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && AskBeforeBox.SelectedItem is Choice<string> choice) _app.Config.Settings.AssistantAskBefore = choice.Value;
    }

    private void OnAnimateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling && AnimateBox.SelectedItem is Choice<int> choice) _app.Config.Settings.AssistantAnimateMax = choice.Value;
    }

    private void OnOpenPanel(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("assistant");
}
