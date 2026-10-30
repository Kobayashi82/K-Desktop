using KDesktop.Core.Voice;

namespace KDesktop.Core.Remote.Web;

/// <summary>An assistant the phone can talk to: an active one (on the desktop; K-Desktop too when it is active).</summary>
/// <param name="Busy">Thinking or answering now.</param>
public sealed record WebAssistantInfo(string Id, string Name, bool Busy, WebIcon? Icon);

/// <summary>A line of a conversation (<c>user</c> or <c>assistant</c>); not <paramref name="Final"/> while it is being written.</summary>
/// <param name="Written">A long answer's whole text in Markdown (stage 14.2; null: none): <paramref name="Text"/> is its summary.</param>
/// <param name="Images">Its pictures, by their key (never the path on the PC): <c>api/assistants/image?id=…&amp;k=…</c>.</param>
public sealed record WebChatLine(string Role, string Turn, string Text, bool Final, DateTime At, string? Written = null, IReadOnlyList<string>? Images = null);

/// <summary>
/// A piece of an answer's voice for the phone that asked (16-bit mono PCM at <paramref name="Rate"/>), or, with
/// <paramref name="End"/>, the end of that turn's voice (<paramref name="Stopped"/>: cut).
/// </summary>
/// <summary>Something an assistant did (Panel → Assistant → Activity), in words for people: a chip under its answer.</summary>
public sealed record WebDid(string Text, bool Ok, DateTimeOffset At);

public sealed record WebSpeech(string Device, string Assistant, string Turn, int Index, int Rate, byte[]? Pcm, bool End = false, bool Stopped = false);

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

    /// <summary>The path of a picture of the conversation with <paramref name="id"/> by its key; null: not one of its pictures.</summary>
    string? Image(string id, string key);

    /// <summary>What it did lately (its activity), oldest first.</summary>
    IReadOnlyList<WebDid> Activity(string id);

    /// <summary>Sends it an order as written on the phone: null when it went, else why not (<c>notActive</c>, <c>notRunning</c>).</summary>
    /// <param name="device">The phone that wrote it: its answer's voice goes there with <paramref name="speak"/>
    /// (<c>phone</c> or <c>both</c>; null: on the PC).</param>
    string? Send(string id, string text, string? device = null, string? speak = null);

    /// <summary>Stops what it is answering.</summary>
    void Stop(string id);

    /// <summary>An order said into the phone for this assistant: transcribed by the PC and sent to it (any thread).</summary>
    Task<RemoteVoiceResult> VoiceAsync(string id, float[] samples, string? device = null, string? speak = null);

    /// <summary>The module, who is active or what they are doing changed.</summary>
    event Action? Changed;

    /// <summary>A line of a conversation is new or grew (null: the conversation was cleared).</summary>
    event Action<string, WebChatLine?>? Line;

    /// <summary>It did something (UI thread).</summary>
    event Action<string, WebDid>? Did;

    /// <summary>The voice of an answer for the phone that asked (any thread).</summary>
    event Action<WebSpeech>? Speech;
}
