using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

/// <summary>
/// The brain of 5.0.107 from K-Desktop's side: moving the settings of one provider to the new ones, reading links of
/// Hugging Face and the models of a repository, and what an assistant suggests.
/// </summary>
public class BrainSourcesTests
{
    private static AppConfig Upgraded(Action<AppSettings> old)
    {
        var config = new AppConfig();
        old(config.Settings);
        config.Upgrade();
        return config;
    }

    [Fact]
    public void TheLlamaServerOfBeforeGoesToTheList()
    {
        var s = Upgraded(s =>
        {
            s.AssistantBrainProvider = BrainProvider.LlamaServer;
            s.AssistantBrainServer = @"E:\Bonsai\bin\cuda\llama-server.exe";
            s.AssistantBrainModel = @"E:\Bonsai\models\b.gguf";
        }).Settings;
        Assert.Equal([@"E:\Bonsai\bin\cuda\llama-server.exe"], s.AssistantBrainServers);
        Assert.Equal(@"E:\Bonsai\models\b.gguf", s.AssistantBrainModel);
        Assert.Empty(s.AssistantBrainRemotes);
        Assert.Null(s.AssistantBrainProvider);
        Assert.Null(s.AssistantBrainServer);
    }

    [Fact]
    public void AnotherServerOfBeforeBecomesR1WithItsModels()
    {
        var s = Upgraded(s =>
        {
            s.AssistantBrainProvider = BrainProvider.LlamaSwap;
            s.AssistantBrainEndpoint = "http://192.168.1.150:8080";
            s.AssistantBrainApiKey = "protected";
            s.AssistantBrainModel = "qwen3.5-4b";
            s.AssistantModels = new() { ["lander"] = "gemma4", ["dot"] = @"E:\m\d.gguf" };
        }).Settings;
        var remote = Assert.Single(s.AssistantBrainRemotes);
        Assert.Equal(new BrainRemote("r1", RemoteKind.LlamaSwap, "http://192.168.1.150:8080", "protected"), remote);
        Assert.Equal("r1/qwen3.5-4b", s.AssistantBrainModel);
        Assert.Equal("r1/gemma4", s.AssistantModels["lander"]);
        Assert.Equal(@"E:\m\d.gguf", s.AssistantModels["dot"]);
        Assert.Null(s.AssistantBrainEndpoint);
        Assert.Null(s.AssistantBrainApiKey);
    }

    [Fact]
    public void OllamaUntouchedWasOnlyTheDefaultAndNoneIsNoAI()
    {
        var untouched = Upgraded(s => s.AssistantBrainProvider = BrainProvider.Ollama).Settings;
        Assert.Empty(untouched.AssistantBrainRemotes);
        Assert.Equal(string.Empty, untouched.AssistantBrainModel);

        var off = Upgraded(s => s.AssistantBrainProvider = BrainProvider.None).Settings;
        Assert.Equal(ModelRefs.None, off.AssistantBrainModel);

        var fresh = Upgraded(_ => { }).Settings;
        Assert.Empty(fresh.AssistantBrainRemotes);
        Assert.True(fresh.AssistantBrainOneAtATime);
    }

    [Theory]
    [InlineData("https://huggingface.co/unsloth/Qwen3.5-9B-GGUF", "unsloth/Qwen3.5-9B-GGUF", null, null)]
    [InlineData("https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/blob/main/Qwen3.5-9B-Q4_K_M.gguf", "unsloth/Qwen3.5-9B-GGUF", "Qwen3.5-9B-Q4_K_M.gguf", null)]
    [InlineData("huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/sub/x.gguf?download=true", "unsloth/Qwen3.5-9B-GGUF", "sub/x.gguf", null)]
    [InlineData("hf.co/unsloth/Qwen3.5-9B-GGUF:Q4_K_M", "unsloth/Qwen3.5-9B-GGUF", null, "Q4_K_M")]
    [InlineData("unsloth/Qwen3.5-9B-GGUF/Qwen3.5-9B-Q4_K_M.gguf", "unsloth/Qwen3.5-9B-GGUF", "Qwen3.5-9B-Q4_K_M.gguf", null)]
    [InlineData("  unsloth/Qwen3.5-9B-GGUF  ", "unsloth/Qwen3.5-9B-GGUF", null, null)]
    public void LinksOfHuggingFaceAreRead(string text, string repo, string? file, string? quant)
    {
        var reference = HuggingFace.Parse(text);
        Assert.NotNull(reference);
        Assert.Equal((repo, file, quant), (reference.Repo, reference.File, reference.Quant));
    }

    [Theory]
    [InlineData("")]
    [InlineData("qwen3")]
    [InlineData("https://example.com")]
    [InlineData("a b/c")]
    public void WhatIsNotARepositoryIsNot(string text) => Assert.Null(HuggingFace.Parse(text));

    [Fact]
    public void TheFilesOfARepositoryBecomeModels()
    {
        HfFile[] files =
        [
            new("Qwen3.5-9B-Q8_0.gguf", 9_500_000_000, "a"),
            new("Qwen3.5-9B-Q4_K_M.gguf", 5_680_000_000, "b"),
            new("mmproj-BF16.gguf", 921_000_000, "c"),
            new("mmproj-F16.gguf", 918_000_000, "d"),
            new("big/Big-Q4_K_M-00001-of-00002.gguf", 30_000_000_000, "e"),
            new("big/Big-Q4_K_M-00002-of-00002.gguf", 20_000_000_000, "f"),
            new("imatrix_unsloth.gguf", 5_000_000, ""),
        ];
        var models = HuggingFace.Group("unsloth/Qwen3.5-9B-GGUF", files);
        Assert.Equal(["Qwen3.5-9B-Q4_K_M", "Qwen3.5-9B-Q8_0", "Big-Q4_K_M-00001-of-00002", "mmproj-F16", "mmproj-BF16"], models.Select(m => m.Name));
        Assert.Equal(50_000_000_000, models.First(m => m.Name.StartsWith("Big", StringComparison.Ordinal)).Size);
        Assert.Equal("Qwen3.5-9B-Q4_K_M", HuggingFace.Pick(models, new HfRef("unsloth/Qwen3.5-9B-GGUF", Quant: "Q4_K_M"))!.Name);
        Assert.Equal("Qwen3.5-9B-Q8_0", HuggingFace.Pick(models, new HfRef("unsloth/Qwen3.5-9B-GGUF", "Qwen3.5-9B-Q8_0.gguf"))!.Name);
        Assert.Null(HuggingFace.Pick(models, new HfRef("unsloth/Qwen3.5-9B-GGUF")));
        // F16 is the usual projector.
        var model = models[0];
        var projector = HuggingFace.ProjectorFor(models, model);
        Assert.Equal("mmproj-F16", projector!.Name);

        var (download, path) = HuggingFace.FilesOf(model, projector, @"D:\llm");
        Assert.Equal(@"D:\llm\Qwen3.5-9B-GGUF\Qwen3.5-9B-Q4_K_M.gguf", path);
        Assert.Equal(["https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf", "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/mmproj-F16.gguf"],
            download.Select(f => f.Url));
        Assert.Equal(@"D:\llm\Qwen3.5-9B-GGUF\mmproj-F16.gguf", download[1].Path);
        Assert.Equal("d", download[1].Sha256);
    }

    [Fact]
    public void AnAssistantSuggestsOneModelOrSeveral()
    {
        var one = AssistantManifest.Parse(Manifest("\"unsloth/Qwen3.5-9B-GGUF:Q4_K_M\""));
        Assert.Equal(["unsloth/Qwen3.5-9B-GGUF:Q4_K_M"], one.Model!.Models.Select(m => m.ToString()));
        var several = AssistantManifest.Parse(Manifest("[\"unsloth/Qwen3.5-9B-GGUF/Qwen3.5-9B-Q4_K_M.gguf\", \"unsloth/Qwen3.5-4B-GGUF:Q4_K_M\"]"));
        Assert.Equal(["unsloth/Qwen3.5-9B-GGUF", "unsloth/Qwen3.5-4B-GGUF"], several.Model!.Models.Select(m => m.Repo));
    }

    private static string Manifest(string suggested) => $$"""
        {
          "id": "test", "name": "Test", "version": "1.0.0", "sdk": "1.4", "author": "K",
          "entry": "lib/Test.dll!Test.Assistant", "traits": { "travel": "fly" },
          "model": { "suggested": {{suggested}} }
        }
        """;
}
