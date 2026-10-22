using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>Opening addresses in the user's browser (the process runs as the user, so the browser is not elevated).</summary>
internal static class Browser
{
    public static bool Open(Uri uri)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Asistente: no se ha podido abrir {uri.Host}: {ex.Message}");
            return false;
        }
    }

    /// <summary>A web address from what the model wrote («github.com», «https://…»): only http and https, with a host.</summary>
    public static Uri? Address(string? text)
    {
        var clean = (text ?? string.Empty).Trim().Trim('"', '\'', '<', '>');
        if (clean.Length == 0 || clean.Contains(' ', StringComparison.Ordinal)) return null;
        if (!clean.Contains("://", StringComparison.Ordinal)) clean = "https://" + clean;
        return Uri.TryCreate(clean, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
               && uri.Host.Contains('.', StringComparison.Ordinal) ? uri : null;
    }
}

/// <summary>«Abre github.com».</summary>
public sealed class OpenWebTool(Func<Uri, bool>? open = null) : ITool
{
    public string Name => "open_web";

    public string Description => "Opens in the browser a website the user names (youtube, github.com). Never guess an address: a document, manual or photo of the PC is open_file.";

    public string ParametersSchemaJson => ToolText.OneString("url", "The address, e.g. github.com");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.open_web.intent", Browser.Address(arguments.GetString("url"))?.Host ?? ToolText.Clean(arguments.GetString("url")));

    /// <summary>The same site is not opened twice this close together (a small model asked for es.wikipedia.org, then wikipedia.org).</summary>
    public static readonly TimeSpan Again = TimeSpan.FromSeconds(20);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _opened = new(StringComparer.OrdinalIgnoreCase);

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (Browser.Address(arguments.GetString("url")) is not { } uri) return Task.FromResult(ToolResult.Fail("That is not a web address."));
        // The AI's address must come from somewhere: «ábreme una página de chistes» opened a made-up «jokes.one».
        if (context.Sources is { } sources && !Mentioned(uri, sources))
        {
            Log.Info($"Asistente: open_web con una dirección inventada ({uri.Host})");
            return Task.FromResult(ToolResult.Fail($"Not done: you made up \"{uri.Host}\" (the user did not say it and no result gave it). Never guess addresses: to find a real page use look_up_web, or open a search with search_web."));
        }
        var site = SiteOf(uri);
        var now = DateTime.UtcNow;
        if (_opened.TryGetValue(site, out var last) && now - last < Again)
            return Task.FromResult(new ToolResult($"{uri.Host} was already opened a moment ago; it is open.") { Done = Loc.T("tool.open_web.done", uri.Host) });
        if (!(open ?? Browser.Open)(uri)) return Task.FromResult(ToolResult.Fail("The browser did not open."));
        _opened[site] = now;
        return Task.FromResult(new ToolResult($"Opened {uri.Host}.") { Done = Loc.T("tool.open_web.done", uri.Host) });
    }

    /// <summary>
    /// Whether the site's name is in <paramref name="sources"/> as it sounds (written together or apart: «el tiempo» for
    /// eltiempo.es; «tracksseries» for trackseries.tv; «youtube» for youtube.com).
    /// </summary>
    public static bool Mentioned(Uri uri, string sources)
    {
        var labels = uri.Host.Split('.');
        var name = labels.Length >= 2 ? labels[^2] : uri.Host;
        var key = PhoneticKey.Encode(string.Concat(TextSimilarity.Words(name)));
        if (key.Length == 0) return true;
        // Word by word, then together: «abre el tiempo» as one word would read «abreel…» the English way.
        var text = string.Concat(TextSimilarity.Words(sources).Select(PhoneticKey.Encode));
        return text.Contains(key, StringComparison.Ordinal)
               || sources.Contains(uri.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The site without its language or «www» and its ending («es.wikipedia.org» and «wikipedia.com» are «wikipedia»), with its path.</summary>
    private static string SiteOf(Uri uri)
    {
        var labels = uri.Host.Split('.');
        return (labels.Length >= 2 ? labels[^2] : uri.Host) + uri.AbsolutePath.TrimEnd('/');
    }
}

/// <summary>«Busca recetas de lentejas».</summary>
public sealed class SearchWebTool(Func<Uri, bool>? open = null) : ITool
{
    public string Name => "search_web";

    public string Description => "Opens a search in the user's browser, for them to look at. To answer a question yourself with facts from the internet, use look_up_web.";

    public string ParametersSchemaJson => ToolText.OneString("query", "What to search for");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.search_web.intent", ToolText.Clean(arguments.GetString("query")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.GetString("query")?.Trim();
        if (string.IsNullOrEmpty(query)) return Task.FromResult(ToolResult.Fail("There is nothing to search for."));
        var uri = new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(query));
        return Task.FromResult((open ?? Browser.Open)(uri)
            ? new ToolResult("The search is open in the browser.") { Done = Loc.T("tool.search_web.done", ToolText.Clean(query)) }
            : ToolResult.Fail("The browser did not open."));
    }
}

/// <summary>«Abre la calculadora»: a program of the Start menu, found by its name like the launcher does.</summary>
public sealed class OpenAppTool(Func<Task<IReadOnlyList<InstalledApp>>>? apps = null, Func<InstalledApp, bool>? launch = null) : ITool
{
    public string Name => "open_app";

    public string Description => "Opens (starts) a program installed on the PC (from the Start menu) by its name. Only opens: it cannot close, minimize or change a program.";

    public string ParametersSchemaJson => ToolText.OneString("name", "The program's name, e.g. Calculator, Spotify");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.open_app.intent", ToolText.Clean(arguments.GetString("name")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var name = arguments.GetString("name")?.Trim();
        if (string.IsNullOrEmpty(name)) return ToolResult.Fail("Say which program.");
        if (await FindAsync(name).ConfigureAwait(false) is not { } app) return ToolResult.Fail($"There is no program called «{name}».");
        return (launch ?? InstalledApps.Launch)(app)
            ? new ToolResult($"Opened {app.Name}.") { Done = Loc.T("tool.open_app.done", app.Name) }
            : ToolResult.Fail($"{app.Name} did not open.");
    }

    /// <summary>The installed program a name means, or null («abre Steam» without Steam installed goes to the AI).</summary>
    public async Task<InstalledApp?> FindAsync(string name) => Find(await (apps ?? InstalledApps.GetAsync)().ConfigureAwait(false), name);

    /// <summary>
    /// The installed program a name means, word by word (<see cref="AppFinder"/>): the launcher's subsequence search took
    /// «Steam» for "Inno Setup Example Scripts".
    /// </summary>
    public static InstalledApp? Find(IReadOnlyList<InstalledApp> apps, string name) => AppFinder.FindApp(name, apps);
}
