using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>Choosing which servers of another program's file come in (Options → Tools → Import).</summary>
public partial class McpImportWindow : FluentWindow
{
    private readonly List<(CheckBox Box, McpImportCandidate Candidate)> _boxes = [];

    public McpImportWindow(IReadOnlyList<McpImportCandidate> found, string fileName)
    {
        InitializeComponent();
        IntroText.Text = Loc.T("mcpImport.intro", fileName);
        foreach (var candidate in found)
        {
            var box = new CheckBox
            {
                Content = candidate.Entry.Id,
                // What the assistants already do, or one already there (it would be replaced), unticked.
                IsChecked = candidate.Overlap is null && !candidate.Exists,
                FontWeight = FontWeights.SemiBold,
            };
            AutomationProperties.SetName(box, candidate.Entry.Id);
            box.Checked += (_, _) => Count();
            box.Unchecked += (_, _) => Count();
            var details = new StackPanel { Margin = new Thickness(28, 0, 0, 0) };
            details.Children.Add(Line(candidate.Entry.Summary, null));
            if (candidate.Secrets.Count > 0) details.Children.Add(Line(Loc.T("mcpImport.secrets", string.Join(", ", candidate.Secrets.Keys)), null));
            if (candidate.Overlap is { } overlap) details.Children.Add(Line(Loc.T(overlap), "Kd.Warning"));
            if (candidate.Exists) details.Children.Add(Line(Loc.T("mcpImport.exists"), "Kd.Warning"));
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(box);
            row.Children.Add(details);
            Rows.Children.Add(row);
            _boxes.Add((box, candidate));
        }
        Count();
    }

    /// <summary>The ones ticked, once «Import» was pressed.</summary>
    public IReadOnlyList<McpImportCandidate> Chosen { get; private set; } = [];

    private TextBlock Line(string text, string? brush)
    {
        var line = new TextBlock { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap };
        if (brush is not null) line.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return line;
    }

    private void Count()
    {
        var ticked = _boxes.Count(b => b.Box.IsChecked == true);
        ImportButton.Content = Loc.T("mcpImport.import", ticked);
        ImportButton.IsEnabled = ticked > 0;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        Chosen = [.. _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Candidate)];
        DialogResult = true;
    }
}
