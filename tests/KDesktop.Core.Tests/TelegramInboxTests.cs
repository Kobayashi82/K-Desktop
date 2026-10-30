using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Tests;

/// <summary>Telegram, phase 2: every incoming message is dropped, told to the owner or served with its level.</summary>
public sealed class TelegramInboxTests
{
    private const long Bot = 777;
    private const string BotName = "kd_ana_bot";
    private const long Owner = 1;

    private static TelegramSettings Settings() => new()
    {
        Owner = new TelegramOwner { Id = Owner, Name = "Yo" },
        Contacts =
        [
            new TelegramContact { Id = 2, Name = "Ana", Level = TelegramLevel.ReadOnlyTools },
            new TelegramContact { Id = 3, Name = "Pepe", Level = TelegramLevel.Ignore },
        ],
        Blocked = [new TelegramPerson { Id = 4, Name = "Spam" }],
        Groups = [new TelegramGroup { Id = -50, Title = "Ruidoso", Ignore = true }],
    };

    private static TelegramIncoming M(long from, string text = "hola", TelegramChatKind kind = TelegramChatKind.Private, long chat = 0, long? replyTo = null) =>
        new(1, chat == 0 ? from : chat, kind, kind == TelegramChatKind.Private ? null : "Casa", 1, new TelegramUser(from, "X", null), DateTimeOffset.UtcNow, text, ReplyToUserId: replyTo);

    private static TelegramVerdict C(TelegramIncoming m, TelegramSettings? s = null) => TelegramInbox.Classify(m, s ?? Settings(), Bot, BotName);

    [Fact]
    public void InPrivate()
    {
        Assert.Equal((TelegramRoute.Owner, TelegramLevel.LikeOwner), (C(M(Owner)).Route, C(M(Owner)).Level));
        Assert.Equal((TelegramRoute.Contact, TelegramLevel.ReadOnlyTools), (C(M(2)).Route, C(M(2)).Level));
        Assert.Equal("ignored", C(M(3)).Why);
        Assert.Equal("blocked", C(M(4)).Why);
        var stranger = C(M(9));
        Assert.Equal(TelegramRoute.Stranger, stranger.Route);
        Assert.Equal(TelegramLevel.Ignore, stranger.Level);
        Assert.False(stranger.Served);
        Assert.True(C(M(2)).Served);
    }

    [Fact]
    public void EverybodyElseNeverAboveChatWithoutTheRisk()
    {
        var s = Settings();
        s.Others.Level = TelegramLevel.LikeOwner;
        Assert.Equal(TelegramLevel.ChatOnly, C(M(9), s).Level);
        s.Others.RiskAccepted = true;
        Assert.Equal(TelegramLevel.LikeOwner, C(M(9), s).Level);
        s.Others.Level = TelegramLevel.ChatOnly;
        s.Others.RiskAccepted = false;
        Assert.True(C(M(9), s).Served);
    }

    [Fact]
    public void InGroups()
    {
        // Only what mentions the bot or replies to it.
        Assert.Equal("notAddressed", C(M(Owner, "hola a todos", TelegramChatKind.Group, -10)).Why);
        Assert.Equal(TelegramRoute.Owner, C(M(Owner, "@KD_Ana_Bot pon música", TelegramChatKind.Group, -10)).Route);
        Assert.Equal(TelegramRoute.Owner, C(M(Owner, "/comandos@kd_ana_bot", TelegramChatKind.Group, -10)).Route);
        Assert.Equal(TelegramRoute.Contact, C(M(2, "vale", TelegramChatKind.Group, -10, replyTo: Bot)).Route);
        Assert.Equal("notAddressed", C(M(2, "vale", TelegramChatKind.Group, -10, replyTo: 5)).Why);

        // It is the level of whoever writes, not the group's.
        Assert.Equal(TelegramLevel.ReadOnlyTools, C(M(2, "@kd_ana_bot hola", TelegramChatKind.Group, -10)).Level);
        Assert.Equal("ignored", C(M(3, "@kd_ana_bot hola", TelegramChatKind.Group, -10)).Why);
        Assert.Equal("blocked", C(M(4, "@kd_ana_bot hola", TelegramChatKind.Group, -10)).Why);
        Assert.Equal(TelegramRoute.Stranger, C(M(9, "@kd_ana_bot hola", TelegramChatKind.Group, -10)).Route);

        // An ignored group: not even the owner.
        Assert.Equal("groupIgnored", C(M(Owner, "@kd_ana_bot hola", TelegramChatKind.Group, -50)).Why);
        Assert.Equal("channel", C(M(Owner, "@kd_ana_bot", TelegramChatKind.Channel, -60)).Why);
        Assert.Equal("pon música", TelegramInbox.WithoutMention("@KD_ana_bot pon música", BotName));
    }

    [Fact]
    public void Limits()
    {
        var limiter = new TelegramLimiter();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++) Assert.True(limiter.AllowIn(2, now, 3));
        Assert.False(limiter.AllowIn(2, now.AddSeconds(30), 3));
        Assert.True(limiter.AllowIn(5, now.AddSeconds(30), 3)); // each contact on its own
        Assert.True(limiter.AllowIn(2, now.AddSeconds(61), 3));

        Assert.True(limiter.AllowNew(now, 2));
        Assert.True(limiter.AllowNew(now, 2));
        Assert.False(limiter.AllowNew(now.AddMinutes(30), 2));
        Assert.True(limiter.AllowNew(now.AddMinutes(61), 2));

        Assert.True(limiter.ShouldTell(2, now, TimeSpan.FromMinutes(10)));
        Assert.False(limiter.ShouldTell(2, now.AddMinutes(5), TimeSpan.FromMinutes(10)));
        Assert.True(limiter.ShouldTell(2, now.AddMinutes(11), TimeSpan.FromMinutes(10)));
    }
}
