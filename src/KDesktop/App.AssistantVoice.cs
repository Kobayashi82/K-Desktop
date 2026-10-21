using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;
using KDesktop.Views.Assistant;

namespace KDesktop;

/// <summary>
/// The voice of the assistants, from K-Desktop's side (phase 5). K-Desktop keeps the one microphone and the one Parakeet:
/// «K-Desktop, …» runs commands as always, and an assistant's own word («Lander, …») sends what follows to it as
/// <c>heard</c> (stage 5.1). With the module off every word is K-Desktop's.
/// </summary>
public partial class App
{
    /// <summary>
    /// An assistant's own word is only listened to while it is on the desktop (UI thread; called when the desktop, the
    /// words or the module change): hidden, it neither answers nor runs K-Desktop's commands. The word «for the
    /// assistant» always works.
    /// </summary>
    private void UpdateSleepingWakeWords()
    {
        ClaimWakeWordsByName();
        var s = Config.Settings;
        Voice.WakeWords.Asleep = s.AssistantWakeWords
            .Where(p => p.Key != AssistantTargeting.AnyAssistant && (!s.AssistantEnabled || !s.AssistantsOnDesktop.Contains(p.Key)))
            .Select(p => p.Value)
            .ToHashSet();
    }

    /// <summary>
    /// A recorded word with an installed assistant's name (the one its manifest suggests, or its name) that nobody has is
    /// that assistant's (UI thread): saying «Dot» calls Dot, not K-Desktop's commands. Only once per assistant
    /// (<see cref="Core.Model.AppSettings.AssistantWakeDecided"/>), so a word taken away from it by hand stays away. Not a
    /// word with its name that is already someone else's («Lander» as the word for any assistant).
    /// </summary>
    private void ClaimWakeWordsByName()
    {
        var s = Config.Settings;
        if (!s.AssistantEnabled) return;
        foreach (var installed in AssistantPackages.Installed())
        {
            var id = installed.Id;
            if (s.AssistantWakeDecided.Contains(id)) continue;
            if (s.AssistantWakeWords.ContainsKey(id))
            {
                s.AssistantWakeDecided = [.. s.AssistantWakeDecided, id];
                continue;
            }
            var name = installed.Manifest.WakeWord is { Length: > 0 } suggested ? suggested : installed.Manifest.Name;
            if (HasWakeWord(id, name) || FreeWakeWordNamed(name) is not { } word) continue;
            s.AssistantWakeWords = AssistantTargeting.Assign(s.AssistantWakeWords, id, word.Id);
            s.AssistantWakeDecided = [.. s.AssistantWakeDecided, id];
            Log.Info($"Asistente: la palabra «{word.Name}» pasa a ser de «{id}» (se llama como él y no era de nadie)");
        }
    }

    /// <summary>
    /// Who an order called with a wake word is for (any thread): an assistant's own word, that assistant (only listened to
    /// while it is on the desktop: <see cref="UpdateSleepingWakeWords"/>); the word «for the assistant», the one of «Cuando
    /// no le hablas a nadie en concreto», or the module without a body with nobody on the desktop. Null: it is K-Desktop's.
    /// </summary>
    private VoiceAddressee? VoiceAddresseeOf(string wakeWordId)
    {
        var s = Config.Settings;
        if (!s.AssistantEnabled || AssistantTargeting.ForWakeWord(s.AssistantWakeWords, wakeWordId) is not { } owner) return null;
        var who = owner == AssistantTargeting.AnyAssistant ? Dispatcher.Invoke(PickAssistant) : owner;
        if (who is not null && AssistantPackages.Find(who) is null)
        {
            Log.Warn($"Asistente: la palabra de «{who}» llama a un asistente que ya no está instalado");
            return null;
        }
        return AddresseeFor(who);
    }

    /// <summary>An assistant (null: the module without a body) as the one an order is for: it gets the text and shows that it listens.</summary>
    private VoiceAddressee AddresseeFor(string? who) =>
        new(who is null ? "K-Desktop" : AssistantName(who),
            (text, source) => Dispatcher.Invoke(() => SendToAssistant(who, text, source)),
            active => SendListening(who, active));

    /// <summary><c>listening</c> (stage 5.2): the character shows that it is being listened to.</summary>
    private void SendListening(string? who, bool active)
    {
        if (_assistant is not { IsRunning: true } supervisor) return;
        supervisor.Send(new ListeningMessage(active, who));
        if (active) WarmAssistantCommands();
    }

    /// <summary>
    /// While the order is said, a search of the commands gets everything ready (stage 5.7): the first one of a session
    /// took a few hundred milliseconds more.
    /// </summary>
    private void WarmAssistantCommands() => Task.Run(() =>
    {
        try { AssistantCommands.Match(new MatchMessage("warm", "abre"), AssistantCommands.Available(Config), Config.Settings.VoiceMedia, Voice.Semantic); }
        catch (Exception ex) { Log.Warn($"Asistente: no se ha podido preparar la búsqueda de comandos: {ex.Message}"); }
    });

    /// <summary>
    /// The assistants' talk key (UI thread): while it is held, what is said goes to the one of «Cuando no le hablas a nadie
    /// en concreto» (with nobody on the desktop, the module without a body).
    /// </summary>
    private void TalkToAssistant(Func<bool> held)
    {
        if (_assistant is null) return;
        var who = PickAssistant();
        if (who is not null) AssistantTalkedTo(who);
        _ = Voice.ListenAsync(held, AddresseeFor(who));
    }

    /// <summary>
    /// <c>listen</c> (stage 5.3, UI thread): the assistant asked something back to an order said aloud; a few seconds
    /// without the wake word for its answer, which goes to it as usual.
    /// </summary>
    private void OnAssistantListen(ListenMessage listen)
    {
        if (_assistant is null || Config.Settings.Paused || !VoiceModels.Ready) return;
        if (listen.Assistant is { } id && AssistantPackages.Find(id) is null) return;
        var seconds = Math.Clamp(listen.Seconds, 1, ListenMessage.MaxSeconds);
        Log.Info($"Asistente: «{listen.Assistant ?? "(sin cuerpo)"}» espera respuesta: se escucha {seconds} s sin palabra");
        _ = Voice.ListenForAsync(AddresseeFor(listen.Assistant), TimeSpan.FromSeconds(seconds));
    }

    /// <summary>Whether the last order to this assistant (null: without a body) was said aloud: its questions may be answered aloud.</summary>
    private bool TalkedByVoice(string? assistant) =>
        VoiceModels.Ready && _chatSources.GetValueOrDefault(assistant ?? string.Empty) == "voice";

    /// <summary>
    /// The little window asks for permission and the order came by voice (stage 5.3): K-Desktop listens meanwhile, without
    /// the wake word, and «sí», «no» or «sí, y no vuelvas a preguntar» answer it (twice at most, until it closes).
    /// </summary>
    private async void ListenForAnswer(AskWindow window, string who, bool canTrust, TimeSpan time)
    {
        // Not disposed: the window may be answered (and cancel it) after this is over.
        var stop = new CancellationTokenSource();
        _ = window.Answer.ContinueWith(_ => stop.Cancel(), TaskScheduler.Default);
        window.ShowListening();
        var until = DateTime.Now + time;
        for (var attempt = 0; attempt < 2 && !window.Answer.IsCompleted; attempt++)
        {
            var left = until - DateTime.Now - TimeSpan.FromSeconds(1);
            if (left < TimeSpan.FromSeconds(2)) break;
            var understood = false;
            var to = new VoiceAddressee(who, (text, _) =>
            {
                if (SpokenReply.Of(text) is not { } reply)
                {
                    Log.Info($"Asistente: «{text}» no es ni sí ni no");
                    Ui.ShowOsd(Loc.T("assistantAsk.sayYesNo", text), Core.Engine.OsdKind.Warning);
                    return false;
                }
                understood = true;
                var choice = reply switch
                {
                    SpokenReplyKind.Yes => "yes",
                    SpokenReplyKind.Always => canTrust ? "always" : "yes",
                    _ => "no",
                };
                Log.Info($"Asistente: «{text}» → {choice} (por voz)");
                Dispatcher.Invoke(() => window.Choose(choice));
                return true;
            });
            try
            {
                await Voice.ListenForAsync(to, left < TimeSpan.FromSeconds(ListenMessage.MaxSeconds) ? left : TimeSpan.FromSeconds(ListenMessage.MaxSeconds), stop.Token);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            if (understood) break;
        }
    }

    private volatile bool _talkHeld;
    private string? _talking;

    /// <summary><c>talk</c>: a long click on an assistant records the user for it while the button is held (UI thread).</summary>
    private void OnAssistantTalk(TalkMessage talk)
    {
        if (!talk.Active)
        {
            if (_talking == talk.Assistant) _talkHeld = false;
            return;
        }
        if (!IsOnDesktop(talk.Assistant)) return;
        _talking = talk.Assistant;
        _talkHeld = true;
        AssistantTalkedTo(talk.Assistant);
        Log.Info($"Asistente: clic mantenido sobre «{talk.Assistant}»: se le escucha");
        _ = Voice.ListenAsync(() => _talkHeld, AddresseeFor(talk.Assistant));
    }

    /// <summary>The word «for the assistant» (Options → Assistant → General), or null.</summary>
    public string? AnyAssistantWakeWord => Config.Settings.AssistantWakeWords.GetValueOrDefault(AssistantTargeting.AnyAssistant);

    /// <summary>
    /// After installing an assistant (its card): it is called by the word it suggests. One already recorded with that name
    /// and free is given to it; otherwise the user records it now.
    /// </summary>
    public void OfferAssistantWakeWord(string id, System.Windows.Window? owner)
    {
        if (AssistantPackages.Find(id) is not { } installed) return;
        var name = installed.Manifest.WakeWord is { Length: > 0 } suggested ? suggested : installed.Manifest.Name;
        if (HasWakeWord(id, name)) return;
        if (FreeWakeWordNamed(name) is { } existing)
        {
            SetAssistantWakeWord(id, existing.Id);
            Ui.ShowOsd(Loc.T("assistantCard.wakeGiven", existing.Name, installed.Manifest.Name), Core.Engine.OsdKind.Success);
            return;
        }
        if (!VoiceModels.AllInstalled || Voice.Models.IsDownloading)
        {
            Ui.ShowOsd(Loc.T("wake.needModels"), Core.Engine.OsdKind.Info);
            return;
        }
        if (Views.Dialogs.WakeWordDialog.Record(owner, suggestedName: name) is { } word) SetAssistantWakeWord(id, word.Id);
    }

    /// <summary>
    /// Whether that assistant is called by voice already: its own word, or one with its name that is already in use (as
    /// the word for any assistant, «Lander» on the user's PC). Another one would be the same word twice.
    /// </summary>
    public bool HasWakeWord(string id, string name)
    {
        var words = Config.Settings.AssistantWakeWords;
        if (words.ContainsKey(id)) return true;
        var taken = words.Values.ToHashSet(StringComparer.Ordinal);
        return Voice.WakeWords.Words.Any(w => taken.Contains(w.Id) && string.Equals(w.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A recorded word with this name that no assistant has yet (K-Desktop's own words count as free).</summary>
    public WakeWord? FreeWakeWordNamed(string name)
    {
        var taken = Config.Settings.AssistantWakeWords.Values.ToHashSet(StringComparer.Ordinal);
        return Voice.WakeWords.Words.FirstOrDefault(w => !taken.Contains(w.Id) && w.Variants.Count > 0
            && string.Equals(w.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
