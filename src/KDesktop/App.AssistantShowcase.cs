using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop;

/// <summary>
/// The showcase of each assistant on the desktop (stage 13.9): what it can show (the host's entrances, exits, states,
/// reactions and gestures, and its own animations) and «do this one now». The «Try» tab of its card uses it.
/// </summary>
public partial class App
{
    /// <summary>What an assistant can show came (UI thread).</summary>
    public event Action<ShowcaseListMessage>? AssistantShowcaseListed;

    /// <summary>An item of the showcase ended (UI thread).</summary>
    public event Action<ShowcaseDoneMessage>? AssistantShowcaseDone;

    /// <summary>Asks what it can show (answered with <see cref="AssistantShowcaseListed"/>); false when it is not on the desktop.</summary>
    public bool AskAssistantShowcase(string id)
    {
        if (!IsAssistantLoaded(id)) return false;
        _assistant!.Send(new ShowcaseMessage(id, ShowcaseMessage.List));
        return true;
    }

    /// <summary>«Do this one now» (the one before stops); <see cref="AssistantShowcaseDone"/> says how it went.</summary>
    public bool PlayAssistantShowcase(string id, ShowcaseEntry item)
    {
        if (!IsAssistantLoaded(id)) return false;
        Core.Log.Info($"Asistente: escaparate de «{id}»: {item.Id}");
        _assistant!.Send(new ShowcaseMessage(id, ShowcaseMessage.Play, item.Id, item.Own));
        return true;
    }

    public void StopAssistantShowcase(string id)
    {
        if (IsAssistantLoaded(id)) _assistant!.Send(new ShowcaseMessage(id, ShowcaseMessage.Stop));
    }

    /// <summary>
    /// The name of an item: the host's ones by K-Desktop's texts; its own ones by their names in the language of K-Desktop
    /// (else English, else any), or K-Desktop's text for that id (the animations of the assistants without code), or the id.
    /// </summary>
    public static string ShowcaseName(ShowcaseEntry item)
    {
        if (item.Own && item.Names is { Count: > 0 } names)
        {
            var language = Loc.Instance.Language.Split('-')[0];
            foreach (var wanted in new[] { language, "en" })
                if (names.FirstOrDefault(n => n.Key.Split('-')[0].Equals(wanted, StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } name) return name;
            return names.Values.First();
        }
        var key = (item.Own ? "showcase.own." : "showcase.item.") + item.Id;
        var text = Loc.T(key);
        return text == key ? item.Id : text;
    }

    /// <summary>The title of a group (one K-Desktop does not know: «Other»).</summary>
    public static string ShowcaseGroupName(string group)
    {
        var key = "showcase.group." + group;
        var text = Loc.T(key);
        return text == key ? Loc.T("showcase.group.other") : text;
    }
}
