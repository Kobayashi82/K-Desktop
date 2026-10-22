using KDesktop.Core.Assistant;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>Stage 1.11: several assistants on the desktop and their wake words; 5.0.113: each one called by its own word or key.</summary>
public class AssistantTargetingTests
{
    private static readonly string[] Both = ["dot", "lander"];

    [Fact]
    public void KDesktopTurnsToTheLastOneTalkedTo()
    {
        Assert.Equal("lander", AssistantTargeting.Pick("lander", Both));
        // Nobody talked to yet, or that one left the desktop: the first one put there.
        Assert.Equal("dot", AssistantTargeting.Pick(null, Both));
        Assert.Equal("dot", AssistantTargeting.Pick("blob", Both));
        Assert.Null(AssistantTargeting.Pick("dot", []));
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

        // Saved again: the old setting is gone and the list stays.
        var saved = Json.Serialize(config);
        Assert.DoesNotContain("assistantActive", saved);
        var again = Json.Deserialize<AppConfig>(saved)!;
        Assert.False(again.Upgrade());
        Assert.Equal(["dot"], again.Settings.AssistantsOnDesktop);
    }

    /// <summary>5.0.113: the word «for the assistant» is nobody's, and the one key to talk goes to the last one talked to.</summary>
    [Fact]
    public void WhatWasForAnyAssistantGoes()
    {
        var config = Json.Deserialize<AppConfig>("""
            { "schemaVersion": 3, "settings": { "assistantEnabled": true, "assistantTarget": "fixed", "assistantTargetId": "dot",
              "assistantWakeWords": { "lander": "w1", "*": "w2" }, "assistantTalkHotkey": "Pause", "assistantLastTalkedTo": "lander" } }
            """)!;
        Assert.True(config.Upgrade());
        Assert.Equal(new Dictionary<string, string> { ["lander"] = "w1" }, config.Settings.AssistantWakeWords);
        Assert.Equal(new Dictionary<string, string> { ["lander"] = "Pause" }, config.Settings.AssistantTalkHotkeys);
        var saved = Json.Serialize(config);
        Assert.DoesNotContain("assistantTalkHotkey\"", saved);
        Assert.DoesNotContain("assistantTarget", saved);
        Assert.False(Json.Deserialize<AppConfig>(saved)!.Upgrade());

        // Nobody talked to yet: K-Desktop's.
        var empty = Json.Deserialize<AppConfig>("""{ "schemaVersion": 3, "settings": { "assistantTalkHotkey": "F8" } }""")!;
        Assert.True(empty.Upgrade());
        Assert.Equal("F8", empty.Settings.AssistantTalkHotkeys[AssistantTargeting.KDesktop]);
    }

    /// <summary>5.0.113: a key to talk is of one at most, and not one a command or K-Desktop already answers.</summary>
    [Fact]
    public void KeysToTalkSayWhoHasThem()
    {
        var config = new AppConfig();
        config.Settings.AssistantEnabled = true;
        config.Settings.AssistantTalkHotkeys = new() { ["lander"] = "Pause" };
        config.Commands.Add(new Command { Name = "Mío", Hotkeys = ["F8"] });
        Assert.Equal(["lander"], config.HotkeyUsers("Pause"));
        Assert.Empty(config.HotkeyUsers("Pause", exceptAssistant: "lander"));
        Assert.Equal(["Mío"], config.HotkeyUsers("F8", exceptAssistant: "lander"));
        Assert.Contains("Pause", config.Settings.OwnHotkeys);
    }

    [Fact]
    public void WakeWordsAndKeysOfTheAssistantsAreSaved()
    {
        var config = new AppConfig();
        config.Settings.AssistantWakeWords = AssistantTargeting.Assign(config.Settings.AssistantWakeWords, "dot", "w1");
        config.Settings.AssistantTalkHotkeys = new() { ["dot"] = "F9" };
        var back = Json.Deserialize<AppConfig>(Json.Serialize(config))!;
        Assert.Equal("w1", back.Settings.AssistantWakeWords["dot"]);
        Assert.Equal("F9", back.Settings.AssistantTalkHotkeys["dot"]);
    }
}
