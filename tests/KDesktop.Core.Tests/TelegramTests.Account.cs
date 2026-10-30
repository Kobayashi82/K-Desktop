using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Tests;

/// <summary>The account with a number (telegram, phase 6): its own settings, no buttons, and which one writes.</summary>
public sealed partial class TelegramTests
{
    private const string ApiHash = "0123456789abcdef0123456789abcdef";

    /// <summary>An account already logged in (the session is the fake connection's), with Ana (42) as its owner.</summary>
    private async Task<(TelegramService Service, FakeConnection Connection)> AccountLinked()
    {
        var connection = new FakeConnection { Capabilities = TelegramCapabilities.Account };
        var service = new TelegramService(Path.Combine(_folder, "Account"), Protect, Unprotect, hash =>
        {
            Assert.Equal(ApiHash, hash);
            return connection;
        }, TelegramVia.Account) { RetryBase = TimeSpan.FromMilliseconds(20) };
        Assert.False(service.On);
        Assert.False(service.Configured);
        service.Update(s =>
        {
            s.Account = new TelegramAccountSettings { ApiId = 1, ApiHashProtected = Protect(ApiHash), Phone = "+34600000000", Id = 777, Name = "KD" };
            s.Owner = new TelegramOwner { Id = 42, Name = "Ana" };
        });
        service.Start();
        await Until(() => service.State == TelegramState.Connected);
        return (service, connection);
    }

    [Fact]
    public async Task TheAccountIsAConnectionOfItsOwn()
    {
        var (service, _) = await AccountLinked();
        using var _ = service;
        Assert.True(service.On);
        Assert.Equal(TelegramVia.Account, service.Kind);
        Assert.Equal(777, service.MeId);
        // Who it is follows Telegram (the fake says «K-Desktop de Ana», @kd_ana_bot).
        Assert.Equal("kd_ana_bot", service.MeUsername);
        Assert.Equal("K-Desktop de Ana", service.Account!.Name);
        Assert.Null(service.Bot);
        Assert.True(File.Exists(Path.Combine(_folder, "Account", "settings.json")));
        service.SetEnabled(false);
        Assert.False(service.On);
        Assert.Equal(TelegramState.Off, service.State);
    }

    [Fact]
    public async Task WithoutButtonsTheyAreAnsweredWithTheNumberOrTheWord()
    {
        var (service, connection) = await AccountLinked();
        using var _ = service;

        var yes = service.AskOwnerAsync("¿Vacío la papelera?", TimeSpan.FromSeconds(5));
        await Until(() => connection.Sent.Any(s => s.Text.Contains("¿Vacío la papelera?", StringComparison.Ordinal)));
        var asked = connection.Sent.Last().Text;
        Assert.Contains("1 · ✓ " + Loc.T("telegram.ask.yes"), asked, StringComparison.Ordinal);
        Assert.Contains("2 · ✗ " + Loc.T("telegram.ask.no"), asked, StringComparison.Ordinal);
        Assert.Empty(connection.Keyboards);
        // Someone else answering does not count.
        connection.Incoming.Add(From(5, "1"));
        connection.Incoming.Add(From(42, "1"));
        Assert.True(await yes);
        await Until(() => connection.Edited.Any(e => e.Text.StartsWith("✓ ", StringComparison.Ordinal)));

        // With the word, without case or accents.
        var no = service.AskOwnerAsync("¿Apago el PC?", TimeSpan.FromSeconds(5));
        await Until(() => connection.Sent.Any(s => s.Text.Contains("¿Apago el PC?", StringComparison.Ordinal)));
        connection.Incoming.Add(From(42, Loc.T("telegram.ask.no").ToUpperInvariant()));
        Assert.False(await no);
    }

    [Fact]
    public async Task ItWritesWithTheAccountOrTheBot()
    {
        var (bot, _) = await Linked();
        using var b = bot;
        var (account, _) = await AccountLinked();
        using var a = account;
        account.Update(s => s.Contacts.Add(new TelegramContact { Id = 2, Name = "Ana García", Phone = "+34 600 111 222" }));
        bot.Update(s => s.Contacts.Add(new TelegramContact { Id = 3, Name = "Luis" }));

        Assert.Same(account, TelegramAssistant.Choose(bot, account, "Ana García"));
        Assert.Same(bot, TelegramAssistant.Choose(bot, account, "Luis"));
        Assert.Same(account, TelegramAssistant.Choose(bot, account, string.Empty));
        Assert.Equal(["Ana García", "Luis"], TelegramAssistant.Reachable(bot, account).Where(n => !n.Contains('(', StringComparison.Ordinal)).Order());

        // By a part of the name, without accents, or by the number.
        Assert.Equal(2, account.Recipient("garcia"));
        Assert.Equal(2, account.Recipient("600111222"));
        account.Update(s => s.Contacts.Add(new TelegramContact { Id = 4, Name = "Ana Pérez" }));
        Assert.Null(account.Recipient("ana"));

        // Switched off, the bot writes.
        account.SetEnabled(false);
        Assert.Same(bot, TelegramAssistant.Choose(bot, account, "Ana García"));
    }
}
