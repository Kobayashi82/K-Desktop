using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace KDesktop.Core.Platform;

/// <summary>A place on the map, with the name it was found by.</summary>
public sealed record GeoPlace(string Name, double Latitude, double Longitude)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} ({Latitude:0.##}, {Longitude:0.##})");
}

/// <summary>
/// Where the user is, for sunrise and sunset (5.0.341): a town written by the user (Open-Meteo's geocoding, the same as
/// the weather; free, no key) or, without one, Windows' location if it is allowed. Remembered while K-Desktop runs (a
/// town for good, Windows' location for six hours).
/// </summary>
public static class HomeLocation
{
    private static readonly ConcurrentDictionary<string, GeoPlace> Towns = new(StringComparer.OrdinalIgnoreCase);
    private static (GeoPlace Place, DateTime Until)? _windows;
    private static readonly Lazy<HttpClient> Client = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(8) });

    /// <summary>The town in the options (the weather's), set by the app; empty: Windows' location.</summary>
    public static Func<string?> DefaultPlace { get; set; } = () => null;

    /// <summary>The last place found (for a quick answer without waiting); null before any.</summary>
    public static GeoPlace? Last { get; private set; }

    /// <summary><paramref name="place"/> or, empty, the default one; null when it cannot be found (no internet, no permission).</summary>
    public static async Task<GeoPlace?> FindAsync(string? place, CancellationToken token = default)
    {
        var town = string.IsNullOrWhiteSpace(place) ? DefaultPlace()?.Trim() : place.Trim();
        var found = string.IsNullOrEmpty(town) ? await WindowsAsync(token).ConfigureAwait(false) : await TownAsync(town, token).ConfigureAwait(false);
        if (found is not null) Last = found;
        return found;
    }

    private static async Task<GeoPlace?> TownAsync(string town, CancellationToken token)
    {
        if (Towns.TryGetValue(town, out var known)) return known;
        try
        {
            // «Málaga, España»: the first part is searched, the rest picks among the results.
            var parts = town.Split(',', 2, StringSplitOptions.TrimEntries);
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(parts[0])}&count=10&language={Uri.EscapeDataString(Localization.Loc.Instance.Language)}&format=json";
            using var response = await Client.Value.GetAsync(new Uri(url), token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
            if (!json.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
            var best = results.EnumerateArray().FirstOrDefault(r => parts.Length > 1 &&
                (Text(r, "country").Contains(parts[1], StringComparison.OrdinalIgnoreCase) || Text(r, "admin1").Contains(parts[1], StringComparison.OrdinalIgnoreCase)));
            if (best.ValueKind == JsonValueKind.Undefined) best = results[0];
            var found = new GeoPlace(Text(best, "name"), best.GetProperty("latitude").GetDouble(), best.GetProperty("longitude").GetDouble());
            Towns[town] = found;
            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Log.Warn($"Ubicación: no se pudo buscar «{town}» ({ex.Message})");
            return null;
        }

        static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? string.Empty : string.Empty;
    }

    private static async Task<GeoPlace?> WindowsAsync(CancellationToken token)
    {
        if (_windows is { } cached && cached.Until > DateTime.UtcNow) return cached.Place;
        try
        {
            var access = await Geolocator.RequestAccessAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            if (access != GeolocationAccessStatus.Allowed)
            {
                Log.Info("Ubicación: Windows no deja usarla; pon tu ciudad");
                return null;
            }
            var position = await new Geolocator().GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(6)).AsTask(token).ConfigureAwait(false);
            var point = position.Coordinate.Point.Position;
            var place = new GeoPlace(Localization.Loc.T("location.windows"), point.Latitude, point.Longitude);
            _windows = (place, DateTime.UtcNow.AddHours(6));
            return place;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            Log.Warn($"Ubicación: Windows no la da ({ex.Message})");
            return null;
        }
    }
}
