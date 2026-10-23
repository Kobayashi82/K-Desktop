using KDesktop.Core.Voice;

namespace KDesktop.Core.Remote.Web;

/// <summary>An assistant the phone can talk to: an active one (on the desktop; K-Desktop too when it is active).</summary>
/// <param name="Busy">Thinking or answering now.</param>
public sealed record WebAssistantInfo(string Id, string Name, bool Busy, WebIcon? Icon);

/// <summary>A line of a conversation (<c>user</c> or <c>assistant</c>); not <paramref name="Final"/> while it is being written.</summary>
public sealed record WebChatLine(string Role, string Turn, string Text, bool Final, DateTime At);

/// <summary>
/// The assistants as the web app sees them (phase 12): the same conversations as Panel → Assistant, and orders written or
/// said into the phone (source <c>phone</c>). Everything but <see cref="VoiceAsync"/> runs on the UI thread; the events
/// are raised there.
/// </summary>
public interface IWebAssistants
{
    /// <summary>The module is on.</summary>
    bool Enabled { get; }

    /// <summary>The active ones, in the order they were put on the desktop.</summary>
    IReadOnlyList<WebAssistantInfo> List();

    /// <summary>The conversation with one this session, oldest first.</summary>
    IReadOnlyList<WebChatLine> Chat(string id);

    /// <summary>Sends it an order as written on the phone: null when it went, else why not (<c>notActive</c>, <c>notRunning</c>).</summary>
    string? Send(string id, string text);

    /// <summary>Stops what it is answering.</summary>
    void Stop(string id);

    /// <summary>An order said into the phone for this assistant: transcribed by the PC and sent to it (any thread).</summary>
    Task<RemoteVoiceResult> VoiceAsync(string id, float[] samples);

    /// <summary>The module, who is active or what they are doing changed.</summary>
    event Action? Changed;

    /// <summary>A line of a conversation is new or grew (null: the conversation was cleared).</summary>
    event Action<string, WebChatLine?>? Line;
}
