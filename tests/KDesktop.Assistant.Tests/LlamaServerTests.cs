using System.Text.Json.Nodes;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant.Brain;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The brain on a llama-server the assistant process starts itself (Bonsai 2 27B, 5.0.99): the models it finds, its
/// options, the settings that travel to the process, and the tool calls a model leaves written as text.
/// </summary>
public sealed class LlamaServerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-llama-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>What Bonsai answered in the bench (21/10/2026), half closed, and the full Qwen coder format.</summary>
    [Fact]
    public void ToolCallsWrittenAsTextAreRead()
    {
        var leaked = TextToolCalls.Parse("<function=find_command> <parameter=query> desactivar notificaciones");
        var call = Assert.Single(leaked);
        Assert.Equal("find_command", call.Name);
        Assert.Equal("desactivar notificaciones", call.Arguments.GetProperty("query").GetString());

        var full = TextToolCalls.Parse("""
            <tool_call>
            <function=set_timer>
            <parameter=minutes>
            10
            </parameter>
            <parameter=reason>
            la pasta
            </parameter>
            </function>
            </tool_call>
            <tool_call><function=get_time></function></tool_call>
            """);
        Assert.Equal(["set_timer", "get_time"], full.Select(c => c.Name));
        Assert.Equal(10, full[0].Arguments.GetProperty("minutes").GetInt32());
        Assert.Equal("la pasta", full[0].Arguments.GetProperty("reason").GetString());

        Assert.Empty(TextToolCalls.Parse("Son las cinco y diez."));
        Assert.True(TextToolCalls.Starts("  <tool_call>"));
        Assert.False(TextToolCalls.Starts("Hecho. <function=x>"));
    }

    [Fact]
    public void ItsOptionsAreSplitLikeACommandLine()
    {
        Assert.Equal(["-c", "32768", "--mmproj", @"C:\Mis modelos\v.gguf"], LlamaServerChatModel.SplitArgs(@"-c 32768  --mmproj ""C:\Mis modelos\v.gguf"""));
        Assert.Empty(LlamaServerChatModel.SplitArgs("   "));
    }

    /// <summary>The Bonsai demo's layout: the server in bin\cuda, the models in models\bonsai2-gguf\27B with their extras.</summary>
    [Fact]
    public void TheModelsNearTheServerAreFoundWithoutTheirExtras()
    {
        var server = Touch(@"bin\cuda\llama-server.exe");
        Touch(@"models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-PQ2_0.gguf");
        Touch(@"models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-PTQ1_0.gguf");
        Touch(@"models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-mmproj-Q8_0.gguf");
        Touch(@"models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-dspark-dflash.gguf");
        Touch(@"models\big\big-00001-of-00002.gguf");
        Touch(@"models\big\big-00002-of-00002.gguf");
        var elsewhere = Path.Combine(Path.GetTempPath(), "kdesktop-llama-other-" + Guid.NewGuid().ToString("N")[..8] + ".gguf");
        File.WriteAllText(elsewhere, "x");
        try
        {
            var found = LlamaServerChatModel.FindModels(server, elsewhere).Select(f => f.Name).ToList();
            Assert.Equal(["big-00001-of-00002.gguf", Path.GetFileName(elsewhere), "Ternary-Bonsai-2-27B-PQ2_0.gguf", "Ternary-Bonsai-2-27B-PTQ1_0.gguf"], found);
        }
        finally
        {
            File.Delete(elsewhere);
        }
    }

    [Fact]
    public async Task WithoutTheProgramItSaysSoInsteadOfAnAddress()
    {
        using var model = new LlamaServerChatModel(Path.Combine(_folder, "nada", "llama-server.exe"), "", null);
        var error = await Assert.ThrowsAsync<ChatModelException>(() => model.ListModelsAsync(default));
        Assert.Equal(ChatModelFailure.Unreachable, error.Failure);
        Assert.Contains("llama-server.exe", error.Message, StringComparison.Ordinal);
        Assert.Empty(await model.ListLoadedAsync(default));
    }

    [Fact]
    public void ItsSettingsTravelToTheProcess()
    {
        var config = new BrainConfig(BrainProvider.LlamaServer, Model: @"E:\m\b.gguf", Server: @"E:\b\llama-server.exe", ServerArgs: "-c 32768", KeepMinutes: 0);
        var back = BrainConfig.FromJson(JsonNode.Parse(config.ToJson().ToJsonString())!.AsObject());
        Assert.Equal((BrainProvider.LlamaServer, @"E:\m\b.gguf", @"E:\b\llama-server.exe", "-c 32768", 0),
            (back.Provider, back.Model, back.Server, back.ServerArgs, back.KeepMinutes));
        // An older K-Desktop does not send it: the usual half hour.
        Assert.Equal(BrainConfig.DefaultKeepMinutes, BrainConfig.FromJson(new JsonObject { ["provider"] = "ollama" }).KeepMinutes);
        Assert.Equal("b", BrainProviders.ModelLabel(@"E:\m\b.gguf"));
        Assert.Equal("qwen3:4b", BrainProviders.ModelLabel("qwen3:4b"));
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
