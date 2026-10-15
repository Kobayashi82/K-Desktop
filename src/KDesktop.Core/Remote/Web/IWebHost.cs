using KDesktop.Core.Engine;
using KDesktop.Core.Model;

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
    WebIcon? Icon(Command? command, QuickMenuItem? item = null);

    /// <summary>How many times each command has run (by id), to rank search results.</summary>
    IReadOnlyDictionary<string, long> Runs();
}
