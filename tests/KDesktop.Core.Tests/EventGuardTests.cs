using KDesktop.Core.Engine.Events;

namespace KDesktop.Core.Tests;

public class EventGuardTests
{
    private static readonly DateTime T0 = new(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CooldownBlocksRepeatsForAWhile()
    {
        var guard = new EventGuard();
        Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("a", 5, T0));
        Assert.Equal(EventGuard.Verdict.Cooldown, guard.TryFire("a", 5, T0.AddSeconds(4)));
        Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("a", 5, T0.AddSeconds(5)));
        Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("b", 5, T0.AddSeconds(1))); // each event on its own
    }

    [Fact]
    public void NoCooldownRunsAlways()
    {
        var guard = new EventGuard();
        Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("a", 0, T0));
        Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("a", 0, T0));
    }

    [Fact]
    public void ALoopStopsTheEvent()
    {
        var guard = new EventGuard();
        for (var i = 0; i < EventGuard.MaxPerMinute; i++)
            Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("a", 0, T0.AddSeconds(i)));
        Assert.Equal(EventGuard.Verdict.Loop, guard.TryFire("a", 0, T0.AddSeconds(20)));
        Assert.True(guard.IsSuspended("a", T0.AddSeconds(21)));
        Assert.Equal(EventGuard.Verdict.Suspended, guard.TryFire("a", 0, T0.AddMinutes(5)));
        // After the pause it works again, counting from zero
        Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("a", 0, T0.AddSeconds(20) + EventGuard.Pause));
        Assert.False(guard.IsSuspended("a", T0.AddSeconds(21) + EventGuard.Pause));
    }

    [Fact]
    public void SpreadOutFiringIsNotALoop()
    {
        var guard = new EventGuard();
        for (var i = 0; i < 50; i++)
            Assert.Equal(EventGuard.Verdict.Run, guard.TryFire("a", 0, T0.AddSeconds(i * 7)));
    }

    [Theory]
    [InlineData("", 'E', null, true)]
    [InlineData("E:", 'E', "USB", true)]
    [InlineData("e", 'E', null, true)]
    [InlineData("E:\\", 'F', "USB", false)]
    [InlineData("kingston", 'F', "KINGSTON 64", true)]
    [InlineData("música", 'G', "Musica", true)]
    [InlineData("sandisk", 'F', "KINGSTON", false)]
    [InlineData("sandisk", 'F', null, false)]
    public void DriveFilter(string filter, char letter, string? label, bool expected) =>
        Assert.Equal(expected, KDesktop.Core.Model.DriveTrigger.Matches(filter, letter, label));

    [Fact]
    public void NetworkStates()
    {
        var home = new KDesktop.Core.Model.NetworkTrigger { Name = "miwifi" };
        Assert.True(home.IsActive(["MiWiFi_5G"]));
        Assert.False(home.IsActive(["Red 2"]));
        Assert.False(home.IsActive([]));
        Assert.True(new KDesktop.Core.Model.NetworkTrigger().IsActive(["Red 2"]));
        var offline = new KDesktop.Core.Model.NetworkTrigger { State = KDesktop.Core.Model.NetworkState.Offline };
        Assert.True(offline.IsActive([]));
        Assert.False(offline.IsActive(["Red 2"]));
    }

    [Fact]
    public void ConnectedNetworksCanBeRead()
    {
        // This PC has a network: Windows' list manager answers without throwing.
        var names = KDesktop.Core.Platform.NetworkInfo.ConnectedNetworks();
        Assert.NotNull(names);
    }

    [Fact]
    public void AudioNotificationsCanBeRegistered()
    {
        using var watcher = new KDesktop.Core.Platform.AudioWatcher();
    }

    [Fact]
    public void AudioDeviceNames()
    {
        var t = new KDesktop.Core.Model.AudioDeviceTrigger { Name = "auriculares" };
        Assert.True(t.Matches("Auriculares (Realtek Audio)"));
        Assert.False(t.Matches("Altavoces (Realtek Audio)"));
        Assert.True(new KDesktop.Core.Model.AudioDeviceTrigger().Matches("Lo que sea"));
    }
}
