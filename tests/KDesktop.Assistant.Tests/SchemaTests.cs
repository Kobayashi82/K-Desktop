using System.Text.Json.Nodes;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 0.3: the Lander examples validate against the package schemas, and broken ones do not.</summary>
public class SchemaTests
{
    [Theory]
    [InlineData("assistant", "assistant.json")]
    [InlineData("settings", "settings.json")]
    [InlineData("panel", "panel.json")]
    [InlineData("persona", "persona/es.json")]
    [InlineData("voice", "voice/lander.json")]
    public void LanderExamplesAreValid(string schema, string file)
    {
        var errors = Contracts.ValidateFile(schema, Path.Combine(Contracts.LanderPackage, file));
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    public static TheoryData<string, string> BrokenManifests => new()
    {
        { "without id", "id" },
        { "without entry", "entry" },
        { "without traits", "traits" },
    };

    [Theory]
    [MemberData(nameof(BrokenManifests))]
    public void ManifestWithoutRequiredFieldIsRejected(string _, string field)
    {
        var json = Contracts.Mutate(Path.Combine(Contracts.LanderPackage, "assistant.json"), m => m.Remove(field));
        Assert.NotEmpty(Contracts.Validate("assistant", json));
    }

    [Theory]
    [InlineData("id", "Lander Bonito")]
    [InlineData("version", "1.0")]
    [InlineData("sdk", "uno")]
    [InlineData("entry", "../evil.dll!X")]
    [InlineData("voice", "../../config.json")]
    [InlineData("updates", "http://example.com/x.kai")]
    public void ManifestWithBadValueIsRejected(string field, string value)
    {
        var json = Contracts.Mutate(Path.Combine(Contracts.LanderPackage, "assistant.json"), m => m[field] = value);
        Assert.NotEmpty(Contracts.Validate("assistant", json));
    }

    [Fact]
    public void UnknownGestureIsRejected()
    {
        var json = Contracts.Mutate(Path.Combine(Contracts.LanderPackage, "assistant.json"),
            m => m["traits"]!["gestures"]!.AsArray().Add("fly"));
        Assert.NotEmpty(Contracts.Validate("assistant", json));
    }

    [Fact]
    public void UnknownManifestFieldIsRejected()
    {
        var json = Contracts.Mutate(Path.Combine(Contracts.LanderPackage, "assistant.json"), m => m["runAsAdmin"] = true);
        Assert.NotEmpty(Contracts.Validate("assistant", json));
    }

    [Theory]
    [InlineData("""{ "type": "slider", "key": "x", "label": "X" }""")]                                // no min / max
    [InlineData("""{ "type": "toggle", "key": "x", "label": "X", "default": "yes" }""")]              // wrong default
    [InlineData("""{ "type": "toggle", "key": "Bad Key", "label": "X" }""")]                          // key format
    [InlineData("""{ "type": "toggle", "key": "x", "label": "X", "min": 1 }""")]                      // field of another control
    [InlineData("""{ "type": "gallery", "key": "x", "label": "X" }""")]                               // neither source nor items
    [InlineData("""{ "type": "button", "label": "X" }""")]                                            // does nothing
    [InlineData("""{ "type": "choice", "key": "x", "label": "X", "options": ["only one"] }""")]       // one option
    [InlineData("""{ "type": "web", "key": "x", "label": "X" }""")]                                   // unknown control
    public void BrokenSettingIsRejected(string item)
    {
        var json = $$"""{ "pages": [ { "title": "P", "sections": [ { "items": [ {{item}} ] } ] } ] }""";
        Assert.NotEmpty(Contracts.Validate("settings", json));
    }

    [Fact]
    public void SettingTextCanBeTranslated()
    {
        var json = """
            { "pages": [ { "title": { "es": "General", "en": "General" }, "sections": [ { "items": [
              { "type": "toggle", "key": "x", "label": { "es": "Sí", "en": "Yes" }, "default": true } ] } ] } ] }
            """;
        Assert.Empty(Contracts.Validate("settings", json));
    }

    [Theory]
    [InlineData("""{ "type": "stat", "label": "X" }""")]
    [InlineData("""{ "type": "buttons", "items": [ { "label": "X" } ] }""")]
    [InlineData("""{ "type": "chart", "bind": "x" }""")]
    public void BrokenPanelCardIsRejected(string card)
    {
        Assert.NotEmpty(Contracts.Validate("panel", $$"""{ "cards": [ {{card}} ] }"""));
    }

    [Theory]
    [InlineData("""{ "fx": "pitch" }""")]
    [InlineData("""{ "fx": "echo", "ms": 90, "feedback": 1.5 }""")]
    [InlineData("""{ "fx": "autotune" }""")]
    public void BrokenVoiceEffectIsRejected(string fx)
    {
        var json = $$"""{ "name": "X", "engine": { "type": "pocket" }, "chain": [ {{fx}} ] }""";
        Assert.NotEmpty(Contracts.Validate("voice", json));
    }

    [Fact]
    public void PersonaNeedsAPrompt()
    {
        Assert.NotEmpty(Contracts.Validate("persona", """{ "phrases": { "ok": ["Vale."] } }"""));
        Assert.NotEmpty(Contracts.Validate("persona", """{ "prompt": "Un robot que ayuda.", "phrases": { "ok": [] } }"""));
    }

    [Fact]
    public void EveryLanderSettingKeyIsUnique()
    {
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(Contracts.LanderPackage, "settings.json")))!;
        var keys = settings["pages"]!.AsArray()
            .SelectMany(p => p!["sections"]!.AsArray())
            .SelectMany(s => s!["items"]!.AsArray())
            .Select(i => i!["key"]?.GetValue<string>())
            .OfType<string>().ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void LanderSettingsButtonsUseDeclaredExtras()
    {
        var extras = JsonNode.Parse(File.ReadAllText(Path.Combine(Contracts.LanderPackage, "assistant.json")))!["extras"]!.AsArray()
            .Select(e => e!["id"]!.GetValue<string>()).ToHashSet();
        foreach (var file in new[] { "settings.json", "panel.json" })
        {
            var text = File.ReadAllText(Path.Combine(Contracts.LanderPackage, file));
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"extra\"\\s*:\\s*\"([^\"]+)\""))
                Assert.Contains(m.Groups[1].Value, extras);
        }
    }
}
