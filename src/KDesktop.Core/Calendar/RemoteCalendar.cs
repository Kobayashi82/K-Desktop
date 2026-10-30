using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Accounts;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Calendar;

/// <summary>A change made without connection, sent when it comes back (in order).</summary>
public sealed record PendingChange(string Op, CalendarEvent Event, CalendarEvent? Before = null);

/// <summary>
/// The common part of the calendars of an account (Google): everything is kept in Calendar\Cache (reading never
/// waits for the network), the changes go to the server at once and, without connection, are kept in a queue and sent
/// when it comes back. A conflict (someone changed it elsewhere: HTTP 412) never overwrites: online it is said
/// (<see cref="CalendarConflictException"/>) and the server's version is read again; from the queue the server's wins
/// and it is logged. The changed appearances of a series are kept apart (as the servers do) and put inside it to be read.
/// </summary>
public abstract class RemoteCalendar : ICalendarSource, IDisposable
{
    protected sealed class CacheData
    {
        /// <summary>Every calendar of the account (to choose in the options).</summary>
        public List<CalendarInfo> All { get; set; } = [];

        /// <summary>The citas, as the server keeps them: series, single ones and changed appearances (with <see cref="CalendarEvent.SeriesId"/>).</summary>
        public List<CalendarEvent> Events { get; set; } = [];

        /// <summary>Per calendar: what the server gave to ask only for what changed (Google's syncToken).</summary>
        public Dictionary<string, string> SyncTokens { get; set; } = [];

        public List<PendingChange> Pending { get; set; } = [];

        public DateTimeOffset? LastSync { get; set; }
    }

    protected AccountService Accounts { get; }

    protected HttpClient Http { get; }

    private readonly string _cacheFile;
    protected Lock Gate { get; } = new();

    protected CacheData Cache { get; set; }

    private string? _problem;

    protected RemoteCalendar(AccountService accounts, CalendarAccountSettings settings, string cacheFile, HttpClient http)
    {
        Accounts = accounts;
        Settings = settings;
        _cacheFile = cacheFile;
        Http = http;
        try { Cache = CalendarJson.Load<CacheData>(cacheFile) ?? new CacheData(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Calendario: copia de {cacheFile} no válida: {ex.Message}");
            Cache = new CacheData();
        }
    }

    public CalendarAccountSettings Settings { get; }

    public Account? Account => Accounts.Get(Settings.AccountId);

    public string Id => CalendarService.AccountSourceId(Settings.AccountId);

    public abstract CalendarSourceKind Kind { get; }

    public string Name => "Google · " + (Account?.Email ?? Settings.AccountId);

    public CalendarCapabilities Capabilities => Account?.Uses.HasFlag(AccountUse.CalendarWrite) == true
        ? CalendarCapabilities.Write | CalendarCapabilities.Recurrence | CalendarCapabilities.Overrides | CalendarCapabilities.Reminders | CalendarCapabilities.Colors
        : CalendarCapabilities.Reminders | CalendarCapabilities.Colors;

    public string? Problem => Account is { Disconnected: true } ? Loc.T("accounts.disconnected") : _problem;

    public DateTimeOffset? LastSync
    {
        get { lock (Gate) return Cache.LastSync; }
    }

    public event Action? Changed;

    protected void RaiseChanged() => Changed?.Invoke();

    /// <summary>Every calendar of the account, for the options.</summary>
    public IReadOnlyList<CalendarInfo> AllCalendars
    {
        get { lock (Gate) return [.. Cache.All]; }
    }

    /// <summary>The ids of the calendars shown: the ones chosen, or the main one.</summary>
    private HashSet<string> Chosen()
    {
        lock (Gate)
        {
            if (Settings.Calendars.Count > 0) return [.. Settings.Calendars];
            var main = Cache.All.FirstOrDefault(c => IsMain(c)) ?? Cache.All.FirstOrDefault();
            return main is null ? [] : [main.Id];
        }
    }

    /// <summary>Whether a calendar is the account's main one.</summary>
    protected abstract bool IsMain(CalendarInfo calendar);

    public IReadOnlyList<CalendarInfo> Calendars
    {
        get
        {
            var chosen = Chosen();
            lock (Gate) return [.. Cache.All.Where(c => chosen.Contains(c.Id))];
        }
    }

    public IReadOnlyList<CalendarEvent> Events
    {
        get
        {
            var chosen = Chosen();
            List<CalendarEvent> flat;
            lock (Gate) flat = [.. Cache.Events.Where(e => chosen.Contains(e.CalendarId))];
            return Merge(flat);
        }
    }

    /// <summary>The changed appearances inside their series; one whose series is not there stays alone (as a cita of its own).</summary>
    public static List<CalendarEvent> Merge(IEnumerable<CalendarEvent> flat)
    {
        var list = flat.ToList();
        var children = list.Where(e => e.SeriesId is not null).ToLookup(e => e.SeriesId!);
        var masters = list.Where(e => e.SeriesId is null).ToList();
        var ids = masters.Select(m => m.Id).ToHashSet();
        var result = masters.Select(m => children.Contains(m.Id) ? m with { Overrides = [.. children[m.Id]] } : m).ToList();
        result.AddRange(list.Where(e => e.SeriesId is not null && !ids.Contains(e.SeriesId) && !e.Cancelled)
            .Select(e => e with { RecurrenceId = null, SeriesId = null }));
        return result;
    }

    // ── what each server does ────────────────────────────────────────────

    protected abstract Task<List<CalendarInfo>> FetchCalendarsAsync(CancellationToken cancel);

    /// <summary>Brings one calendar's citas up to date in <see cref="Cache"/> (with its token; a full read when there is none or it expired).</summary>
    protected abstract Task SyncCalendarAsync(CalendarInfo calendar, CancellationToken cancel);

    protected abstract Task<CalendarEvent> CreateAsync(CalendarEvent e, CancellationToken cancel);

    /// <summary>Changes a series or a single cita itself (not its appearances).</summary>
    protected abstract Task<CalendarEvent> PatchAsync(CalendarEvent e, CancellationToken cancel);

    protected abstract Task RemoveAsync(CalendarEvent e, CancellationToken cancel);

    /// <summary>Changes one appearance of a series (<paramref name="changed"/> has its RecurrenceId).</summary>
    protected abstract Task<CalendarEvent> PatchOccurrenceAsync(CalendarEvent series, CalendarEvent changed, CancellationToken cancel);

    /// <summary>Deletes one appearance of a series.</summary>
    protected abstract Task CancelOccurrenceAsync(CalendarEvent series, DateTime recurrenceId, CancellationToken cancel);

    // ── HTTP ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a request with the account's token (a 401 renews it once). 412 → <see cref="CalendarConflictException"/>;
    /// 403 → <see cref="CalendarException"/> (no permission). Other errors are thrown as <see cref="HttpRequestException"/>.
    /// </summary>
    protected async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken cancel, bool allowNotFound = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await Accounts.AccessTokenAsync(Settings.AccountId, cancel).ConfigureAwait(false);
            using var request = make();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                Accounts.Forget(Settings.AccountId);
                continue;
            }
            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)) return response;
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            var status = response.StatusCode;
            response.Dispose();
            if (status == HttpStatusCode.PreconditionFailed) throw new CalendarConflictException(Loc.T("agenda.conflict"), null);
            if (status == HttpStatusCode.Forbidden) throw new CalendarException(Loc.T("calendar.error.forbidden", Name, Short(body)));
            throw new HttpRequestException($"{(int)status} {Short(body)}", null, status);
        }
    }

    private static string Short(string text) => text.Length > 300 ? text[..300] + "…" : text;

    protected static StringContent JsonBody(object node) => new(node is string s ? s : JsonSerializer.Serialize(node), Encoding.UTF8, "application/json");

    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancel).ConfigureAwait(false);
    }

    private static bool IsOffline(Exception ex) =>
        ex is HttpRequestException { StatusCode: null } or TaskCanceledException or IOException;

    // ── ICalendarSource ──────────────────────────────────────────────────

    public async Task<CalendarEvent> AddAsync(CalendarEvent e, CancellationToken cancel = default)
    {
        e = e with { SourceId = Id, Overrides = [] };
        try
        {
            var created = await CreateAsync(e, cancel).ConfigureAwait(false);
            Put(created);
            return created;
        }
        catch (Exception ex) when (IsOffline(ex) && !cancel.IsCancellationRequested)
        {
            var local = e with { Id = "pending-" + Guid.NewGuid().ToString("N")[..12], Uid = string.IsNullOrEmpty(e.Uid) ? CalendarEvent.NewUid() : e.Uid };
            Queue(new PendingChange("add", local));
            Put(local);
            return local;
        }
    }

    public async Task<CalendarEvent> UpdateAsync(CalendarEvent e, CancellationToken cancel = default)
    {
        CalendarEvent? before;
        lock (Gate) before = Merge(Cache.Events.Where(x => x.Id == e.Id || x.SeriesId == e.Id)).FirstOrDefault(x => x.Id == e.Id);
        if (e.Id.StartsWith("pending-", StringComparison.Ordinal))
        {
            // Not in the server yet: the queued «add» takes the new version.
            lock (Gate)
            {
                var index = Cache.Pending.FindIndex(p => p.Op == "add" && p.Event.Id == e.Id);
                if (index >= 0) Cache.Pending[index] = Cache.Pending[index] with { Event = e with { Overrides = [] } };
            }
            Put(e with { Overrides = [] });
            return e;
        }
        try
        {
            var result = await ApplyAsync(e, before, cancel).ConfigureAwait(false);
            return result;
        }
        catch (CalendarConflictException)
        {
            // Read again what the server has now; nothing was overwritten.
            _ = SyncAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (IsOffline(ex) && !cancel.IsCancellationRequested)
        {
            Queue(new PendingChange("update", e, before));
            PutMerged(e);
            return e;
        }
    }

    /// <summary>Sends a change: the series itself when it changed, then each appearance changed or deleted.</summary>
    private async Task<CalendarEvent> ApplyAsync(CalendarEvent e, CalendarEvent? before, CancellationToken cancel)
    {
        var main = e with { Overrides = [] };
        var oldMain = before is null ? null : before with { Overrides = [] };
        var newExceptions = e.Exceptions.Except(before?.Exceptions ?? []).ToList();
        var sameRule = before is not null && before.Recurrence == e.Recurrence && before.Start == e.Start;
        CalendarEvent saved = main;
        if (oldMain is null || !Same(main with { Exceptions = sameRule ? oldMain.Exceptions : main.Exceptions }, oldMain))
        {
            saved = await PatchAsync(main, cancel).ConfigureAwait(false);
            Put(saved);
        }
        if (sameRule)
        {
            foreach (var rid in newExceptions) await CancelOccurrenceAsync(saved, rid, cancel).ConfigureAwait(false);
            foreach (var o in e.Overrides)
            {
                var old = before!.Overrides.FirstOrDefault(x => x.RecurrenceId == o.RecurrenceId);
                if (old is not null && Same(o, old)) continue;
                var changed = await PatchOccurrenceAsync(saved, o, cancel).ConfigureAwait(false);
                Put(changed with { SeriesId = saved.Id });
            }
        }
        if (newExceptions.Count > 0 || e.Overrides.Count > 0)
            lock (Gate)
            {
                var index = Cache.Events.FindIndex(x => x.Id == saved.Id);
                if (index >= 0) Cache.Events[index] = Cache.Events[index] with { Exceptions = [.. Cache.Events[index].Exceptions.Union(newExceptions)] };
            }
        Save();
        RaiseChanged();
        lock (Gate) return Merge(Cache.Events.Where(x => x.Id == saved.Id || x.SeriesId == saved.Id)).FirstOrDefault(x => x.Id == saved.Id) ?? saved;
    }

    /// <summary>Whether two versions say the same (what K-Desktop shows and changes).</summary>
    private static bool Same(CalendarEvent a, CalendarEvent b) =>
        a.Title == b.Title && a.Start == b.Start && a.End == b.End && a.AllDay == b.AllDay && a.Location == b.Location && a.Notes == b.Notes
        && a.Recurrence == b.Recurrence && a.Reminders.SequenceEqual(b.Reminders) && a.Exceptions.SequenceEqual(b.Exceptions)
        && a.CalendarId == b.CalendarId && a.MeetingUrl == b.MeetingUrl && a.Cancelled == b.Cancelled;

    public async Task DeleteAsync(CalendarEvent e, CancellationToken cancel = default)
    {
        if (e.Id.StartsWith("pending-", StringComparison.Ordinal))
        {
            lock (Gate) Cache.Pending.RemoveAll(p => p.Event.Id == e.Id);
            Drop(e.Id);
            return;
        }
        try
        {
            await RemoveAsync(e, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsOffline(ex) && !cancel.IsCancellationRequested)
        {
            Queue(new PendingChange("delete", e));
        }
        Drop(e.Id);
    }

    public async Task SyncAsync(CancellationToken cancel = default)
    {
        if (Account is not { } account || account.Disconnected)
        {
            RaiseChanged();
            return;
        }
        try
        {
            await FlushAsync(cancel).ConfigureAwait(false);
            var all = await FetchCalendarsAsync(cancel).ConfigureAwait(false);
            lock (Gate) Cache.All = all;
            foreach (var calendar in Calendars) await SyncCalendarAsync(calendar, cancel).ConfigureAwait(false);
            lock (Gate) Cache.LastSync = DateTimeOffset.UtcNow;
            _problem = null;
            Save();
        }
        catch (AccountDisconnectedException)
        {
            // Problem says it.
        }
        catch (CalendarException ex)
        {
            _problem = ex.Message;
        }
        catch (Exception ex) when (IsOffline(ex) && !cancel.IsCancellationRequested)
        {
            _problem = Loc.T("calendar.problem.offline");
        }
        catch (HttpRequestException ex)
        {
            _problem = Loc.T("calendar.problem.http", (int?)ex.StatusCode ?? 0);
            Log.Warn($"Calendario: «{Name}»: {ex.Message}");
        }
        RaiseChanged();
    }

    /// <summary>Sends what was changed without connection, in order. A conflict: the server's version wins, and it is logged.</summary>
    private async Task FlushAsync(CancellationToken cancel)
    {
        while (true)
        {
            PendingChange? next;
            lock (Gate) next = Cache.Pending.FirstOrDefault();
            if (next is null) return;
            try
            {
                switch (next.Op)
                {
                    case "add":
                        var created = await CreateAsync(next.Event with { Id = string.Empty }, cancel).ConfigureAwait(false);
                        Drop(next.Event.Id);
                        Put(created);
                        break;
                    case "update":
                        await ApplyAsync(next.Event, next.Before, cancel).ConfigureAwait(false);
                        break;
                    case "delete":
                        await RemoveAsync(next.Event, cancel).ConfigureAwait(false);
                        break;
                }
            }
            catch (CalendarConflictException)
            {
                Log.Warn($"Calendario: «{next.Event.Title}» cambió en el servidor mientras no había conexión; se queda la versión del servidor");
                _problem = Loc.T("calendar.problem.conflict", next.Event.Title);
            }
            catch (CalendarException ex)
            {
                Log.Warn($"Calendario: un cambio sin conexión no se ha podido hacer: {ex.Message}");
            }
            lock (Gate)
            {
                Cache.Pending.Remove(next);
            }
            Save();
        }
    }

    // ── the cache ────────────────────────────────────────────────────────

    private void Queue(PendingChange change)
    {
        lock (Gate) Cache.Pending.Add(change);
        Log.Info($"Calendario: sin conexión; «{change.Event.Title}» se enviará al volver ({change.Op})");
        Save();
    }

    /// <summary>Keeps one cita (or changed appearance) as the server has it.</summary>
    protected void Put(CalendarEvent e)
    {
        lock (Gate)
        {
            Cache.Events.RemoveAll(x => x.Id == e.Id);
            Cache.Events.Add(e with { Overrides = [] });
        }
        Save();
        RaiseChanged();
    }

    /// <summary>Keeps a series with its appearances (a change made without connection).</summary>
    private void PutMerged(CalendarEvent e)
    {
        lock (Gate)
        {
            Cache.Events.RemoveAll(x => x.Id == e.Id || x.SeriesId == e.Id);
            Cache.Events.Add(e with { Overrides = [] });
            Cache.Events.AddRange(e.Overrides.Select(o => o with { SeriesId = e.Id }));
        }
        Save();
        RaiseChanged();
    }

    protected void Drop(string id)
    {
        lock (Gate) Cache.Events.RemoveAll(x => x.Id == id || x.SeriesId == id);
        Save();
        RaiseChanged();
    }

    protected void Save()
    {
        try
        {
            lock (Gate) CalendarJson.Save(_cacheFile, Cache);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Calendario: no se ha podido guardar la copia de «{Name}»: {ex.Message}");
        }
    }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}
