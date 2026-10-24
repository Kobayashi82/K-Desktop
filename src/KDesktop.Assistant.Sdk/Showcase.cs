namespace KDesktop.Assistant.Sdk;

/// <summary>
/// One of its own animations, for the showcase: a dance, a trick, the way it plays with a ball… What the host
/// already asks of every assistant (entrances, exits, states, reactions, gestures) is not listed: the host launches those
/// itself. K-Desktop shows these in the assistant's «Try» tab and in the catalog, and asks for one with
/// <see cref="IAssistant.PlayShowcaseAsync"/>.
/// </summary>
/// <param name="Id">Its own id, unique among its items («lander.loop», «dance»).</param>
/// <param name="Name">
/// Its name by language code (<c>es</c>, <c>en</c>…), as in its manifest: K-Desktop shows the one of its language, else the
/// English one, else any.
/// </param>
/// <param name="Group">Where K-Desktop puts it.</param>
public sealed record ShowcaseItem(string Id, IReadOnlyDictionary<string, string> Name, ShowcaseGroup Group = ShowcaseGroup.Other);

/// <summary>Groups of the showcase for an assistant's own animations (<see cref="ShowcaseItem"/>).</summary>
/// <remarks>New ones may come in later versions; K-Desktop shows the ones it does not know as <see cref="Other"/>.</remarks>
public enum ShowcaseGroup
{
    Other,

    /// <summary>What it does on its own while nothing happens (stretching, looking around, humming…).</summary>
    Idle,

    /// <summary>Ways of moving (walking, flying, jumping, climbing…).</summary>
    Moving,

    /// <summary>Playing: games, toys, a ball…</summary>
    Playing,

    /// <summary>Moods and feelings (shy, bored, in love…).</summary>
    Feelings,

    /// <summary>Tricks and things to show off (a loop, a dance…).</summary>
    Tricks,
}
