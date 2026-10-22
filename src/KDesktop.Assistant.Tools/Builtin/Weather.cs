using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;
using Windows.Devices.Geolocation;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>A place on the map, as the user said it and as the geocoder named it.</summary>
public sealed record Place(string Name, double Latitude, double Longitude, string? Region = null, string? Country = null)
{
    /// <summary>«Madrid (Comunidad de Madrid, España)».</summary>
    public string Full => Region is null && Country is null ? Name : $"{Name} ({string.Join(", ", new[] { Region, Country }.OfType<string>().Distinct())})";
}

/// <summary>One day of the forecast, in the place's own time.</summary>
public sealed record WeatherDay(DateOnly Date, int Code, double Min, double Max, int? RainChance, double Rain, double Wind, string? Sunrise, string? Sunset);

/// <summary>One hour of the forecast.</summary>
public sealed record WeatherHour(DateTime At, double Temperature, int? RainChance, int Code);

/// <summary>The weather now (in the place's time) and the next days.</summary>
public sealed record Forecast(DateTime LocalNow, double Temperature, double FeelsLike, int Humidity, double Wind, double Rain, int Code,
    IReadOnlyList<WeatherDay> Days, IReadOnlyList<WeatherHour> Hours);

/// <summary>
/// Open-Meteo (stage 10.3, <c>tiempo</c>): free, without a key, every place on Earth. Its geocoder turns a name into a
/// place (in the user's language) and its forecast gives now, every hour and 7 days, in °C and km/h, in the place's time.
/// Places are kept for the session; forecasts for 10 minutes.
/// </summary>
public sealed class OpenMeteo
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly Dictionary<string, Place?> _places = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTime At, Forecast Forecast)> _forecasts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public OpenMeteo(HttpMessageHandler? network = null)
    {
        _http = network is null
            ? new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
            : new HttpClient(network, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("K-Desktop");
    }

    /// <summary>
    /// The place called <paramref name="name"/> («Madrid», «Valencia, Venezuela»: what goes after a comma picks among the
    /// places with that name); null when there is none.
    /// </summary>
    public async Task<Place?> FindAsync(string name, string language, CancellationToken cancellationToken)
    {
        var key = language + "|" + name.Trim().ToLowerInvariant();
        lock (_gate)
            if (_places.TryGetValue(key, out var known)) return known;
        var parts = name.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(parts[0])}&count=10&language={Uri.EscapeDataString(language)}&format=json";
        using var document = JsonDocument.Parse(await _http.GetStringAsync(new Uri(url), cancellationToken).ConfigureAwait(false));
        var found = new List<Place>();
        if (document.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            foreach (var r in results.EnumerateArray())
                found.Add(new Place(r.GetProperty("name").GetString() ?? parts[0], r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(),
                    Text(r, "admin1"), Text(r, "country")));
        var place = found.FirstOrDefault();
        if (parts.Length > 1)
        {
            var wanted = string.Join(' ', TextSimilarity.Words(parts[1]));
            place = found.FirstOrDefault(p => new[] { p.Region, p.Country }.OfType<string>().Any(n => string.Join(' ', TextSimilarity.Words(n)).Contains(wanted, StringComparison.Ordinal)))
                    ?? place;
        }
        lock (_gate) _places[key] = place;
        return place;
    }

    public async Task<Forecast> ForecastAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{latitude:0.###},{longitude:0.###}");
        lock (_gate)
            if (_forecasts.TryGetValue(key, out var kept) && DateTime.UtcNow - kept.At < Fresh) return kept.Forecast;
        var url = "https://api.open-meteo.com/v1/forecast?"
            + string.Create(CultureInfo.InvariantCulture, $"latitude={latitude:0.####}&longitude={longitude:0.####}")
            + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m"
            + "&hourly=temperature_2m,precipitation_probability,weather_code"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,wind_speed_10m_max,sunrise,sunset"
            + "&timezone=auto&forecast_days=7";
        using var document = JsonDocument.Parse(await _http.GetStringAsync(new Uri(url), cancellationToken).ConfigureAwait(false));
        var forecast = Read(document.RootElement);
        lock (_gate) _forecasts[key] = (DateTime.UtcNow, forecast);
        return forecast;
    }

    /// <summary>Open-Meteo's answer as a <see cref="Forecast"/> (public for the tests).</summary>
    public static Forecast Read(JsonElement root)
    {
        var current = root.GetProperty("current");
        var offset = root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0;
        var localNow = DateTime.UtcNow.AddSeconds(offset);

        var daily = root.GetProperty("daily");
        var dates = daily.GetProperty("time").EnumerateArray().Select(d => DateOnly.Parse(d.GetString()!, CultureInfo.InvariantCulture)).ToList();
        var days = new List<WeatherDay>();
        for (var i = 0; i < dates.Count; i++)
            days.Add(new WeatherDay(dates[i], (int)(At(daily, "weather_code", i) ?? 0), At(daily, "temperature_2m_min", i) ?? 0, At(daily, "temperature_2m_max", i) ?? 0,
                At(daily, "precipitation_probability_max", i) is { } chance ? (int)Math.Round(chance) : null, At(daily, "precipitation_sum", i) ?? 0,
                At(daily, "wind_speed_10m_max", i) ?? 0, Clock(daily, "sunrise", i), Clock(daily, "sunset", i)));

        var hourly = root.GetProperty("hourly");
        var hours = new List<WeatherHour>();
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => t.GetString()!).ToList();
        for (var i = 0; i < times.Count; i++)
            if (DateTime.TryParse(times[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                hours.Add(new WeatherHour(at, At(hourly, "temperature_2m", i) ?? 0,
                    At(hourly, "precipitation_probability", i) is { } chance ? (int)Math.Round(chance) : null, (int)(At(hourly, "weather_code", i) ?? 0)));

        return new Forecast(localNow, Number(current, "temperature_2m"), Number(current, "apparent_temperature"), (int)Math.Round(Number(current, "relative_humidity_2m")),
            Number(current, "wind_speed_10m"), Number(current, "precipitation"), (int)Number(current, "weather_code"), days, hours);
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static double Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static double? At(JsonElement block, string name, int i) =>
        block.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array && i < list.GetArrayLength() && list[i].ValueKind == JsonValueKind.Number
            ? list[i].GetDouble()
            : null;

    /// <summary>«2026-10-22T08:21» → «08:21».</summary>
    private static string? Clock(JsonElement block, string name, int i) =>
        block.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array && i < list.GetArrayLength() && list[i].GetString() is { } text && text.IndexOf('T') is var t and > 0
            ? text[(t + 1)..]
            : null;
}

/// <summary>
/// «¿Qué tiempo hace?», «¿va a llover mañana en Bilbao?», «¿qué tiempo hará esta semana?» (stage 10.3, <c>tiempo</c>):
/// the forecast of Open-Meteo for the place said or, without one, where the user is: the place of Options (Assistant
/// module → General) or, when empty, Windows' location if the user allows it.
/// </summary>
public sealed class WeatherTool(Func<string?> home, OpenMeteo? meteo = null, Func<CancellationToken, Task<(double Latitude, double Longitude)?>>? locate = null) : ITool
{
    private static readonly string[] Whens = ["now", "today", "tomorrow", "week"];

    private readonly OpenMeteo _meteo = meteo ?? new OpenMeteo();

    public string Name => "weather";

    public string Description => "The weather now and the forecast (every hour for two days, each day for a week) of a place, or of where the user is when no place is said: temperature, sky, chance of rain, wind, sunrise and sunset. Use it for anything about the weather, rain, cold or heat.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"place":{"type":"string","description":"The city or town, as the user said it (\"Bilbao\", \"Valencia, Venezuela\"); leave it out for where the user is"},"when":{"type":"string","enum":["now","today","tomorrow","week"],"description":"now (default), today, tomorrow, or the next 7 days"},"date":{"type":"string","description":"A day of the next week, YYYY-MM-DD, instead of when"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => arguments.GetString("place") is { Length: > 0 } place
        ? Loc.T("tool.weather.intentIn", ToolText.Clean(place))
        : Loc.T("tool.weather.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var language = Loc.Instance.Language;
        var said = arguments.GetString("place")?.Trim();
        Place? place;
        try
        {
            place = await PlaceAsync(said, language, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Warn($"Asistente: el tiempo no ha podido buscar «{said}»: {ex.Message}");
            return new ToolResult("The weather service could not be reached (no internet?). Say so.") { Failed = true, Reply = Loc.T("tool.weather.offline") };
        }
        if (place is null)
            return string.IsNullOrEmpty(said)
                ? new ToolResult("Where the user is is not known: no place in Options and Windows' location is off. Ask them which city, and tell them they can write it in Options → Assistant module → General.")
                {
                    Failed = true, Reply = Loc.T("tool.weather.noHome"),
                }
                : new ToolResult($"No place called «{said}» was found. Ask the user to say it another way (with its country).") { Failed = true, Reply = Loc.T("tool.weather.notFound", said) };

        Forecast forecast;
        try
        {
            forecast = await _meteo.ForecastAsync(place.Latitude, place.Longitude, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Log.Warn($"Asistente: el tiempo de {place.Name} no ha llegado: {ex.Message}");
            return new ToolResult("The weather service could not be reached (no internet?). Say so.") { Failed = true, Reply = Loc.T("tool.weather.offline") };
        }

        var when = arguments.GetString("when")?.Trim().ToLowerInvariant() is { } w && Whens.Contains(w) ? w : "now";
        WeatherDay? day = null;
        if (arguments.GetString("date") is { Length: > 0 } date && DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var asked))
        {
            day = forecast.Days.FirstOrDefault(d => d.Date == asked);
            if (day is null) return new ToolResult(string.Create(CultureInfo.InvariantCulture, $"There is only a forecast from {forecast.Days[0].Date:yyyy-MM-dd} to {forecast.Days[^1].Date:yyyy-MM-dd}. Say so.")) { Failed = true };
            when = "day";
        }
        else if (when == "tomorrow" && forecast.Days.Count > 1) day = forecast.Days[1];

        return new ToolResult(Describe(place, forecast, when, day))
        {
            Done = Loc.T("tool.weather.done", place.Name),
            Reply = Say(place.Name, forecast, when, day),
        };
    }

    /// <summary>The place said; without one, the one of Options; without it, Windows' location.</summary>
    private async Task<Place?> PlaceAsync(string? said, string language, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(said)) return await _meteo.FindAsync(said, language, cancellationToken).ConfigureAwait(false);
        if (home()?.Trim() is { Length: > 0 } mine) return await _meteo.FindAsync(mine, language, cancellationToken).ConfigureAwait(false);
        return await (locate ?? WindowsLocationAsync)(cancellationToken).ConfigureAwait(false) is { } here
            ? new Place(Loc.T("tool.weather.here"), here.Latitude, here.Longitude)
            : null;
    }

    /// <summary>Where Windows says the PC is, when the user lets programs know it (Privacy → Location); null otherwise.</summary>
    public static async Task<(double Latitude, double Longitude)?> WindowsLocationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            if (access != GeolocationAccessStatus.Allowed) return null;
            var position = await new Geolocator().GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(4)).AsTask(cancellationToken).ConfigureAwait(false);
            return (position.Coordinate.Point.Position.Latitude, position.Coordinate.Point.Position.Longitude);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or TimeoutException or InvalidOperationException)
        {
            Log.Info($"Asistente: sin la ubicación de Windows ({ex.Message})");
            return null;
        }
    }

    /// <summary>For the model, in English, with what it may be asked about the time asked for.</summary>
    public static string Describe(Place place, Forecast forecast, string when, WeatherDay? day)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Weather in {place.Full}, local time there {forecast.LocalNow:yyyy-MM-dd ddd HH:mm} (°C, km/h):\n");
        if (when is "now" or "today")
        {
            text.Append(CultureInfo.InvariantCulture,
                $"Now: {forecast.Temperature:0} °C (feels like {forecast.FeelsLike:0}), {Sky(forecast.Code)}, humidity {forecast.Humidity} %, wind {forecast.Wind:0} km/h{(forecast.Rain > 0 ? $", {forecast.Rain:0.#} mm of rain this hour" : "")}.\n");
            if (forecast.Days.Count > 0) text.Append(Line("Today", forecast.Days[0])).Append('\n');
            text.Append("Next hours: ").Append(Hours(forecast, forecast.LocalNow, 24)).Append('\n');
        }
        else if (when == "week")
        {
            foreach (var d in forecast.Days) text.Append(Line(d.Date.ToString("ddd", CultureInfo.InvariantCulture), d)).Append('\n');
        }
        else if (day is not null)
        {
            text.Append(Line(when == "tomorrow" ? "Tomorrow" : "That day", day)).Append('\n');
            text.Append("By hours: ").Append(Hours(forecast, day.Date.ToDateTime(new TimeOnly(6, 0)), 18)).Append('\n');
        }
        return text.Append("Answer briefly in the user's language, with words rather than lists.").ToString();
    }

    private static string Line(string name, WeatherDay d) => string.Create(CultureInfo.InvariantCulture,
        $"{name} {d.Date:yyyy-MM-dd}: {d.Min:0} to {d.Max:0} °C, {Sky(d.Code)}, rain chance {d.RainChance?.ToString(CultureInfo.InvariantCulture) ?? "?"} % ({d.Rain:0.#} mm), wind up to {d.Wind:0} km/h, sunrise {d.Sunrise}, sunset {d.Sunset}.");

    /// <summary>Every 3 hours from <paramref name="from"/>: «15:00 21 °C 10 % partly cloudy».</summary>
    private static string Hours(Forecast forecast, DateTime from, int span) =>
        string.Join("; ", forecast.Hours.Where(h => h.At >= from.AddMinutes(-59) && h.At <= from.AddHours(span) && h.At.Hour % 3 == 0)
            .Select(h => string.Create(CultureInfo.InvariantCulture, $"{h.At:HH:mm} {h.Temperature:0} °C {h.RainChance?.ToString(CultureInfo.InvariantCulture) ?? "?"} % {Sky(h.Code)}")));

    /// <summary>What the assistant says without the AI, in the user's language.</summary>
    public static string Say(string place, Forecast forecast, string when, WeatherDay? day)
    {
        var culture = Loc.Instance.Culture;
        string Degrees(double value) => Math.Round(value).ToString("0", culture);
        string Rain(WeatherDay d) => d.RainChance is >= 20 and var chance ? Loc.T("tool.weather.sayRain", chance) : string.Empty;
        switch (when)
        {
            case "week":
                var days = forecast.Days.Select(d => Loc.T("tool.weather.sayWeekDay", culture.DateTimeFormat.GetDayName(d.Date.DayOfWeek), Degrees(d.Min), Degrees(d.Max), SkyFor(d.Code)));
                return Loc.T("tool.weather.sayWeek", place, string.Join("; ", days));
            case "tomorrow" or "day" when day is not null:
                var name = when == "tomorrow" ? Loc.T("tool.weather.tomorrow") : culture.DateTimeFormat.GetDayName(day.Date.DayOfWeek);
                return Loc.T("tool.weather.sayDay", Capital(name, culture), place, Degrees(day.Min), Degrees(day.Max), SkyFor(day.Code), Rain(day));
            default:
                var today = forecast.Days.Count > 0 ? forecast.Days[0] : null;
                var now = Loc.T("tool.weather.sayNow", place, Degrees(forecast.Temperature), SkyFor(forecast.Code));
                return today is null ? now : now + " " + Loc.T("tool.weather.sayToday", Degrees(today.Min), Degrees(today.Max), Rain(today));
        }
    }

    private static string Capital(string text, CultureInfo culture) => text.Length > 0 ? char.ToUpper(text[0], culture) + text[1..] : text;

    /// <summary>The WMO code of the sky, in groups (Open-Meteo's table).</summary>
    public static string SkyGroup(int code) => code switch
    {
        0 => "clear",
        1 => "mainlyClear",
        2 => "partlyCloudy",
        3 => "overcast",
        45 or 48 => "fog",
        51 or 53 or 55 => "drizzle",
        56 or 57 or 66 or 67 => "freezingRain",
        61 => "lightRain",
        63 => "rain",
        65 => "heavyRain",
        71 => "lightSnow",
        73 or 77 => "snow",
        75 => "heavySnow",
        80 or 81 => "showers",
        82 => "heavyShowers",
        85 or 86 => "snowShowers",
        95 => "thunderstorm",
        96 or 99 => "hail",
        _ => "unknown",
    };

    private static string Sky(int code) => SkyGroup(code) switch
    {
        "clear" => "clear sky",
        "mainlyClear" => "mainly clear",
        "partlyCloudy" => "partly cloudy",
        "overcast" => "overcast",
        "fog" => "fog",
        "drizzle" => "drizzle",
        "freezingRain" => "freezing rain",
        "lightRain" => "light rain",
        "rain" => "rain",
        "heavyRain" => "heavy rain",
        "lightSnow" => "light snow",
        "snow" => "snow",
        "heavySnow" => "heavy snow",
        "showers" => "rain showers",
        "heavyShowers" => "heavy rain showers",
        "snowShowers" => "snow showers",
        "thunderstorm" => "thunderstorm",
        "hail" => "thunderstorm with hail",
        _ => "unknown sky",
    };

    private static string SkyFor(int code) => Loc.T("tool.weather.sky." + SkyGroup(code));
}
