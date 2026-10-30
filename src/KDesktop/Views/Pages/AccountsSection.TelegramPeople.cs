using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options › Cuentas › Telegram Bot and › Telegram, who may write to it and to whom it may write (telegram, phases 2 and
/// 6): who waits for a decision, the contacts with their level (each one in its window; the account adds them from its
/// contacts in Telegram or by their number), everybody else, the limits, the groups and the blocked.
/// </summary>
public partial class AccountsSection
{
    private static readonly int[] PerMinuteChoices = [3, 6, 10, 20, 30];
    private static readonly int[] PerHourChoices = [0, 2, 3, 5, 10, 20];

    private void BuildPeople()
    {
        var card = CalendarUi.Card(Root, Loc.T("telegram.people.title"), Loc.T(IsAccount ? "telegram.people.introAccount" : "telegram.people.intro"));
        if (IsAccount)
        {
            var add = CalendarUi.Button(Loc.T("telegram.add.title"), SymbolRegular.PersonAdd24, AddContact, primary: true);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 0, 0, 8);
            add.IsEnabled = Telegram.State == TelegramState.Connected;
            card.Children.Add(add);
        }
        List<TelegramPerson> pending = Telegram.Read(s => s.Pending.ToList());
        List<TelegramContact> contacts = Telegram.Read(s => s.Contacts.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        if (pending.Count > 0) card.Children.Add(CalendarUi.Label(Loc.T("telegram.people.waiting")));
        foreach (var person in pending)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(CalendarUi.Button(Loc.T("telegram.stranger.allow"), SymbolRegular.PersonAdd24, () => Telegram.Allow(person.Id, TelegramLevel.ChatOnly), primary: true));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.PersonProhibited24, Loc.T("telegram.stranger.ignore"), () => Telegram.Allow(person.Id, TelegramLevel.Ignore)));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Prohibited24, Loc.T("telegram.stranger.block"), () => Telegram.Block(person.Id), danger: true));
            card.Children.Add(PersonRow(person.Shown, person.Text is { Length: > 0 } said ? $"«{said}»" : Loc.T("telegram.people.wrote", person.When.ToLocalTime().ToString("g", Loc.Instance.Culture)), actions));
        }
        if (pending.Count > 0 && contacts.Count > 0) card.Children.Add(CalendarUi.Rule());
        foreach (var contact in contacts)
        {
            var summary = Loc.T("telegram.level." + contact.Level)
                          + (contact.Level is TelegramLevel.ChosenCommands or TelegramLevel.ChosenTools ? " · " + Loc.T("telegram.people.chosen", contact.Commands.Count + contact.Tools.Count) : string.Empty)
                          + (contact.MayWrite ? string.Empty : " · " + Loc.T("telegram.people.noWrite"))
                          + (contact.Phone is { Length: > 0 } phone ? " · " + phone : string.Empty);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Edit24, Loc.T("telegram.people.edit"), () => EditContact(contact.Id)));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Prohibited24, Loc.T("telegram.stranger.block"), () => Telegram.Block(contact.Id)));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("telegram.people.forget"), () => _ = ForgetAsync(contact.Id, contact.Shown), danger: true));
            var row = PersonRow(contact.Shown, summary, actions);
            if (contact.Level == TelegramLevel.Ignore) row.Opacity = 0.55;
            card.Children.Add(row);
        }
        if (pending.Count == 0 && contacts.Count == 0) card.Children.Add(CalendarUi.Caption(Loc.T("telegram.people.none")));
        BuildOthers();
        BuildGroups();
        BuildBlocked();
    }

    /// <summary>A contact for the account (phase 6.4): from its contacts in Telegram or by their number; then what they may do.</summary>
    private void AddContact()
    {
        if (Dialogs.TelegramAddContactDialog.Add(Window.GetWindow(this)) is { } added) EditContact(added.Id);
        Build();
    }

    private static Grid PersonRow(string name, string detail, FrameworkElement actions)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        texts.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(CalendarUi.Status(detail, "TextFillColorSecondaryBrush"));
        grid.Children.Add(texts);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private void EditContact(long id)
    {
        if (Dialogs.TelegramContactDialog.Edit(Window.GetWindow(this), Telegram, id)) Build();
    }

    private async Task ForgetAsync(long id, string shown)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("telegram.people.forget"),
            Content = Loc.T("telegram.people.forgetText", shown),
            PrimaryButtonText = Loc.T("common.remove"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        var telegram = Telegram;
        if (await box.ShowCenteredAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) telegram.Forget(id);
    }

    /// <summary>«Todos los demás»: their level (above «Solo charla» only with «Entiendo el riesgo»), the notice, and the limits.</summary>
    private void BuildOthers()
    {
        var card = CalendarUi.Card(Root, Loc.T("telegram.others.title"), Loc.T("telegram.others.intro"));
        var others = Telegram.Settings.Others;
        var level = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Value",
            ItemsSource = Enum.GetValues<TelegramLevel>().Select(l => new NamedChoice<TelegramLevel>(l, Loc.T("telegram.level." + l))).ToList(),
            SelectedValue = others.Level,
        };
        level.SelectionChanged += (_, _) =>
        {
            if (level.SelectedValue is not TelegramLevel value || value == others.Level) return;
            Telegram.Update(s =>
            {
                s.Others.Level = value;
                if (value <= TelegramLevel.ChatOnly) s.Others.RiskAccepted = false;
            });
        };
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.others.level"), Loc.T("telegram.level." + others.Level + ".hint"), level, 240));
        if (others.Level > TelegramLevel.ChatOnly)
        {
            var risk = new CheckBox { Content = Loc.T("telegram.others.risk"), IsChecked = others.RiskAccepted, Margin = new Thickness(0, 4, 0, 0) };
            risk.Click += (_, _) => Telegram.Update(s => s.Others.RiskAccepted = risk.IsChecked == true);
            card.Children.Add(CalendarUi.Status(Loc.T(others.RiskAccepted ? "telegram.others.riskOn" : "telegram.others.riskOff"), "Kd.Warning"));
            card.Children.Add(risk);
        }
        var notify = new ToggleSwitch { IsChecked = others.Notify };
        notify.Click += (_, _) => Telegram.Update(s => s.Others.Notify = notify.IsChecked == true);
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.others.notify"), Loc.T("telegram.others.notifyHint"), notify, 0));

        card.Children.Add(CalendarUi.Rule());
        var limits = Telegram.Settings.Limits;
        var perMinute = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Value",
            ItemsSource = PerMinuteChoices.Select(n => new NamedChoice<int>(n, Loc.T("telegram.limits.perMinuteN", n))).ToList(),
            SelectedValue = PerMinuteChoices.Contains(limits.PerMinute) ? limits.PerMinute : 6,
        };
        perMinute.SelectionChanged += (_, _) =>
        {
            if (perMinute.SelectedValue is int n) Telegram.Update(s => s.Limits.PerMinute = n);
        };
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.limits.perMinute"), Loc.T("telegram.limits.perMinuteHint"), perMinute, 200));
        var gaming = new ToggleSwitch { IsChecked = limits.OnlyOwnerWhileGaming };
        gaming.Click += (_, _) => Telegram.Update(s => s.Limits.OnlyOwnerWhileGaming = gaming.IsChecked == true);
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.limits.gaming"), Loc.T("telegram.limits.gamingHint"), gaming, 0));
        var perHour = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Value",
            ItemsSource = PerHourChoices.Select(n => new NamedChoice<int>(n, n == 0 ? Loc.T("telegram.limits.none") : Loc.T("telegram.limits.perHourN", n))).ToList(),
            SelectedValue = PerHourChoices.Contains(limits.NewPerHour) ? limits.NewPerHour : 5,
        };
        perHour.SelectionChanged += (_, _) =>
        {
            if (perHour.SelectedValue is int n) Telegram.Update(s => s.Limits.NewPerHour = n);
        };
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.limits.perHour"), Loc.T("telegram.limits.perHourHint"), perHour, 200));
    }

    private void BuildGroups()
    {
        var card = CalendarUi.Card(Root, Loc.T("telegram.groups.title"), Loc.T("telegram.groups.intro", Telegram.MeUsername.Length > 0 ? "@" + Telegram.MeUsername : Telegram.Account?.Name ?? string.Empty));
        var groups = Telegram.Read(s => s.Groups.ToList());
        if (groups.Count == 0) card.Children.Add(CalendarUi.Caption(Loc.T("telegram.groups.none")));
        foreach (var group in groups)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var answer = new ToggleSwitch { IsChecked = !group.Ignore, ToolTip = Loc.T("telegram.groups.answer"), Margin = new Thickness(0, 0, 6, 0) };
            System.Windows.Automation.AutomationProperties.SetName(answer, Loc.T("telegram.groups.answer"));
            answer.Click += (_, _) => Telegram.Update(s =>
            {
                if (s.Groups.FirstOrDefault(g => g.Id == group.Id) is { } g) g.Ignore = answer.IsChecked != true;
            });
            actions.Children.Add(answer);
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("telegram.groups.forget"), () => Telegram.Update(s => s.Groups.RemoveAll(g => g.Id == group.Id)), danger: true));
            var row = PersonRow(group.Title.Length > 0 ? group.Title : group.Id.ToString(Loc.Instance.Culture), Loc.T(group.Ignore ? "telegram.groups.ignored" : "telegram.groups.mentions"), actions);
            if (group.Ignore) row.Opacity = 0.55;
            card.Children.Add(row);
        }
    }

    private void BuildBlocked()
    {
        var blocked = Telegram.Read(s => s.Blocked.ToList());
        if (blocked.Count == 0) return;
        var card = CalendarUi.Card(Root, Loc.T("telegram.blocked.title"), Loc.T("telegram.blocked.intro"));
        foreach (var person in blocked)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.PersonAvailable24, Loc.T("telegram.blocked.unblock"), () => Telegram.Unblock(person.Id)));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("telegram.people.forget"), () => _ = ForgetAsync(person.Id, person.Shown), danger: true));
            card.Children.Add(PersonRow(person.Shown, Loc.T("telegram.blocked.since", person.When.ToLocalTime().ToString("d", Loc.Instance.Culture)), actions));
        }
    }
}
