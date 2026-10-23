using System.Text;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Sharing;

namespace KDesktop.Core.Tests;

public class McpShareTests
{
    [Fact]
    public void OnlyTheRightKeyMatches()
    {
        var key = McpShare.NewKey();
        var hash = McpShare.HashOf(key);
        Assert.True(McpShare.Matches(key, hash));
        Assert.True(McpShare.Matches(" " + key + "\r", hash));
        Assert.False(McpShare.Matches(key + "x", hash));
        Assert.False(McpShare.Matches(null, hash));
        Assert.False(McpShare.Matches(key, string.Empty));
        Assert.NotEqual(key, McpShare.NewKey());
    }

    [Fact]
    public void TheConfigurationIsWhatTheProgramsRead()
    {
        var json = JsonNode.Parse(McpShare.ClientConfig(@"D:\K\KDesktop.exe", "abc", @"D:\datos"))!;
        var server = json["mcpServers"]!["k-desktop"]!;
        Assert.Equal(@"D:\K\KDesktop.exe", (string?)server["command"]);
        Assert.Equal(["--mcp", "--data", @"D:\datos"], server["args"]!.AsArray().Select(a => (string?)a));
        Assert.Equal("abc", (string?)server["env"]![McpShare.KeyVariable]);

        var plain = JsonNode.Parse(McpShare.ClientConfig(@"D:\K\KDesktop.exe", "abc"))!;
        Assert.Equal(["--mcp"], plain["mcpServers"]!["k-desktop"]!["args"]!.AsArray().Select(a => (string?)a));
    }

    [Fact]
    public async Task TheHelloLeavesTheConversationInThePipe()
    {
        var bytes = Encoding.ASCII.GetBytes("KDMCP1 secreto\n{\"jsonrpc\":\"2.0\"}\n");
        using var stream = new MemoryStream(bytes);
        Assert.Equal("secreto", await McpShare.ReadHelloAsync(stream, CancellationToken.None));
        Assert.Equal("{\"jsonrpc\":\"2.0\"}\n", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public async Task ADifferentFirstLineIsRefused()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":\"initialize\"}\n"));
        Assert.Null(await McpShare.ReadHelloAsync(stream, CancellationToken.None));
        using var endless = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 1000)));
        Assert.Null(await McpShare.ReadHelloAsync(endless, CancellationToken.None));
    }

    [Fact]
    public void EachDataFolderHasItsOwnPipe() =>
        Assert.NotEqual(McpShare.PipeNameFor(@"C:\a", 1), McpShare.PipeNameFor(@"C:\b", 1));
}
