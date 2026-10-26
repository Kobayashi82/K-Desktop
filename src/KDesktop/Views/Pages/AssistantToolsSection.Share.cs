using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Sharing;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Tools → Share (phase 12): the assistants' tools as an MCP server for other programs (<see cref="McpShare"/>):
/// on or off, what they may use, the configuration to paste into the program (with the key) and who uses them now.
/// </summary>
public partial class AssistantToolsSection
{
    private string ShareSignature()
    {
        var s = _app.Config.Settings;
        var clients = State?.Shared ?? [];
        return $"{s.AssistantShareTools}|{s.AssistantShareScope}|{s.AssistantShareKey?.Length}|{s.AssistantShareNeedsKey}|{State?.SharedTools}|"
            + string.Join(",", clients.Select(c => $"{c.Name}{c.Since}{c.Calls}"));
    }

    private void FillShare()
    {
        var s = _app.Config.Settings;
        var card = Card(Loc.T("toolsOptions.share"), Loc.T("toolsOptions.shareIntro"), SymbolRegular.Share24);

        var on = Toggle(s.AssistantShareTools, Loc.T("toolsOptions.shareOn"), value =>
        {
            if (value && s.AssistantShareNeedsKey && _app.AssistantShareKey is null) _app.NewAssistantShareKey();
            s.AssistantShareTools = value;
            _shown = null;
            Refresh();
        });
        card.Add(SettingLine(Loc.T("toolsOptions.shareOn"), Loc.T("toolsOptions.shareOnHint"), on));

        // The key is optional: the pipe is this user's alone (never from another PC or user).
        var needsKey = Toggle(s.AssistantShareNeedsKey, Loc.T("toolsOptions.shareNeedsKey"), value =>
        {
            if (value && _app.AssistantShareKey is null) _app.NewAssistantShareKey();
            s.AssistantShareNeedsKey = value;
            _shown = null;
            Refresh();
        });
        needsKey.IsEnabled = s.AssistantShareTools;
        card.Add(SettingLine(Loc.T("toolsOptions.shareNeedsKey"), Loc.T(s.AssistantShareNeedsKey ? "toolsOptions.shareNeedsKeyHint" : "toolsOptions.shareNoKeyHint"), needsKey));

        var scope = new ComboBox { MinWidth = 260 };
        foreach (var (value, text) in new[] { (McpShare.ScopeRead, Loc.T("toolsOptions.shareRead")), (McpShare.ScopeAll, Loc.T("toolsOptions.shareAll")), (McpShare.ScopeFree, Loc.T("toolsOptions.shareFree")) })
        {
            var item = new ComboBoxItem { Content = text, Tag = value };
            scope.Items.Add(item);
            if (value == s.AssistantShareScope) scope.SelectedItem = item;
        }
        if (scope.SelectedItem is null) scope.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetName(scope, Loc.T("toolsOptions.shareScope"));
        scope.SelectionChanged += (_, _) =>
        {
            if (scope.SelectedItem is not ComboBoxItem { Tag: string value } || value == s.AssistantShareScope) return;
            s.AssistantShareScope = value;
            _shown = null;
            Dispatcher.BeginInvoke(Refresh);
        };
        scope.IsEnabled = s.AssistantShareTools;
        card.Add(SettingLine(Loc.T("toolsOptions.shareScope"), Loc.T(s.AssistantShareScope switch
        {
            McpShare.ScopeAll => "toolsOptions.shareAllHint",
            McpShare.ScopeFree => "toolsOptions.shareFreeHint",
            _ => "toolsOptions.shareReadHint",
        }), scope));

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Wide(SymbolRegular.Copy24, Loc.T("toolsOptions.shareCopy"), _ => CopyShareConfig(), primary: true, enabled: s.AssistantShareTools));
        if (s.AssistantShareNeedsKey) buttons.Children.Add(Wide(SymbolRegular.KeyReset24, Loc.T("toolsOptions.shareNewKey"), _ => NewShareKey(), enabled: s.AssistantShareTools));
        card.Add(buttons);
        card.Add(Caption(Loc.T("toolsOptions.shareWhere")));
        if (s.AssistantShareTools && Running && State is { } state)
        {
            // Where they come from: the built-in ones and the user's, switched on (only those that read, with «Solo lo que
            // consulta»); never those of the user's MCP servers (the program can use those by itself).
            bool Counts(ToolInfo t) => t.Enabled && (s.AssistantShareScope != McpShare.ScopeRead || t.ReadsOnly);
            var count = Caption(Loc.T("toolsOptions.shareCount", state.SharedTools, ToolsOf("builtin").Count(Counts), ToolsOf("json").Count(Counts)));
            count.Margin = new Thickness(0, 10, 0, 0);
            card.Add(count);
        }
        Body.Children.Add(card.Border);

        var users = Card(Loc.T("toolsOptions.shareUsers"), null, SymbolRegular.PeopleCommunity24);
        var clients = State?.Shared ?? [];
        if (clients.Count == 0) users.Add(Caption(Loc.T(s.AssistantShareTools ? "toolsOptions.shareNobody" : "toolsOptions.shareOff")));
        else
            users.Add(Rows(clients.Select(c =>
            {
                var body = new StackPanel();
                var name = Title(c.Name == "?" ? Loc.T("toolsOptions.shareUnknown") : c.Name);
                name.FontWeight = FontWeights.SemiBold;
                body.Children.Add(name);
                body.Children.Add(Caption(Loc.T("toolsOptions.shareSince", c.Since.ToLocalTime().ToString("t", Loc.Instance.Culture), c.Calls)));
                return (UIElement)Row(SymbolRegular.PlugConnected24, body, Buttons(), c.Name);
            })));
        Body.Children.Add(users.Border);
    }

    /// <summary>A setting: its name and what it means on the left, its control on the right.</summary>
    private FrameworkElement SettingLine(string title, string hint, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        text.Children.Add(Title(title));
        text.Children.Add(Caption(hint));
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private void CopyShareConfig()
    {
        try
        {
            Clipboard.SetText(_app.AssistantShareConfig());
            _app.Ui.ShowOsd(Loc.T("toolsOptions.shareCopied"), Core.Engine.OsdKind.Success);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
        {
            _app.Ui.ShowOsd(ex.Message, Core.Engine.OsdKind.Warning);
        }
    }

    private async void NewShareKey()
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("toolsOptions.shareNewKey"),
            Width = 480,
            MaxWidth = 480,
            Content = Loc.T("toolsOptions.shareNewKeyQuestion"),
            PrimaryButtonText = Loc.T("toolsOptions.shareNewKey"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        _app.NewAssistantShareKey();
        CopyShareConfig();
        _shown = null;
        Refresh();
    }
}
