using KDesktop.Core.Remote.Web;

namespace KDesktop.Core.Tests;

public class WebDevicesTests
{
    [Fact]
    public void TheOpenCodeOnlyPairsABrowserOfThisPc()
    {
        var devices = new WebDevices();
        var code = devices.NewOpenCode("Navegador de este PC");

        // From another device it does nothing, and it is not spent.
        Assert.Null(devices.Pair(code, "Móvil", "192.168.1.20", local: false));

        var paired = devices.Pair(code, "Firefox", "127.0.0.1", local: true);
        Assert.NotNull(paired);
        Assert.True(paired.Value.Device.ThisPc);
        Assert.Same(paired.Value.Device, devices.Authenticate(paired.Value.Token, "127.0.0.1"));

        // Once only.
        Assert.Null(devices.Pair(code, "Firefox", "127.0.0.1", local: true));
    }

    [Fact]
    public void EveryBrowserOfThisPcIsTheSameDevice()
    {
        var devices = new WebDevices();
        var before = devices.List().Count(d => d.ThisPc);
        var first = devices.Pair(devices.NewOpenCode("PC"), null, "127.0.0.1", local: true)!.Value;
        var second = devices.Pair(devices.NewOpenCode("PC"), null, "127.0.0.1", local: true)!.Value;

        Assert.Equal(first.Device.Id, second.Device.Id);
        Assert.NotEqual(first.Token, second.Token);
        Assert.NotNull(devices.Authenticate(first.Token, null));
        Assert.NotNull(devices.Authenticate(second.Token, null));
        Assert.Equal(Math.Max(before, 1), devices.List().Count(d => d.ThisPc));
    }

    [Fact]
    public void AWrongCodeDoesNotPair()
    {
        var devices = new WebDevices();
        devices.NewOpenCode("PC");
        Assert.Null(devices.Pair("0123456789abcdef0123456789abcdef", null, "127.0.0.1", local: true));
    }
}
