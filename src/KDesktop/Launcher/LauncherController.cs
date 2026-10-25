using System.Data;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using KDesktop.Core;
using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Native;
using KDesktop.Services;

namespace KDesktop.Launcher;

/// <summary>
/// The launcher (its hotkey and options in Settings › Lanzador): a search box over the commands, the installed programs
/// and the web, with a small calculator. Results are ranked by how well they match and, if wanted, by how often they are used.
/// </summary>
public sealed partial class LauncherController
{
    private const int MaxResults = 30;
    private const int EmptyResults = 8;

    private readonly Func<AppConfig> _config;
    private readonly CommandEngine _engine;
    private readonly AchievementService _achievements;
    private readonly IUiServices _ui;
    private readonly LauncherUsage _usage = new();

    private LauncherWindow? _window;
    private ForegroundWindow _previous = new(IntPtr.Zero, 0, string.Empty);
    private List<Candidate> _commands = [];
    private List<Candidate> _apps = [];
    private IReadOnlyList<InstalledApp>? _appsSource;
    private List<LauncherItem> _items = [];
    private int _selected = -1;

    public LauncherController(Func<AppConfig> config, CommandEngine engine, AchievementService achievements, IUiServices ui)
    {
        _config = config;
        _engine = engine;
        _achievements = achievements;
        _ui = ui;
        ThemeService.Changed += () => _window?.ApplyTheme();
        InstalledApps.Updated += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (IsOpen && Settings.LauncherApps) Search(_window!.Query); // the list arrived while typing
        });
    }

    public bool IsOpen => _window?.IsVisible == true;

    private AppSettings Settings => _config().Settings;

    /// <summary>Creates the window once so the first opening is instant, and reads the programs in the background.</summary>
    public void Prewarm()
    {
        var w = Window;
        w.Left = -32000;
        w.Top = -32000;
        w.Show();
        w.HideNow();
        InstalledApps.Refresh();
    }

    private LauncherWindow Window
    {
        get
        {
            if (_window is not null) return _window;
            var w = new LauncherWindow();
            w.QueryChanged += Search;
            w.MoveRequested += Move;
            w.ExecuteRequested += () => Execute(_selected >= 0 && _selected < _items.Count ? _items[_selected] : null);
            w.CloseRequested += () => Close(restoreFocus: true);
            w.ItemHovered += item => Select(_items.IndexOf(item), scroll: false);
            w.ItemClicked += Execute;
            w.Deactivated += (_, _) => { if (IsOpen) Close(restoreFocus: false); };
            w.Dropped += RememberPosition;
            return _window = w;
        }
    }

    public void Show(ForegroundWindow previous)
    {
        // The same hotkey again closes it.
        if (IsOpen)
        {
            Close(restoreFocus: true);
            return;
        }

        _previous = previous;
        _commands = CommandCandidates();
        if (Settings.LauncherApps) InstalledApps.Refresh();

        var w = Window;
        w.ResetQuery();
        Search(string.Empty);

        // On the monitor of the mouse: where it was left, or centred a bit above the middle (like the Start menu search).
        var cursor = UiNative.CursorPosition();
        var work = UiNative.WorkAreaAt(cursor);
        w.Left = -32000;
        w.Top = -32000;
        w.Show();
        var scale = UiNative.GetDpiForWindow(w.Hwnd) / 96.0;
        var width = (int)Math.Round(w.Width * scale);
        var (fx, fy) = SavedPosition() ?? (0.5, 0.2);
        var x = (int)Math.Round(work.Left + fx * work.Width - width / 2.0);
        var y = (int)Math.Round(work.Top + fy * work.Height);
        UiNative.MoveWindow(w.Hwnd, Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width)), Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - 120)));
        w.PlayShow();
        w.Activate();
        WindowTools.ForceForeground(w.Hwnd);
        w.FocusSearch();
    }

    private (double X, double Y)? SavedPosition()
    {
        var parts = Settings.LauncherPosition.Split(';');
        return parts.Length == 2 &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
               x is >= 0 and <= 1 && y is >= 0 and <= 1
            ? (x, y) : null;
    }

    /// <summary>Dropped somewhere: remember it relative to the screen it is on (top edge centre).</summary>
    private void RememberPosition()
    {
        if (_window is not { } w || !UiNative.GetWindowRect(w.Hwnd, out var rect)) return;
        var work = UiNative.WorkAreaAt(new UiNative.POINT { X = (rect.Left + rect.Right) / 2, Y = rect.Top + 10 });
        if (work.Width <= 0 || work.Height <= 0) return;
        var fx = Math.Clamp(((rect.Left + rect.Right) / 2.0 - work.Left) / work.Width, 0, 1);
        var fy = Math.Clamp((rect.Top - work.Top) / (double)work.Height, 0, 1);
        Settings.LauncherPosition = string.Create(CultureInfo.InvariantCulture, $"{fx:0.####};{fy:0.####}");
        w.FocusSearch();
    }

    /// <summary>Forgets which programs were opened most (Settings › Lanzador).</summary>
    public void ClearUsage() => _usage.Clear();

    public void Close(bool restoreFocus)
    {
        if (!IsOpen) return;
        _window!.HideNow();
        if (restoreFocus && _previous.Handle != IntPtr.Zero) WindowTools.ForceForeground(_previous.Handle);
    }

    // ── search ───────────────────────────────────────────────────────────

    /// <param name="Usage">Times it was run (read when ranking, so it is always current).</param>
    /// <param name="Bonus">Commands are the user's own: they win ties against programs.</param>
    private sealed record Candidate(SearchKey Name, SearchKey? Extra, Func<long> Usage, int Bonus, Func<LauncherItem> Create);

    private List<Candidate> CommandCandidates()
    {
        var config = _config();
        var runs = _achievements.Snapshot().Data.CommandRuns;
        return config.Commands
            .Where(c => c.Enabled && (config.Settings.LauncherHidden || !c.Hidden))
            .Select(c =>
            {
                var details = string.Join("  ", c.Actions.Where(a => a.Enabled).Select(a => a.Summary));
                return new Candidate(new SearchKey(c.Name), details.Length > 0 ? new SearchKey(details) : null,
                    () => runs.GetValueOrDefault(c.Id), 20,
                    () => new LauncherItem(LauncherItemKind.Command, c.Name, c.PrimaryAction?.Summary ?? "",
                        c.HotkeysText.Length > 0 ? c.HotkeysText : Loc.T("launcher.command"), () => IconProvider.ForCommand(c), c));
            })
            .ToList();
    }

    /// <summary>Search keys of the programs, rebuilt only when the list of programs changes.</summary>
    private List<Candidate> AppCandidates()
    {
        var source = InstalledApps.Current;
        if (ReferenceEquals(source, _appsSource)) return _apps;
        _appsSource = source;
        return _apps = source.Select(app => new Candidate(new SearchKey(app.Name), null, () => _usage.Get(app.Id), 0,
            () => new LauncherItem(LauncherItemKind.App, app.Name, Loc.T("launcher.app"), "",
                () => IconProvider.Shell(app.ParsingName), app))).ToList();
    }

    private void Search(string query)
    {
        var settings = Settings;
        var candidates = settings.LauncherApps ? _commands.Concat(AppCandidates()) : _commands;
        var byUse = settings.LauncherByUse;
        var tokens = FuzzyMatch.Tokens(query);
        var items = new List<LauncherItem>();

        if (tokens.Length == 0)
        {
            // Nothing typed yet: the most used ones (if wanted).
            if (byUse) items.AddRange(candidates.Where(c => c.Usage() > 0).OrderByDescending(c => c.Usage()).Take(EmptyResults).Select(c => c.Create()));
            ShowItems(items, Loc.T("launcher.hint"));
            return;
        }

        var result = settings.LauncherCalculator ? Calculate(query) : null;
        if (result is not null)
            items.Add(new LauncherItem(LauncherItemKind.Calculator, "= " + result, Loc.T("launcher.calculator"), Loc.T("launcher.copy"),
                () => IconProvider.Kd("Calculator24", "teal"), result));

        items.AddRange(candidates
            .Select(c =>
            {
                var score = FuzzyMatch.Score(tokens, c.Name);
                if (c.Extra is { } extra) score = Math.Max(score, FuzzyMatch.Score(tokens, extra) * 6 / 10);
                return (Candidate: c, Score: score == 0 ? 0 : score + c.Bonus + (byUse ? UsageBoost(c.Usage()) : 0));
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Name.Text, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxResults)
            .Select(x => x.Candidate.Create()));

        if (settings.LauncherWeb && result is null)
            items.Add(new LauncherItem(LauncherItemKind.Web, Loc.T("launcher.web", query.Trim()), SearchEngines.Name(settings), "",
                () => IconProvider.Kd("Globe24", "sky"), query.Trim()));

        ShowItems(items, Loc.T("launcher.noResults"));
    }

    private static int UsageBoost(long usage) => usage <= 0 ? 0 : (int)Math.Min(250, 60 * Math.Log2(1 + usage));

    private void ShowItems(List<LauncherItem> items, string emptyText)
    {
        _items = items;
        _window?.SetResults(items, emptyText);
        _selected = -1;
        Select(items.Count > 0 ? 0 : -1, scroll: true);
    }

    private void Move(int delta)
    {
        if (_items.Count == 0) return;
        var index = _selected + delta;
        // Single steps wrap around; page jumps stop at the ends.
        index = Math.Abs(delta) == 1 ? (index + _items.Count) % _items.Count : Math.Clamp(index, 0, _items.Count - 1);
        Select(index, scroll: true);
    }

    private void Select(int index, bool scroll)
    {
        if (index == _selected) return;
        if (_selected >= 0 && _selected < _items.Count) _items[_selected].IsSelected = false;
        _selected = index;
        if (index < 0 || index >= _items.Count) return;
        _items[index].IsSelected = true;
        if (scroll) _window?.BringIntoView(_items[index]);
    }

    // ── calculator ───────────────────────────────────────────────────────

    [GeneratedRegex(@"^[\d\s.,+\-*/()]+$")]
    private static partial Regex MathExpression();

    [GeneratedRegex(@"\d\s*[+\-*/]\s*[\d(]|^\s*\(")]
    private static partial Regex HasOperation();

    /// <summary>"2+3*4", "(1,5 + 2) / 3"… → the result in the current culture; null if it is not a calculation.</summary>
    private static string? Calculate(string query)
    {
        if (!MathExpression().IsMatch(query) || !HasOperation().IsMatch(query) || !query.Any(char.IsDigit)) return null;
        try
        {
            // Both "1,5" and "1.5" are decimals (Spanish keyboards type a comma).
            var value = new DataTable { Locale = CultureInfo.InvariantCulture }.Compute(query.Replace(',', '.'), null);
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(number) ? Math.Round(number, 10).ToString("G15", Loc.Instance.Culture) : null;
        }
        catch
        {
            return null;
        }
    }

    // ── execution ────────────────────────────────────────────────────────

    private void Execute(LauncherItem? item)
    {
        if (item is null) return;
        var previous = _previous;
        _achievements.Increment(Stat.LauncherUsed);
        switch (item.Payload)
        {
            case Command command:
                Close(restoreFocus: true);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(30);
                    await _engine.RunAsync(command, TriggerSource.Launcher, previous);
                });
                break;
            case InstalledApp app:
                Close(restoreFocus: false);
                _usage.Increment(app.Id);
                _ = Task.Run(() => InstalledApps.Launch(app));
                break;
            case string text when item.Kind == LauncherItemKind.Web:
                Close(restoreFocus: false);
                var url = SearchEngines.Url(Settings, text);
                _ = Task.Run(() => ProcessLauncher.Launch(new LaunchSpec(url), asAdmin: false));
                break;
            case string result when item.Kind == LauncherItemKind.Calculator:
                try { Clipboard.SetText(result); } catch (Exception ex) { Log.Warn("No se pudo copiar el resultado: " + ex.Message); }
                Close(restoreFocus: true);
                _ui.ShowOsd(Loc.T("launcher.copied", result), OsdKind.Success, look: OsdLook.Of("Calculator24"));
                break;
        }
    }
}

/// <summary>The web searches the launcher offers (Settings › Lanzador).</summary>
public static class SearchEngines
{
    public static readonly (string Id, string Name, string Url)[] All =
    [
        ("google", "Google", "https://www.google.com/search?q={0}"),
        ("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={0}"),
        ("bing", "Bing", "https://www.bing.com/search?q={0}"),
        ("brave", "Brave Search", "https://search.brave.com/search?q={0}"),
    ];

    public static string Name(AppSettings s) =>
        s.LauncherSearchEngine == "custom" ? CustomHost(s) : All.FirstOrDefault(e => e.Id == s.LauncherSearchEngine).Name ?? "Google";

    /// <summary>The address to open for <paramref name="text"/> ({0} in a custom one is the text; without it, it goes at the end).</summary>
    public static string Url(AppSettings s, string text)
    {
        var template = s.LauncherSearchEngine == "custom" && s.LauncherSearchUrl.Trim().Length > 0
            ? CommandEngine.NormalizeUrl(s.LauncherSearchUrl.Trim())
            : All.FirstOrDefault(e => e.Id == s.LauncherSearchEngine).Url ?? All[0].Url;
        var escaped = Uri.EscapeDataString(text);
        return template.Contains("{0}", StringComparison.Ordinal) ? template.Replace("{0}", escaped, StringComparison.Ordinal) : template + escaped;
    }

    private static string CustomHost(AppSettings s) =>
        Uri.TryCreate(CommandEngine.NormalizeUrl(s.LauncherSearchUrl.Trim().Replace("{0}", "x")), UriKind.Absolute, out var u) ? u.Host : "Web";
}

/// <summary>How many times each program was opened from the launcher (DataDir\launcher.json), to rank it higher.</summary>
internal sealed class LauncherUsage
{
    private static string FilePath => Path.Combine(AppPaths.DataDir, "launcher.json");

    private Dictionary<string, long>? _apps;

    private Dictionary<string, long> Apps => _apps ??= Load();

    public long Get(string id) => Apps.GetValueOrDefault(id);

    /// <summary>Forgets which programs were used (Settings › Lanzador).</summary>
    public void Clear()
    {
        Apps.Clear();
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception ex) { Log.Warn("No se pudo borrar el uso del lanzador: " + ex.Message); }
    }

    public void Increment(string id)
    {
        Apps[id] = Get(id) + 1;
        try { File.WriteAllText(FilePath, Json.Serialize(new Data { Apps = Apps })); }
        catch (Exception ex) { Log.Warn("No se pudo guardar el uso del lanzador: " + ex.Message); }
    }

    private static Dictionary<string, long> Load()
    {
        try
        {
            if (File.Exists(FilePath) && Json.Deserialize<Data>(File.ReadAllText(FilePath)) is { } data)
                return new Dictionary<string, long>(data.Apps, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { Log.Warn("No se pudo leer el uso del lanzador: " + ex.Message); }
        return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class Data
    {
        public Dictionary<string, long> Apps { get; set; } = [];
    }
}
