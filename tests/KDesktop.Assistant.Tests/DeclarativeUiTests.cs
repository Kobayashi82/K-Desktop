using System.Globalization;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 1.9: settings.json and panel.json as K-Desktop draws them, the values it saves and the panel data.</summary>
public class DeclarativeUiTests
{
    private static string Root => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", ".."));

    private static string Dot(string file) => File.ReadAllText(Path.Combine(Root, "samples/DotAssistant/Package", file));

    private static string Lander(string file) => File.ReadAllText(Path.Combine(Contracts.LanderPackage, file));

    [Fact]
    public void TheLanderSettingsReadWithEveryControl()
    {
        var layout = SettingsLayout.Parse(Lander("settings.json"));
        Assert.Equal(["General", "Aspecto", "Comportamiento", "Pilotaje", "Sonido", "Piloto"], layout.Pages.Select(p => p.Title.In("es")));
        Assert.Equal("kd:Settings", layout.Pages[0].Icon);

        var items = layout.Items.ToList();
        var types = items.Select(i => i.Type).ToHashSet();
        foreach (var type in new[] { "toggle", "slider", "choice", "gallery", "apps", "hotkey", "button", "info", "keys" }) Assert.Contains(type, types);

        var activity = items.Single(i => i.Key == "activity");
        Assert.Equal((0d, 2d, 0.1), (activity.Min, activity.Max, activity.Step));
        Assert.Equal("Tranquilo", activity.MinLabel!.In("es"));

        var errors = items.Single(i => i.Key == "pilotErrors");
        Assert.Equal(4, errors.Options.Count);
        Assert.Equal(2, errors.Options[2].Value.GetValue<int>());
        Assert.Equal("Normales", errors.Options[2].Label.In("es"));

        Assert.Equal("skins", items.Single(i => i.Key == "skin").Source);
        Assert.Equal("landOnContent", items.Single(i => i.Key == "contentExcludedApps").EnabledWhen);
        Assert.True(items.Single(i => i.Type == "button" && i.ResetData).Confirm is not null);

        var keys = items.Single(i => i.Type == "keys").KeyRows;
        Assert.Equal(7, keys.Count);
        Assert.Equal(["A", "D"], keys[2].Keys);
    }

    [Fact]
    public void PlainTextOptionsAreValueAndLabel()
    {
        var layout = SettingsLayout.Parse("""
            { "pages": [ { "title": "P", "sections": [ { "items": [
              { "type": "choice", "key": "speed", "label": "Maniobras", "options": ["Vistosas", "Normales"], "default": "Normales" }
            ] } ] } ] }
            """);
        var choice = layout.Items.Single();
        Assert.Equal("Vistosas", choice.Options[0].Value.GetValue<string>());
        Assert.Equal("Vistosas", choice.Options[0].Label.In("fr"));
    }

    [Fact]
    public void DefaultsAgreeWithTheValuesTheAssistantGets()
    {
        foreach (var json in new[] { Dot("settings.json"), Lander("settings.json") })
            Assert.True(JsonNode.DeepEquals(AssistantSettingsValues.Defaults(json), SettingsLayout.Parse(json).Defaults()));
    }

    [Fact]
    public void PanelsRead()
    {
        var dot = PanelLayout.Parse(Dot("panel.json"));
        Assert.Equal(["stat", "buttons"], dot.Cards.Select(c => c.Type));
        Assert.Equal("dot.jump", dot.Cards[1].Items.Single().Extra);

        var lander = PanelLayout.Parse(Lander("panel.json"));
        var progress = lander.Cards.Single(c => c.Type == "progress");
        Assert.Equal("pilot.nextLevel", progress.Max!.GetValue<string>());
        Assert.Equal(3, lander.Cards.Single(c => c.Type == "buttons").Items.Count);
    }

    [Fact]
    public void SettingsAndPanelButtonsOfDotUseItsExtras()
    {
        var extras = AssistantManifest.Parse(Dot("assistant.json")).Extras.Select(e => e.Id).ToHashSet();
        Assert.All(SettingsLayout.Parse(Dot("settings.json")).Items.Where(i => i.Extra is not null), i => Assert.Contains(i.Extra!, extras));
        Assert.All(PanelLayout.Parse(Dot("panel.json")).Cards.SelectMany(c => c.Items), b => Assert.Contains(b.Extra, extras));
    }

    [Fact]
    public void BindsFollowDottedKeys()
    {
        var data = JsonNode.Parse("""{ "pilot": { "level": 7 }, "jumps": 3 }""")!.AsObject();
        Assert.Equal(7, AssistantUi.Resolve(data, "pilot.level")!.GetValue<int>());
        Assert.Equal(3, AssistantUi.Resolve(data, "jumps")!.GetValue<int>());
        Assert.Null(AssistantUi.Resolve(data, "pilot.points"));
        Assert.Null(AssistantUi.Resolve(data, "jumps.more"));
        Assert.Null(AssistantUi.Resolve(null, "jumps"));
    }

    [Fact]
    public void PublishedDataIsMergedKeyByKey()
    {
        var data = JsonNode.Parse("""{ "pilot": { "level": 7, "points": 10 }, "races": { "best": ["A"] } }""")!.AsObject();
        AssistantUi.Merge(data, JsonNode.Parse("""{ "pilot": { "points": 12 }, "races": { "best": ["B", "C"] }, "jumps": 1 }""")!.AsObject());
        Assert.Equal(7, data["pilot"]!["level"]!.GetValue<int>());
        Assert.Equal(12, data["pilot"]!["points"]!.GetValue<int>());
        Assert.Equal(2, data["races"]!["best"]!.AsArray().Count); // lists replace
        Assert.Equal(1, data["jumps"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("sound", true)]
    [InlineData("!sound", false)]
    [InlineData("missing", false)]
    [InlineData("!missing", true)]
    public void EnabledWhenFollowsAToggle(string? when, bool enabled) =>
        Assert.Equal(enabled, AssistantUi.IsEnabled(when, new JsonObject { ["sound"] = true }));

    [Theory]
    [InlineData(31.0, 12.0, 64.0, 2.0, 32.0)]
    [InlineData(0.31, 0.0, 2.0, 0.1, 0.3)]
    [InlineData(100.0, 0.0, 1.0, 0.05, 1.0)]
    [InlineData(-5.0, 0.0, 10.0, null, 0.0)]
    public void ValuesSnapToTheirSteps(double value, double min, double max, double? step, double expected) =>
        Assert.Equal(expected, AssistantUi.Snap(value, min, max, step), 10);

    [Fact]
    public void WholeNumbersAreSavedWhole()
    {
        Assert.Equal("28", AssistantUi.NumberNode(28, 12, 2).ToJsonString());
        Assert.Equal("0.3", AssistantUi.NumberNode(0.3, 0, 0.1).ToJsonString());
        Assert.Equal("1", AssistantUi.NumberNode(1, 0, null).ToJsonString());
    }

    [Fact]
    public void NumbersUseTheItemsFormat()
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        Assert.Equal("28 px", AssistantUi.FormatNumber("{0} px", 28, es));
        Assert.Equal("1,5 s", AssistantUi.FormatNumber("{0} s", 1.5, es));
        Assert.Equal("50 %", AssistantUi.FormatNumber("{0:P0}", 0.5, es).Replace(' ', ' '));
        Assert.Equal("3 / 10 puntos", AssistantUi.FormatNumber("{0} / {1} puntos", 3, es, 10));
        Assert.Equal("3 / 10", AssistantUi.FormatNumber(null, 3, es, 10));
        Assert.Equal("1.234", AssistantUi.FormatNumber(null, 1234, es));
        Assert.Equal("7", AssistantUi.FormatNumber("{0", 7, es)); // a broken format shows the number
    }

    [Fact]
    public void CardsShowAnyKindOfValue()
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        Assert.Equal("—", AssistantUi.Display(null, null, es, "es"));
        Assert.Equal("132", AssistantUi.Display(JsonValue.Create(132), null, es, "es"));
        Assert.Equal("Circuito A", AssistantUi.Display(JsonValue.Create("Circuito A"), null, es, "es"));
        Assert.Equal("Hola", AssistantUi.Display(JsonNode.Parse("""{"es":"Hola","en":"Hi"}"""), null, es, "es"));
        Assert.Equal(["A — 0:41", "B"], AssistantUi.Lines(JsonNode.Parse("""["A — 0:41", "B"]"""), "es"));
    }

    [Fact]
    public void AchievementsAndGalleriesComeFromTheData()
    {
        var achievements = PanelAchievement.List(JsonNode.Parse("""
            [ "firstLanding",
              { "id": "perfect10", "name": { "es": "Diez perfectos" }, "unlocked": false, "progress": 0.4 } ]
            """), "es");
        Assert.Equal(("firstLanding", true), (achievements[0].Name, achievements[0].Unlocked));
        Assert.Equal(("Diez perfectos", false, 0.4), (achievements[1].Name, achievements[1].Unlocked, achievements[1].Progress));

        var skins = GalleryEntry.List(JsonNode.Parse("""
            [ { "id": "classic", "name": "Clásico", "image": "assets/skins/classic.png" },
              { "id": "gold", "name": "Dorado", "image": "assets/skins/gold.png", "locked": true, "lockText": "Nivel 10" },
              { "name": "sin id" } ]
            """));
        Assert.Equal(["classic", "gold"], skins.Select(s => s.Id));
        Assert.True(skins[1].Locked);
        Assert.Equal("Nivel 10", skins[1].LockText!.In("es"));
    }

    [Fact]
    public void OnlyTheChangedValuesAreSaved()
    {
        const string id = "decl-ui-test";
        AssistantSettingsValues.WriteSaved(id, new JsonObject { ["size"] = 40 });
        Assert.Equal(40, AssistantSettingsValues.ReadSaved(id)["size"]!.GetValue<int>());

        // What is saved goes over the defaults; keys the package no longer has are left out.
        var folder = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "pkg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"), Dot("settings.json"));
        AssistantSettingsValues.WriteSaved(id, new JsonObject { ["size"] = 40, ["gone"] = 1 });
        var manifest = AssistantManifest.Parse(Dot("assistant.json")) with { Id = id };
        var values = AssistantSettingsValues.Read(folder, manifest);
        Assert.Equal(40, values["size"]!.GetValue<int>());
        Assert.Equal("blue", values["color"]!.GetValue<string>());
        Assert.False(values.ContainsKey("gone"));

        AssistantSettingsValues.WriteSaved(id, []);
        Assert.False(File.Exists(AssistantPaths.SettingsValues(id)));
        Assert.Empty(AssistantSettingsValues.ReadSaved(id));
    }

    [Fact]
    public async Task ThePanelGoesAtMostFourTimesASecond()
    {
        var sent = new List<PanelMessage>();
        var publisher = new PanelPublisher(m =>
        {
            lock (sent) sent.Add(m);
        });
        publisher.Publish("dot", new JsonObject { ["jumps"] = 1 });
        for (var i = 2; i <= 50; i++) publisher.Publish("dot", new JsonObject { ["jumps"] = i, ["at"] = new JsonObject { ["i"] = i } });
        publisher.Publish("dot", new JsonObject { ["other"] = true });

        // The first at once; the rest merged into one, a moment later.
        lock (sent) Assert.Single(sent);
        await Task.Delay(PanelPublisher.Interval * 3);
        lock (sent)
        {
            Assert.Equal(2, sent.Count);
            var last = sent[1];
            Assert.Equal("dot", last.Assistant);
            Assert.Equal(50, last.Data["jumps"]!.GetValue<int>());
            Assert.Equal(50, last.Data["at"]!["i"]!.GetValue<int>());
            Assert.True(last.Data["other"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task EachAssistantWaitsItsTurnWithoutMixing()
    {
        var sent = new List<PanelMessage>();
        var publisher = new PanelPublisher(m => { lock (sent) sent.Add(m); });
        publisher.Publish("dot", new JsonObject { ["a"] = 1 });    // at once
        publisher.Publish("dot", new JsonObject { ["a"] = 2 });    // waits…
        publisher.Publish("lander", new JsonObject { ["b"] = 3 }); // …and so does another assistant's, apart
        publisher.Publish("dot", new JsonObject { ["a"] = 4 });    // merged with Dot's pending one
        await Task.Delay(PanelPublisher.Interval * 2);
        lock (sent)
        {
            Assert.Equal(["dot", "dot", "lander"], sent.Select(m => m.Assistant));
            Assert.Equal(4, sent[1].Data["a"]!.GetValue<int>());
            Assert.False(sent[1].Data.ContainsKey("b"));
            Assert.Equal(["b"], sent[2].Data.Select(p => p.Key));
        }
    }
}
