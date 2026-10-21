using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// Options → Brain → What no longer asks (stage 4.3): the commands an assistant runs and the tools it uses without
/// asking first, because the user said «yes, and don't ask again»; each one can go back to asking. What always asks
/// (power, scripts…) never gets here. The rows are made in code (an ItemsControl hides its buttons from UI Automation).
/// </summary>
public partial class TrustView : UserControl
{
    private readonly App _app = App.Current;

    public TrustView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.Config.Settings.PropertyChanged += OnSettingChanged;
            Fill();
        };
        Unloaded += (_, _) => _app.Config.Settings.PropertyChanged -= OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.AssistantTrustedCommands) or nameof(AppSettings.AssistantTrustedTools)) Fill();
    }

    private void Fill()
    {
        var s = _app.Config.Settings;
        Rows.Children.Clear();
        foreach (var id in s.AssistantTrustedCommands)
        {
            var name = _app.Config.FindCommand(id)?.Name ?? Loc.T("trust.gone");
            Rows.Children.Add(Row(SymbolRegular.Flash24, Loc.T("trust.command", name), () =>
                s.AssistantTrustedCommands = [.. s.AssistantTrustedCommands.Where(c => c != id)]));
        }
        foreach (var tool in s.AssistantTrustedTools)
            Rows.Children.Add(Row(SymbolRegular.Wrench24, Loc.T("trust.tool", tool), () =>
                s.AssistantTrustedTools = [.. s.AssistantTrustedTools.Where(t => t != tool)]));
        EmptyText.Visibility = Rows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Grid Row(SymbolRegular symbol, string text, Action remove)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = symbol, FontSize = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        grid.Children.Add(icon);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var button = new Button { Content = Loc.T("trust.askAgain"), Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(button, Loc.T("trust.askAgain") + ": " + text);
        button.Click += (_, _) => remove();
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        return grid;
    }
}
