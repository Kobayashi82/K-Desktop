using KDesktop.Core.Model;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Engine.Events;

/// <summary>«Llega un mensaje de Telegram» (telegram, phase 3.2): what Telegram serves fires the events that wait for it.</summary>
public sealed partial class EventService
{
    /// <summary>What Telegram serves (its file already kept); set by K-Desktop at start.</summary>
    public TelegramCommands? Telegram
    {
        get;
        set
        {
            if (field is not null) field.Arrived -= OnTelegramArrived;
            field = value;
            if (field is not null) field.Arrived += OnTelegramArrived;
        }
    }

    /// <summary>What the account with a number serves (phase 6): the same events.</summary>
    public TelegramCommands? TelegramAccount
    {
        get;
        set
        {
            if (field is not null) field.Arrived -= OnTelegramArrived;
            field = value;
            if (field is not null) field.Arrived += OnTelegramArrived;
        }
    }

    private void OnTelegramArrived(TelegramIncoming m, TelegramVerdict v, string? path)
    {
        List<EventRule> rules;
        lock (_lock)
        {
            if (_disposed) return;
            rules = [.. Rules<TelegramTrigger>().Where(r => r.Trigger.Matches(m, v.Route == TelegramRoute.Owner)).Select(r => r.Rule)];
        }
        foreach (var rule in rules)
        {
            Log.Info($"Eventos: mensaje de Telegram de {m.From.Id} → «{rule.Name}»");
            Fire(rule, false, Variables(m, path));
        }
    }

    /// <summary>{telegram_de}, {telegram_texto}, {telegram_archivo}, {telegram_id} (and {telegram_from}…); {resultado}: the text.</summary>
    public static CommandVariables Variables(TelegramIncoming m, string? path)
    {
        var v = new CommandVariables();
        void Set(string es, string en, string value)
        {
            v.SetResult(value, es);
            v.SetResult(value, en);
        }
        Set("telegram_de", "telegram_from", m.From.Shown);
        Set("telegram_archivo", "telegram_file", path ?? string.Empty);
        v.SetResult(m.From.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "telegram_id");
        Set("telegram_texto", "telegram_text", m.Text); // last: {resultado} is the text
        return v;
    }
}
