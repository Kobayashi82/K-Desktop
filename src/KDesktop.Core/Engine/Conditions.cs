using System.Runtime.InteropServices;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

/// <summary>Checks the condition of «Si…» against the PC as it is now.</summary>
public static class Conditions
{
    /// <summary>Whether <paramref name="c"/> is met («No» already applied).</summary>
    public static async Task<bool> IsMetAsync(ConditionAction c, ForegroundWindow foreground, CancellationToken token)
    {
        var value = await CheckAsync(c, foreground, DateTime.Now, token).ConfigureAwait(false);
        return value != c.Negate;
    }

    private static async Task<bool> CheckAsync(ConditionAction c, ForegroundWindow foreground, DateTime now, CancellationToken token)
    {
        var text = TextVariables.Expand(c.Text).Trim();
        switch (c.Condition)
        {
            case ConditionKind.ProcessRunning:
                return text.Length > 0 && ProcessSnapshot.IsRunning(ProcessNames.Normalize(text));

            case ConditionKind.Foreground:
                return text.Length > 0 && (foreground.ProcessName == ProcessNames.Normalize(text) || Contains(foreground.Title, text));

            case ConditionKind.Schedule:
                return InSchedule(c, now);

            case ConditionKind.Device:
                return await Task.Run(() => PnpDevices.Present().Any(d => text.Length == 0 || Contains(d.Name, text)), token).ConfigureAwait(false);

            case ConditionKind.AudioOutput:
                return await Task.Run(() => AudioDevices.Playback().FirstOrDefault(d => d.IsDefault) is { } output &&
                                            (text.Length == 0 || Contains(output.Name, text) || Contains(output.Adapter, text)), token).ConfigureAwait(false);

            case ConditionKind.AudioPlaying:
                return await Task.Run(() => (text.Length == 0 ? MediaPlayback.IsPlaying(400) : MediaPlayback.IsPlaying(text, 400)) == true, token).ConfigureAwait(false);

            case ConditionKind.Network:
                var networks = await Task.Run(NetworkInfo.ConnectedNetworks, token).ConfigureAwait(false);
                return text.Length == 0 ? networks.Count > 0 : networks.Any(n => Contains(n, text));

            case ConditionKind.PowerSource:
                return OnBattery() is { } battery && battery == (c.PowerSource == PowerSource.Battery);

            case ConditionKind.HostReachable:
                return text.Length > 0 && await WakeOnLan.AnswersAsync(text, 1500).ConfigureAwait(false);

            case ConditionKind.FileExists:
                var path = Environment.ExpandEnvironmentVariables(text.Trim('"'));
                return path.Length > 0 && (File.Exists(path) || Directory.Exists(path));

            case ConditionKind.Clipboard:
                var clipboard = TextVariables.ClipboardText();
                return text.Length == 0 ? clipboard.Length > 0 : clipboard.Contains(text, StringComparison.CurrentCultureIgnoreCase);

            case ConditionKind.Load:
                var load = await Task.Run(() => SystemLoad.Sample(c.Resource), token).ConfigureAwait(false);
                return load > c.Percent;

            case ConditionKind.Variable:
                return Compare(TextVariables.Expand(c.Value).Trim(), c.Test, text);
        }
        return false;
    }

    /// <summary>
    /// «Un texto o variable»: <paramref name="value"/> (already expanded) against <paramref name="wanted"/>. Words in any
    /// case; numbers as numbers («7» is less than «10», «2,5» and «2.5» are the same).
    /// </summary>
    public static bool Compare(string value, VariableTest test, string wanted)
    {
        var numbers = Number(value) is { } a && Number(wanted) is { } b ? (a, b) : ((double, double)?)null;
        return test switch
        {
            VariableTest.NotEmpty => value.Length > 0,
            VariableTest.Contains => wanted.Length == 0 ? value.Length > 0 : value.Contains(wanted, StringComparison.CurrentCultureIgnoreCase),
            VariableTest.Equals => numbers is var (x, y) ? x == y : string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase),
            VariableTest.Greater => numbers is var (x, y) ? x > y : string.Compare(value, wanted, StringComparison.OrdinalIgnoreCase) > 0,
            VariableTest.Less => numbers is var (x, y) ? x < y : string.Compare(value, wanted, StringComparison.OrdinalIgnoreCase) < 0,
            _ => false,
        };

        static double? Number(string text) =>
            double.TryParse(text.Trim().TrimEnd('%').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>«Horario»: inside the time range (it may cross midnight) on one of the days.</summary>
    public static bool InSchedule(ConditionAction c, DateTime now)
    {
        if (!c.Days.Includes(now.DayOfWeek)) return false;
        var from = EventTime.Parse(c.TimeFrom);
        var to = EventTime.Parse(c.TimeTo);
        return (from is null && to is null) || EventConditions.InRange(TimeOnly.FromDateTime(now), from ?? TimeOnly.MinValue, to ?? TimeOnly.MaxValue);
    }

    /// <summary>Every word of <paramref name="wanted"/> is in <paramref name="text"/> (no case, no accents).</summary>
    public static bool Contains(string? text, string wanted)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var folded = new string(text.Select(FuzzyMatch.Fold).ToArray());
        return FuzzyMatch.Tokens(wanted).All(token => folded.Contains(token, StringComparison.Ordinal));
    }

    /// <summary>Whether the PC runs on battery now (null: Windows can't tell).</summary>
    public static bool? OnBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        return status.ACLineStatus switch
        {
            0 => true,
            1 => false,
            _ => null,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
