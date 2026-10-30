using System.Net;
using System.Net.Http.Headers;

namespace KDesktop.Core.Calendar;

/// <summary>
/// A .ics read from an address every few minutes (read only): the secret address of a Google or Outlook calendar, the
/// holidays, a team's matches… It asks with ETag / If-Modified-Since, so nothing is downloaded when nothing changed,
/// and keeps the last copy in Calendar\Cache, so it is there without network.
/// </summary>
public sealed class IcsSubscription : ICalendarSource
{
    public const string MainCalendar = "main";

    /// <summary>A .ics bigger than this is not read (a mistake, or not a calendar).</summary>
    private const long MaxBytes = 20 * 1024 * 1024;

    private sealed class Cache
    {
        public List<CalendarEvent> Events { get; set; } = [];
        public string? Etag { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public DateTimeOffset? LastSync { get; set; }
        public string? RemoteName { get; set; }
    }

    private readonly string _cacheFile;
    private readonly HttpClient _http;
    private readonly Func<string, string?> _unprotect;
    private readonly Lock _gate = new();
    private Cache _cache;

    public IcsSubscription(IcsSubscriptionSettings settings, string cacheFile, HttpClient http, Func<string, string?> unprotect)
    {
        Settings = settings;
        _cacheFile = cacheFile;
        _http = http;
        _unprotect = unprotect;
        try { _cache = CalendarJson.Load<Cache>(cacheFile) ?? new Cache(); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Calendario: copia de «{settings.Name}» no válida: {ex.Message}");
            _cache = new Cache();
        }
    }

    public IcsSubscriptionSettings Settings { get; }

    public string Id => Settings.Id;

    public string Name => Settings.Name.Length > 0 ? Settings.Name : _cache.RemoteName ?? "ICS";

    public CalendarSourceKind Kind => CalendarSourceKind.Subscription;

    public CalendarCapabilities Capabilities => CalendarCapabilities.None;

    public string? Problem { get; private set; }

    public DateTimeOffset? LastSync
    {
        get { lock (_gate) return _cache.LastSync; }
    }

    public event Action? Changed;

    public IReadOnlyList<CalendarInfo> Calendars =>
    [
        new CalendarInfo { Id = MainCalendar, SourceId = Id, Name = Name, Color = Settings.Color, ReadOnly = true },
    ];

    public IReadOnlyList<CalendarEvent> Events
    {
        get { lock (_gate) return _cache.Events; }
    }

    private CalendarException ReadOnly() => new(Localization.Loc.T("calendar.error.readOnly", Name));

    public Task<CalendarEvent> AddAsync(CalendarEvent e, CancellationToken cancel = default) => throw ReadOnly();

    public Task<CalendarEvent> UpdateAsync(CalendarEvent e, CancellationToken cancel = default) => throw ReadOnly();

    public Task DeleteAsync(CalendarEvent e, CancellationToken cancel = default) => throw ReadOnly();

    /// <summary>«webcal://…» → «https://…»; spaces out.</summary>
    public static string Normalize(string url)
    {
        url = url.Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        else if (url.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[10..];
        return url;
    }

    /// <summary>Whether it looks like an address that can be read («https://…», «webcal://…»).</summary>
    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(Normalize(url), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public async Task SyncAsync(CancellationToken cancel = default)
    {
        var url = _unprotect(Settings.UrlProtected);
        if (string.IsNullOrEmpty(url))
        {
            // DPAPI of another user or PC (a backup restored elsewhere): it must be typed again.
            Problem = Localization.Loc.T("calendar.problem.urlLost");
            Changed?.Invoke();
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        string? etag;
        DateTimeOffset? modified;
        lock (_gate)
        {
            etag = _cache.Etag;
            modified = _cache.LastModified;
        }
        if (etag is not null && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
        else if (modified is { } m) request.Headers.IfModifiedSince = m;
        request.Headers.Accept.ParseAdd("text/calendar, */*;q=0.5");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            SetProblem(Localization.Loc.T("calendar.problem.offline"));
            Log.Info($"Calendario: «{Name}» sin conexión: {ex.Message}");
            return;
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                lock (_gate) _cache.LastSync = DateTimeOffset.UtcNow;
                Save();
                SetProblem(null);
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                SetProblem(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? Localization.Loc.T("calendar.problem.urlGone", (int)response.StatusCode)
                    : Localization.Loc.T("calendar.problem.http", (int)response.StatusCode));
                return;
            }
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                SetProblem(Localization.Loc.T("calendar.problem.tooBig"));
                return;
            }
            var text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            if (!text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                SetProblem(Localization.Loc.T("calendar.problem.notIcs"));
                return;
            }
            var ics = IcsFormat.Read(text, Id, MainCalendar);
            lock (_gate)
            {
                _cache = new Cache
                {
                    Events = [.. ics.Events],
                    Etag = response.Headers.ETag?.ToString(),
                    LastModified = response.Content.Headers.LastModified,
                    LastSync = DateTimeOffset.UtcNow,
                    RemoteName = ics.Name,
                };
            }
            Save();
            Problem = null;
            Log.Info($"Calendario: «{Name}» actualizado ({ics.Events.Count} citas)");
            Changed?.Invoke();
        }
    }

    private void SetProblem(string? problem)
    {
        if (Problem == problem) return;
        Problem = problem;
        Changed?.Invoke();
    }

    private void Save()
    {
        try
        {
            lock (_gate) CalendarJson.Save(_cacheFile, _cache);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Calendario: no se ha podido guardar la copia de «{Name}»: {ex.Message}");
        }
    }
}
