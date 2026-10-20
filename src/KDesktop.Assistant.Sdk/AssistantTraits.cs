namespace KDesktop.Assistant.Sdk;

public enum TravelMode { Fly, Walk, Teleport }

/// <summary>
/// What an assistant can do, as declared in the «traits» of its assistant.json. The module reads it from the
/// manifest (K-Desktop never loads the assistant's code), so it is data, not something the assistant computes.
/// </summary>
/// <param name="TravelSpeed">Rough speed in px/s at 100 % scaling, to estimate how long a trip takes.</param>
/// <param name="CanMoveWindows">If false, the module moves the window itself while the assistant does its gesture.</param>
/// <param name="CanReachMaximized">Whether it can reach the buttons of a maximized window.</param>
/// <param name="Gestures">The gestures it knows. Communicate and React are always assumed.</param>
public sealed record AssistantTraits(
    TravelMode Travel,
    double TravelSpeed,
    bool CanMoveWindows,
    bool CanReachMaximized,
    IReadOnlySet<GestureKind> Gestures)
{
    public bool Knows(GestureKind kind) => kind is GestureKind.Communicate or GestureKind.React || Gestures.Contains(kind);
}
