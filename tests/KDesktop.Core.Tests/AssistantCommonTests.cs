using System.Text.Json.Nodes;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Core.Tests;

/// <summary>Stage 13.13: the settings every assistant has the same way, kept and sent only when not as usual.</summary>
public class AssistantCommonTests
{
    [Fact]
    public void OnlyWhatIsNotAsUsualTravelsAndItComesBackWithinItsRanges()
    {
        var all = new Dictionary<string, AssistantCommon>
        {
            ["lander"] = new() { Size = 1.5, Dragging = false },
            ["kobii"] = AssistantCommon.Default,
        };
        var json = AssistantCommon.ToJson(all);
        Assert.Equal(["lander"], json.Select(p => p.Key));

        var back = AssistantCommon.FromJson(json);
        Assert.Equal(all["lander"], back["lander"]);

        // A hand-edited config: back within the ranges; something unreadable is left out (as usual).
        var odd = AssistantCommon.FromJson(new JsonObject
        {
            ["pip"] = new JsonObject { ["size"] = 9, ["volume"] = -1, ["liveliness"] = 5 },
            ["dot"] = new JsonObject { ["size"] = "big" },
        });
        Assert.Equal(new AssistantCommon { Size = AssistantCommon.MaxSize, Volume = 0, Liveliness = AssistantCommon.MaxLiveliness }, odd["pip"]);
        Assert.False(odd.ContainsKey("dot"));
        Assert.True(AssistantCommon.Default.IsDefault);
    }

    [Fact]
    public void AnAssistantTakesWhatItsManifestNamesAndOneWithoutCodeTakesThemAll()
    {
        static AssistantManifest Manifest(string entry, string common) => AssistantManifest.Parse($$"""
            { "id": "x", "name": "X", "version": "1.0.0", "author": "a", "sdk": "1.9", "entry": "{{entry}}", {{common}}
              "traits": { "travel": "walk", "gestures": [] } }
            """);

        var coded = Manifest("lib/X.dll!X.X", "\"common\": [\"size\", \"dragging\"],");
        Assert.True(coded.Takes(AssistantCommon.SizeKey));
        Assert.True(coded.Takes(AssistantCommon.DraggingKey));
        Assert.False(coded.Takes(AssistantCommon.VolumeKey));
        Assert.False(coded.Takes(AssistantCommon.LivelinessKey));

        var old = Manifest("lib/X.dll!X.X", string.Empty);
        Assert.DoesNotContain(AssistantCommon.Keys, old.Takes);

        var character = Manifest("character.json", string.Empty);
        Assert.All(AssistantCommon.Keys, key => Assert.True(character.Takes(key)));
    }

    /// <summary>Stage 13.7: one that draws what it says itself, and the user who prefers K-Desktop's bubble for it.</summary>
    [Fact]
    public void OwnSpeechIsForOneWithCodeThatSaysSoAndTheBubbleChoiceTravels()
    {
        static AssistantManifest Manifest(string entry, string speech) => AssistantManifest.Parse($$"""
            { "id": "x", "name": "X", "version": "1.0.0", "author": "a", "sdk": "1.15", "entry": "{{entry}}", {{speech}}
              "traits": { "travel": "walk", "gestures": [] } }
            """);

        Assert.True(Manifest("lib/X.dll!X.X", "\"speech\": \"own\",").OwnSpeech);
        Assert.False(Manifest("lib/X.dll!X.X", "\"speech\": \"host\",").OwnSpeech);
        Assert.False(Manifest("lib/X.dll!X.X", string.Empty).OwnSpeech);
        Assert.False(Manifest("character.json", "\"speech\": \"own\",").OwnSpeech);

        var json = AssistantCommon.ToJson(new Dictionary<string, AssistantCommon> { ["kumo"] = new() { KDesktopBubble = true } });
        Assert.Equal("""{"kumo":{"size":1,"volume":1,"liveliness":1,"dragging":true,"kdesktopBubble":true}}""", json.ToJsonString());
        Assert.True(AssistantCommon.FromJson(json)["kumo"].KDesktopBubble);
    }
}
