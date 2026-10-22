using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Updates, the «Asistentes» block (stage 9.5): each installed assistant, its version and what its address
/// has now, with «Actualizar…» (downloads it and opens its card) and «Omitir esta versión». Only with the module on.
/// </summary>
public partial class SettingsPage
{
    private void UpdateAssistantUpdates()
    {
        var installed = _app.Config.Settings.AssistantEnabled ? AssistantPackages.Installed() : [];
        AssistantUpdatesBlock.Visibility = installed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AssistantUpdatesRows.Children.Clear();
        if (installed.Count == 0) return;

        AssistantUpdatesHint.Text = _app.CheckingAssistantUpdates ? Loc.T("assistantUpdates.checking")
            : _app.AssistantUpdatesChecked is { } when ? Loc.T("assistantUpdates.hint") + " · " + Loc.T("updates.lastCheck", when.ToString("g", Loc.Instance.Culture))
            : Loc.T("assistantUpdates.hint");

        var rows = new StackPanel();
        var first = true;
        foreach (var assistant in installed.OrderBy(a => a.Manifest.Name, StringComparer.Create(Loc.Instance.Culture, ignoreCase: true)))
        {
            if (!first) rows.Children.Add(Separator());
            first = false;
            rows.Children.Add(AssistantUpdateRow(assistant, _app.AssistantUpdateStatuses.FirstOrDefault(s => s.Id == assistant.Id)));
        }
        AssistantUpdatesRows.Children.Add(new Border { Style = (Style)FindResource("ListPanel"), Child = rows, Padding = new Thickness(14, 0, 14, 0) });
    }

    private FrameworkElement AssistantUpdateRow(InstalledAssistant assistant, AssistantUpdateStatus? status)
    {
        var name = assistant.Manifest.Name;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(title);

        var skipped = status is not null && _app.IsSkipped(status);
        var (line, brush) = status switch
        {
            null when string.IsNullOrWhiteSpace(assistant.Manifest.Updates) => (Loc.T("assistantUpdates.noAddress", assistant.Version), "TextFillColorTertiaryBrush"),
            null => (assistant.Version, "TextFillColorSecondaryBrush"),
            { Result: AssistantUpdateResult.UpToDate } => (Loc.T("assistantUpdates.upToDate", assistant.Version), "TextFillColorSecondaryBrush"),
            { Result: AssistantUpdateResult.Available } s => (Loc.T(skipped ? "assistantUpdates.skippedLine" : "assistantUpdates.newLine", s.Installed, s.Latest!),
                skipped ? "TextFillColorSecondaryBrush" : "Kd.AccentText"),
            { Result: AssistantUpdateResult.NeedsNewerKDesktop } s => (Loc.T("assistantUpdates.needsLine", s.Installed, s.Latest!), "Kd.Warning"),
            { } s => (Loc.T("assistantUpdates.failedLine", s.Installed, s.Error ?? "?"), "Kd.Danger"),
        };
        body.Children.Add(Caption(line, brush));
        if (status is { IsNewer: true, Changes: { } changes } && !skipped && changes.In(Loc.Instance.Language) is { Length: > 0 } text)
        {
            var notes = Caption(text, "TextFillColorSecondaryBrush");
            notes.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(notes);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (status is { Result: AssistantUpdateResult.Available })
        {
            var downloading = _app.IsDownloadingAssistantUpdate(assistant.Id);
            var update = new Button
            {
                Content = Loc.T(downloading ? "assistantUpdates.downloading" : "assistantUpdates.update"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowDownload24 },
                Appearance = skipped ? ControlAppearance.Secondary : ControlAppearance.Primary,
                IsEnabled = !downloading,
            };
            AutomationProperties.SetName(update, Loc.T("assistantUpdates.update") + " " + name);
            update.Click += (_, _) => _ = _app.OpenAssistantUpdateAsync(assistant.Id);
            buttons.Children.Add(update);

            var skip = new Button
            {
                Content = Loc.T(skipped ? "assistantUpdates.unskip" : "assistantUpdates.skip"),
                Appearance = ControlAppearance.Transparent,
                Margin = new Thickness(6, 0, 0, 0),
            };
            AutomationProperties.SetName(skip, Loc.T(skipped ? "assistantUpdates.unskip" : "assistantUpdates.skip") + " " + name);
            skip.Click += (_, _) => _app.SkipAssistantUpdate(status, !skipped);
            buttons.Children.Add(skip);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Tile(assistant));
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        var row = new Border { Child = grid, Padding = new Thickness(0, 12, 0, 12) };
        AutomationProperties.SetName(row, name);
        return row;
    }

    /// <summary>Its icon, or the generic one of an assistant.</summary>
    private static FrameworkElement Tile(InstalledAssistant assistant)
    {
        var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        if (AssistantLook.Icon(assistant) is { } icon)
            tile.Child = new System.Windows.Controls.Image { Source = icon, Width = 32, Height = 32 };
        else
        {
            var glyph = new SymbolIcon { Symbol = SymbolRegular.Bot24, FontSize = 18 };
            glyph.SetResourceReference(ForegroundProperty, "Kd.AccentText");
            tile.Child = glyph;
            tile.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
        }
        return tile;
    }

    private TextBlock Caption(string text, string brush)
    {
        var block = new TextBlock { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static Border Separator()
    {
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
        return line;
    }
}
