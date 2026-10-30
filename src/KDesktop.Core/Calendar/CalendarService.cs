using KDesktop.Core.Localization;

namespace KDesktop.Core.Calendar;

/// <summary>A reminder that is due: «Dentista dentro de 10 min».</summary>
public sealed record CalendarReminder(CalendarOccurrence Occurrence, int Minutes, CalendarInfo? Calendar);

/// <summary>What a change did, so it can be undone: citas added, removed, and the previous version of those changed.</summary>
public sealed record CalendarChange
{
    public IReadOnlyList<CalendarEvent> Added { get; init; } = [];
    public IReadOnlyList<CalendarEvent> Removed { get; init; } = [];
    public IReadOnlyList<CalendarEvent> Before { get; init; } = [];

    /// <summary>The cita as it is now (the new one, or the changed one).</summary>
    public CalendarEvent? Result { get; init; }
}

/// <summary>
/// The calendar of K-Desktop: the own calendar, the .ics subscriptions and the accounts' calendars, seen as one. It
/// lives in K-Desktop's process, so the reminders and the events work with the assistant off; the assistant and the
/// phone ask it. Reading never waits for the network (each source keeps its citas); it brings the sources up to date in
/// the background and rings the reminders.
/// </summary>
public sealed class CalendarService : IDisposable
{
    public static string DefaultFolder => Path.Combine(AppPaths.DataDir, "Calendar");

    /// <summary>How far ahead the reminders and the events look (the longest reminder: four weeks).</summary>
    private static readonly TimeSpan Ahead = TimeSpan.FromDays(29);

    private static readonly TimeSpan ReminderTick = TimeSpan.FromSeconds(20);

    private readonly string _folder;
    private readonly Func<string, string> _protect;
    private readonly Func<string, string?> _unprotect;
    private readonly TimeProvider _time;
    private readonly HttpClient _http;
    private readonly Lock _gate = new();
    private readonly List<ICalendarSource> _sources = [];
    private readonly Dictionary<string, DateTimeOffset> _syncTried = new(StringComparer.Ordinal);
    private readonly HashSet<string> _syncing = new(StringComparer.Ordinal);
    private ITimer? _reminderTimer, _syncTimer;
    private DateTimeOffset _lastReminderCheck;
    private List<CalendarOccurrence>? _upcoming;
    private DateTimeOffset _upcomingFrom;
    private bool _disposed;

    /// <param name="protect">Encrypts a secret (the address of a subscription): <see cref="Platform.Secrets.Protect"/> in K-Desktop.</param>
    public CalendarService(string folder, Func<string, string> protect, Func<string, string?> unprotect, HttpClient? http = null, TimeProvider? time = null)
    {
        _folder = folder;
        _protect = protect;
        _unprotect = unprotect;
        _time = time ?? TimeProvider.System;
        _http = http ?? CreateHttp();
        Settings = LoadSettings();
        Local = new LocalCalendar(Path.Combine(folder, "local.json"), Loc.T("calendar.defaultName") is var n && n != "calendar.defaultName" ? n : "Personal");
        Add(Local);
        foreach (var s in Settings.Subscriptions) Add(new IcsSubscription(s, CacheFile(s.Id), _http, unprotect));
    }

    public static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"K-Desktop/{AppPaths.FullVersion}");
        return http;
    }

    public LocalCalendar Local { get; }

    public CalendarSettings Settings { get; }

    /// <summary>The citas or the calendars changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>A reminder is due (a timer thread).</summary>
    public event Action<CalendarReminder>? ReminderDue;

    /// <summary>Makes the source of an account (Google) from its settings; set by K-Desktop when the accounts are ready.</summary>
    public Func<CalendarAccountSettings, ICalendarSource?>? AccountSources { get; set; }

    public IReadOnlyList<ICalendarSource> Sources
    {
        get { lock (_gate) return [.. _sources]; }
    }

    public string CacheFile(string sourceId) => Path.Combine(_folder, "Cache", sourceId + ".json");

    private void Add(ICalendarSource source)
    {
        lock (_gate) _sources.Add(source);
        source.Changed += OnSourceChanged;
    }

    private void Remove(string sourceId)
    {
        ICalendarSource? source;
        lock (_gate)
        {
            source = _sources.FirstOrDefault(s => s.Id == sourceId);
            if (source is null) return;
            _sources.Remove(source);
        }
        source.Changed -= OnSourceChanged;
        (source as IDisposable)?.Dispose();
        try { File.Delete(CacheFile(sourceId)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        OnSourceChanged();
    }

    /// <summary>Something around the calendar changed (an account disconnected): whoever shows it looks again.</summary>
    public void NotifyChanged() => OnSourceChanged();

    private void OnSourceChanged()
    {
        lock (_gate) _upcoming = null;
        Changed?.Invoke();
    }

    /// <summary>Starts the reminders and the synchronisation in the background, and the accounts' sources.</summary>
    public void Start()
    {
        if (AccountSources is { } make)
            foreach (var account in Settings.Accounts)
                if (make(account) is { } source) Add(source);
        _lastReminderCheck = _time.GetUtcNow();
        _reminderTimer = _time.CreateTimer(_ => CheckReminders(), null, ReminderTick, ReminderTick);
        _syncTimer = _time.CreateTimer(_ => SyncDue(), null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
    }

    // ── calendars ────────────────────────────────────────────────────────

    /// <summary>Every calendar of every source, with the name, colour and visibility chosen here.</summary>
    public IReadOnlyList<CalendarInfo> Calendars
    {
        get
        {
            var list = new List<CalendarInfo>();
            foreach (var source in Sources)
                foreach (var c in source.Calendars) list.Add(Look(c));
            return list;
        }
    }

    private CalendarInfo Look(CalendarInfo c)
    {
        if (c.SourceId == LocalCalendar.SourceId) return c;
        lock (_gate)
        {
            if (!Settings.Looks.TryGetValue(c.SourceId + "/" + c.Id, out var look)) return c;
            return c with
            {
                Name = string.IsNullOrWhiteSpace(look.Name) ? c.Name : look.Name,
                Color = look.Color ?? c.Color,
                Visible = look.Visible,
                DefaultReminder = look.DefaultReminder ?? c.DefaultReminder,
            };
        }
    }

    public CalendarInfo? Calendar(string sourceId, string calendarId) =>
        Calendars.FirstOrDefault(c => c.SourceId == sourceId && c.Id == calendarId);

    public CalendarInfo? CalendarOf(CalendarEvent e) => Calendar(e.SourceId, e.CalendarId);

    public ICalendarSource? Source(string id) => Sources.FirstOrDefault(s => s.Id == id);

    /// <summary>Where new citas go: the one chosen, if it still exists and can be written; else the own one.</summary>
    public CalendarInfo DefaultCalendar
    {
        get
        {
            var all = Calendars;
            if (Settings.DefaultCalendar?.Split('/', 2) is [var source, var id] &&
                all.FirstOrDefault(c => c.SourceId == source && c.Id == id && !c.ReadOnly) is { } chosen &&
                Source(source) is { } s && s.Capabilities.HasFlag(CalendarCapabilities.Write)) return chosen;
            return all.First(c => c.SourceId == LocalCalendar.SourceId);
        }
    }

    /// <summary>
    /// A calendar by what the user calls it («trabajo», «el de Google»): its name first (no accents, any case, part of
    /// it), then the kind of source. Null when none or several equally fit.
    /// </summary>
    public CalendarInfo? FindCalendar(string? name, bool writable = false)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var all = Calendars.Where(c => !writable || (!c.ReadOnly && Source(c.SourceId)?.Capabilities.HasFlag(CalendarCapabilities.Write) == true)).ToList();
        var wanted = Fold(name);
        var exact = all.Where(c => Fold(c.Name) == wanted).ToList();
        if (exact.Count == 1) return exact[0];
        var words = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToList();
        var partial = all.Where(c => Fold(c.Name) is var n && (n.Contains(wanted, StringComparison.Ordinal) || (words.Count > 0 && words.Any(w => n.Contains(w, StringComparison.Ordinal))))).ToList();
        if (partial.Count == 1) return partial[0];
        var kind = wanted.Contains("google", StringComparison.Ordinal) || wanted.Contains("gmail", StringComparison.Ordinal) ? CalendarSourceKind.Google
            : wanted.Contains("k-desktop", StringComparison.Ordinal) || wanted.Contains("kdesktop", StringComparison.Ordinal) || wanted.Contains("propio", StringComparison.Ordinal) ? CalendarSourceKind.Local
            : (CalendarSourceKind?)null;
        if (kind is { } k)
        {
            var ofKind = all.Where(c => Source(c.SourceId)?.Kind == k).ToList();
            if (ofKind.Count >= 1) return ofKind[0];
        }
        return null;
    }

    public static string Fold(string text) =>
        new string(text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    /// <summary>Changes how a calendar looks here (name, colour, shown, default reminder).</summary>
    public void SetLook(CalendarInfo info)
    {
        if (info.SourceId == LocalCalendar.SourceId)
        {
            Local.UpdateCalendar(info);
            return;
        }
        lock (_gate)
        {
            Settings.Looks[info.SourceId + "/" + info.Id] = new CalendarLook
            {
                Name = info.Name, Color = info.Color, Visible = info.Visible, DefaultReminder = info.DefaultReminder,
            };
            SaveSettings();
        }
        OnSourceChanged();
    }

    // ── reading ──────────────────────────────────────────────────────────

    /// <summary>The appearances of every cita in [<paramref name="from"/>, <paramref name="to"/>), in order. <paramref name="visibleOnly"/>: only the calendars shown in the agenda.</summary>
    public IReadOnlyList<CalendarOccurrence> Occurrences(DateTimeOffset from, DateTimeOffset to, bool visibleOnly = false)
    {
        var calendars = Calendars.ToDictionary(c => c.SourceId + "/" + c.Id);
        var list = new List<CalendarOccurrence>();
        foreach (var source in Sources)
            foreach (var e in source.Events)
            {
                if (visibleOnly && calendars.TryGetValue(e.SourceId + "/" + e.CalendarId, out var c) && !c.Visible) continue;
                list.AddRange(Recurrence.Expand(e, from, to));
            }
        list.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : string.CompareOrdinal(a.Event.Title, b.Event.Title));
        return list;
    }

    /// <summary>The appearances in a span whose title, place or notes have these words (no accents, any case; all of them).</summary>
    public IReadOnlyList<CalendarOccurrence> Search(string? text, DateTimeOffset from, DateTimeOffset to, CalendarInfo? calendar = null)
    {
        var all = Occurrences(from, to).Where(o => calendar is null || (o.Series.SourceId == calendar.SourceId && o.Series.CalendarId == calendar.Id));
        if (string.IsNullOrWhiteSpace(text)) return [.. all];
        var words = Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();
        if (words.Count == 0) return [.. all];
        return [.. all.Where(o =>
        {
            var hay = Fold($"{o.Event.Title} {o.Event.Location} {o.Event.Notes}");
            return words.All(w => hay.Contains(w, StringComparison.Ordinal));
        })];
    }

    /// <summary>A cita (a series or a single one) by its source and id, as it is now.</summary>
    public CalendarEvent? Find(string sourceId, string id) =>
        Source(sourceId)?.Events.FirstOrDefault(e => e.Id == id);

    /// <summary>The next appearances from now on (for the phone, the tray and the events), already worked out and kept for a while.</summary>
    public IReadOnlyList<CalendarOccurrence> Upcoming()
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_upcoming is not null && now - _upcomingFrom < TimeSpan.FromHours(1)) return _upcoming;
        }
        var list = Occurrences(now - TimeSpan.FromDays(1), now + Ahead).ToList();
        lock (_gate)
        {
            _upcoming = list;
            _upcomingFrom = now;
        }
        return list;
    }

    // ── changing ─────────────────────────────────────────────────────────

    private ICalendarSource Writable(string sourceId, string calendarId, bool recurring)
    {
        var source = Source(sourceId) ?? throw new CalendarException(Loc.T("calendar.error.noCalendar"));
        if (!source.Capabilities.HasFlag(CalendarCapabilities.Write) || Calendar(sourceId, calendarId) is { ReadOnly: true })
            throw new CalendarException(Loc.T("calendar.error.readOnly", Calendar(sourceId, calendarId)?.Name ?? source.Name));
        if (recurring && !source.Capabilities.HasFlag(CalendarCapabilities.Recurrence))
            throw new CalendarException(Loc.T("calendar.error.noRecurrence", source.Name));
        return source;
    }

    /// <summary>
    /// Adds a cita to its calendar (<see cref="CalendarEvent.SourceId"/> and <see cref="CalendarEvent.CalendarId"/>;
    /// empty: the default one). Without reminders it gets the calendar's default one.
    /// </summary>
    public async Task<CalendarChange> AddAsync(CalendarEvent e, CancellationToken cancel = default)
    {
        if (string.IsNullOrEmpty(e.SourceId))
        {
            var d = DefaultCalendar;
            e = e with { SourceId = d.SourceId, CalendarId = d.Id };
        }
        var source = Writable(e.SourceId, e.CalendarId, e.IsRecurring);
        if (e.Reminders.Count == 0 && Calendar(e.SourceId, e.CalendarId)?.DefaultReminder is { } reminder) e = e with { Reminders = [reminder] };
        var added = await source.AddAsync(e, cancel).ConfigureAwait(false);
        Log.Info($"Calendario: cita nueva «{added.Title}» el {added.Start:yyyy-MM-dd HH:mm} en {source.Name}");
        return new CalendarChange { Added = [added], Result = added };
    }

    /// <summary>
    /// Changes an appearance (<paramref name="scope"/>: only it, it and the following, or all) or a cita that does not
    /// repeat. <paramref name="changed"/> has the new data of that appearance. Moving it to another calendar of another
    /// source makes a copy there and deletes it here.
    /// </summary>
    public async Task<CalendarChange> ChangeAsync(CalendarOccurrence occurrence, CalendarEvent changed, EditScope scope, CancellationToken cancel = default)
    {
        var series = Find(occurrence.Series.SourceId, occurrence.Series.Id) ?? throw new CalendarException(Loc.T("calendar.error.notFound"));
        var source = Writable(series.SourceId, series.CalendarId, series.IsRecurring);
        var targetSource = string.IsNullOrEmpty(changed.SourceId) ? series.SourceId : changed.SourceId;
        var targetCalendar = string.IsNullOrEmpty(changed.CalendarId) ? series.CalendarId : changed.CalendarId;

        if (targetSource != series.SourceId)
        {
            // Another source: a copy there (the whole series), the old one goes.
            Writable(targetSource, targetCalendar, series.IsRecurring);
            var copy = (series.IsRecurring && occurrence.RecurrenceId is { } r0 ? CalendarEdits.ChangeAll(series, r0, changed) : changed) with
            {
                Id = string.Empty, Uid = CalendarEvent.NewUid(), SourceId = targetSource, CalendarId = targetCalendar, Etag = null,
            };
            var added = await Source(targetSource)!.AddAsync(copy, cancel).ConfigureAwait(false);
            await source.DeleteAsync(series, cancel).ConfigureAwait(false);
            return new CalendarChange { Added = [added], Removed = [series], Result = added };
        }

        changed = changed with { SourceId = series.SourceId, CalendarId = targetCalendar };
        if (!series.IsRecurring || occurrence.RecurrenceId is not { } rid)
        {
            var updated = await source.UpdateAsync(changed with
            {
                Id = series.Id, Uid = series.Uid, Etag = series.Etag, Recurrence = changed.Recurrence,
                Exceptions = series.Exceptions, Overrides = series.Overrides,
            }, cancel).ConfigureAwait(false);
            return new CalendarChange { Before = [series], Result = updated };
        }

        switch (scope)
        {
            case EditScope.This:
                if (!source.Capabilities.HasFlag(CalendarCapabilities.Overrides)) throw new CalendarException(Loc.T("calendar.error.noOverrides", source.Name));
                var withOne = await source.UpdateAsync(CalendarEdits.WithOverride(series, rid, changed with { Recurrence = null }), cancel).ConfigureAwait(false);
                return new CalendarChange { Before = [series], Result = withOne.Overrides.FirstOrDefault(o => o.RecurrenceId == rid) ?? withOne };
            case EditScope.ThisAndFollowing:
                var (before, after) = CalendarEdits.Split(series, rid, changed);
                if (before is null)
                {
                    var whole = await source.UpdateAsync(after, cancel).ConfigureAwait(false);
                    return new CalendarChange { Before = [series], Result = whole };
                }
                await source.UpdateAsync(before, cancel).ConfigureAwait(false);
                var next = await source.AddAsync(after, cancel).ConfigureAwait(false);
                return new CalendarChange { Before = [series], Added = [next], Result = next };
            default:
                var all = await source.UpdateAsync(CalendarEdits.ChangeAll(series, rid, changed), cancel).ConfigureAwait(false);
                return new CalendarChange { Before = [series], Result = all };
        }
    }

    /// <summary>Deletes an appearance (only it, it and the following, or the whole series) or a cita that does not repeat.</summary>
    public async Task<CalendarChange> DeleteAsync(CalendarOccurrence occurrence, EditScope scope, CancellationToken cancel = default)
    {
        var series = Find(occurrence.Series.SourceId, occurrence.Series.Id) ?? throw new CalendarException(Loc.T("calendar.error.notFound"));
        var source = Writable(series.SourceId, series.CalendarId, false);
        if (!series.IsRecurring || occurrence.RecurrenceId is not { } rid || scope == EditScope.All)
        {
            await source.DeleteAsync(series, cancel).ConfigureAwait(false);
            Log.Info($"Calendario: borrada «{series.Title}» de {source.Name}");
            return new CalendarChange { Removed = [series] };
        }
        if (scope == EditScope.This)
        {
            if (!source.Capabilities.HasFlag(CalendarCapabilities.Overrides)) throw new CalendarException(Loc.T("calendar.error.noOverrides", source.Name));
            var without = await source.UpdateAsync(CalendarEdits.WithoutOccurrence(series, rid), cancel).ConfigureAwait(false);
            return new CalendarChange { Before = [series], Result = without };
        }
        var (before, _) = CalendarEdits.Split(series, rid, occurrence.Event);
        if (before is null)
        {
            await source.DeleteAsync(series, cancel).ConfigureAwait(false);
            return new CalendarChange { Removed = [series] };
        }
        var cut = await source.UpdateAsync(before, cancel).ConfigureAwait(false);
        return new CalendarChange { Before = [series], Result = cut };
    }

    /// <summary>
    /// Undoes a change: the added go, the removed come back (with the same UID in the own calendar; in Google
    /// as a copy) and the changed are as they were.
    /// </summary>
    public async Task RevertAsync(CalendarChange change, CancellationToken cancel = default)
    {
        foreach (var e in change.Added)
            if (Find(e.SourceId, e.Id) is { } now && Source(e.SourceId) is { } s) await s.DeleteAsync(now, cancel).ConfigureAwait(false);
        foreach (var e in change.Before)
            if (Source(e.SourceId) is { } s)
            {
                var current = Find(e.SourceId, e.Id);
                if (current is not null) await s.UpdateAsync(e with { Etag = current.Etag }, cancel).ConfigureAwait(false);
                else await s.AddAsync(e with { Etag = null }, cancel).ConfigureAwait(false);
            }
        foreach (var e in change.Removed)
            if (Source(e.SourceId) is { } s) await s.AddAsync(e.SourceId == LocalCalendar.SourceId ? e : e with { Id = string.Empty, Etag = null }, cancel).ConfigureAwait(false);
    }

    // ── .ics ─────────────────────────────────────────────────────────────

    /// <summary>Imports a .ics into an own calendar. Returns how many citas.</summary>
    public int ImportIcs(string text, string calendarId)
    {
        var ics = IcsFormat.Read(text, LocalCalendar.SourceId, calendarId);
        var count = Local.Import(ics.Events, calendarId);
        Log.Info($"Calendario: importadas {count} citas");
        return count;
    }

    /// <summary>A calendar (any) as .ics.</summary>
    public string ExportIcs(string sourceId, string calendarId)
    {
        var info = Calendar(sourceId, calendarId);
        var events = Source(sourceId)?.Events.Where(e => e.CalendarId == calendarId) ?? [];
        return IcsFormat.Write(events, info?.Name, info?.Color);
    }

    // ── subscriptions ────────────────────────────────────────────────────

    public IcsSubscription AddSubscription(string name, string url, string? color = null)
    {
        var settings = new IcsSubscriptionSettings
        {
            Name = name.Trim(),
            UrlProtected = _protect(IcsSubscription.Normalize(url)),
            Color = color ?? CalendarColors.Palette[(Settings.Subscriptions.Count + 1) % CalendarColors.Palette.Count],
        };
        lock (_gate)
        {
            Settings.Subscriptions.Add(settings);
            SaveSettings();
        }
        var source = new IcsSubscription(settings, CacheFile(settings.Id), _http, _unprotect);
        Add(source);
        _ = SyncAsync(source);
        OnSourceChanged();
        return source;
    }

    public void UpdateSubscription(IcsSubscriptionSettings changed, string? newUrl = null)
    {
        lock (_gate)
        {
            var s = Settings.Subscriptions.FirstOrDefault(x => x.Id == changed.Id);
            if (s is null) return;
            s.Name = changed.Name.Trim();
            s.Color = changed.Color;
            s.RefreshMinutes = Math.Max(5, changed.RefreshMinutes);
            if (!string.IsNullOrWhiteSpace(newUrl)) s.UrlProtected = _protect(IcsSubscription.Normalize(newUrl));
            SaveSettings();
        }
        if (!string.IsNullOrWhiteSpace(newUrl) && Source(changed.Id) is IcsSubscription sub) _ = SyncAsync(sub);
        OnSourceChanged();
    }

    public void RemoveSubscription(string id)
    {
        lock (_gate)
        {
            Settings.Subscriptions.RemoveAll(s => s.Id == id);
            foreach (var key in Settings.Looks.Keys.Where(k => k.StartsWith(id + "/", StringComparison.Ordinal)).ToList()) Settings.Looks.Remove(key);
            SaveSettings();
        }
        Remove(id);
    }

    // ── accounts ─────────────────────────────────────────────────────────

    /// <summary>Shows the calendars of an account (Opciones › Calendarios); <paramref name="calendars"/> empty: its main one.</summary>
    public void UseAccount(string accountId, IEnumerable<string> calendars)
    {
        var list = calendars.ToList();
        lock (_gate)
        {
            var existing = Settings.Accounts.FirstOrDefault(a => a.AccountId == accountId);
            if (existing is null) Settings.Accounts.Add(new CalendarAccountSettings { AccountId = accountId, Calendars = list });
            else existing.Calendars = list;
            SaveSettings();
        }
        var sourceId = AccountSourceId(accountId);
        Remove(sourceId);
        if (AccountSources?.Invoke(Settings.Accounts.First(a => a.AccountId == accountId)) is { } source)
        {
            Add(source);
            _ = SyncAsync(source);
        }
        OnSourceChanged();
    }

    /// <summary>Stops showing the calendars of an account (its cache goes; nothing is deleted in the server).</summary>
    public void ForgetAccount(string accountId)
    {
        var sourceId = AccountSourceId(accountId);
        lock (_gate)
        {
            Settings.Accounts.RemoveAll(a => a.AccountId == accountId);
            foreach (var key in Settings.Looks.Keys.Where(k => k.StartsWith(sourceId + "/", StringComparison.Ordinal)).ToList()) Settings.Looks.Remove(key);
            if (Settings.DefaultCalendar?.StartsWith(sourceId + "/", StringComparison.Ordinal) == true) Settings.DefaultCalendar = null;
            SaveSettings();
        }
        Remove(sourceId);
    }

    public static string AccountSourceId(string accountId) => "acc-" + accountId;

    // ── synchronisation ──────────────────────────────────────────────────

    /// <summary>Brings every source up to date now («Actualizar»).</summary>
    public Task SyncAllAsync(CancellationToken cancel = default) =>
        Task.WhenAll(Sources.Where(s => s.Kind != CalendarSourceKind.Local).Select(s => SyncAsync(s, cancel)));

    private static TimeSpan SyncEvery(ICalendarSource source) => source switch
    {
        IcsSubscription s => TimeSpan.FromMinutes(Math.Max(5, s.Settings.RefreshMinutes)),
        _ => TimeSpan.FromMinutes(5),
    };

    private void SyncDue()
    {
        if (_disposed) return;
        var now = _time.GetUtcNow();
        foreach (var source in Sources)
        {
            if (source.Kind == CalendarSourceKind.Local) continue;
            DateTimeOffset tried;
            lock (_gate) tried = _syncTried.GetValueOrDefault(source.Id);
            var last = source.LastSync is { } l && l > tried ? l : tried;
            if (now - last >= SyncEvery(source)) _ = SyncAsync(source);
        }
    }

    private async Task SyncAsync(ICalendarSource source, CancellationToken cancel = default)
    {
        lock (_gate)
        {
            if (!_syncing.Add(source.Id)) return;
            _syncTried[source.Id] = _time.GetUtcNow();
        }
        try
        {
            await source.SyncAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            Log.Warn($"Calendario: no se ha podido actualizar «{source.Name}»: {ex.Message}");
        }
        finally
        {
            lock (_gate) _syncing.Remove(source.Id);
        }
    }

    // ── reminders ────────────────────────────────────────────────────────

    /// <summary>
    /// Rings the reminders whose moment came since the last look. After a sleep, one whose cita has not ended yet still
    /// rings (late, but useful); one of a cita already over does not.
    /// </summary>
    internal void CheckReminders()
    {
        if (_disposed) return;
        var now = _time.GetUtcNow();
        var since = _lastReminderCheck;
        _lastReminderCheck = now;
        if (since == default) return;
        var due = new List<CalendarReminder>();
        foreach (var o in Upcoming())
        {
            var source = Source(o.Series.SourceId);
            if (source is null || (source.Kind is CalendarSourceKind.Google && !Settings.AccountReminders)) continue;
            foreach (var minutes in o.Event.Reminders)
            {
                var at = o.Start - TimeSpan.FromMinutes(minutes);
                if (at > since && at <= now && now < (o.End > o.Start ? o.End : o.Start + TimeSpan.FromMinutes(5)))
                    due.Add(new CalendarReminder(o, minutes, CalendarOf(o.Series)));
            }
        }
        foreach (var r in due)
        {
            Log.Info($"Calendario: aviso de «{r.Occurrence.Event.Title}» ({r.Minutes} min antes)");
            try { ReminderDue?.Invoke(r); }
            catch (Exception ex) { Log.Error("Calendario: error al avisar", ex); }
        }
    }

    // ── settings ─────────────────────────────────────────────────────────

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private CalendarSettings LoadSettings()
    {
        try
        {
            return CalendarJson.Load<CalendarSettings>(SettingsFile) ?? new CalendarSettings();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Calendario: no se han podido leer las opciones: {ex.Message}");
            return new CalendarSettings();
        }
    }

    /// <summary>Writes the options (after changing <see cref="Settings"/> by hand).</summary>
    public void SaveSettings()
    {
        try
        {
            lock (_gate) CalendarJson.Save(SettingsFile, Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Calendario: no se han podido guardar las opciones: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _reminderTimer?.Dispose();
        _syncTimer?.Dispose();
        foreach (var s in Sources.OfType<IDisposable>()) s.Dispose();
    }
}
