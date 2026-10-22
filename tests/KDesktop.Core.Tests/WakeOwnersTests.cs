using KDesktop.Core.Assistant;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Tests;

/// <summary>5.0.109: the recorded wake words are a pool and each one has one owner; nobody's are not listened to.</summary>
public class WakeOwnersTests
{
    private static readonly Dictionary<string, string> Assistants = new() { ["lander"] = "w-lander", ["dot"] = "w-dot", ["kdesktop"] = "w-any" };

    [Fact]
    public void EachWordHasOneOwner()
    {
        List<string> kdesktop = ["w-kd"];
        Assert.Equal("lander", WakeOwners.Of("w-lander", Assistants, kdesktop));
        Assert.Equal(AssistantTargeting.KDesktop, WakeOwners.Of("w-any", Assistants, kdesktop));
        Assert.Equal(WakeOwners.Commands, WakeOwners.Of("w-kd", Assistants, kdesktop));
        Assert.Null(WakeOwners.Of("w-loose", Assistants, kdesktop));
    }

    /// <summary>What the user saw: «Dot» with Dot hidden opened K-Desktop's listening and did nothing.</summary>
    [Fact]
    public void OnlyWordsOfSomeoneThatCanAnswerAreListenedTo()
    {
        string[] words = ["w-lander", "w-dot", "w-any", "w-kd", "w-loose"];
        List<string> kdesktop = ["w-kd"];
        Assert.Equal(["w-dot", "w-loose"], WakeOwners.Asleep(words, Assistants, kdesktop, moduleOn: true, onDesktop: ["lander", "kdesktop"]).Order());
        Assert.Equal(["w-any", "w-dot", "w-lander", "w-loose"], WakeOwners.Asleep(words, Assistants, kdesktop, moduleOn: false, onDesktop: ["lander"]).Order());
    }

    [Fact]
    public void BeforeEveryWordNoAssistantHadWasKDesktops() =>
        Assert.Equal(["w-kd", "w-loose"], WakeOwners.Migrated(["w-lander", "w-kd", "w-any", "w-loose"], Assistants));
}
