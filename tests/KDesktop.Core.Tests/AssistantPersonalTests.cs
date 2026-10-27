using System.Text.Json.Nodes;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>5.0.105: what each assistant has of its own, and the language of one flow of work.</summary>
public class AssistantPersonalTests
{
    [Fact]
    public void AFlowOfWorkSpeaksItsOwnLanguageAndTheRestKeepsTheirs()
    {
        Assert.Equal("es", Loc.Instance.Language);
        Assert.Equal("Restablecer", Loc.T("assistantPersonal.reset"));
        using (Loc.Instance.Use("en"))
        {
            Assert.Equal("en", Loc.Instance.Language);
            Assert.Equal("Reset", Loc.T("assistantPersonal.reset"));
            Assert.Equal("en", Loc.Instance.Culture.Name);
            using (Loc.Instance.Use("fr")) Assert.Equal("Rétablir", Loc.T("assistantPersonal.reset"));
            Assert.Equal("Reset", Loc.T("assistantPersonal.reset"));
        }
        Assert.Equal("Restablecer", Loc.T("assistantPersonal.reset"));
        // None, empty or the loaded one: nothing changes.
        using (Loc.Instance.Use(null)) Assert.Equal("es", Loc.Instance.Language);
        using (Loc.Instance.Use("es")) Assert.Equal("es", Loc.Instance.Language);
    }

    [Fact]
    public async Task TheLanguageFollowsWhatTheFlowAwaitsAndStarts()
    {
        Task<string> inner;
        using (Loc.Instance.Use("it")) inner = LaterAsync();
        // Outside, at once, the loaded one again; inside, still its own after the awaits and in what it started.
        Assert.Equal("es", Loc.Instance.Language);
        Assert.Equal("Ripristina|Ripristina", await inner);

        static async Task<string> LaterAsync()
        {
            await Task.Delay(30);
            var started = await Task.Run(() => Loc.T("assistantPersonal.reset"));
            return Loc.T("assistantPersonal.reset") + "|" + started;
        }
    }

    [Fact]
    public void WhatItHasOfItsOwnTravelsAndIsSaved()
    {
        var personal = new Dictionary<string, AssistantPersonal>
        {
            ["lander"] = new() { Language = "en", Prompt = "You are Lander.", KokoroVoice = "am_adam", Profile = AssistantPersonal.OwnProfile, Speed = 1.25 },
            ["dot"] = new() { Engine = "windows" },
            ["kumo"] = new() { Volume = 0.6 },
        };
        var section = AssistantPersonal.ToJson(personal);
        Assert.Equal("en", section["lander"]!["language"]!.GetValue<string>());
        // What it does not have of its own does not travel.
        Assert.Null(section["dot"]!["prompt"]);
        Assert.Null(section["dot"]!["speed"]);

        var back = AssistantPersonal.FromJson(JsonNode.Parse(section.ToJsonString())!.AsObject());
        Assert.Equal(personal["lander"], back["lander"]);
        Assert.Equal(personal["dot"], back["dot"]);
        Assert.True(back["dot"].HasOwnVoice);
        Assert.False(back["dot"].WithoutVoice().HasOwnVoice);
        Assert.True(back["dot"].WithoutVoice().IsEmpty);
        Assert.False(back["lander"].WithoutVoice().IsEmpty);
        // 5.0.289: its own volume is part of its voice.
        Assert.Equal(0.6, back["kumo"].Volume);
        Assert.True(back["kumo"].HasOwnVoice);
        Assert.True(back["kumo"].WithoutVoice().IsEmpty);
        Assert.Empty(AssistantPersonal.FromJson(new JsonObject { ["bad"] = "nope" }));

        var config = new AppConfig();
        config.Settings.AssistantPersonal = personal;
        var read = Json.Deserialize<AppConfig>(Json.Serialize(config))!;
        Assert.Equal(personal["lander"], read.Settings.AssistantPersonal["lander"]);
    }

    /// <summary>5.0.112: what K-Desktop had of its own under the empty key (5.0.111) moves to its id as an assistant.</summary>
    [Fact]
    public void KDesktopsOwnMovesToItsId()
    {
        var config = new AppConfig();
        var own = new AssistantPersonal { Language = "en", Prompt = "Be brief." };
        config.Settings.AssistantPersonal = new() { [""] = own, ["lander"] = new AssistantPersonal { Language = "fr" } };
        Assert.True(config.Upgrade());
        Assert.False(config.Settings.AssistantPersonal.ContainsKey(""));
        Assert.Equal(own, config.Settings.AssistantPersonal[AssistantTargeting.KDesktop]);
        Assert.Equal("fr", config.Settings.AssistantPersonal["lander"].Language);
    }
}
