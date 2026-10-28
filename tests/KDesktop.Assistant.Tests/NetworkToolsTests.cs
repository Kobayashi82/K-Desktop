using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>B6 (5.0.349): <c>network_check</c>, over made-up reports (nothing is pinged).</summary>
public class NetworkToolsTests
{
    private static readonly LinkStatus Wifi = new(["Casa"], false, new WifiStatus("Casa_5G", 82, 866), ["192.168.1.10", "10.0.0.2"], "192.168.1.1");

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void InternetThatWorks()
    {
        var text = NetworkCheckTool.Describe(new NetworkReport(Wifi, new InternetStatus(InternetState.Online, 14, null), null, null), "internet");
        Assert.Contains("Connected by Wi-Fi to \"Casa_5G\" (signal 82% (good), 866 Mb/s link).", text, StringComparison.Ordinal);
        Assert.Contains("Local IP: 192.168.1.10 (also 10.0.0.2); router 192.168.1.1.", text, StringComparison.Ordinal);
        Assert.Contains("Internet works (ping 14 ms).", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Public IP", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenItFailsItSaysWhere()
    {
        var cable = new LinkStatus(["Red"], true, null, ["192.168.1.10"], "192.168.1.1");
        Assert.Contains("Connected by cable to \"Red\", not by Wi-Fi.", NetworkCheckTool.Describe(new NetworkReport(cable, new InternetStatus(InternetState.Online, 9, null), null, null), "internet"), StringComparison.Ordinal);
        Assert.Contains("the router answers", NetworkCheckTool.Describe(new NetworkReport(cable, new InternetStatus(InternetState.NoInternet, null, true), null, null), "internet"), StringComparison.Ordinal);
        Assert.Contains("the router does not answer either", NetworkCheckTool.Describe(new NetworkReport(cable, new InternetStatus(InternetState.NoInternet, null, false), null, null), "internet"), StringComparison.Ordinal);
        Assert.Contains("(DNS)", NetworkCheckTool.Describe(new NetworkReport(cable, new InternetStatus(InternetState.NoDns, 20, null), null, null), "internet"), StringComparison.Ordinal);
        Assert.Contains("sign-in page", NetworkCheckTool.Describe(new NetworkReport(cable, new InternetStatus(InternetState.Portal, null, null), null, null), "internet"), StringComparison.Ordinal);
        var none = new LinkStatus([], false, null, [], null);
        var offline = NetworkCheckTool.Describe(new NetworkReport(none, new InternetStatus(InternetState.NoNetwork, null, null), null, null), "internet");
        Assert.Contains("Not connected to any network.", offline, StringComparison.Ordinal);
        Assert.DoesNotContain("Local IP", offline, StringComparison.Ordinal);
    }

    [Fact]
    public void WifiWithoutPermissionUsesTheNetworkName()
    {
        // Windows 11 hides the Wi-Fi details from programs without location permission.
        var hidden = new LinkStatus(["Casa"], false, new WifiStatus(null, null, null), ["192.168.1.10"], "192.168.1.1");
        Assert.Contains("Connected by Wi-Fi to \"Casa\".", NetworkCheckTool.Describe(new NetworkReport(hidden, null, null, null), "wifi"), StringComparison.Ordinal);
    }

    [Fact]
    public void PublicIpOnlyWhenAsked()
    {
        Assert.Contains("Public IP (what the internet sees): 88.12.34.56.", NetworkCheckTool.Describe(new NetworkReport(Wifi, null, "88.12.34.56", null, true), "ip"), StringComparison.Ordinal);
        Assert.Contains("could not be found", NetworkCheckTool.Describe(new NetworkReport(Wifi, null, null, null, true), "ip"), StringComparison.Ordinal);
    }

    [Fact]
    public void AMachineOrAWebsite()
    {
        string Host(HostStatus? host, string asked = "x") => NetworkCheckTool.Describe(new NetworkReport(Wifi, null, null, host), "host", asked);
        Assert.Contains("NAS (192.168.1.20) answers ping in 2 ms.", Host(new HostStatus("NAS", true, "192.168.1.20", 2, null, null, null)), StringComparison.Ordinal);
        Assert.Contains("example.com (93.184.215.14) does not answer ping; its website answers (HTTP 200).", Host(new HostStatus("example.com", true, "93.184.215.14", null, 200, null, null)), StringComparison.Ordinal);
        Assert.Contains("port 22 is open", Host(new HostStatus("192.168.1.5", true, "192.168.1.5", null, null, 22, true)), StringComparison.Ordinal);
        var off = Host(new HostStatus("192.168.1.5", true, "192.168.1.5", null, null, null, null));
        Assert.Contains("its website does not answer either", off, StringComparison.Ordinal);
        Assert.Contains("may be off", off, StringComparison.Ordinal);
        Assert.Contains("was not found", Host(new HostStatus("nadaquever.invalid", false, null, null, null, null, null)), StringComparison.Ordinal);
        Assert.Contains("is not a machine", Host(null, "¿?"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatIsAskedIsWhatIsChecked()
    {
        var asked = new List<string>();
        var tool = new NetworkCheckTool((internet, ip, host, _) =>
        {
            asked.Add($"{internet}|{ip}|{host}");
            return Task.FromResult(new NetworkReport(Wifi, internet ? new InternetStatus(InternetState.Online, 10, null) : null, ip ? "1.2.3.4" : null, null, ip));
        });
        await tool.RunAsync(Args(new { }), new ToolContext(null), default);
        await tool.RunAsync(Args(new { about = "ip" }), new ToolContext(null), default);
        var host = await tool.RunAsync(Args(new { host = "NAS" }), new ToolContext(null), default);
        await tool.RunAsync(Args(new { about = "host" }), new ToolContext(null), default);
        await tool.RunAsync(Args(new { about = "wifi" }), new ToolContext(null), default);
        Assert.Equal(["True|False|", "False|True|", "False|False|NAS", "True|False|", "True|False|"], asked);
        Assert.True(tool.ReadsOnly);
        Assert.Equal(ToolRisk.Safe, tool.Risk);
        Assert.False(host.Failed);
    }

    [Theory]
    [InlineData("google.com", "google.com", null, true)]
    [InlineData("https://www.ejemplo.es/pagina", "www.ejemplo.es", null, true)]
    [InlineData("http://192.168.1.2:8080", "192.168.1.2", 8080, true)]
    [InlineData("192.168.1.20", "192.168.1.20", null, false)]
    [InlineData("servidor:22", "servidor", 22, false)]
    [InlineData("«NAS»", "NAS", null, false)]
    [InlineData("el router", "192.168.1.1", null, false)]
    [InlineData("dropbox.com", "dropbox.com", null, true)]
    public void TheTargetIsWhatTheUserSaid(string said, string host, int? port, bool web)
    {
        var target = NetworkCheck.Target(said, "192.168.1.1");
        Assert.NotNull(target);
        Assert.Equal((host, port, web), target.Value);
    }

    [Fact]
    public void NothingToCheck()
    {
        Assert.Null(NetworkCheck.Target("  ", null));
        Assert.Null(NetworkCheck.Target("el ordenador de Ana", null));
    }
}
