using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop;

/// <summary>
/// The voice of the assistants, from K-Desktop's side (phase 5). K-Desktop keeps the one microphone and the one Parakeet:
/// «K-Desktop, …» runs commands as always, and an assistant's own word («Lander, …») sends what follows to it as
/// <c>heard</c> (stage 5.1). With the module off every word is K-Desktop's.
/// </summary>
public partial class App
{
    /// <summary>
    /// Who an order called with a wake word is for (any thread): an assistant's own word, that assistant (also when it is
    /// not on the desktop: it answers without its body); the word «for the assistant», the one of «Cuando no le hablas a
    /// nadie en concreto», or the module without a body with nobody on the desktop. Null: it is K-Desktop's.
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
        return new VoiceAddressee(who is null ? "K-Desktop" : AssistantName(who),
            (text, source) => Dispatcher.Invoke(() => SendToAssistant(who, text, source)));
    }

    /// <summary>The word «for the assistant» (Options → Assistant → General), or null.</summary>
    public string? AnyAssistantWakeWord => Config.Settings.AssistantWakeWords.GetValueOrDefault(AssistantTargeting.AnyAssistant);

    /// <summary>
    /// After installing an assistant (its card): it is called by the word it suggests. One already recorded with that name
    /// and free is given to it; otherwise the user records it now.
    /// </summary>
    public void OfferAssistantWakeWord(string id, System.Windows.Window? owner)
    {
        if (AssistantPackages.Find(id) is not { } installed || Config.Settings.AssistantWakeWords.ContainsKey(id)) return;
        var name = installed.Manifest.WakeWord is { Length: > 0 } suggested ? suggested : installed.Manifest.Name;
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

    /// <summary>A recorded word with this name that no assistant has yet (K-Desktop's own words count as free).</summary>
    public WakeWord? FreeWakeWordNamed(string name)
    {
        var taken = Config.Settings.AssistantWakeWords.Values.ToHashSet(StringComparer.Ordinal);
        return Voice.WakeWords.Words.FirstOrDefault(w => !taken.Contains(w.Id) && w.Variants.Count > 0
            && string.Equals(w.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
