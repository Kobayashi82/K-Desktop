using KDesktop.Core.Achievements;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop;

/// <summary>
/// The achievements of the calendar, the mail and Telegram: counted where they happen (the services), whatever asked for
/// them (an action, the Agenda, an assistant, a message); and «connected» ones when an account, a subscription or an owner is
/// there (also those set before the achievements existed, at start).
/// </summary>
public partial class App
{
    private void WireAccountAchievements()
    {
        Calendar.Added += _ => Achievements.Increment(Stat.CalendarAdded);
        Calendar.Changed += CheckAccountAchievements;
        Mail.Sent += () => Achievements.Increment(Stat.MailSent);
        Mail.Changed += CheckAccountAchievements;
        foreach (var telegram in (TelegramService[])[Telegram, TelegramAccount])
        {
            telegram.Sent += () => Achievements.Increment(Stat.TelegramSent);
            telegram.Received += (m, _) =>
            {
                if (!m.IsButton) Achievements.Increment(Stat.TelegramServed);
            };
            telegram.Changed += CheckAccountAchievements;
        }
        CheckAccountAchievements();
    }

    private void CheckAccountAchievements()
    {
        if (Calendar.Settings.Accounts.Count > 0 || Calendar.Settings.Subscriptions.Count > 0) Achievements.Unlock("cal-connect");
        if (Mail.Accounts.Count > 0) Achievements.Unlock("mail-connect");
        if (Telegram.Owner is not null) Achievements.Unlock("tg-bot");
        if (TelegramAccount.Owner is not null) Achievements.Unlock("tg-account");
    }
}
