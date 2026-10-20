using KDesktop.Core.Assistant;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>Stage 1.11: several assistants on the desktop, who takes what is not addressed to anyone, and their wake words.</summary>
public class AssistantTargetingTests
{
    private static readonly string[] Both = ["dot", "lander"];

    [Fact]
    public void TheLastOneTalkedToTakesIt()
    {
        Assert.Equal("lander", AssistantTargeting.Pick(AssistantTarget.LastTalkedTo, null, "lander", Both));
        // Nobody talked to yet, or that one left the desktop: the first one put there.
        Assert.Equal("dot", AssistantTargeting.Pick(AssistantTarget.LastTalkedTo, null, null, Both));
        Assert.Equal("dot", AssistantTargeting.Pick(AssistantTarget.LastTalkedTo, null, "blob", Both));
        Assert.Null(AssistantTargeting.Pick(AssistantTarget.LastTalkedTo, null, "dot", []));
    }

    [Fact]
    public void AFixedOneTakesItWhileItIsOnTheDesktop()
    {
        Assert.Equal("lander", AssistantTargeting.Pick(AssistantTarget.Fixed, "lander", "dot", Both));
        // Not on the desktop: as «the last one talked to».
        Assert.Equal("dot", AssistantTargeting.Pick(AssistantTarget.Fixed, "blob", "dot", Both));
        Assert.Equal("dot", AssistantTargeting.Pick(AssistantTarget.Fixed, null, null, Both));
    }

    [Fact]
    public void AtRandomPicksAnyOfThemOnEveryOrder()
    {
        var random = new Random(82);
        var picked = Enumerable.Range(0, 40).Select(_ => AssistantTargeting.Pick(AssistantTarget.Random, null, null, Both, random)).OfType<string>().ToHashSet();
        Assert.Equal(Both.ToHashSet(), picked);
        Assert.Equal("dot", AssistantTargeting.Pick(AssistantTarget.Random, null, null, ["dot"], random));
    }

    [Fact]
    public void AWakeWordCallsOneAssistantAtMost()
    {
        var words = AssistantTargeting.Assign(new Dictionary<string, string>(), "dot", "w1");
        words = AssistantTargeting.Assign(words, "lander", "w2");
        Assert.Equal("dot", AssistantTargeting.ForWakeWord(words, "w1"));
        Assert.Equal("lander", AssistantTargeting.ForWakeWord(words, "w2"));
        Assert.Null(AssistantTargeting.ForWakeWord(words, "w3"));

        // Giving Dot's word to the Lander takes it from Dot (and the Lander's old one is free again).
        words = AssistantTargeting.Assign(words, "lander", "w1");
        Assert.Equal(new Dictionary<string, string> { ["lander"] = "w1" }, words);
        words = AssistantTargeting.Assign(words, "lander", null);
        Assert.Empty(words);
    }

    [Fact]
    public void TheOneAssistantOfBeforeIsStillOnTheDesktop()
    {
        var config = Json.Deserialize<AppConfig>("""{ "schemaVersion": 3, "settings": { "assistantEnabled": true, "assistantActive": "dot" } }""")!;
        Assert.True(config.Upgrade());
        Assert.Equal(["dot"], config.Settings.AssistantsOnDesktop);
        Assert.Null(config.Settings.AssistantActive);
        Assert.Equal(AssistantTarget.LastTalkedTo, config.Settings.AssistantTarget);

        // Saved again: the old setting is gone and the list stays.
        var saved = Json.Serialize(config);
        Assert.DoesNotContain("assistantActive", saved);
        var again = Json.Deserialize<AppConfig>(saved)!;
        Assert.False(again.Upgrade());
        Assert.Equal(["dot"], again.Settings.AssistantsOnDesktop);
    }

    [Fact]
    public void WakeWordsOfTheAssistantsAreSaved()
    {
        var config = new AppConfig();
        config.Settings.AssistantWakeWords = AssistantTargeting.Assign(config.Settings.AssistantWakeWords, "dot", "w1");
        config.Settings.AssistantTarget = AssistantTarget.Fixed;
        config.Settings.AssistantTargetId = "dot";
        var back = Json.Deserialize<AppConfig>(Json.Serialize(config))!;
        Assert.Equal("w1", back.Settings.AssistantWakeWords["dot"]);
        Assert.Equal(AssistantTarget.Fixed, back.Settings.AssistantTarget);
        Assert.Equal("dot", back.Settings.AssistantTargetId);
    }
}
