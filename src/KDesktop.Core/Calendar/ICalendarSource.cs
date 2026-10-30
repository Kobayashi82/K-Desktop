using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KDesktop.Core.Calendar;

public enum CalendarSourceKind { Local, Subscription, Google }

/// <summary>
/// Where citas come from: the own calendar, a .ics subscription, a Google account. Each keeps all its
/// citas in memory (and in a file of its own), so reading never waits for the network; changes go to the source.
/// </summary>
public interface ICalendarSource
{
    /// <summary>Stable id («local», «ics-…», «google-…»).</summary>
    string Id { get; }

    /// <summary>What the user sees in the options («K-Desktop», «Festivos», «Google · ana@gmail.com»).</summary>
    string Name { get; }

    CalendarSourceKind Kind { get; }

    CalendarCapabilities Capabilities { get; }

    IReadOnlyList<CalendarInfo> Calendars { get; }

    /// <summary>All its citas (the series not expanded), as last known.</summary>
    IReadOnlyList<CalendarEvent> Events { get; }

    /// <summary>The last problem with it, in the user's words («Sin conexión», «Desconectada — volver a conectar»); null: fine.</summary>
    string? Problem { get; }

    /// <summary>When it was last brought up to date from its server (null: never, or it has none).</summary>
    DateTimeOffset? LastSync { get; }

    /// <summary>Adds a cita; returns it as the source keeps it (with its id and etag).</summary>
    Task<CalendarEvent> AddAsync(CalendarEvent e, CancellationToken cancel = default);

    /// <summary>Replaces a cita (the same <see cref="CalendarEvent.Id"/>); returns it as kept.</summary>
    Task<CalendarEvent> UpdateAsync(CalendarEvent e, CancellationToken cancel = default);

    Task DeleteAsync(CalendarEvent e, CancellationToken cancel = default);

    /// <summary>Brings it up to date with its server and sends what was changed without connection. Nothing to do for the own one.</summary>
    Task SyncAsync(CancellationToken cancel = default);

    /// <summary>Its citas or calendars changed (any thread).</summary>
    event Action? Changed;
}

/// <summary>A source cannot do what was asked (read only, no series…): the message says it to the user.</summary>
public sealed class CalendarException(string message) : Exception(message);

/// <summary>Someone else changed the cita in its server meanwhile (HTTP 412): it was read again and nothing was overwritten.</summary>
public sealed class CalendarConflictException(string message, CalendarEvent? current) : Exception(message)
{
    public CalendarEvent? Current { get; } = current;
}

/// <summary>JSON for the calendar's files (its own, so the calendar does not depend on the rest of the configuration).</summary>
public static class CalendarJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new WallTimeConverter() },
    };

    /// <summary>Writes <paramref name="value"/> to <paramref name="file"/> through a temporary file, so a crash never leaves it half written.</summary>
    public static void Save<T>(string file, T value, JsonSerializerOptions? options = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, options ?? Options));
        File.Move(tmp, file, overwrite: true);
    }

    public static T? Load<T>(string file, JsonSerializerOptions? options = null) where T : class
    {
        if (!File.Exists(file)) return null;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(file), options ?? Options);
    }

    /// <summary>Wall-clock times are written without zone («2026-10-30T09:00:00»), and read back the same.</summary>
    private sealed class WallTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.SpecifyKind(DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTimeKind.Unspecified);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
    }
}
