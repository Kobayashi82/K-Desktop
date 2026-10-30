using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Remote.Web;

/// <summary>An icon rendered for the web app: PNG bytes, addressed by their hash (so the phone caches them forever).</summary>
public sealed record WebIcon(string Hash, byte[] Png);

/// <summary>What the web server needs from the application (implemented by the WPF layer).</summary>
public interface IWebHost
{
    CommandEngine Engine { get; }

    /// <summary>Runs <paramref name="func"/> on the UI thread and waits: the configuration and the icons live there.</summary>
    T Invoke<T>(Func<T> func);

    /// <summary>Icon of a command or of a QuickMenu item (UI thread). Null when it has none.</summary>
    /// <param name="light">For a light background (white glyph icons are shown inverted), else for a dark one.</param>
    WebIcon? Icon(Command? command, QuickMenuItem? item = null, bool light = false);

    /// <summary>The icon of a program file (the volume of each program). Null when it has none.</summary>
    WebIcon? FileIcon(string path) => null;

    /// <summary>How many times each command has run (by id), to rank search results.</summary>
    IReadOnlyDictionary<string, long> Runs();

    /// <summary>Voice orders can be understood (the models are downloaded).</summary>
    bool VoiceReady { get; }

    /// <summary>A voice order recorded on the phone: 16 kHz mono samples.</summary>
    Task<RemoteVoiceResult> VoiceAsync(float[] samples);

    /// <summary>The OSD and the rest of the app's interface (what the phone shares says it arrived); null in tests.</summary>
    IUiServices? Ui => null;

    /// <summary>Talking to the assistants from the phone; null where there are none (tests).</summary>
    IWebAssistants? Assistants { get; }
}
