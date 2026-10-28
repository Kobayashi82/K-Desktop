namespace KDesktop.Core.Engine.Events;

/// <summary>
/// Sunrise and sunset of a day at a place, worked out here (the NOAA formulas, about a minute off), no internet. Null on
/// a day the sun does not rise or does not set there (polar day or night).
/// </summary>
public static class SunTimes
{
    /// <summary>The moments (UTC) the sun rises and sets on <paramref name="date"/> (a local day of the place).</summary>
    public static (DateTime? Rise, DateTime? Set) For(DateOnly date, double latitude, double longitude)
    {
        // Days since J2000 at noon, and from them the sun's position.
        var day = date.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(12)), DateTimeKind.Utc) - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var n = Math.Round(day.TotalDays);
        var meanNoon = n - longitude / 360.0;
        var anomaly = Normalize(357.5291 + 0.98560028 * meanNoon);
        var center = 1.9148 * Sin(anomaly) + 0.0200 * Sin(2 * anomaly) + 0.0003 * Sin(3 * anomaly);
        var eclipticLongitude = Normalize(anomaly + center + 180 + 102.9372);
        var transit = 2451545.0 + meanNoon + 0.0053 * Sin(anomaly) - 0.0069 * Sin(2 * eclipticLongitude);
        var declination = Asin(Sin(eclipticLongitude) * Sin(23.4397));
        // −0.833°: the sun's half width and the bending of the light near the horizon.
        var cosHour = (Sin(-0.833) - Sin(latitude) * Sin(declination)) / (Cos(latitude) * Cos(declination));
        if (cosHour is < -1 or > 1) return (null, null);
        var hour = Acos(cosHour);
        return (FromJulian(transit - hour / 360.0), FromJulian(transit + hour / 360.0));
    }

    /// <summary>
    /// The next sunrise or sunset after <paramref name="afterUtc"/> plus <paramref name="offsetMinutes"/>, looking up to
    /// a week ahead (a place without one for that long: never).
    /// </summary>
    public static DateTime Next(Model.SunEvent which, int offsetMinutes, double latitude, double longitude, DateTime afterUtc)
    {
        // The local day of the place, near enough from its longitude.
        var local = DateOnly.FromDateTime(afterUtc.AddHours(longitude / 15.0));
        for (var i = -1; i <= 7; i++)
        {
            var (rise, set) = For(local.AddDays(i), latitude, longitude);
            if ((which == Model.SunEvent.Sunrise ? rise : set) is { } at && at.AddMinutes(offsetMinutes) > afterUtc) return at.AddMinutes(offsetMinutes);
        }
        return DateTime.MaxValue;
    }

    /// <summary>Whether the sun is up at <paramref name="utc"/> (polar day: yes; polar night: no).</summary>
    public static bool IsDay(DateTime utc, double latitude, double longitude)
    {
        var local = DateOnly.FromDateTime(utc.AddHours(longitude / 15.0));
        var (rise, set) = For(local, latitude, longitude);
        if (rise is null || set is null)
        {
            // No sunrise or no sunset that day: up all day if the sun is above the horizon at noon.
            var noon = local.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(12)), DateTimeKind.Utc).AddHours(-longitude / 15.0);
            return SunHigh(noon, latitude);
        }
        return utc >= rise && utc < set;
    }

    private static bool SunHigh(DateTime noonUtc, double latitude)
    {
        var days = (noonUtc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
        var anomaly = Normalize(357.5291 + 0.98560028 * days);
        var eclipticLongitude = Normalize(anomaly + 1.9148 * Sin(anomaly) + 180 + 102.9372);
        var declination = Asin(Sin(eclipticLongitude) * Sin(23.4397));
        return 90 - Math.Abs(latitude - declination) > 0;
    }

    private static DateTime FromJulian(double julian) =>
        new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(julian - 2451545.0);

    private static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;
    private static double Sin(double degrees) => Math.Sin(degrees * Math.PI / 180);
    private static double Cos(double degrees) => Math.Cos(degrees * Math.PI / 180);
    private static double Asin(double value) => Math.Asin(value) * 180 / Math.PI;
    private static double Acos(double value) => Math.Acos(value) * 180 / Math.PI;
}
