using System.Text.Json;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>B11 (5.0.353): <c>send_to_phone</c> through a K-Desktop of the test (nothing reaches a real phone).</summary>
public sealed class SendToPhoneToolTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("kd-send-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class Desk : ICommandBridge
    {
        public List<SendPhoneMessage> Sent { get; } = [];

        public PhoneSentMessage? Answer { get; set; } = new("x", true, ["Pixel 8 · App"]);

        public Task<PhoneSentMessage?> SendToPhoneAsync(SendPhoneMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(request);
            return Task.FromResult(Answer);
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }

    [Fact]
    public async Task ATextGoesToKDesktop()
    {
        var desk = new Desk();
        var tool = new SendToPhoneTool(desk);
        var result = await tool.RunAsync(Args(new { title = "Fontanero", text = "Luis: 600 123 456", device = "pixel" }), new ToolContext("kobii"), default);
        Assert.True(result.Acted);
        var sent = Assert.Single(desk.Sent);
        Assert.Equal(("kobii", "Fontanero", "Luis: 600 123 456", "pixel"), (sent.Assistant, sent.Title, sent.Text, sent.Device));
        Assert.Contains("Pixel 8 · App", result.Reply, StringComparison.Ordinal);
        // It leaves the PC: asked the first time.
        Assert.Equal(ToolRisk.AskOnce, tool.Risk);
        Assert.Contains("Fontanero", tool.Intent(Args(new { title = "Fontanero" })), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileMustBeThere()
    {
        var desk = new Desk();
        var tool = new SendToPhoneTool(desk);
        var missing = await tool.RunAsync(Args(new { file = Path.Combine(_folder, "no.pdf") }), new ToolContext(null), default);
        Assert.True(missing.Failed);
        Assert.Empty(desk.Sent);
        var file = Path.Combine(_folder, "factura.pdf");
        await File.WriteAllTextAsync(file, "pdf");
        await tool.RunAsync(Args(new { file }), new ToolContext(null), default);
        Assert.Equal(file, Assert.Single(desk.Sent).File);
        Assert.True((await tool.RunAsync(Args(new { }), new ToolContext(null), default)).Failed);
    }

    [Fact]
    public async Task WithoutADeviceItSaysWhichThereAre()
    {
        var desk = new Desk { Answer = new PhoneSentMessage("x", false, [], "noDevice", ["Pixel 8 · App", "Galaxy Tab · Chrome"]) };
        var result = await new SendToPhoneTool(desk).RunAsync(Args(new { text = "hola", device = "iphone" }), new ToolContext(null), default);
        Assert.True(result.Failed);
        Assert.Contains("Galaxy Tab · Chrome", result.Text, StringComparison.Ordinal);
        desk.Answer = new PhoneSentMessage("x", false, [], "noDevice");
        Assert.Contains("pair one", (await new SendToPhoneTool(desk).RunAsync(Args(new { text = "hola" }), new ToolContext(null), default)).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mándame al móvil la dirección", true)]
    [InlineData("pásamelo al teléfono", true)]
    [InlineData("send it to my phone", true)]
    [InlineData("mándame al telegram que compre pan", false)]
    public void OnlyAnOrderForThePhoneIsRemindedOfIt(string said, bool reaches)
    {
        // «Mándame al telegram…» with no Telegram tool must not end up on the phone (the reminder of an unsent message).
        Assert.Equal(reaches, ActionClaims.Reaches("send_to_phone", said));
        Assert.True(ActionClaims.Reaches("send_telegram", said));
    }
}
