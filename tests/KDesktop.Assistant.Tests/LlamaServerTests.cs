using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant.Brain;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The brain on the llama-servers the assistant process starts itself (5.0.99; several since 5.0.107): the models found in
/// the folders, which server opens each one, their vision projectors, the options they start with, and the tool calls a
/// model leaves written as text.
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
        Assert.Equal(["-c", "32768", "--mmproj", @"C:\Mis modelos\v.gguf"], LlamaServerPool.SplitArgs(@"-c 32768  --mmproj ""C:\Mis modelos\v.gguf"""));
        Assert.Empty(LlamaServerPool.SplitArgs("   "));
    }

    /// <summary>
    /// The user's layout: llama.cpp with its «Models» (a folder per model, with their projectors) and Bonsai's demo (the
    /// server in bin\cuda, the models in models\bonsai2-gguf\27B with their extras), and a model chosen elsewhere.
    /// </summary>
    [Fact]
    public void TheModelsOfTheFoldersAreFoundWithTheirServerAndVision()
    {
        var llama = Touch(@"Llama-CPP\llama-server.exe");
        var bonsai = Touch(@"Bonsai\Bonsai-demo\bin\cuda\llama-server.exe");
        Touch(@"Llama-CPP\Models\Qwen 3.5 9B\Qwen3.5-9B-Q4_K_M.gguf");
        Touch(@"Llama-CPP\Models\Qwen 3.5 9B\Qwen3.5-9B-mmproj-BF16.gguf");
        Touch(@"Llama-CPP\Models\Qwen 3.5 9B\Qwen3.5-9B-uncensored-heretic-Q4_K_M.gguf");
        Touch(@"Llama-CPP\Models\Qwen 3.5 9B\Qwen3.5-9B-uncensored-heretic-mmproj-BF16.gguf");
        Touch(@"Llama-CPP\Models\big\big-00001-of-00002.gguf", 3);
        Touch(@"Llama-CPP\Models\big\big-00002-of-00002.gguf", 4);
        Touch(@"Bonsai\Bonsai-demo\models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-PQ2_0.gguf");
        Touch(@"Bonsai\Bonsai-demo\models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-mmproj-Q8_0.gguf");
        Touch(@"Bonsai\Bonsai-demo\models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-dspark-dflash.gguf");
        var elsewhere = Touch(@"Otros\suelto.gguf");

        var found = GgufFiles.Find([Path.Combine(_folder, @"Llama-CPP\Models"), Path.Combine(_folder, @"Bonsai\Bonsai-demo\models"), Path.Combine(_folder, "no-está")],
            [elsewhere], [llama, bonsai]);
        Assert.Equal(["big-00001-of-00002.gguf", "Qwen3.5-9B-Q4_K_M.gguf", "Qwen3.5-9B-uncensored-heretic-Q4_K_M.gguf", "suelto.gguf", "Ternary-Bonsai-2-27B-PQ2_0.gguf"],
            found.Select(m => Path.GetFileName(m.Path)));
        LocalModel Of(string name) => found.First(m => Path.GetFileName(m.Path) == name);
        Assert.Equal(7, Of("big-00001-of-00002.gguf").Size);
        Assert.Equal("Qwen3.5-9B-mmproj-BF16.gguf", Path.GetFileName(Of("Qwen3.5-9B-Q4_K_M.gguf").Projector));
        Assert.Equal("Qwen3.5-9B-uncensored-heretic-mmproj-BF16.gguf", Path.GetFileName(Of("Qwen3.5-9B-uncensored-heretic-Q4_K_M.gguf").Projector));
        Assert.Equal("Ternary-Bonsai-2-27B-mmproj-Q8_0.gguf", Path.GetFileName(Of("Ternary-Bonsai-2-27B-PQ2_0.gguf").Projector));
        Assert.Null(Of("suelto.gguf").Projector);
        // Bonsai's models open with Bonsai's server; the rest with the usual one, the first.
        Assert.Equal(bonsai, Of("Ternary-Bonsai-2-27B-PQ2_0.gguf").Server);
        Assert.Equal(llama, Of("Qwen3.5-9B-Q4_K_M.gguf").Server);
        Assert.Equal(llama, Of("suelto.gguf").Server);
        Assert.Equal(Path.Combine(_folder, @"Bonsai\Bonsai-demo\models"), GgufFiles.ModelsFolderNear(bonsai));
    }

    [Theory]
    [InlineData("Qwen3.5-4B-Q4_K_M.gguf", "Qwen3.5-4B-mmproj-F16.gguf")]
    [InlineData("Qwen3.5-4B-abliterated.i1-Q4_K_M.gguf", "Qwen3.5-4B-mmproj-F16.gguf")]
    [InlineData("Qwen3.8-27B-GSQ-RCO-IQ3_S.gguf", "mmproj-Qwen3.8-27B-BF16.gguf")]
    public void TheProjectorWithTheMostWordsInCommonGoesWithTheModel(string model, string expected) =>
        Assert.Equal(expected, GgufFiles.BestProjector(model, ["Qwen3.5-4B-heretic.mmproj-f16.gguf", "Qwen3.5-4B-mmproj-F16.gguf", "mmproj-Qwen3.8-27B-BF16.gguf"]));

    [Fact]
    public void TheOnlyProjectorOfAFolderIsTheOne() =>
        Assert.Equal("mmproj-F16.gguf", GgufFiles.BestProjector("Qwen3.5-9B-Q4_K_M.gguf", ["mmproj-F16.gguf"]));

    /// <summary>The options llama-server starts with: no fixed layers (<c>--fit</c> decides), an 8-bit cache, the projector, and that model's own last.</summary>
    [Fact]
    public void ItStartsWithItsProjectorAndTheModelsOwnOptionsLast()
    {
        var own = new Dictionary<string, string> { [@"e:\M\q.gguf"] = "-c 32768" };
        using var pool = new LlamaServerPool([@"E:\l\llama-server.exe"], [], own, null, oneAtATime: true, vision: true, [], () => 12);
        var model = new LocalModel(@"E:\m\q.gguf", 5, @"E:\m\mmproj.gguf", 1, @"E:\l\llama-server.exe");
        var args = pool.Arguments(model, 1234, "q");
        Assert.Equal(["-m", @"E:\m\q.gguf", "--mmproj", @"E:\m\mmproj.gguf"], args.Take(4));
        Assert.DoesNotContain("-ngl", args);
        Assert.Contains("q8_0", args);
        Assert.Equal(["-c", "32768"], args.TakeLast(2));
        // Another model does not get them.
        var other = new LocalModel(@"E:\m\r.gguf", 5, null, 0, @"E:\l\llama-server.exe");
        Assert.Equal("{\"enable_thinking\": false}", pool.Arguments(other, 1234, "r")[^1]);
        // 5.0.200: a server that knows it is told «--reasoning off» instead.
        Assert.Equal(["--reasoning", "off"], pool.Arguments(other, 1234, "r", reasoningOff: true).TakeLast(2));
        using var blind = new LlamaServerPool([@"E:\l\llama-server.exe"], [], null, null, oneAtATime: true, vision: false, [], () => 12);
        Assert.DoesNotContain("--mmproj", blind.Arguments(model, 1234, "q"));
        Assert.Equal("llama-server (Bonsai-demo)", LlamaServerPool.ServerName(@"E:\Bonsai-demo\bin\cuda\llama-server.exe"));
    }

    [Fact]
    public async Task WithoutTheProgramItSaysSoInsteadOfAnAddress()
    {
        var model = Touch(@"m\q.gguf");
        using var pool = new LlamaServerPool([Path.Combine(_folder, "nada", "llama-server.exe")], [Path.Combine(_folder, "m")], null, null, true, true, [], () => 12);
        Assert.Single(await pool.ListModelsAsync(default));
        var error = await Assert.ThrowsAsync<ChatModelException>(() => pool.PreloadAsync(model, default));
        Assert.Equal(ChatModelFailure.Unreachable, error.Failure);
        Assert.Contains("llama-server.exe", error.Message, StringComparison.Ordinal);
        Assert.Empty(await pool.ListLoadedAsync(default));
    }

    private string Touch(string relative, int bytes = 1)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
