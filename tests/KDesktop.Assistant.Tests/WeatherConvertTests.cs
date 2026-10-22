using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 10.3: the weather (Open-Meteo, faked here) with its fixed rules, and converting units (no network) and
/// currencies (the ECB's rates, faked, kept on disk for when there is no internet).
/// </summary>
public sealed class WeatherConvertTests
{
    private static JsonElement Args(object arguments) => JsonSerializer.SerializeToElement(arguments);

    /// <summary>Open-Meteo's geocoder and forecast, from memory; <see cref="Down"/>: no internet.</summary>
    private sealed class FakeMeteo : HttpMessageHandler
    {
        public bool Down { get; set; }

        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            if (Down) throw new HttpRequestException("no internet");
            var uri = request.RequestUri!.ToString();
            var json = uri.Contains("geocoding", StringComparison.Ordinal) ? Places(uri) : Forecast();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }

        private static string Places(string uri) => uri.Contains("name=Nowhere", StringComparison.Ordinal)
            ? """{"generationtime_ms":0.5}"""
            : uri.Contains("name=Valencia", StringComparison.Ordinal)
                ? """{"results":[{"name":"Valencia","latitude":39.47,"longitude":-0.38,"country":"España","admin1":"Comunidad Valenciana"},{"name":"Valencia","latitude":10.16,"longitude":-68.0,"country":"Venezuela","admin1":"Carabobo"}]}"""
                : """{"results":[{"name":"Bilbao","latitude":43.26,"longitude":-2.93,"country":"España","admin1":"País Vasco"}]}""";

        private static string Forecast()
        {
            var start = new DateOnly(2026, 10, 22);
            var days = Enumerable.Range(0, 7).Select(start.AddDays).ToList();
            var hours = Enumerable.Range(0, 7 * 24).Select(h => start.ToDateTime(TimeOnly.MinValue).AddHours(h)).ToList();
            var root = new JsonObject
            {
                ["utc_offset_seconds"] = 7200,
                ["current"] = new JsonObject
                {
                    ["temperature_2m"] = 18.4, ["apparent_temperature"] = 17.2, ["relative_humidity_2m"] = 61, ["precipitation"] = 0,
                    ["weather_code"] = 2, ["wind_speed_10m"] = 12.3,
                },
                ["hourly"] = new JsonObject
                {
                    ["time"] = new JsonArray([.. hours.Select(h => (JsonNode)h.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture))]),
                    ["temperature_2m"] = new JsonArray([.. hours.Select(h => (JsonNode)(10.0 + h.Hour / 2.0))]),
                    ["precipitation_probability"] = new JsonArray([.. hours.Select(h => (JsonNode)(h.Day == 23 ? 80 : 5))]),
                    ["weather_code"] = new JsonArray([.. hours.Select(h => (JsonNode)(h.Day == 23 ? 63 : 1))]),
                },
                ["daily"] = new JsonObject
                {
                    ["time"] = new JsonArray([.. days.Select(d => (JsonNode)d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))]),
                    ["weather_code"] = new JsonArray([.. days.Select(d => (JsonNode)(d.Day == 23 ? 63 : 0))]),
                    ["temperature_2m_max"] = new JsonArray([.. days.Select(d => (JsonNode)(20.6 + d.Day - 22))]),
                    ["temperature_2m_min"] = new JsonArray([.. days.Select(d => (JsonNode)(11.2 + d.Day - 22))]),
                    ["precipitation_probability_max"] = new JsonArray([.. days.Select(d => (JsonNode)(d.Day == 23 ? 80 : 10))]),
                    ["precipitation_sum"] = new JsonArray([.. days.Select(d => (JsonNode)(d.Day == 23 ? 6.5 : 0))]),
                    ["wind_speed_10m_max"] = new JsonArray([.. days.Select(_ => (JsonNode)20)]),
                    ["sunrise"] = new JsonArray([.. days.Select(d => (JsonNode)(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T08:21"))]),
                    ["sunset"] = new JsonArray([.. days.Select(d => (JsonNode)(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T19:05"))]),
                },
            };
            return root.ToJsonString();
        }
    }

    [Fact]
    public async Task TheWeatherOfAPlaceNowTomorrowAndThisWeek()
    {
        var network = new FakeMeteo();
        var tool = new WeatherTool(() => null, new OpenMeteo(network), _ => Task.FromResult<(double, double)?>(null));

        var now = await tool.RunAsync(Args(new { place = "Bilbao" }), new ToolContext("lander"), default);
        Assert.False(now.Failed);
        Assert.Contains("Bilbao (País Vasco, España)", now.Text, StringComparison.Ordinal);
        Assert.Contains("Now: 18 °C (feels like 17), partly cloudy", now.Text, StringComparison.Ordinal);
        Assert.Contains("sunset 19:05", now.Text, StringComparison.Ordinal);
        Assert.Equal("En Bilbao ahora: 18 °C, parcialmente nublado. Hoy, entre 11 y 21 °C.", now.Reply);

        var tomorrow = await tool.RunAsync(Args(new { place = "Bilbao", when = "tomorrow" }), new ToolContext("lander"), default);
        Assert.Equal("Mañana en Bilbao: entre 12 y 22 °C, lluvia, con un 80 % de probabilidad de lluvia.", tomorrow.Reply);
        Assert.Contains("By hours:", tomorrow.Text, StringComparison.Ordinal);

        var week = await tool.RunAsync(Args(new { place = "Bilbao", when = "week" }), new ToolContext("lander"), default);
        Assert.StartsWith("En Bilbao: jueves, de 11 a 21 °C, despejado; viernes, de 12 a 22 °C, lluvia;", week.Reply, StringComparison.Ordinal);

        var day = await tool.RunAsync(Args(new { place = "Bilbao", date = "2026-10-25" }), new ToolContext("lander"), default);
        Assert.StartsWith("Domingo en Bilbao: entre 14 y 24 °C", day.Reply, StringComparison.Ordinal);

        // The place is looked up once; the forecast is kept for a while.
        Assert.Equal(1, network.Asked.Count(u => u.Host.StartsWith("geocoding", StringComparison.Ordinal)));
        Assert.Equal(1, network.Asked.Count(u => u.Host.StartsWith("api.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task APlaceIsPickedByItsCountryAndTheHomeOneIsUsedWithoutOne()
    {
        var network = new FakeMeteo();
        var meteo = new OpenMeteo(network);
        Assert.Equal("Venezuela", (await meteo.FindAsync("Valencia, Venezuela", "es", default))?.Country);
        Assert.Equal("España", (await meteo.FindAsync("Valencia", "es", default))?.Country);

        var home = new WeatherTool(() => "Bilbao", meteo, _ => throw new InvalidOperationException("Windows' location should not be asked"));
        Assert.StartsWith("En Bilbao ahora", (await home.RunAsync(Args(new { }), new ToolContext(null), default)).Reply, StringComparison.Ordinal);

        var located = new WeatherTool(() => "", meteo, _ => Task.FromResult<(double, double)?>((43.26, -2.93)));
        Assert.StartsWith("En tu zona ahora", (await located.RunAsync(Args(new { }), new ToolContext(null), default)).Reply, StringComparison.Ordinal);

        var lost = await new WeatherTool(() => "", meteo, _ => Task.FromResult<(double, double)?>(null)).RunAsync(Args(new { }), new ToolContext(null), default);
        Assert.True(lost.Failed);
        Assert.Contains("Opciones", lost.Reply, StringComparison.Ordinal);

        var nowhere = await home.RunAsync(Args(new { place = "Nowhere" }), new ToolContext(null), default);
        Assert.True(nowhere.Failed);
        Assert.Contains("Nowhere", nowhere.Reply, StringComparison.Ordinal);

        network.Down = true;
        var offline = await new WeatherTool(() => "", new OpenMeteo(network)).RunAsync(Args(new { place = "Bilbao" }), new ToolContext(null), default);
        Assert.True(offline.Failed);
        Assert.Contains("internet", offline.Reply, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("¿Qué tiempo hace?", QuickRuleKind.Weather, null)]
    [InlineData("Lander, ¿qué tiempo hace en Bilbao?", QuickRuleKind.Weather, "Bilbao")]
    [InlineData("¿Va a llover mañana?", QuickRuleKind.WeatherTomorrow, null)]
    [InlineData("¿Qué tiempo hará mañana en San Sebastián?", QuickRuleKind.WeatherTomorrow, "San Sebastián")]
    [InlineData("¿Qué tiempo hará el fin de semana?", QuickRuleKind.WeatherWeek, null)]
    [InlineData("What's the weather like in London?", QuickRuleKind.Weather, "London")]
    [InlineData("Wie wird das Wetter morgen?", QuickRuleKind.WeatherTomorrow, null)]
    public void TheWeatherIsAskedWithoutTheAi(string said, QuickRuleKind kind, string? place)
    {
        var rule = QuickRules.Match(said, ["Lander"]);
        Assert.NotNull(rule);
        Assert.Equal(kind, rule.Kind);
        Assert.Equal(place, TalkService.WeatherArguments(rule)["place"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("¿Qué tiempo hace en Bilbao y pon un temporizador de diez minutos para la pasta?")]
    [InlineData("¿Cuánto tiempo queda?")]
    public void LongerOrdersAreNotTheWeather(string said) =>
        Assert.NotEqual(QuickRuleKind.Weather, QuickRules.Match(said, ["Lander"])?.Kind);

    [Theory]
    [InlineData(10, "km", "mi", "10 km son 6,21 mi.")]
    [InlineData(350, "°F", "celsius", "350 °F son 176,67 °C.")]
    [InlineData(3, "onzas", "gramos", "3 oz son 85,05 g.")]
    [InlineData(2, "libras", "kg", "2 lb son 0,907 kg.")]
    [InlineData(3, "gigas", "MB", "3 GB son 3.000 MB.")]
    [InlineData(100, "km/h", "mph", "100 km/h son 62,14 mph.")]
    [InlineData(1, "pulgada", "cm", "1 in son 2,54 cm.")]
    public async Task UnitsWithoutTheNetwork(double value, string from, string to, string reply)
    {
        var tool = new ConvertTool(new EcbRates(Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N"), "rates.json"), new FakeEcb()));
        var result = await tool.RunAsync(Args(new { value, from, to }), new ToolContext(null), default);
        Assert.False(result.Failed, result.Text);
        Assert.Equal(reply, result.Reply);
    }

    [Fact]
    public async Task WhatCannotBeConvertedSaysWhy()
    {
        var tool = new ConvertTool(new EcbRates(Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N"), "rates.json"), new FakeEcb()));
        Assert.Contains("different things", (await tool.RunAsync(Args(new { value = 1, from = "km", to = "kg" }), new ToolContext(null), default)).Text, StringComparison.Ordinal);
        Assert.Contains("«furlongs»", (await tool.RunAsync(Args(new { value = 1, from = "furlongs", to = "m" }), new ToolContext(null), default)).Text, StringComparison.Ordinal);
    }

    /// <summary>The ECB's daily XML, from memory; <see cref="Down"/>: no internet.</summary>
    private sealed class FakeEcb : HttpMessageHandler
    {
        public bool Down { get; set; }

        public int Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            if (Down) throw new HttpRequestException("no internet");
            const string xml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
                  <Cube><Cube time='2026-10-22'><Cube currency='USD' rate='1.0800'/><Cube currency='JPY' rate='162.50'/><Cube currency='GBP' rate='0.8400'/></Cube></Cube>
                </gesmes:Envelope>
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") });
        }
    }

    [Fact]
    public async Task CurrenciesWithTheEcbsRatesKeptForWhenThereIsNoInternet()
    {
        var file = Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N"), "rates.json");
        var network = new FakeEcb();
        var tool = new ConvertTool(new EcbRates(file, network));

        var dollars = await tool.RunAsync(Args(new { value = 50, from = "dólares", to = "euros" }), new ToolContext(null), default);
        Assert.Equal("50,00 USD son 46,30 EUR (cambio del BCE del 22/10/2026).", dollars.Reply);
        Assert.Contains("1 USD = 0.925926 EUR", dollars.Text, StringComparison.Ordinal);

        // «libras» to money is sterling; to kilos, a weight.
        var pounds = await tool.RunAsync(Args(new { value = 10, from = "libras", to = "EUR" }), new ToolContext(null), default);
        Assert.Equal("10,00 GBP son 11,90 EUR (cambio del BCE del 22/10/2026).", pounds.Reply);
        Assert.Equal(1, network.Asked);
        Assert.True(File.Exists(file));

        // Another session without internet: the rates kept, saying they could not be updated.
        network.Down = true;
        var offline = new ConvertTool(new EcbRates(file, network));
        var kept = JsonNode.Parse(File.ReadAllText(file))!;
        kept["fetched"] = "2026-01-01T00:00:00Z";
        File.WriteAllText(file, kept.ToJsonString());
        var stale = await offline.RunAsync(Args(new { value = 1000, from = "JPY", to = "USD" }), new ToolContext(null), default);
        Assert.False(stale.Failed, stale.Text);
        Assert.Contains("could not be updated", stale.Text, StringComparison.Ordinal);
        Assert.Equal("1.000,00 JPY son 6,65 USD (cambio del BCE del 22/10/2026).", stale.Reply);

        var nothing = await new ConvertTool(new EcbRates(file + ".none", network)).RunAsync(Args(new { value = 1, from = "USD", to = "EUR" }), new ToolContext(null), default);
        Assert.True(nothing.Failed);
        Assert.Contains("internet", nothing.Reply, StringComparison.Ordinal);
    }
}
