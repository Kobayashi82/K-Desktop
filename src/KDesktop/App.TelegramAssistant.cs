using System.Collections.Concurrent;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop;

/// <summary>
/// Talking to the assistant from Telegram (telegram, phase 4): a message that is not a command goes to the assistant of
/// that person (or the one on the screen) as an order answered quietly (<c>reply</c>), with its origin: who wrote it and
/// what they may use, applied in the assistants' process (the tools offered and run) and here (the commands and what
/// asks). «Escribiendo…» while it thinks; the answer, long ones whole and in several messages, with what it did under it.
/// The questions go to the owner: on the PC if they are there, else by Telegram. Telegram's tools for the assistants
/// (telegram_send, telegram_read, telegram_send_file) are answered here too.
/// </summary>
public partial class App
{
    /// <summary>How long an answer by Telegram is waited for (a big model, a question to the owner by Telegram).</summary>
    private static readonly TimeSpan TelegramAnswerTime = TimeSpan.FromMinutes(12);

    /// <summary>The orders from Telegram being answered, by their origin's key.</summary>
    private readonly ConcurrentDictionary<string, TelegramTurn> _telegramTurns = new(StringComparer.Ordinal);

    /// <summary>The last order of each chat: an earlier one replaced by it says nothing.</summary>
    private readonly ConcurrentDictionary<long, string> _telegramLatest = new();

    private TelegramTurn? TelegramTurnOf(string? key) => key is null ? null : _telegramTurns.GetValueOrDefault(key);

    /// <summary>What is written to the bot and to the account goes to the assistant the same way.</summary>
    private void WireTelegramTalk()
    {
        TelegramCommands.Talk = TalkFromTelegramAsync;
        TelegramAccountCommands.Talk = TalkFromTelegramAsync;
    }

    /// <summary>A message for the assistant (any thread): sent, waited for, and its answer back to the chat.</summary>
    private async Task TalkFromTelegramAsync(TelegramIncoming m, TelegramVerdict v, string text, string? file)
    {
        // It answers by where it was written to: the bot or the account (phase 6).
        var telegram = TelegramOf(m.Via);
        var commands = await Dispatcher.InvokeAsync(() => Config.Commands.ToList());
        var turn = TelegramTalk.TurnFor(m, v, telegram.Settings, commands, TelegramCommands.ConfirmFor, file is not null);
        var reply = Guid.NewGuid().ToString("N")[..12];
        var answered = new TaskCompletionSource<RepliedMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var did = new List<(string Did, bool Ok)>();
        string? id = null;
        void Collect(ActivityItem item)
        {
            if (item.Entry.By == turn.Origin.By && FromProcess(item.Assistant) == id) lock (did) did.Add((item.Entry.Did, item.Entry.Ok));
        }

        var why = await Dispatcher.InvokeAsync(() =>
        {
            if (_assistant is null) return "telegram.talk.off";
            id = TelegramTalk.AssistantFor(telegram.Settings, m.From.Id) is { } chosen && IsAssistant(chosen) ? chosen : PickAssistant() ?? AssistantTargeting.KDesktop;
            _telegramTurns[turn.Origin.Key] = turn;
            _telegramLatest[m.ChatId] = turn.Origin.Key;
            _replies[reply] = answered;
            AssistantDid += Collect;
            IReadOnlyList<string>? files = file is null ? null : [file];
            if (!BringingOut(id, text, HeardMessage.SourceTelegram, reply, files: files, origin: turn.Origin)
                && !SendToAssistant(id, text, HeardMessage.SourceTelegram, files, reply: reply, origin: turn.Origin))
            {
                EndReply(reply, null);
                return "telegram.talk.notRunning";
            }
            Log.Info($"Telegram: orden para «{id}» de {(turn.Owner ? "el dueño" : m.From.Id.ToString(Loc.Instance.Culture))} ({turn.Origin.Scope}{(turn.Origin.Untrusted ? ", no fiable" : "")})");
            return null;
        });
        if (why is not null)
        {
            telegram.Post(m.ChatId, Loc.T(why));
            Forget();
            return;
        }

        using var typing = new CancellationTokenSource();
        KeepTyping(telegram, m.ChatId, false, typing.Token);
        RepliedMessage? answer;
        try
        {
            answer = await answered.Task.WaitAsync(TelegramAnswerTime);
        }
        catch (TimeoutException)
        {
            answer = null;
            await Dispatcher.InvokeAsync(() => StopAssistant(id!));
        }
        finally
        {
            await typing.CancelAsync();
            Forget();
        }

        var latest = _telegramLatest.TryGetValue(m.ChatId, out var last) && last == turn.Origin.Key;
        if (answer is not { Ok: true })
        {
            // Replaced by a newer message of that chat: that one answers.
            if (latest) telegram.Post(m.ChatId, Loc.T(answer is null ? "telegram.talk.noAnswer" : "telegram.talk.failed"));
            return;
        }
        var whole = answer.Written is { Length: > 0 } written ? written : answer.Text;
        string lines;
        lock (did) lines = telegram.Settings.ShowDid ? TelegramTalk.DidLines(did) : string.Empty;
        // With its voice (phase 5.2): what it says, as a voice note; the whole text of a long answer goes written too.
        var (asText, asVoice) = TelegramVoice.AnswerWith(telegram.Settings, m.From.Id, m.Content == TelegramContent.Voice);
        var note = asVoice && answer.Text.Trim().Length > 0 ? await VoiceForTelegramAsync(telegram, id!, answer.Text, m.ChatId) : null;
        var withText = note is null || asText || answer.Written is { Length: > 0 };
        if (withText) telegram.Post(m.ChatId, lines.Length > 0 ? whole + "\n\n" + lines : whole);
        if (note is not null) await telegram.SendAsync(m.ChatId, withText ? string.Empty : lines, note, TelegramFileKind.Voice);
        Log.Info($"Telegram: respuesta de «{id}» ({whole.Length} letras{(did.Count > 0 ? $", {did.Count} acciones" : "")})");

        void Forget()
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                AssistantDid -= Collect;
                _replies.Remove(reply);
            });
            _telegramTurns.TryRemove(turn.Origin.Key, out _);
            _telegramLatest.TryRemove(new KeyValuePair<long, string>(m.ChatId, turn.Origin.Key));
        }
    }

    /// <summary>«Escribiendo…» (or «Grabando un audio…») lasts about 5 s: again every 4 until <paramref name="stop"/>.</summary>
    private static void KeepTyping(TelegramService telegram, long chat, bool voice, CancellationToken stop) =>
        _ = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    telegram.Typing(chat, voice);
                    await Task.Delay(TimeSpan.FromSeconds(4), stop).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

    /// <summary>How long the voice of an answer is waited for.</summary>
    private static readonly TimeSpan TelegramVoiceTime = TimeSpan.FromMinutes(2);

    /// <summary>The voice notes being made for Telegram, by their turn.</summary>
    private readonly ConcurrentDictionary<string, TelegramVoice> _telegramVoices = new(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="text"/> as a voice note in the voice of <paramref name="id"/> (phase 5.2): asked to the assistants'
    /// process (<c>voice</c> <c>render</c>), which sends it back as for the phone. Its path, or null (no voice, too slow).
    /// </summary>
    private async Task<string?> VoiceForTelegramAsync(TelegramService telegram, string id, string text, long chat)
    {
        var turn = "telegram-" + Guid.NewGuid().ToString("N")[..12];
        var voice = new TelegramVoice(TelegramCommands.CleanVoiceFolder(TelegramCommands.VoiceFolder()));
        _telegramVoices[turn] = voice;
        using var recording = new CancellationTokenSource();
        try
        {
            if (!await Dispatcher.InvokeAsync(() => _assistant?.Send(new VoiceMessage("render", text, ProcessIdOf(id), Turn: turn)) == true)) return null;
            KeepTyping(telegram, chat, true, recording.Token);
            return await voice.File.WaitAsync(TelegramVoiceTime);
        }
        catch (TimeoutException)
        {
            Log.Warn("Telegram: la voz de la respuesta no llegó a tiempo: va escrita");
            return null;
        }
        finally
        {
            await recording.CancelAsync();
            _telegramVoices.TryRemove(turn, out _);
        }
    }

    /// <summary>A piece of a voice note for Telegram (any thread); false: not one (it is for the phone).</summary>
    private bool OnTelegramVoice(PipeMessage message)
    {
        switch (message)
        {
            case SpeechAudioMessage piece when _telegramVoices.TryGetValue(piece.Turn, out var voice):
                try { voice.Add(piece.Index, piece.Rate, Convert.FromBase64String(piece.Pcm)); }
                catch (FormatException) { /* a broken piece: left out */ }
                return true;
            case SpeechEndMessage end when _telegramVoices.TryRemove(end.Turn, out var voice):
                voice.End(!end.Stopped);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A question of something asked by Telegram (a tool, a command, an event): the owner answers, on the PC if they are
    /// there (the usual window) or else by Telegram with «Sí» / «No», which expire. <c>yes</c>, <c>no</c> or <c>timeout</c>;
    /// never «don't ask again».
    /// </summary>
    private async Task<string> AskForTelegramAsync(string origin, string key, string? assistant, string text)
    {
        if (TelegramTurnOf(origin) is not { } turn)
        {
            Log.Info($"Telegram: pregunta de una orden que ya no está ({text}): no");
            return "no";
        }
        var name = AssistantName(FromProcess(assistant));
        var telegram = TelegramOf(turn.Message.Via);
        if (!turn.Owner) telegram.Post(turn.Message.ChatId, Loc.T("telegram.talk.waiting"));
        if (TelegramCommands.OwnerPresent())
        {
            var here = turn.Owner ? text : Loc.T("telegram.talk.askPc", turn.Message.From.Shown, text);
            var choice = await Dispatcher.InvokeAsync(() => AskUserAsync(key, assistant, here, false, 30)).Task.Unwrap();
            if (choice is "yes" or "always" or "no") return choice == "no" ? "no" : "yes";
        }
        var yes = await telegram.AskOwnerAsync(TelegramTalk.Question(turn, name, text), TelegramCommands.ConfirmFor);
        Log.Info($"Telegram: «{text}» por Telegram → {(yes is null ? "sin respuesta" : yes.Value ? "sí" : "no")}");
        return yes switch
        {
            true => "yes",
            false => "no",
            _ => "timeout",
        };
    }

    /// <summary>What K-Desktop applies to what an order from Telegram runs (null: not one).</summary>
    private RemoteLimits? RemoteOf(string? origin, bool userCommands) =>
        TelegramTurnOf(origin) is { } turn ? new RemoteLimits(c => !userCommands || turn.Allows(c), !turn.Owner || turn.Origin.Untrusted) : null;

    /// <summary>Who an order from Telegram runs things for, in the history («Lander · pedido por Ana vía Telegram»); null: not one.</summary>
    private string? RemoteWho(string? origin, string? assistant) =>
        TelegramTurnOf(origin) is { } turn ? $"{AssistantName(FromProcess(assistant))} · {turn.Origin.By}" : null;

    /// <summary>
    /// Something run by an order from Telegram that may not run anything (only talk, or only read), or whose order is
    /// gone: refused here (the process would not have asked for it). True when it was refused.
    /// </summary>
    private bool RefusedRemote(AssistantSupervisor supervisor, string? origin, string run, bool events = false)
    {
        if (origin is null) return false;
        var turn = TelegramTurnOf(origin);
        if (turn is { MayAct: true } && (!events || turn.Owner || turn.Origin.Scope == HeardOrigin.ScopeAll)) return false;
        Log.Info("Telegram: algo pedido que esa persona no puede hacer: no se ejecuta");
        supervisor.Send(new EndMessage(run, "declined", [Loc.T("telegram.talk.notAllowed")], Reason: "notAllowed"));
        return true;
    }

    /// <summary>telegram_send, telegram_read, telegram_send_file (off the pipe's thread).</summary>
    private void OnTelegramTool(AssistantSupervisor supervisor, TelegramMessage request) =>
        _ = Task.Run(async () =>
        {
            TelegramAnswer answer;
            try
            {
                // Someone who is not the owner only reads their own chat.
                var turn = TelegramTurnOf(request.Origin);
                long? only = turn is { Owner: false } ? turn.Message.ChatId : null;
                answer = request.Origin is not null && turn is null
                    ? new TelegramAnswer(false, "notAllowed")
                    : await TelegramAssistant.HandleAsync(Telegram, TelegramAccount, request.Request, only, turn?.Message.Via).ConfigureAwait(false);
                if (answer.Ok && request.Request.Do == "send") Log.Info($"Asistente: mensaje por Telegram ({request.Assistant ?? "módulo"})");
            }
            catch (Exception ex)
            {
                Log.Error("Asistente: error en Telegram", ex);
                answer = new TelegramAnswer(false, "error", Message: ex.Message);
            }
            supervisor.Send(new TelegramDoneMessage(request.Id, answer));
        });
}
