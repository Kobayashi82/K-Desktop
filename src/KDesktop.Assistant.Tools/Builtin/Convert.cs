using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>A unit: its symbol, what it measures and how many of the base unit it is (temperatures convert apart).</summary>
public sealed record Unit(string Symbol, string Kind, double Factor);

/// <summary>
/// The units <c>convert</c> knows (stage 10.3), without the network: their symbols and names in English and in the
/// languages of K-Desktop, folded (lower case, no accents, no spaces or dots). A plural «s»/«es» is tried off too.
/// </summary>
public static class Units
{
    private static readonly (Unit Unit, string Names)[] Table =
    [
        // Length (metre)
        (new("mm", "length", 0.001), "mm|millimeter|millimetre|milimetro|millimetre|millimeter|millimetro|milimetro"),
        (new("cm", "length", 0.01), "cm|centimeter|centimetre|centimetro|centimètre|zentimeter|centimetro"),
        (new("m", "length", 1), "m|meter|metre|metro|mètre"),
        (new("km", "length", 1000), "km|kilometer|kilometre|kilometro|kilomètre|kilometer|chilometro|quilometro|kms"),
        (new("in", "length", 0.0254), "in|inch|inches|pulgada|pouce|zoll|pollice|polegada|\""),
        (new("ft", "length", 0.3048), "ft|foot|feet|pie|pies|pied|fuss|piede|pe|'"),
        (new("yd", "length", 0.9144), "yd|yard|yarda|verge|iarda|jarda"),
        (new("mi", "length", 1609.344), "mi|mile|milla|mille|meile|miglio|miglia|milha"),
        (new("nmi", "length", 1852), "nmi|nautical mile|milla nautica|millas nauticas|mille marin|seemeile|miglio nautico|milha nautica"),
        // Mass (kilogram)
        (new("mg", "mass", 1e-6), "mg|milligram|miligramo|milligramme|milligramm|milligrammo|miligrama"),
        (new("g", "mass", 0.001), "g|gr|gram|gramme|gramo|grammo|grama|gramm"),
        (new("kg", "mass", 1), "kg|kilo|kilogram|kilogramme|kilogramo|kilogramm|chilogrammo|quilo|quilograma|kilos"),
        (new("t", "mass", 1000), "t|tonne|ton|tonelada|tonnellata|tonne metrique|metric ton"),
        (new("oz", "mass", 0.028349523125), "oz|ounce|onza|once|unze|oncia|onca"),
        (new("lb", "mass", 0.45359237), "lb|lbs|pound|libra|livre|pfund|libbra"),
        (new("st", "mass", 6.35029318), "st|stone"),
        // Volume (litre)
        (new("ml", "volume", 0.001), "ml|milliliter|millilitre|mililitro|millilitro"),
        (new("cl", "volume", 0.01), "cl|centiliter|centilitre|centilitro"),
        (new("dl", "volume", 0.1), "dl|deciliter|decilitre|decilitro"),
        (new("l", "volume", 1), "l|liter|litre|litro|lt"),
        (new("m³", "volume", 1000), "m3|cubic meter|cubic metre|metro cubico|metros cubicos|metre cube|kubikmeter|metro cubo"),
        (new("tsp", "volume", 0.00492892159375), "tsp|teaspoon|cucharadita|cuillere a cafe|teeloffel|cucchiaino|colher de cha"),
        (new("tbsp", "volume", 0.01478676478125), "tbsp|tablespoon|cucharada|cuillere a soupe|essloffel|cucchiaio|colher de sopa"),
        (new("fl oz", "volume", 0.0295735295625), "floz|fluid ounce|onza liquida|onzas liquidas|once liquide"),
        (new("cup", "volume", 0.2365882365), "cup|taza|tasse|tazza|xicara"),
        (new("pt", "volume", 0.473176473), "pt|pint|pinta|pinte"),
        (new("qt", "volume", 0.946352946), "qt|quart|cuarto de galon"),
        (new("gal", "volume", 3.785411784), "gal|gallon|galon|galone|galao|us gallon"),
        (new("imp gal", "volume", 4.54609), "impgal|imperial gallon|uk gallon|galon imperial|galones imperiales"),
        // Area (square metre)
        (new("cm²", "area", 1e-4), "cm2|square centimeter|centimetro cuadrado|centimetros cuadrados"),
        (new("m²", "area", 1), "m2|square meter|square metre|metro cuadrado|metros cuadrados|metre carre|quadratmeter|metro quadrato|metro quadrado|sqm"),
        (new("km²", "area", 1e6), "km2|square kilometer|square kilometre|kilometro cuadrado|kilometros cuadrados|kilometre carre|quadratkilometer"),
        (new("ha", "area", 1e4), "ha|hectare|hectarea|ettaro|hektar"),
        (new("ac", "area", 4046.8564224), "ac|acre|acres"),
        (new("ft²", "area", 0.09290304), "ft2|sqft|square foot|square feet|pie cuadrado|pies cuadrados"),
        (new("mi²", "area", 2589988.110336), "mi2|square mile|milla cuadrada|millas cuadradas"),
        // Speed (metre per second)
        (new("m/s", "speed", 1), "m/s|mps|meters per second|metros por segundo|metre par seconde"),
        (new("km/h", "speed", 1 / 3.6), "km/h|kmh|kph|kilometers per hour|kilometres per hour|kilometros por hora|kilometre par heure|kilometres heure|stundenkilometer|chilometri orari|quilometros por hora"),
        (new("mph", "speed", 0.44704), "mph|mi/h|miles per hour|millas por hora|miles par heure|meilen pro stunde|miglia orarie|milhas por hora"),
        (new("kn", "speed", 0.514444444444), "kn|kt|knot|nudo|noeud|knoten|nodo"),
        // Time (second)
        (new("ms", "time", 0.001), "ms|millisecond|milisegundo|milliseconde|millisekunde|millisecondo"),
        (new("s", "time", 1), "s|sec|second|segundo|seconde|sekunde|secondo"),
        (new("min", "time", 60), "min|minute|minuto|minuten"),
        (new("h", "time", 3600), "h|hr|hour|hora|heure|stunde|ora"),
        (new("d", "time", 86400), "d|day|dia|jour|tag|tage|giorno|giorni"),
        (new("wk", "time", 604800), "wk|week|semana|semaine|woche|wochen|settimana|settimane"),
        (new("mo", "time", 2629746), "mo|month|mes|meses|mois|monat|monate|mese|mesi"),
        (new("yr", "time", 31556952), "yr|year|ano|anos|an|annee|jahr|jahre|anno|anni"),
        // Data (byte)
        (new("bit", "data", 0.125), "bit|bits"),
        (new("B", "data", 1), "b|byte|octeto|octet"),
        (new("KB", "data", 1e3), "kb|kilobyte|ko"),
        (new("MB", "data", 1e6), "mb|megabyte|mo|megas"),
        (new("GB", "data", 1e9), "gb|gigabyte|go|gigas|giga"),
        (new("TB", "data", 1e12), "tb|terabyte|to|teras"),
        (new("KiB", "data", 1024), "kib|kibibyte"),
        (new("MiB", "data", 1048576), "mib|mebibyte"),
        (new("GiB", "data", 1073741824), "gib|gibibyte"),
        (new("TiB", "data", 1099511627776), "tib|tebibyte"),
        // Energy (joule)
        (new("J", "energy", 1), "j|joule|julio"),
        (new("kJ", "energy", 1000), "kj|kilojoule|kilojulio"),
        (new("cal", "energy", 4.184), "cal|calorie|caloria|kalorie|caloria"),
        (new("kcal", "energy", 4184), "kcal|kilocalorie|kilocaloria|kalorien|kilokalorie"),
        (new("Wh", "energy", 3600), "wh|watt hour|vatio hora"),
        (new("kWh", "energy", 3.6e6), "kwh|kilowatt hour|kilovatio hora|kilovatios hora"),
        (new("BTU", "energy", 1055.05585262), "btu"),
        // Power (watt)
        (new("W", "power", 1), "w|watt|vatio"),
        (new("kW", "power", 1000), "kw|kilowatt|kilovatio"),
        (new("hp", "power", 745.69987158227), "hp|horsepower|horse power"),
        (new("CV", "power", 735.49875), "cv|caballo|caballos|caballo de vapor|caballos de vapor|ch|ps|pferdestarke|cavallo|cavalli|cavalo"),
        // Pressure (pascal)
        (new("Pa", "pressure", 1), "pa|pascal"),
        (new("hPa", "pressure", 100), "hpa|hectopascal"),
        (new("kPa", "pressure", 1000), "kpa|kilopascal"),
        (new("bar", "pressure", 1e5), "bar|bares"),
        (new("mbar", "pressure", 100), "mbar|millibar|milibar"),
        (new("atm", "pressure", 101325), "atm|atmosphere|atmosfera|atmosphere"),
        (new("psi", "pressure", 6894.757293168), "psi"),
        (new("mmHg", "pressure", 133.322387415), "mmhg|millimeter of mercury|milimetros de mercurio"),
        // Temperature (special)
        (new("°C", "temperature", 0), "c|°c|celsius|centigrade|centigrado|grados celsius|grado celsius|grados centigrados|degree celsius|degrees celsius"),
        (new("°F", "temperature", 0), "f|°f|fahrenheit|grados fahrenheit|degree fahrenheit|degrees fahrenheit"),
        (new("K", "temperature", 0), "k|kelvin|kelvins|grados kelvin"),
    ];

    private static readonly Dictionary<string, List<Unit>> ByName = Index();

    private static Dictionary<string, List<Unit>> Index()
    {
        var index = new Dictionary<string, List<Unit>>(StringComparer.Ordinal);
        foreach (var (unit, names) in Table)
            foreach (var name in names.Split('|').Append(unit.Symbol))
            {
                var key = Fold(name);
                if (key.Length == 0) continue;
                if (!index.TryGetValue(key, out var list)) index[key] = list = [];
                if (!list.Contains(unit)) list.Add(unit);
            }
        return index;
    }

    /// <summary>The units <paramref name="said"/> may be (several when it is ambiguous: «mo», «to»); empty: none.</summary>
    public static IReadOnlyList<Unit> Find(string said)
    {
        var key = Fold(said);
        if (ByName.TryGetValue(key, out var found)) return found;
        foreach (var cut in new[] { "es", "s" })
            if (key.Length > cut.Length + 1 && key.EndsWith(cut, StringComparison.Ordinal) && ByName.TryGetValue(key[..^cut.Length], out var plural))
                return plural;
        return [];
    }

    /// <summary>Lower case, no accents, «²» as «2», without spaces, dots, «de» or «per»/«por» glue kept as it is.</summary>
    public static string Fold(string text)
    {
        var folded = new StringBuilder();
        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c) || c is '.' or '-' or '_') continue;
            folded.Append(c switch { '²' => '2', '³' => '3', 'º' => '°', _ => FuzzyMatch.Fold(c) });
        }
        return folded.ToString();
    }

    public static double Convert(double value, Unit from, Unit to)
    {
        if (from.Kind != "temperature") return value * from.Factor / to.Factor;
        var celsius = from.Symbol switch { "°F" => (value - 32) * 5 / 9, "K" => value - 273.15, _ => value };
        return to.Symbol switch { "°F" => celsius * 9 / 5 + 32, "K" => celsius + 273.15, _ => celsius };
    }
}

/// <summary>The euro reference rates of the European Central Bank: how many of each currency one euro is, on a day.</summary>
public sealed record ExchangeRates(DateOnly Date, IReadOnlyDictionary<string, double> PerEuro, DateTime Fetched);

/// <summary>
/// The ECB's daily rates (stage 10.3): about 30 currencies against the euro, published every working day around 16:00
/// CET, free and without a key. Fetched again after 6 hours; the last ones are kept in <c>Assistant\rates.json</c>, so
/// without internet the conversion still works (saying of which day it is).
/// </summary>
public sealed partial class EcbRates : IDisposable
{
    private const string Address = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);

    private readonly string _file;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ExchangeRates? _rates;

    public EcbRates(string file, HttpMessageHandler? network = null)
    {
        _file = file;
        _http = network is null
            ? new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
            : new HttpClient(network, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>The rates (with <c>EUR</c> = 1), fresh when possible; the ones kept when the ECB cannot be reached; null: never had any.</summary>
    public async Task<(ExchangeRates Rates, bool Stale)?> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _rates ??= Load();
            if (_rates is { } kept && DateTime.UtcNow - kept.Fetched < Fresh) return (kept, false);
            try
            {
                var xml = await _http.GetStringAsync(new Uri(Address), cancellationToken).ConfigureAwait(false);
                if (Parse(xml, DateTime.UtcNow) is { } fresh)
                {
                    _rates = fresh;
                    Save(fresh);
                    return (fresh, false);
                }
                Log.Warn("Asistente: los cambios del BCE no se han podido leer");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Log.Warn($"Asistente: sin los cambios del BCE ({ex.Message})");
            }
            return _rates is { } old ? (old, true) : null;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _gate.Dispose();
        _http.Dispose();
    }

    /// <summary>The ECB's XML (<c>&lt;Cube time='…'&gt;</c> and <c>&lt;Cube currency='USD' rate='1.08'/&gt;</c>); null: not that.</summary>
    public static ExchangeRates? Parse(string xml, DateTime fetched)
    {
        if (Day().Match(xml) is not { Success: true } day || !DateOnly.TryParse(day.Groups[1].Value, CultureInfo.InvariantCulture, out var date)) return null;
        var rates = new Dictionary<string, double>(StringComparer.Ordinal) { ["EUR"] = 1 };
        foreach (Match m in Rate().Matches(xml))
            if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate > 0) rates[m.Groups[1].Value] = rate;
        return rates.Count > 1 ? new ExchangeRates(date, rates, fetched) : null;
    }

    private sealed record Kept(string Date, Dictionary<string, double> Rates, DateTime Fetched);

    private ExchangeRates? Load()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            var kept = JsonSerializer.Deserialize<Kept>(File.ReadAllText(_file), Json.Options);
            return kept is null || !DateOnly.TryParse(kept.Date, CultureInfo.InvariantCulture, out var date) ? null : new ExchangeRates(date, kept.Rates, kept.Fetched);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: {_file} no se ha podido leer ({ex.Message})");
            return null;
        }
    }

    private void Save(ExchangeRates rates)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(new Kept(rates.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), new(rates.PerEuro), rates.Fetched), Json.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: {_file} no se ha podido guardar ({ex.Message})");
        }
    }

    [GeneratedRegex(@"time=['""](\d{4}-\d{2}-\d{2})['""]")]
    private static partial Regex Day();

    [GeneratedRegex(@"currency=['""]([A-Z]{3})['""]\s+rate=['""]([0-9.]+)['""]")]
    private static partial Regex Rate();
}

/// <summary>The currencies by their names and signs (the ECB's ones), folded like <see cref="Units.Fold"/>.</summary>
public static class Currencies
{
    private static readonly Dictionary<string, string> Names = Index(new()
    {
        ["EUR"] = "€|euro|euros|eur",
        ["USD"] = "$|us$|dolar|dolares|dollar|dollars|us dollar|dolar estadounidense|dolares estadounidenses|dolar americano|dolares americanos|dollaro|dollari|dolares americanos",
        ["GBP"] = "£|libra esterlina|libras esterlinas|pound sterling|pounds sterling|british pound|british pounds|livre sterling|sterlina|sterline|pfund sterling",
        ["JPY"] = "¥|yen|yenes|japanese yen|yen japones",
        ["CNY"] = "yuan|yuanes|renminbi|chinese yuan|yuan chino",
        ["CHF"] = "franco suizo|francos suizos|swiss franc|swiss francs|franc suisse|francs suisses|franken|schweizer franken|franco svizzero|franchi svizzeri",
        ["MXN"] = "peso mexicano|pesos mexicanos|mexican peso|mexican pesos",
        ["BRL"] = "real|reales|reais|real brasileno|reales brasilenos|brazilian real|brazilian reais",
        ["INR"] = "rupia|rupias|rupia india|rupias indias|indian rupee|indian rupees|rupee|rupees",
        ["CAD"] = "dolar canadiense|dolares canadienses|canadian dollar|canadian dollars|dollar canadien",
        ["AUD"] = "dolar australiano|dolares australianos|australian dollar|australian dollars|dollar australien",
        ["NZD"] = "dolar neozelandes|dolares neozelandeses|new zealand dollar|new zealand dollars",
        ["HKD"] = "dolar de hong kong|dolares de hong kong|hong kong dollar|hong kong dollars",
        ["SGD"] = "dolar de singapur|dolares de singapur|singapore dollar|singapore dollars",
        ["SEK"] = "corona sueca|coronas suecas|swedish krona|swedish kronor",
        ["NOK"] = "corona noruega|coronas noruegas|norwegian krone|norwegian kroner",
        ["DKK"] = "corona danesa|coronas danesas|danish krone|danish kroner",
        ["CZK"] = "corona checa|coronas checas|czech koruna",
        ["ISK"] = "corona islandesa|coronas islandesas|icelandic krona",
        ["PLN"] = "zloty|zlotys|esloti|eslotis|polish zloty",
        ["HUF"] = "forinto|forintos|florin hungaro|forint|forints|hungarian forint",
        ["RON"] = "leu|lei|leu rumano|romanian leu",
        ["BGN"] = "lev|leva|lev bulgaro|bulgarian lev",
        ["TRY"] = "lira turca|liras turcas|turkish lira|turkish liras",
        ["KRW"] = "won|wones|won surcoreano|south korean won|korean won",
        ["ZAR"] = "rand|rands|south african rand",
        ["ILS"] = "shekel|sheqel|shekels|nuevo shekel|israeli shekel",
        ["THB"] = "baht|bat|thai baht",
        ["PHP"] = "peso filipino|pesos filipinos|philippine peso",
        ["IDR"] = "rupia indonesia|rupias indonesias|indonesian rupiah|rupiah",
        ["MYR"] = "ringgit|malaysian ringgit",
    });

    /// <summary>«libra», «pound»: weight or sterling, by what it is converted to.</summary>
    private static readonly HashSet<string> Pounds = ["libra", "libras", "pound", "pounds", "livre", "livres", "pfund", "libbra", "libbre"];

    private static Dictionary<string, string> Index(Dictionary<string, string> names)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, list) in names)
        {
            index[Units.Fold(code)] = code;
            foreach (var name in list.Split('|')) index.TryAdd(Units.Fold(name), code);
        }
        return index;
    }

    /// <summary>The ISO code of <paramref name="said"/> («USD», «dólares», «$», «libras» → GBP), or null.</summary>
    public static string? Find(string said)
    {
        var key = Units.Fold(said);
        if (Pounds.Contains(key)) return "GBP";
        return Names.TryGetValue(key, out var code) ? code : null;
    }
}

/// <summary>
/// «¿Cuántas millas son 10 km?», «pasa 350 °F a Celsius», «¿cuánto son 50 dólares en euros?» (stage 10.3, <c>convertir</c>):
/// units without the network (<see cref="Units"/>) and currencies with the ECB's rates (<see cref="EcbRates"/>).
/// </summary>
public sealed class ConvertTool(EcbRates rates) : ITool
{
    public string Name => "convert";

    public string Description => "Converts an amount between units (length, weight, volume, area, speed, temperature, time, data, energy, power, pressure) or between currencies (the European Central Bank's euro rates, updated every working day). Units as symbols or names (km, mi, lb, kg, °F, l, gal, km/h, mph, GB); currencies as ISO codes (EUR, USD, GBP, JPY…). Use it rather than calculate for any conversion.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"value":{"type":"number","description":"The amount to convert (1 when the user asks for a rate)"},"from":{"type":"string","description":"The unit or currency it is in"},"to":{"type":"string","description":"The unit or currency to convert to"}},"required":["from","to"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.convert.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var value = arguments.GetNumber("value") ?? 1;
        var from = arguments.GetString("from")?.Trim() ?? string.Empty;
        var to = arguments.GetString("to")?.Trim() ?? string.Empty;
        if (from.Length == 0 || to.Length == 0) return ToolResult.Fail("Say what to convert from and to.");
        var culture = Loc.Instance.Culture;

        var units = (From: Units.Find(from), To: Units.Find(to));
        var money = (From: Currencies.Find(from), To: Currencies.Find(to));
        // «libras» is a weight unless the other side is money only («libras a euros»).
        var isMoney = money.From is not null && money.To is not null && (units.From.Count == 0 || units.To.Count == 0);
        var pair = units.From.SelectMany(f => units.To.Where(t => t.Kind == f.Kind).Select(t => (f, t))).FirstOrDefault();
        if (!isMoney && pair.f is not null)
        {
            var result = Units.Convert(value, pair.f, pair.t);
            var text = string.Create(CultureInfo.InvariantCulture, $"{value:G10} {pair.f.Symbol} = {result:G6} {pair.t.Symbol}");
            return new ToolResult(text) { Done = Loc.T("tool.convert.done", $"{Amount(value, culture)} {pair.f.Symbol}", pair.t.Symbol), Reply = Loc.T("tool.convert.reply", Amount(value, culture), pair.f.Symbol, Amount(result, culture), pair.t.Symbol) };
        }

        if (isMoney && money.From is { } a && money.To is { } b)
        {
            if (await rates.GetAsync(cancellationToken).ConfigureAwait(false) is not { } got)
                return new ToolResult("The exchange rates could not be fetched (no internet?). Say so.") { Failed = true, Reply = Loc.T("tool.convert.offline") };
            var (known, stale) = got;
            if (!known.PerEuro.TryGetValue(a, out var perA) || !known.PerEuro.TryGetValue(b, out var perB))
                return ToolResult.Fail($"The European Central Bank has no rate for {(known.PerEuro.ContainsKey(a) ? b : a)}. It has: {string.Join(", ", known.PerEuro.Keys.Order())}.");
            var result = value / perA * perB;
            var day = known.Date.ToString("d", culture);
            var text = string.Create(CultureInfo.InvariantCulture,
                $"{value:G10} {a} = {result:0.####} {b} (European Central Bank reference rate of {known.Date:yyyy-MM-dd}: 1 {a} = {perB / perA:0.######} {b}{(stale ? "; could not be updated now" : "")})");
            return new ToolResult(text)
            {
                Done = Loc.T("tool.convert.done", $"{Money(value, culture)} {a}", b),
                Reply = Loc.T("tool.convert.replyMoney", Money(value, culture), a, Money(result, culture), b, day),
            };
        }

        if (units.From.Count > 0 && units.To.Count > 0)
            return ToolResult.Fail($"{from} and {to} measure different things: they cannot be converted.");
        var unknown = units.From.Count == 0 && money.From is null ? from : to;
        return ToolResult.Fail($"«{unknown}» is not a unit or currency that can be converted. Use a symbol (km, lb, °F, l, mph, GB, kWh) or an ISO currency code (USD, GBP).");
    }

    /// <summary>A measure for people: two decimals at most, more for small amounts (0,0254).</summary>
    public static string Amount(double value, CultureInfo culture)
    {
        var size = Math.Abs(value);
        if (size == 0) return "0";
        if (size >= 1) return value.ToString("#,##0.##", culture);
        var decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(size)), 2, 10);
        return Math.Round(value, decimals).ToString("0." + new string('#', decimals), culture);
    }

    /// <summary>Money for people: always two decimals.</summary>
    public static string Money(double value, CultureInfo culture) => value.ToString("#,##0.00", culture);
}
