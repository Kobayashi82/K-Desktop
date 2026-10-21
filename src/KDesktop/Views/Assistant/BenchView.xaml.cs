using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KDesktop.Core.Assistant.Bench;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// Options → Brain → Test the model (stage 3.9, plan §5.4): runs the bench (the user's, or the built-in one) with a
/// model of the server and shows how it goes, its score (hits, percentage, mean time), each order that failed with
/// what it did, and the last score of every model tried. The bench is edited as a file («Open the bench»).
/// </summary>
public partial class BenchView : UserControl
{
    private readonly App _app = App.Current;
    private string? _models;

    public BenchView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantBenchChanged += Fill;
            _app.AssistantBrainChanged += Fill;
            _app.AssistantChanged += Fill;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantBenchChanged -= Fill;
            _app.AssistantBrainChanged -= Fill;
            _app.AssistantChanged -= Fill;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            _app.RequestAssistantBench();
            Fill();
        };
    }

    private void Fill()
    {
        var bench = _app.AssistantBench;
        var running = bench is { State: "running" };
        var connected = _app.Assistant is { IsRunning: true };
        var own = bench?.Own ?? File.Exists(BenchFile.UserFile);
        IntroText.Text = Loc.T(own ? "bench.introOwn" : "bench.intro");

        // The models of the server; the one tried last (or the one in use) chosen.
        var models = _app.AssistantBrain?.Models?.Select(m => m.Name).ToList() ?? [];
        var signature = string.Join("|", models);
        if (signature != _models && !ModelBox.IsDropDownOpen)
        {
            _models = signature;
            var keep = ModelBox.SelectedItem as string ?? (bench?.Model is { Length: > 0 } tried ? tried : _app.AssistantBrain?.Model);
            ModelBox.ItemsSource = models;
            ModelBox.SelectedItem = models.FirstOrDefault(m => m == keep) ?? models.FirstOrDefault();
        }
        ModelBox.IsEnabled = !running;
        RunButton.IsEnabled = connected && ModelBox.SelectedItem is not null;
        RunButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        ProgressPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running)
        {
            Progress.Value = bench!.Total == 0 ? 0 : (double)bench.Done / bench.Total;
            ProgressText.Text = Loc.T("bench.progress", bench.Model, bench.Done, bench.Total, bench.Passed);
        }
        ResultText.Visibility = bench is { State: "done" or "failed" or "cancelled" } ? Visibility.Visible : Visibility.Collapsed;
        if (bench is { State: "done" }) ResultText.Text = Score(bench.Model, bench.Passed, bench.Total, bench.MeanMs);
        else if (bench is { State: "failed" }) ResultText.Text = Loc.T("bench.failed", bench.Message ?? "");
        else if (bench is { State: "cancelled" }) ResultText.Text = Loc.T("bench.cancelled");

        Failures.Children.Clear();
        if (bench is { State: "done" or "cancelled" })
            foreach (var failure in bench.Failures) Failures.Children.Add(FailureRow(failure));

        History.Children.Clear();
        var history = bench?.History ?? [];
        HistoryTitle.Visibility = history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var score in history)
        {
            var row = new TextBlock
            {
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap,
                Text = Score(score.Model, score.Passed, score.Total, score.MeanMs) + " · " + score.At.ToLocalTime().ToString("g", Loc.Instance.Culture)
                    + (score.Own ? " · " + Loc.T("bench.own") : string.Empty),
            };
            row.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            History.Children.Add(row);
        }
    }

    /// <summary>«qwen3-4b-instruct: 29/32 (91 %) · 0,5 s».</summary>
    private static string Score(string model, int passed, int total, int meanMs) =>
        Loc.T("bench.score", model, passed, total, total == 0 ? 0 : (int)Math.Round(100.0 * passed / total), (meanMs / 1000.0).ToString("0.0", Loc.Instance.Culture));

    /// <summary>What was asked, what was expected and what it did.</summary>
    private static Border FailureRow(BenchCaseResult failure)
    {
        var did = failure.Route == "failed" ? Loc.T("bench.didFail")
            : failure.Tools.Count > 0 ? string.Join(", ", failure.Tools) : Loc.T("bench.noTool");
        if (failure.Answer.Length > 0) did += " · «" + (failure.Answer.Length > 120 ? failure.Answer[..120] + "…" : failure.Answer) + "»";
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.Inlines.Add(new System.Windows.Documents.Run(failure.Say) { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new System.Windows.Documents.LineBreak());
        text.Inlines.Add(new System.Windows.Documents.Run(Loc.T("bench.expectedGot", failure.Expected, did)));
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var row = new Border { Child = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 3, 0, 3), CornerRadius = new CornerRadius(8) };
        row.SetResourceReference(Border.BackgroundProperty, "Kd.DangerSoft");
        AutomationProperties.SetName(row, failure.Say);
        return row;
    }

    private void OnRun(object sender, RoutedEventArgs e)
    {
        if (ModelBox.SelectedItem is string model) _app.StartAssistantBench(model);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _app.CancelAssistantBench();

    private void OnOpen(object sender, RoutedEventArgs e) => _app.OpenAssistantBench();
}
