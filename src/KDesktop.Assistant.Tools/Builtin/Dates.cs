using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «¿Cuántos días faltan para Navidad?», «¿qué día cae el 25?», «salgo a las 17:40 y tardo 2 h 45, ¿a qué hora llego?»
/// (5.0.105): dates and clock times, exact. Bonsai did them with the calculator («2026-12-25» = 1989, «17:40» cannot be
/// read) and got them wrong even thinking first.
/// </summary>
public sealed partial class DateMathTool(TimeProvider time) : ITool
{
    public string Name => "date_math";

    public string Description =>
        "Exact date and clock-time arithmetic (the calculator cannot do dates or times): the days or weeks between two dates, " +
        "the weekday of a date, a date or time plus or minus a duration, the days in a year or month. The date is today when left empty.";

    public string ParametersSchemaJson => """
        {"type":"object","properties":{
        "op":{"type":"string","enum":["between","weekday","add","days_in"],"description":"between: from date to «to»; weekday: of date; add: date or time plus amount; days_in: of a year (2026) or a month (2026-02)"},
        "date":{"type":"string","description":"YYYY-MM-DD, HH:MM, YYYY-MM-DD HH:MM, a year or YYYY-MM; empty: now"},
        "to":{"type":"string","description":"For between: the other date (YYYY-MM-DD) or time (HH:MM)"},
        "amount":{"type":"string","description":"For add: like 2h 45m, 90m, 3d, 2w, 1mo, 1y; a minus sign subtracts. A quarter of an hour is 15m, three quarters 45m, half an hour 30m"}},
        "required":["op"]}
        """;

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.date_math.intent");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var now = time.GetLocalNow().DateTime;
        try
        {
            var result = (arguments.GetString("op") ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "between" => Between(Read(arguments.GetString("date"), now), Read(arguments.GetString("to") ?? throw new FormatException("«to» is missing"), now)),
                "weekday" => Weekday(Read(arguments.GetString("date"), now)),
                "add" => Add(Read(arguments.GetString("date"), now), arguments.GetString("amount") ?? throw new FormatException("«amount» is missing")),
                "days_in" => DaysIn(arguments.GetString("date"), now),
                var other => throw new FormatException($"unknown op «{other}»"),
            };
            return Task.FromResult(result);
        }
        catch (FormatException ex)
        {
            return Task.FromResult(ToolResult.Fail($"Cannot work it out: {ex.Message}."));
        }
    }

    /// <summary>A moment read from the model's words, and whether it was only a clock time (today).</summary>
    public readonly record struct Moment(DateTime At, bool TimeOnly);

    public static Moment Read(string? text, DateTime now)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0 || t.Equals("now", StringComparison.OrdinalIgnoreCase) || t.Equals("today", StringComparison.OrdinalIgnoreCase)) return new(now, false);
        if (TimeOfDay().Match(t) is { Success: true } clock)
            return new(now.Date + new TimeSpan(int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture), 0), true);
        string[] formats = ["yyyy-M-d", "yyyy-M-d H:mm", "yyyy-M-dTH:mm", "yyyy-M-d H:mm:ss", "yyyy-M-dTH:mm:ss", "d/M/yyyy", "d/M/yyyy H:mm"];
        if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var at)) return new(at, false);
        throw new FormatException($"«{t}» is not a date (YYYY-MM-DD) or a time (HH:MM)");
    }

    private static string Day(DateTime at) => at.ToString("dddd yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Clock(DateTime at) => at.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static ToolResult Between(Moment from, Moment to)
    {
        if (from.TimeOnly || to.TimeOnly)
        {
            var span = to.At - from.At;
            var minutes = (int)Math.Round(span.TotalMinutes);
            return new ToolResult($"From {Clock(from.At)} to {Clock(to.At)}: {Duration(minutes)}.") { Done = Loc.T("tool.date_math.done", Spoken(minutes)) };
        }
        var days = (to.At.Date - from.At.Date).Days;
        var weeks = Math.Abs(days) / 7;
        var rest = Math.Abs(days) % 7;
        var text = $"From {Day(from.At)} to {Day(to.At)}: {Math.Abs(days)} days ({weeks} weeks and {rest} days){(days < 0 ? ", in the past" : "")}.";
        return new ToolResult(text)
        {
            Done = Loc.T("tool.date_math.between", Math.Abs(days), from.At.ToString("d", Loc.Instance.Culture), to.At.ToString("d", Loc.Instance.Culture)),
        };
    }

    private static ToolResult Weekday(Moment at) =>
        new($"{at.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is a {at.At.ToString("dddd", CultureInfo.InvariantCulture)}.")
        {
            Done = Loc.T("tool.date_math.done", at.At.ToString("D", Loc.Instance.Culture)),
        };

    private static ToolResult Add(Moment start, string amount)
    {
        var at = Shift(start.At, amount);
        var calendar = Part().Matches(amount.ToLowerInvariant()).Any(m => m.Groups[2].Value[0] is 'y' or 'w' or 'd' || m.Groups[2].Value.StartsWith("mo", StringComparison.Ordinal));
        if (start.TimeOnly && !calendar)
        {
            var days = (at.Date - start.At.Date).Days;
            var when = days == 0 ? "same day" : $"{(days > 0 ? "+" : "")}{days} day(s), {Day(at)}";
            return new ToolResult($"{Clock(start.At)} {Signed(amount)} = {Clock(at)} ({when}).") { Done = Loc.T("tool.date_math.done", at.ToString("t", Loc.Instance.Culture)) };
        }
        var shown = at.TimeOfDay == TimeSpan.Zero || !start.TimeOnly && start.At.TimeOfDay == at.TimeOfDay ? Day(at) : $"{Day(at)} {Clock(at)}";
        return new ToolResult($"{Day(start.At)} {Signed(amount)} = {shown}.") { Done = Loc.T("tool.date_math.done", at.ToString("D", Loc.Instance.Culture)) };
    }

    private static ToolResult DaysIn(string? text, DateTime now)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0 || Year().IsMatch(t))
        {
            var year = t.Length == 0 ? now.Year : int.Parse(t, CultureInfo.InvariantCulture);
            if (year is < 1 or > 9999) throw new FormatException($"«{t}» is not a year");
            var days = DateTime.IsLeapYear(year) ? 366 : 365;
            return new ToolResult($"The year {year} has {days} days ({(days == 366 ? "a leap year" : "not a leap year")}).")
            {
                Done = Loc.T("tool.date_math.daysIn", days, year.ToString(CultureInfo.InvariantCulture)),
            };
        }
        var month = Read(t.Length <= 7 ? t + "-01" : t, now).At;
        var count = DateTime.DaysInMonth(month.Year, month.Month);
        return new ToolResult($"{month.ToString("yyyy-MM", CultureInfo.InvariantCulture)} ({month.ToString("MMMM", CultureInfo.InvariantCulture)}) has {count} days.")
        {
            Done = Loc.T("tool.date_math.daysIn", count, month.ToString("Y", Loc.Instance.Culture)),
        };
    }

    /// <summary>A date moved by «2h 45m», «-3d», «1mo 2w», «2:45»; a minus at the start subtracts all of it.</summary>
    public static DateTime Shift(DateTime at, string amount)
    {
        var text = amount.Trim().ToLowerInvariant();
        var negative = text.StartsWith('-');
        if (negative) text = text[1..].TrimStart();
        if (text.StartsWith('+')) text = text[1..].TrimStart();
        var sign = negative ? -1 : 1;
        if (HoursMinutes().Match(text) is { Success: true } hm)
            return at.AddMinutes(sign * (int.Parse(hm.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(hm.Groups[2].Value, CultureInfo.InvariantCulture)));
        var parts = Part().Matches(text);
        if (parts.Count == 0 || Part().Replace(text, string.Empty).Trim(' ', ',', '+').Length > 0)
            throw new FormatException($"«{amount}» is not a duration (like 2h 45m, 3d, 2w)");
        foreach (Match part in parts)
        {
            var n = double.Parse(part.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * sign;
            at = part.Groups[2].Value switch
            {
                "y" or "year" or "years" => at.AddYears((int)n),
                "mo" or "month" or "months" => at.AddMonths((int)n),
                "w" or "week" or "weeks" => at.AddDays(n * 7),
                "d" or "day" or "days" => at.AddDays(n),
                "h" or "hour" or "hours" => at.AddHours(n),
                "m" or "min" or "mins" or "minute" or "minutes" => at.AddMinutes(n),
                _ => at.AddSeconds(n),
            };
        }
        return at;
    }

    private static string Signed(string amount) => amount.TrimStart().StartsWith('-') ? amount.Trim() : "+ " + amount.Trim().TrimStart('+').Trim();

    private static string Duration(int minutes) => $"{(minutes < 0 ? "minus " : "")}{Math.Abs(minutes) / 60} h {Math.Abs(minutes) % 60} min";

    private static string Spoken(int minutes) =>
        string.Create(Loc.Instance.Culture, $"{(minutes < 0 ? "-" : "")}{Math.Abs(minutes) / 60}:{Math.Abs(minutes) % 60:00}");

    [GeneratedRegex(@"^(\d{1,2}):(\d{2})$")]
    private static partial Regex TimeOfDay();

    [GeneratedRegex(@"^(\d{1,3}):(\d{2})$")]
    private static partial Regex HoursMinutes();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex Year();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(years?|y|months?|mo|weeks?|w|days?|d|hours?|h|minutes?|mins?|m|seconds?|secs?|s)(?![a-z])")]
    private static partial Regex Part();
}
