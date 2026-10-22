using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Declarative;
using KDesktop.Assistant.Tools.Mcp;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 9.2: MCP servers (a pretend one in Node), their cache, secrets, preferences, and importing mcpo's file.</summary>
public sealed class McpTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-mcp-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IDisposable> _owned = [];
    private readonly List<AskMessage> _asked = [];

    public McpTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string FakeServer => Path.Combine(AppContext.BaseDirectory, "Data", "fake-mcp.js");

    private string ConfigFile => Path.Combine(_folder, "mcp.json");

    private void Config(string servers) => File.WriteAllText(ConfigFile, "{ \"mcpServers\": {" + servers + "} }");

    /// <summary>The pretend server as «fake», with its secret.</summary>
    private static string Fake(string extra = "") =>
        $$$"""
        "fake": { "command": "node", "args": [{{{JsonSerializer.Serialize(FakeServer)}}}], "env": { "FAKE_SECRET": "{{secret:fake.FAKE_SECRET}}" } {{{extra}}} }
        """;

    private (McpServers Mcp, ToolRunner Runner, ToolRegistry Registry) Make()
    {
        var registry = new ToolRegistry();
        var secrets = new ToolSecrets(Path.Combine(_folder, "secrets.json"));
        secrets.Set("fake.FAKE_SECRET", "zz-muy-secreto-77");
        var mcp = new McpServers(ConfigFile, Path.Combine(_folder, "mcp-cache.json"), Path.Combine(_folder, "Logs"), registry, secrets);
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (ask, _) =>
        {
            lock (_asked) _asked.Add(ask);
            return Task.FromResult("yes");
        }, _ => { }, Path.Combine(_folder, "undo.json"), () => AskPolicy.First);
        _owned.Add(mcp);
        return (mcp, runner, registry);
    }

    private async Task Until(Func<bool> done, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!done())
        {
            if (DateTime.UtcNow > deadline)
            {
                var log = Path.Combine(_folder, "Logs", "mcp-fake.log");
                throw new TimeoutException("Sin llegar. Registro: " + (File.Exists(log) ? File.ReadAllText(log) : "(ninguno)"));
            }
            await Task.Delay(100);
        }
    }


    [Fact]
    public async Task AServerIsLearnedOnceThenItsToolsComeFromTheCacheWithoutStartingIt()
    {
        Config(Fake());
        var (mcp, runner, registry) = Make();
        mcp.Start();
        // Started in the background to learn its tools, and stopped again.
        await Until(() => registry.Find("mcp_fake_echo") is not null);
        await Until(() => mcp.Servers[0].State == McpRunState.Stopped);
        Assert.Equal("mcp:fake", registry.GroupOf("mcp_fake_echo"));
        Assert.Equal(ToolRisk.Safe, registry.Find("mcp_fake_echo")!.Risk);
        Assert.True(registry.Find("mcp_fake_echo")!.ReadsOnly);
        Assert.Equal(ToolRisk.AskOnce, registry.Find("mcp_fake_fail")!.Risk);
        // A schema without properties gets them (llama-server refuses one without).
        Assert.Contains("\"properties\":{}", registry.Find("mcp_fake_fail")!.ParametersSchemaJson, StringComparison.Ordinal);
        // Its stderr and the traces it wrote to stdout are in its log, never in the protocol.
        var log = File.ReadAllText(Path.Combine(_folder, "Logs", "mcp-fake.log"));
        Assert.Contains("fake-mcp: started", log, StringComparison.Ordinal);
        Assert.Contains("stdout: Fake MCP server starting", log, StringComparison.Ordinal);

        // Another process start: the tools are there at once, and the server is not started for them.
        var (again, _, registry2) = Make();
        again.Start();
        Assert.NotNull(registry2.Find("mcp_fake_echo"));
        Assert.Equal(McpRunState.Stopped, again.Servers[0].State);
        await Task.Delay(500);
        Assert.Equal(McpRunState.Stopped, again.Servers[0].State);
    }

    [Fact]
    public async Task ACallStartsItAndTheModelNeverSeesTheSecret()
    {
        Config(Fake());
        var (mcp, runner, registry) = Make();
        mcp.Start();
        await Until(() => registry.Find("mcp_fake_echo") is not null);

        var outcome = await runner.RunAsync("mcp_fake_echo", ToolArguments.Parse("""{"text":"hola"}"""), new ToolContext("lander"), "eco", "ai", default);
        Assert.Equal("echo: hola (secret •••)", outcome.Result!.Text);
        Assert.False(outcome.Result.Acted);
        Assert.Empty(_asked);
        Assert.Equal(McpRunState.Running, mcp.Servers[0].State);

        // A tool that may change things asks first (🟡); an error result is a failure.
        var failed = await runner.RunAsync("mcp_fake_fail", ToolArguments.Parse("{}"), new ToolContext("lander"), "falla", "ai", default);
        Assert.True(failed.Result!.Failed);
        Assert.Contains("it went wrong", failed.Result.Text, StringComparison.Ordinal);
        Assert.Single(_asked);

        await mcp.StopAsync("fake");
        Assert.Equal(McpRunState.Stopped, mcp.Servers[0].State);
    }

    [Fact]
    public async Task ACallThatNeverEndsIsCutAndTheServerSaysWhy()
    {
        Config(Fake(""", "timeout": 5"""));
        var (mcp, runner, registry) = Make();
        mcp.Start();
        await Until(() => registry.Find("mcp_fake_slow") is not null);
        var started = DateTime.UtcNow;
        var outcome = await runner.RunAsync("mcp_fake_slow", ToolArguments.Parse("{}"), new ToolContext(null), "lento", "ai", default);
        Assert.True(outcome.Result!.Failed);
        Assert.Contains("5 s", outcome.Result.Text, StringComparison.Ordinal);
        Assert.InRange((DateTime.UtcNow - started).TotalSeconds, 4.5, 15);
    }

    [Fact]
    public async Task AMissingProgramIsSaidPlainly()
    {
        Config("""
            "roto": { "command": "programa-que-no-existe-xyz", "args": ["a"] }
            """);
        var (mcp, _, _) = Make();
        mcp.Start();
        var error = await mcp.TestAsync("roto", default);
        Assert.Equal("Hace falta programa-que-no-existe-xyz y no está instalado.", error);
        Assert.Equal(McpRunState.Failed, mcp.Servers[0].State);
        Assert.Equal("Node.js", McpServer.ProgramName("npx.cmd"));
    }

    [Fact]
    public async Task ASwitchedOffServerOrToolIsNotOffered()
    {
        Config(Fake());
        var (mcp, runner, registry) = Make();
        mcp.Start();
        await Until(() => registry.Find("mcp_fake_echo") is not null);

        registry.SetPreferences(new Dictionary<string, ToolPreference> { ["mcp_fake_echo"] = new(Enabled: false), ["mcp_fake_fail"] = new(Risk: "safe") });
        Assert.Null(registry.Find("mcp_fake_echo"));
        Assert.DoesNotContain(registry.All, t => t.Name == "mcp_fake_echo");
        Assert.Contains(registry.Everything, t => t.Name == "mcp_fake_echo");
        var off = await runner.RunAsync("mcp_fake_echo", ToolArguments.Parse("""{"text":"x"}"""), new ToolContext(null), "eco", "ai", default);
        Assert.False(off.Ran);
        Assert.Contains("apagada", off.Entry.Did, StringComparison.Ordinal);
        // «Safe» by the user's choice: it no longer asks.
        await runner.RunAsync("mcp_fake_fail", ToolArguments.Parse("{}"), new ToolContext(null), "falla", "ai", default);
        Assert.Empty(_asked);

        // The whole server switched off in mcp.json: its tools go.
        var changed = new TaskCompletionSource();
        mcp.Changed += () => { if (registry.FindAny("mcp_fake_fail") is null) changed.TrySetResult(); };
        Config(Fake(""", "disabled": true"""));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(mcp.Servers[0].Entry.Disabled);
    }

    [Fact]
    public void ToolNamesAreWhatModelsTake()
    {
        Assert.Equal("mcp_ft42_usuario", McpServers.NameFor("ft42", "usuario"));
        Assert.Equal("mcp_ace_step_generate_music", McpServers.NameFor("ace-step", "generate.music"));
        Assert.True(McpServers.NameFor("servidor-con-un-nombre-largo", new string('x', 80)).Length <= 64);
    }

    [Theory]
    // What READMEs say to copy (5.0.137, «Pegar»): Claude Desktop, VS Code, VS Code's settings, the servers alone, and
    // without the outer braces (with a comma at the end, as they come out of a longer file).
    [InlineData("""{ "mcpServers": { "weather": { "command": "npx", "args": ["-y", "weather-mcp"] } } }""")]
    [InlineData("""{ "servers": { "weather": { "type": "stdio", "command": "npx" } } }""")]
    [InlineData("""{ "mcp": { "servers": { "weather": { "command": "npx" } } } }""")]
    [InlineData("""{ "weather": { "command": "npx" } }""")]
    [InlineData("""  "weather": { "command": "npx", "args": ["-y", "weather-mcp"] },  """)]
    [InlineData("""
        // weather
        "weather": {
          "url": "https://example.com/mcp"
        }
        """)]
    public void WhatAReadmeSaysToCopyCanBePasted(string pasted) =>
        Assert.Equal(["weather"], McpConfig.Import(pasted, []).Select(f => f.Entry.Id));

    [Theory]
    [InlineData("")]
    [InlineData("npx -y weather-mcp")]
    [InlineData("""{ "name": "weather", "version": "1.0" }""")]
    public void WhatHasNoServersGivesNone(string pasted) => Assert.Empty(McpConfig.Import(pasted, []));

    [Fact]
    public void ImportingMcposFileTakesTheSecretsOutAndSaysWhatIsAlreadyDone()
    {
        const string mcpo = """
            {
              "mcpServers": {
                "telegram": { "command": "node", "args": ["E:\\MCP\\telegram\\build\\index.js"], "env": { "TELEGRAM_TOKEN": "123:abc", "CHAT_ID": "4567" } },
                "ft42": { "command": "python.exe", "args": ["42_mcp.py"], "env": { "FT_CLIENT_ID": "u-1", "FT_CLIENT_SECRET": "s-2", "FT_DEFAULT_CAMPUS": "37" } },
                "jokes": { "type": "streamable_http", "url": "https://jokes.nico.dev/mcp" },
                "filesystem": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "D:\\Downloads"] },
                "kdesktop": { "command": "python.exe", "args": ["E:\\MCP\\kdesktop_mcp.py"] }
              }
            }
            """;
        var found = McpConfig.Import(mcpo, ["ft42"]);
        Assert.Equal(["telegram", "ft42", "jokes", "filesystem", "kdesktop"], found.Select(f => f.Entry.Id));
        var telegram = found[0];
        Assert.Equal("123:abc", telegram.Secrets["telegram.TELEGRAM_TOKEN"]);
        Assert.Equal("4567", telegram.Secrets["telegram.CHAT_ID"]);
        Assert.Equal("{{secret:telegram.TELEGRAM_TOKEN}}", telegram.Entry.Env["TELEGRAM_TOKEN"]);
        Assert.Equal(["telegram.TELEGRAM_TOKEN", "telegram.CHAT_ID"], telegram.Entry.SecretNames);
        Assert.Null(telegram.Overlap);

        var ft42 = found[1];
        Assert.True(ft42.Exists);
        Assert.Equal(2, ft42.Secrets.Count);
        Assert.Equal("37", ft42.Entry.Env["FT_DEFAULT_CAMPUS"]);

        Assert.Equal("http", found[2].Entry.Transport);
        Assert.Null(found[2].Entry.Raw["type"]);
        Assert.Equal("mcpImport.overlap.files", found[3].Overlap);
        Assert.Equal("mcpImport.overlap.kdesktop", found[4].Overlap);

        // What is written validates, and reads back the same.
        McpConfig.Write(ConfigFile, found.Select(f => f.Entry));
        var back = McpConfig.Read(ConfigFile, out var errors);
        Assert.Empty(errors);
        Assert.Equal(5, back.Count);
        Assert.Equal(telegram.Entry.Key, back[0].Key);
        // Switching it off or changing its timeout is not another server.
        back[0].Raw["disabled"] = true;
        Assert.Equal(telegram.Entry.Key, back[0].Key);
    }

    [Fact]
    public void AWrongMcpFileIsTold()
    {
        McpConfig.Parse("""{ "mcpServers": { "a": { "command": "x", "url": "http://y" } } }""", out var both);
        Assert.NotEmpty(both);
        McpConfig.Parse("""{ "mcpServers": { "a b": { "command": "x" } } }""", out var badName);
        Assert.NotEmpty(badName);
        McpConfig.Parse("""{ "mcpServers": { "a": { "command": "x", "idleMinutes": 2 } } }""", out var fine);
        Assert.Empty(fine);
    }
}
