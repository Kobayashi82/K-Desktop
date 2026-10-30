using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Mail;
using KDesktop.Core.Model;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>
/// What is asked by Telegram without the assistant (telegram, phase 3): «/comandos» shows the commands that person may
/// run as buttons; «/nombre» or a button runs it and says how it went; what needs confirming is asked to the owner (on
/// the PC if they are there, else by Telegram, and it expires); the files that arrive are kept in Downloads; and
/// <see cref="Arrived"/> lets the events of «Llega un mensaje de Telegram» fire.
/// </summary>
public sealed class TelegramCommands
{
    /// <summary>Buttons in one «/comandos» (Telegram allows 100).</summary>
    public const int MaxButtons = 60;

    /// <summary>The words that list the commands, besides the one of the language (/comandos, /commands…).</summary>
    private static readonly string[] ListWords = ["comandos", "commands", "commandes", "befehle", "comandi"];

    private readonly TelegramService _telegram;
    private readonly Func<IReadOnlyList<Command>> _commands;
    private readonly Func<Command, Task> _run;
    private readonly Func<Command, ToolRisk> _risk;

    /// <param name="commands">All the commands now.</param>
    /// <param name="run">Runs one (as from Telegram) and finishes when it has.</param>
    /// <param name="risk">How risky a command is (<see cref="ActionRisk.OfCommand"/>).</param>
    public TelegramCommands(TelegramService telegram, Func<IReadOnlyList<Command>> commands, Func<Command, Task> run, Func<Command, ToolRisk> risk)
    {
        _telegram = telegram;
        _commands = commands;
        _run = run;
        _risk = risk;
        telegram.Received += (m, v) => _ = Task.Run(() => HandleSafeAsync(m, v));
    }

    /// <summary>Whether the owner is at the PC now (then the confirmations are asked there first).</summary>
    public Func<bool> OwnerPresent { get; set; } = () => false;

    /// <summary>Asks the owner on the PC: true, false, or null when nobody answered (then it is asked by Telegram).</summary>
    public Func<string, Task<bool?>> AskOnPc { get; set; } = _ => Task.FromResult<bool?>(null);

    /// <summary>The user's «Antes de hacer algo, preguntar» (<see cref="AskPolicy"/>), for what the owner asks.</summary>
    public Func<string?> Policy { get; set; } = () => AskPolicy.Risky;

    /// <summary>Where the files that arrive are kept (Downloads).</summary>
    public Func<string?> DownloadFolder { get; set; } = () => Platform.KnownFolders.Downloads;

    /// <summary>How long a confirmation by Telegram waits.</summary>
    public TimeSpan ConfirmFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A message that is not a command, for the assistant (phase 4): the message, how it was sorted, its text (without the
    /// mention of the bot) and the path of its file if it was kept. Null: the module is not there, nothing is done.
    /// </summary>
    public Func<TelegramIncoming, TelegramVerdict, string, string?, Task>? Talk { get; set; }

    /// <summary>A message to be served arrived (its file, if any, already kept: its path); for the events.</summary>
    public event Action<TelegramIncoming, TelegramVerdict, string?>? Arrived;

    private async Task HandleSafeAsync(TelegramIncoming m, TelegramVerdict v)
    {
        try
        {
            await HandleAsync(m, v).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Telegram: error al atender un mensaje", ex);
        }
    }

    internal async Task HandleAsync(TelegramIncoming m, TelegramVerdict v)
    {
        if (m.IsButton)
        {
            var data = m.CallbackData ?? string.Empty;
            if (data.StartsWith("c:", StringComparison.Ordinal) && Allowed(m, v).FirstOrDefault(c => c.Id == data[2..]) is { } chosen)
            {
                _telegram.Answer(m, "▶ " + chosen.Name);
                await RunAsync(m, v, chosen).ConfigureAwait(false);
            }
            else _telegram.Answer(m, Loc.T("telegram.button.old"));
            return;
        }

        // Its file is kept first (the owner's and the contacts', not someone new's).
        string? path = null;
        if (m.File is not null && v.Route is TelegramRoute.Owner or TelegramRoute.Contact && DownloadFolder() is { } folder)
        {
            path = await _telegram.DownloadAsync(m, folder).ConfigureAwait(false);
            _telegram.Post(m.ChatId, path is null ? Loc.T("telegram.file.notSaved") : Loc.T("telegram.file.saved", Path.GetFileName(path)));
        }
        Arrived?.Invoke(m, v, path);

        var text = m.ChatKind == TelegramChatKind.Group ? TelegramInbox.WithoutMention(m.Text, _telegram.Bot?.Username ?? string.Empty) : m.Text.Trim();
        if (!text.StartsWith('/'))
        {
            // Not a command: for the assistant (phase 4), if that person may talk to it.
            if (text.Length == 0 || Talk is not { } talk) return;
            if (v.Level == TelegramLevel.ChosenCommands)
            {
                _telegram.Post(m.ChatId, Loc.T("telegram.talk.onlyCommands", "/" + Loc.T("telegram.cmd.word")));
                return;
            }
            await talk(m, v, text, path).ConfigureAwait(false);
            return;
        }
        var word = text[1..].Trim();
        var at = word.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0) word = word[..at];
        if (word.Length == 0) return;
        var key = Key(word);
        if (ListWords.Contains(key) || key == Key(Loc.T("telegram.cmd.word")))
        {
            ShowList(m, v);
            return;
        }
        var allowed = Allowed(m, v);
        if (allowed.FirstOrDefault(c => Key(c.Name) == key) is { } command) await RunAsync(m, v, command).ConfigureAwait(false);
        else _telegram.Post(m.ChatId, Loc.T(allowed.Count == 0 ? "telegram.cmd.none" : "telegram.cmd.unknown", "/" + Loc.T("telegram.cmd.word")));
    }

    /// <summary>The name as it is compared: without case, accents, spaces or «_» («/modo_juego» is «Modo juego»).</summary>
    public static string Key(string name) => MailRules.Fold(name).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// The commands this person may run: the owner and «Como tú», every one switched on; «Comandos elegidos» and
    /// «Herramientas elegidas», the ones chosen; anybody else, none.
    /// </summary>
    public IReadOnlyList<Command> Allowed(TelegramIncoming m, TelegramVerdict v)
    {
        var all = _commands().Where(c => c.Enabled && c.Name.Trim().Length > 0);
        if (v.Level == TelegramLevel.LikeOwner) return [.. all];
        if (v.Level is not (TelegramLevel.ChosenCommands or TelegramLevel.ChosenTools)) return [];
        var chosen = _telegram.Read(s => s.Contacts.FirstOrDefault(c => c.Id == m.From.Id)?.Commands.ToHashSet()) ?? [];
        return [.. all.Where(c => chosen.Contains(c.Id))];
    }

    /// <summary>«/comandos»: the commands as buttons, by name, two a row (the hidden ones only by writing them).</summary>
    private void ShowList(TelegramIncoming m, TelegramVerdict v)
    {
        var shown = Allowed(m, v).Where(c => !c.Hidden).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (shown.Count == 0)
        {
            _telegram.Post(m.ChatId, Loc.T("telegram.cmd.none"));
            return;
        }
        var buttons = shown.Take(MaxButtons).Select(c => new TelegramButton(c.Name, "c:" + c.Id)).Chunk(2).Select(row => (IReadOnlyList<TelegramButton>)row).ToList();
        var text = Loc.T("telegram.cmd.choose") + (shown.Count > MaxButtons ? "\n" + Loc.T("telegram.cmd.more", shown.Count - MaxButtons) : string.Empty);
        _telegram.Post(m.ChatId, text, buttons);
    }

    /// <summary>
    /// Runs a command for someone: what needs confirming is asked to the owner first (the owner, as «Antes de hacer algo»
    /// says; anybody else, whatever is not harmless), and the answer says how it went.
    /// </summary>
    private async Task RunAsync(TelegramIncoming m, TelegramVerdict v, Command command)
    {
        var risk = _risk(command);
        var owner = v.Route == TelegramRoute.Owner;
        var asks = owner ? AskPolicy.Asks(risk, Policy()) : risk != ToolRisk.Safe;
        if (asks)
        {
            if (!owner) _telegram.Post(m.ChatId, Loc.T("telegram.confirm.waiting", command.Name));
            var question = owner ? Loc.T("telegram.confirm.own", command.Name) : Loc.T("telegram.confirm.other", m.From.Shown, command.Name);
            var yes = await ConfirmAsync(question).ConfigureAwait(false);
            if (yes != true)
            {
                Log.Info($"Telegram: «{command.Name}» no se ejecuta ({(yes is null ? "sin respuesta" : "no")})");
                _telegram.Post(m.ChatId, Loc.T(yes is null ? "telegram.confirm.expired" : "telegram.confirm.refused", command.Name));
                return;
            }
        }
        Log.Info($"Telegram: «{command.Name}» pedido por {(owner ? "el dueño" : m.From.Id.ToString(Loc.Instance.Culture))}");
        try
        {
            await _run(command).ConfigureAwait(false);
            _telegram.Post(m.ChatId, "✓ " + command.Name);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn($"Telegram: «{command.Name}» falló: {ex.Message}");
            _telegram.Post(m.ChatId, $"✗ {command.Name}: {ex.Message}");
        }
    }

    /// <summary>Asks the owner: on the PC if they are there (and answer), else by Telegram. Null: nobody answered.</summary>
    public async Task<bool?> ConfirmAsync(string question)
    {
        if (OwnerPresent() && await AskOnPc(question).ConfigureAwait(false) is { } here) return here;
        return await _telegram.AskOwnerAsync(question, ConfirmFor).ConfigureAwait(false);
    }
}
