namespace KDesktop.Core.Calendar;

/// <summary>
/// The own calendar: no account, everything in one file of the data folder (Calendar\local.json, in the backups). It
/// can hold several calendars with a name and a colour («Personal», «Trabajo»…); the first time it has «Personal».
/// Thread-safe; written through a temporary file.
/// </summary>
public sealed class LocalCalendar : ICalendarSource
{
    public const string SourceId = "local";

    private sealed class Data
    {
        public List<CalendarInfo> Calendars { get; set; } = [];
        public List<CalendarEvent> Events { get; set; } = [];
    }

    private readonly string _file;
    private readonly Lock _gate = new();
    private Data? _data;

    /// <param name="defaultName">The name of the first calendar («Personal», in the user's language).</param>
    public LocalCalendar(string file, string defaultName = "Personal")
    {
        _file = file;
        DefaultName = defaultName;
    }

    private string DefaultName { get; }

    public string Id => SourceId;

    public string Name => "K-Desktop";

    public CalendarSourceKind Kind => CalendarSourceKind.Local;

    public CalendarCapabilities Capabilities => CalendarCapabilities.Write | CalendarCapabilities.Recurrence | CalendarCapabilities.Overrides | CalendarCapabilities.Colors | CalendarCapabilities.ManageCalendars;

    public string? Problem { get; private set; }

    public DateTimeOffset? LastSync => null;

    public event Action? Changed;

    public IReadOnlyList<CalendarInfo> Calendars
    {
        get { lock (_gate) return [.. Load().Calendars]; }
    }

    public IReadOnlyList<CalendarEvent> Events
    {
        get { lock (_gate) return [.. Load().Events]; }
    }

    public Task<CalendarEvent> AddAsync(CalendarEvent e, CancellationToken cancel = default)
    {
        lock (_gate)
        {
            var data = Load();
            var uid = string.IsNullOrWhiteSpace(e.Uid) ? CalendarEvent.NewUid() : e.Uid;
            // The same UID again (an undo of a delete, a .ics imported twice) replaces the one there.
            data.Events.RemoveAll(x => x.Uid == uid);
            var calendar = data.Calendars.Any(c => c.Id == e.CalendarId) ? e.CalendarId : data.Calendars[0].Id;
            e = Normalize(e with { Id = uid, Uid = uid, SourceId = SourceId, CalendarId = calendar, Updated = DateTimeOffset.UtcNow, Etag = null });
            data.Events.Add(e);
            Save(data);
        }
        Changed?.Invoke();
        return Task.FromResult(e);
    }

    public Task<CalendarEvent> UpdateAsync(CalendarEvent e, CancellationToken cancel = default)
    {
        lock (_gate)
        {
            var data = Load();
            var index = data.Events.FindIndex(x => x.Id == e.Id);
            if (index < 0) throw new CalendarException(Localization.Loc.T("calendar.error.notFound"));
            var calendar = data.Calendars.Any(c => c.Id == e.CalendarId) ? e.CalendarId : data.Events[index].CalendarId;
            e = Normalize(e with { Uid = data.Events[index].Uid, SourceId = SourceId, CalendarId = calendar, Updated = DateTimeOffset.UtcNow });
            data.Events[index] = e;
            Save(data);
        }
        Changed?.Invoke();
        return Task.FromResult(e);
    }

    public Task DeleteAsync(CalendarEvent e, CancellationToken cancel = default)
    {
        lock (_gate)
        {
            var data = Load();
            if (data.Events.RemoveAll(x => x.Id == e.Id) == 0) return Task.CompletedTask;
            Save(data);
        }
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task SyncAsync(CancellationToken cancel = default) => Task.CompletedTask;

    // ── its calendars ────────────────────────────────────────────────────

    public CalendarInfo AddCalendar(string name, string? color = null)
    {
        CalendarInfo info;
        lock (_gate)
        {
            var data = Load();
            info = new CalendarInfo
            {
                Id = Guid.NewGuid().ToString("N")[..10],
                SourceId = SourceId,
                Name = name.Trim(),
                Color = color ?? CalendarColors.Palette[data.Calendars.Count % CalendarColors.Palette.Count],
            };
            data.Calendars.Add(info);
            Save(data);
        }
        Changed?.Invoke();
        return info;
    }

    /// <summary>Changes the name, colour, visibility or default reminder of one.</summary>
    public void UpdateCalendar(CalendarInfo info)
    {
        lock (_gate)
        {
            var data = Load();
            var index = data.Calendars.FindIndex(c => c.Id == info.Id);
            if (index < 0) return;
            data.Calendars[index] = info with { SourceId = SourceId, ReadOnly = false, Name = info.Name.Trim().Length == 0 ? data.Calendars[index].Name : info.Name.Trim() };
            Save(data);
        }
        Changed?.Invoke();
    }

    /// <summary>Removes a calendar and its citas (never the last one). Returns the citas it had, for an undo.</summary>
    public IReadOnlyList<CalendarEvent> RemoveCalendar(string id)
    {
        List<CalendarEvent> removed;
        lock (_gate)
        {
            var data = Load();
            if (data.Calendars.Count <= 1 || data.Calendars.RemoveAll(c => c.Id == id) == 0) return [];
            removed = data.Events.Where(e => e.CalendarId == id).ToList();
            data.Events.RemoveAll(e => e.CalendarId == id);
            Save(data);
        }
        Changed?.Invoke();
        return removed;
    }

    /// <summary>Adds many at once (a .ics imported): the same UID replaces the one there. Returns how many.</summary>
    public int Import(IEnumerable<CalendarEvent> events, string calendarId)
    {
        var count = 0;
        lock (_gate)
        {
            var data = Load();
            if (data.Calendars.All(c => c.Id != calendarId)) calendarId = data.Calendars[0].Id;
            foreach (var e in events)
            {
                var uid = string.IsNullOrWhiteSpace(e.Uid) ? CalendarEvent.NewUid() : e.Uid;
                data.Events.RemoveAll(x => x.Uid == uid);
                data.Events.Add(Normalize(e with
                {
                    Id = uid, Uid = uid, SourceId = SourceId, CalendarId = calendarId, Etag = null,
                    Updated = e.Updated == default ? DateTimeOffset.UtcNow : e.Updated,
                }));
                count++;
            }
            Save(data);
        }
        Changed?.Invoke();
        return count;
    }

    /// <summary>Overrides carry the series' source, calendar and UID.</summary>
    private static CalendarEvent Normalize(CalendarEvent e) => e with
    {
        Overrides = [.. e.Overrides.Select(o => o with
        {
            Uid = e.Uid, SourceId = SourceId, CalendarId = e.CalendarId,
            Id = string.IsNullOrEmpty(o.Id) || o.Id == e.Id ? e.Uid + "_" + IcsTime.Format(o.RecurrenceId ?? o.Start, false) : o.Id,
        })],
    };

    private Data Load()
    {
        if (_data is not null) return _data;
        try
        {
            _data = CalendarJson.Load<Data>(_file) ?? new Data();
            Problem = null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            Log.Warn($"Calendario: no se ha podido leer {_file}: {ex.Message}");
            Problem = ex.Message;
            // Never overwrite a file that could not be read: keep it aside.
            try { if (File.Exists(_file)) File.Copy(_file, _file + ".damaged", overwrite: true); }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { }
            _data = new Data();
        }
        if (_data.Calendars.Count == 0)
            _data.Calendars.Add(new CalendarInfo { Id = "personal", SourceId = SourceId, Name = DefaultName, Color = CalendarColors.Default });
        return _data;
    }

    private void Save(Data data)
    {
        try
        {
            CalendarJson.Save(_file, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Calendario: no se ha podido guardar {_file}: {ex.Message}");
            Problem = ex.Message;
        }
    }
}
