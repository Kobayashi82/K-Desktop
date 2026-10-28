using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

public enum OsdKind { Info, Success, Warning }

/// <summary>What an OSD message is about: Options › OSD lets each group be hidden (warnings are by their kind).</summary>
public enum OsdTopic { Actions, Levels, Voice }

/// <summary>
/// How an OSD message looks besides its text: <paramref name="Icon"/> (a Fluent symbol name, «Speaker224»; null: the
/// one of its kind) and <paramref name="Level"/> (0–1: a bar, for volume and brightness; it makes the topic Levels).
/// </summary>
public sealed record OsdLook(string? Icon = null, double? Level = null, OsdTopic Topic = OsdTopic.Actions)
{
    public OsdTopic EffectiveTopic => Level is null ? Topic : OsdTopic.Levels;

    public static OsdLook Of(string icon) => new(icon);

    /// <summary>A percentage (0–100) as a bar.</summary>
    public static OsdLook Bar(string icon, double percent) => new(icon, Math.Clamp(percent / 100, 0, 1));
}

/// <summary>
/// A notification card («Mostrar notificación»): null <paramref name="Duration"/> and <paramref name="Corner"/> are the
/// ones of Options; <paramref name="Clicked"/> runs when it is clicked.
/// </summary>
public sealed record Notification(
    string Title, string Text, string? Icon, NotifyColor Color, string? Sound, TimeSpan? Duration, NotifyCorner? Corner, Action? Clicked);

/// <summary>An OSD message with a bar that the work in progress keeps up to date (any thread).</summary>
public interface IProgressOsd : IDisposable
{
    /// <summary>New text and how far it is (0–1).</summary>
    void Update(string text, double level);
}

internal sealed class NoProgressOsd : IProgressOsd
{
    public void Update(string text, double level) { }
    public void Dispose() { }
}

/// <summary>Services the engine needs from the UI layer. Implementations must be thread-safe.</summary>
public interface IUiServices
{
    /// <summary>Shows a notification card; completes when it is gone (or was dropped: full screen and «No mostrarlas»).</summary>
    Task ShowNotificationAsync(Notification notification) => Task.CompletedTask;

    /// <summary>A message that stays with a bar (copying files…), updated with <see cref="IProgressOsd.Update"/> until disposed.</summary>
    IProgressOsd ShowProgressOsd(string text, string icon) => new NoProgressOsd();

    /// <summary>Shows a short on-screen message.</summary>
    void ShowOsd(string text, OsdKind kind = OsdKind.Info, TimeSpan? duration = null, OsdLook? look = null);

    /// <summary>
    /// Shows a message that stays until the returned handle is disposed.
    /// <paramref name="countdownTo"/> makes it display the remaining seconds.
    /// </summary>
    IDisposable ShowPersistentOsd(string text, DateTime? countdownTo = null, string? icon = null);

    /// <summary>
    /// The voice OSD: shows <paramref name="text"/> («Escuchando…») with a live microphone meter until it is
    /// finished or disposed.
    /// </summary>
    IVoiceOsd ShowVoiceOsd(string text);

    /// <summary>Opens a QuickMenu at the mouse position.</summary>
    void ShowQuickMenu(string menuId, ForegroundWindow previousForeground);

    /// <summary>«Apagar pantallas» of some monitors (GDI names): covers them in black, or uncovers them if they are.</summary>
    void BlankMonitors(IReadOnlyCollection<string> devices) { }

    void ShowMainWindow();

    /// <summary>«Asistente: acción»: sends an extra to its assistant (bringing it onto the desktop first if needed).</summary>
    void RunAssistantExtra(string assistant, string extra) { }

    /// <summary>«Asistente: preguntar»: an order for an assistant (empty: the default one); without text, the box to write it.</summary>
    void AskAssistant(string assistant, string text) { }

    /// <summary>
    /// An event's «Pedir a un asistente» that an assistant programmed: its order with what was approved then (see
    /// <see cref="Assistant.ScheduledPermit"/>).
    /// </summary>
    void AskAssistantScheduled(EventAsk ask) => AskAssistant(ask.Assistant, ask.Text);

    /// <summary>«Asistente: decir»: an assistant (empty: the default one) says this, without the AI.</summary>
    void AssistantSay(string assistant, string text) { }

    /// <summary>«Asistente: cerebro»: the assistants' model out of the graphics card, or into it.</summary>
    void AssistantBrain(Model.AssistantBrainOp op) { }

    /// <summary>«Dictar»: starts writing what is said where the cursor is, or stops it.</summary>
    void Dictate(Model.SwitchMode mode) { }

    /// <summary>Runs <paramref name="action"/> on the UI thread (needed to modify bound collections).</summary>
    void RunOnUi(Action action);

    /// <summary>Runs exit commands and closes K-Desktop.</summary>
    void RequestExit();

    /// <summary>Puts <paramref name="text"/> in the clipboard as plain text (empty clears it). Returns whether it could.</summary>
    bool SetClipboardText(string text) => false;

    /// <summary>Puts a picture in the clipboard. Returns whether it could.</summary>
    bool SetClipboardImage(Platform.BgraImage image) => false;

    /// <summary>
    /// «Captura de pantalla › Región»: shows <paramref name="desktop"/> (a picture of <paramref name="bounds"/>, every
    /// monitor) frozen and lets the user drag a rectangle; null when cancelled (Esc, right click).
    /// </summary>
    Task<Platform.ScreenRect?> SelectRegionAsync(Platform.BgraImage desktop, Platform.ScreenRect bounds) => Task.FromResult<Platform.ScreenRect?>(null);

    /// <summary>A quick white flash over <paramref name="area"/> (screen pixels): a screenshot was taken.</summary>
    void FlashArea(Platform.ScreenRect area) { }
}

/// <summary>Handle to the voice OSD. Thread-safe.</summary>
public interface IVoiceOsd : IDisposable
{
    /// <summary>Microphone level, 0–1.</summary>
    void SetLevel(float level);

    /// <summary>Replaces the text and hides the meter (e.g. «Pensando…»); it stays until finished.</summary>
    void SetText(string text);

    /// <summary>Last message: shown for the usual OSD time, then hidden.</summary>
    void Finish(string text, OsdKind kind);

    /// <summary>Replaces the text keeping the meter (dictation: the last words written).</summary>
    void SetVoiceText(string text)
    {
    }
}
