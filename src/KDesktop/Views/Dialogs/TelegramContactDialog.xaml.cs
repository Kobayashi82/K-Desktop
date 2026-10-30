using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// A Telegram contact (Opciones › Cuentas › Telegram; telegram, phase 2): what they may do, whether K-Desktop may write to
/// them, and —with «Comandos elegidos» or «Herramientas elegidas»— which commands and tools. Saved in <see cref="TelegramService"/>.
/// </summary>
public partial class TelegramContactDialog : FluentWindow
{
    private static TelegramService Telegram => App.Current.Telegram;

    private readonly long _id;
    private readonly ComboBox _level;
    private readonly TextBlock _levelHint;
    private readonly CheckBox _mayWrite;
    private readonly ComboBox _assistant;
    private readonly ComboBox _answer;
    private readonly StackPanel _assistantPanel = new();
    private readonly StackPanel _commandsPanel = new();
    private readonly StackPanel _toolsPanel = new();
    private readonly List<(CheckBox Box, string Id)> _commands = [];
    private readonly List<(CheckBox Box, string Name)> _tools = [];
    private readonly List<string> _unknownTools = [];

    private TelegramContactDialog(TelegramContact c)
    {
        InitializeComponent();
        _id = c.Id;
        TitleBar.Title = c.Shown;
        Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.contact.intro"), new Thickness(0, 0, 0, 12)));

        Form.Children.Add(CalendarUi.Label(Loc.T("telegram.contact.level")));
        _level = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Value", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Enum.GetValues<TelegramLevel>().Select(l => new NamedChoice<TelegramLevel>(l, Loc.T("telegram.level." + l))).ToList(),
            SelectedValue = c.Level,
        };
        Form.Children.Add(_level);
        _levelHint = CalendarUi.Caption(string.Empty, new Thickness(0, 4, 0, 12));
        Form.Children.Add(_levelHint);
        _mayWrite = new CheckBox { Content = Loc.T("telegram.contact.mayWrite"), IsChecked = c.MayWrite, Margin = new Thickness(0, 0, 0, 12) };
        Form.Children.Add(_mayWrite);

        // Who answers them when they talk to the assistant (telegram, phase 4.4).
        _assistantPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.contact.assistant")));
        _assistant = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Id", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = AccountsSection.TelegramAssistantChoices(Loc.T("telegram.contact.assistantDefault")),
            SelectedValue = c.Assistant ?? string.Empty,
        };
        _assistantPanel.Children.Add(_assistant);
        _assistantPanel.Children.Add(CalendarUi.Caption(Loc.T("telegram.contact.assistantHint"), new Thickness(0, 4, 0, 12)));
        // How: text, voice or both (telegram, phase 5.2); empty, as to everybody.
        _assistantPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.contact.answer")));
        _answer = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Value", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = (List<NamedChoice<string>>)[new(string.Empty, Loc.T("telegram.contact.answerDefault")),
                .. AccountsSection.TelegramAnswerChoices().Select(k => new NamedChoice<string>(k.Value.ToString(), k.Name))],
            SelectedValue = c.Answer?.ToString() ?? string.Empty,
        };
        _assistantPanel.Children.Add(_answer);
        _assistantPanel.Children.Add(CalendarUi.Caption(Loc.T("telegram.talk.answerHint"), new Thickness(0, 4, 0, 12)));
        Form.Children.Add(_assistantPanel);

        // The commands, by name (kept by id: renaming one does not lose it).
        _commandsPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.contact.commands")));
        var commandList = new StackPanel();
        foreach (var command in App.Current.Config.Commands.Where(x => x.Name.Length > 0).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox { Content = command.Name, IsChecked = c.Commands.Contains(command.Id), Margin = new Thickness(0, 2, 0, 2) };
            _commands.Add((box, command.Id));
            commandList.Children.Add(box);
        }
        if (_commands.Count == 0) commandList.Children.Add(CalendarUi.Caption(Loc.T("telegram.contact.noCommands")));
        _commandsPanel.Children.Add(new ScrollViewer { Content = commandList, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12) });
        Form.Children.Add(_commandsPanel);

        // The assistant's tools (known while the module is on; the ones chosen before are kept).
        _toolsPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.contact.tools")));
        var toolList = new StackPanel();
        if (App.Current.AssistantTools?.Tools is { Count: > 0 } tools)
        {
            foreach (var tool in tools.OrderBy(AssistantToolsSection.TitleOf, StringComparer.CurrentCultureIgnoreCase))
            {
                var box = new CheckBox { Content = AssistantToolsSection.TitleOf(tool), IsChecked = c.Tools.Contains(tool.Name), Margin = new Thickness(0, 2, 0, 2), ToolTip = tool.Name };
                _tools.Add((box, tool.Name));
                toolList.Children.Add(box);
            }
            _unknownTools.AddRange(c.Tools.Where(t => tools.All(x => x.Name != t)));
        }
        else
        {
            toolList.Children.Add(CalendarUi.Caption(Loc.T("telegram.contact.toolsOff")));
            _unknownTools.AddRange(c.Tools);
        }
        _toolsPanel.Children.Add(new ScrollViewer { Content = toolList, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Form.Children.Add(_toolsPanel);

        _level.SelectionChanged += (_, _) => ShowLevel();
        ShowLevel();
    }

    /// <summary>Opens the window for the contact <paramref name="id"/>; true when saved.</summary>
    public static bool Edit(Window? owner, long id)
    {
        var contact = Telegram.Read(s => s.Contacts.FirstOrDefault(c => c.Id == id));
        return contact is not null && new TelegramContactDialog(contact) { Owner = owner }.ShowDialog() == true;
    }

    private TelegramLevel Level => _level.SelectedValue is TelegramLevel l ? l : TelegramLevel.ChatOnly;

    private void ShowLevel()
    {
        _levelHint.Text = Loc.T("telegram.level." + Level + ".hint");
        _commandsPanel.Visibility = Level is TelegramLevel.ChosenCommands or TelegramLevel.ChosenTools ? Visibility.Visible : Visibility.Collapsed;
        _toolsPanel.Visibility = Level == TelegramLevel.ChosenTools ? Visibility.Visible : Visibility.Collapsed;
        _assistantPanel.Visibility = Level is TelegramLevel.Ignore or TelegramLevel.ChosenCommands ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var level = Level;
        var mayWrite = _mayWrite.IsChecked == true;
        var assistant = _assistant.SelectedValue is string { Length: > 0 } id ? id : null;
        TelegramAnswerKind? answer = Enum.TryParse<TelegramAnswerKind>(_answer.SelectedValue as string, out var kind) ? kind : null;
        List<string> commands = [.. _commands.Where(x => x.Box.IsChecked == true).Select(x => x.Id)];
        List<string> tools = [.. _tools.Where(x => x.Box.IsChecked == true).Select(x => x.Name), .. _unknownTools];
        Telegram.Update(s =>
        {
            if (s.Contacts.FirstOrDefault(c => c.Id == _id) is not { } contact) return;
            contact.Level = level;
            contact.MayWrite = mayWrite;
            contact.Commands = commands;
            contact.Tools = tools;
            contact.Assistant = assistant;
            contact.Answer = answer;
        });
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
