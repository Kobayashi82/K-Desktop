using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KDesktop.Assistant.Voice.Kokoro;

/// <summary>
/// Numbers and symbols written out in Spanish, before the phonemes (espeak-ng does it by itself; our own rules need the
/// words): «44» → «cuarenta y cuatro», «1,65» → «uno coma sesenta y cinco», «17:40» → «diecisiete cuarenta», «20 %» →
/// «veinte por ciento».
/// </summary>
internal static partial class SpanishNumbers
{
    private static readonly string[] Units =
    [
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece",
        "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós",
        "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve",
    ];

    private static readonly string[] Tens = ["", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];

    private static readonly string[] Hundreds =
        ["", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos"];

    /// <summary>The text with its numbers and symbols in words.</summary>
    public static string Expand(string text)
    {
        text = Percent().Replace(text, m => m.Groups[1].Value + " por ciento");
        text = Clock().Replace(text, m => Say(m.Groups[1].Value) + " " + (m.Groups[2].Value == "00" ? "en punto" : Say(m.Groups[2].Value)));
        text = Number().Replace(text, m => Say(m.Value));
        var words = new StringBuilder(text.Length);
        foreach (var c in text)
            words.Append(c switch
            {
                '&' => " y ",
                '+' => " más ",
                '€' => " euros ",
                '$' => " dólares ",
                '@' => " arroba ",
                '=' => " igual a ",
                _ => c.ToString(),
            });
        return words.ToString();
    }

    /// <summary>«1.234,5» or «1,65» or «3.5» said: thousands with dots are joined, the decimals with «coma».</summary>
    private static string Say(string number)
    {
        number = ThousandDots().IsMatch(number) ? number.Replace(".", "", StringComparison.Ordinal) : number;
        var parts = number.Split([',', '.'], 2);
        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var whole)) return number;
        var said = Words(whole);
        if (parts.Length == 2 && parts[1].Length > 0)
        {
            // «1,05» → «uno coma cero cinco»: the zeros in front are said.
            var decimals = parts[1];
            var zeros = decimals.TakeWhile(c => c == '0').Count();
            said += " coma" + string.Concat(Enumerable.Repeat(" cero", zeros));
            if (zeros < decimals.Length && long.TryParse(decimals[zeros..], NumberStyles.None, CultureInfo.InvariantCulture, out var rest)) said += " " + Words(rest);
        }
        return said;
    }

    /// <summary>A whole number in words («uno» for 1, as said alone).</summary>
    public static string Words(long n)
    {
        if (n < 0) return "menos " + Words(-n);
        if (n < 30) return Units[n];
        if (n < 100) return Tens[n / 10] + (n % 10 == 0 ? "" : " y " + Units[n % 10]);
        if (n == 100) return "cien";
        if (n < 1000) return Hundreds[n / 100] + (n % 100 == 0 ? "" : " " + Words(n % 100));
        if (n < 1_000_000)
        {
            var thousands = n / 1000;
            var head = thousands == 1 ? "mil" : Apocope(Words(thousands)) + " mil";
            return head + (n % 1000 == 0 ? "" : " " + Words(n % 1000));
        }
        if (n < 1_000_000_000_000)
        {
            var millions = n / 1_000_000;
            var head = millions == 1 ? "un millón" : Apocope(Words(millions)) + " millones";
            return head + (n % 1_000_000 == 0 ? "" : " " + Words(n % 1_000_000));
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>«veintiuno mil» → «veintiún mil», «uno» → «un» before a noun-like word.</summary>
    private static string Apocope(string words) =>
        words.EndsWith("veintiuno", StringComparison.Ordinal) ? words[..^9] + "veintiún"
        : words.EndsWith("uno", StringComparison.Ordinal) ? words[..^1]
        : words;

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s?%")]
    private static partial Regex Percent();

    [GeneratedRegex(@"\b([01]?\d|2[0-3]):([0-5]\d)\b")]
    private static partial Regex Clock();

    [GeneratedRegex(@"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:[.,]\d+)?")]
    private static partial Regex Number();

    [GeneratedRegex(@"^\d{1,3}(?:\.\d{3})+(?:,\d+)?$")]
    private static partial Regex ThousandDots();
}
