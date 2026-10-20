using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

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

    public string Description => "Opens a web address in the user's browser.";

    public string ParametersSchemaJson => ToolText.OneString("url", "The address, e.g. github.com");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.open_web.intent", Browser.Address(arguments.GetString("url"))?.Host ?? ToolText.Clean(arguments.GetString("url")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (Browser.Address(arguments.GetString("url")) is not { } uri) return Task.FromResult(ToolResult.Fail("That is not a web address."));
        return Task.FromResult((open ?? Browser.Open)(uri)
            ? new ToolResult($"Opened {uri.Host}.") { Done = Loc.T("tool.open_web.done", uri.Host) }
            : ToolResult.Fail("The browser did not open."));
    }
}

/// <summary>«Busca recetas de lentejas».</summary>
public sealed class SearchWebTool(Func<Uri, bool>? open = null) : ITool
{
    public string Name => "search_web";

    public string Description => "Searches the internet in the user's browser.";

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

    public string Description => "Opens a program installed on the PC (from the Start menu) by its name.";

    public string ParametersSchemaJson => ToolText.OneString("name", "The program's name, e.g. Calculator, Spotify");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.open_app.intent", ToolText.Clean(arguments.GetString("name")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var name = arguments.GetString("name")?.Trim();
        if (string.IsNullOrEmpty(name)) return ToolResult.Fail("Say which program.");
        var list = await (apps ?? InstalledApps.GetAsync)().ConfigureAwait(false);
        if (Find(list, name) is not { } app) return ToolResult.Fail($"There is no program called «{name}».");
        return (launch ?? InstalledApps.Launch)(app)
            ? new ToolResult($"Opened {app.Name}.") { Done = Loc.T("tool.open_app.done", app.Name) }
            : ToolResult.Fail($"{app.Name} did not open.");
    }

    /// <summary>The best match, as the launcher scores it (a weak one is not taken).</summary>
    public static InstalledApp? Find(IReadOnlyList<InstalledApp> apps, string name)
    {
        var tokens = FuzzyMatch.Tokens(name);
        return apps.Select(a => (App: a, Score: FuzzyMatch.Score(tokens, new SearchKey(a.Name))))
            .Where(p => p.Score >= 300)
            .OrderByDescending(p => p.Score).ThenBy(p => p.App.Name.Length)
            .Select(p => p.App)
            .FirstOrDefault();
    }
}
