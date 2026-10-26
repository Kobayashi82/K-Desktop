using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Sharing;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The tools shared with other programs (Options → Compartir): what changes something asks on the PC first, unless the user
/// let them use everything without asking; then only what is set to always ask does.
/// </summary>
public sealed class SharedAskTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-shared-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _asked = [];

    public SharedAskTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    /// <summary>A tool that would change something (here it only says so).</summary>
    private sealed class Acting(string name, ToolRisk risk) : ITool
    {
        public string Name => name;
        public string Description => "Does something.";
        public string ParametersSchemaJson => """{"type":"object","properties":{}}""";
        public IReadOnlyList<string> Examples => [];
        public ToolRisk Risk => risk;
        public string Intent(JsonElement arguments) => name;
        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) => Task.FromResult<ToolResult>("Done.");
    }

    private ToolRunner Make()
    {
        var registry = new ToolRegistry();
        registry.Add(new Acting("change_it", ToolRisk.AskOnce));
        registry.Add(new Acting("always_asks", ToolRisk.AskAlways));
        return new ToolRunner(registry, () => new HashSet<string>(), (ask, _) =>
        {
            lock (_asked) _asked.Add(ask.Tool);
            return Task.FromResult("yes");
        }, _ => { }, Path.Combine(_folder, "undo.json"), () => AskPolicy.Never);
    }

    [Theory]
    [InlineData(false, "change_it", true)]
    [InlineData(false, "always_asks", true)]
    [InlineData(true, "change_it", false)]
    [InlineData(true, "always_asks", true)]
    public async Task AProgramAsksOnThePcUnlessItMayUseEverythingFreely(bool free, string tool, bool asks)
    {
        var runner = Make();
        var outcome = await runner.RunAsync(tool, JsonDocument.Parse("{}").RootElement, new ToolContext(null, Caller: "Claude") { SharedFree = free }, "x",
            McpShare.Route, default);
        Assert.Equal("Done.", outcome.Result!.Text);
        Assert.Equal(asks, _asked.Contains(tool));
    }
}
