using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 3.1 (its own brain since 5.0.107): the servers' clients, naming and choosing the models, and the brain service against fake servers.</summary>
public class BrainTests
{
    private static ModelInfo Model(string name, double billions, params string[] caps) => new(name, billions, (long)(billions * 0.7e9), caps);

    /// <summary>The user's real Ollama list (21/10/2026): the one of about 4 B with tools wins, the embeddings never.</summary>
    [Fact]
    public void ThePickerPrefersAboutFourBillionsWithTools()
    {
        var models = new[]
        {
            Model("embeddinggemma-2:270m", 0.27, "embedding"),
            Model("Qwen3.8-27b:latest", 26.9, "tools", "thinking", "completion"),
            Model("qwen3.5-4b:latest", 4.7, "completion", "vision", "tools", "thinking"),
            Model("gemma4-e4b:latest", 7.52, "tools", "completion"),
            Model("qwen3.6-35b-a3b:latest", 34.7, "tools", "thinking", "completion"),
            Model("tiny-no-tools", 4.5, "completion"),
        };
        Assert.Equal("qwen3.5-4b:latest", ModelPicker.Choose(models, null)!.Name);
        Assert.Equal("gemma4-e4b:latest", ModelPicker.Choose(models, "gemma4-e4b")!.Name);
        Assert.Equal("qwen3.5-4b:latest", ModelPicker.Choose(models, "not-there")!.Name);
        Assert.DoesNotContain(ModelPicker.Chat(models), m => m.Name.StartsWith("embedding", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("qwen3.5-4b", 4)]
    [InlineData("gemma4-12b-vision", 12)]
    [InlineData("qwen3.6-35b-a3b", 35)]
    [InlineData("llama3.2:1.5b", 1.5)]
    [InlineData("mistral", 0)]
    public void SizesAreGuessedFromTheName(string name, double expected) =>
        Assert.Equal(expected, ModelPicker.SizeOf(new ModelInfo(name, 0, 0, [])));

    [Theory]
    [InlineData("4.7B", 4.7)]
    [InlineData("600M", 0.6)]
    [InlineData("", 0)]
    public void OllamaParameterSizesAreRead(string text, double expected) =>
        Assert.Equal(expected, OllamaChatModel.ParseParameterSize(text), 3);

    [Fact]
    public void OllamaRequestsTurnThinkingOffAndCarryTheTools()
    {
        var request = new ChatRequest("qwen3.5:4b", [ChatMessage.System("s"), ChatMessage.User("hola")], [new ToolDefinition("hora", "La hora", """{"type":"object"}""")]);
        using var body = JsonDocument.Parse(OllamaChatModel.BuildChatBody(request, includeThink: true, keepAlive: "3m"));
        var root = body.RootElement;
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Equal("3m", root.GetProperty("keep_alive").GetString());
        Assert.Equal("hora", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());
    }

    [Fact]
    public void OllamaChunksAreRead()
    {
        var text = OllamaChatModel.ParseChunk("""{"message":{"content":"Hola"},"done":false}""");
        Assert.Equal("Hola", text.Content);
        Assert.False(text.Done);
        var call = OllamaChatModel.ParseChunk("""{"message":{"content":"","tool_calls":[{"function":{"name":"hora","arguments":"{\"zona\":\"Madrid\"}"}}]},"done":true}""");
        Assert.True(call.Done);
        Assert.Equal("Madrid", Assert.Single(call.ToolCalls!).Arguments.GetProperty("zona").GetString());
        Assert.Throws<ChatModelException>(() => OllamaChatModel.ParseChunk("""{"error":"boom"}"""));
    }

    [Fact]
    public void OpenAIRequestsTurnReasoningOff()
    {
        using var body = JsonDocument.Parse(OpenAICompatibleChatModel.BuildChatBody(new ChatRequest("m", [ChatMessage.User("hola")], [])));
        Assert.Equal("none", body.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.False(body.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public void OpenAIToolCallFragmentsAreJoined()
    {
        var pending = new SortedDictionary<int, OpenAICompatibleChatModel.PendingCall>();
        OpenAICompatibleChatModel.ParseEvent("""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c1","function":{"name":"ho","arguments":"{\"a\":"}}]}}]}""", pending);
        var (content, finished, _) = OpenAICompatibleChatModel.ParseEvent("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"ra","arguments":"1}"}}]},"finish_reason":"tool_calls"}]}""", pending);
        Assert.True(finished);
        Assert.Equal("", content);
        Assert.Equal("hora", pending[0].Name);
        Assert.Equal("""{"a":1}""", pending[0].Arguments.ToString());
    }

    [Theory]
    [InlineData("http://localhost:11434", EndpointPlace.ThisPc)]
    [InlineData("http://127.0.0.1:8080", EndpointPlace.ThisPc)]
    [InlineData("http://192.168.1.150:8080", EndpointPlace.LocalNetwork)]
    [InlineData("http://nas.local:11434", EndpointPlace.LocalNetwork)]
    [InlineData("http://servidor:11434", EndpointPlace.LocalNetwork)]
    [InlineData("https://api.example.com", EndpointPlace.Internet)]
    [InlineData("localhost", EndpointPlace.Invalid)]
    public void AddressesSayWhereTheServerIs(string endpoint, EndpointPlace place) => Assert.Equal(place, BrainProviders.PlaceOf(endpoint));

    [Fact]
    public void TheConfigTravelsAsJson()
    {
        var config = new BrainConfig("r1/qwen", Servers: [@"E:\a\llama-server.exe", @"E:\b\bin\cuda\llama-server.exe"], Folders: [@"E:\m"],
            ModelArgs: new Dictionary<string, string> { [@"E:\m\q.gguf"] = "-c 32768" },
            KeepMinutes: 0, OneAtATime: false, Vision: false, Remotes: [new BrainRemote("r1", RemoteKind.LlamaSwap, "", "secret")]);
        var back = BrainConfig.FromJson(JsonNode.Parse(config.ToJson().ToJsonString())!.AsObject());
        Assert.Equal(config.SourcesKey, back.SourcesKey);
        Assert.Equal("-c 32768", back.ModelArgList[@"E:\m\q.gguf"]);
        Assert.Equal("r1/qwen", back.Model);
        var remote = Assert.Single(back.RemoteList);
        Assert.Equal(("http://localhost:8080", "secret", RemoteKind.LlamaSwap), (remote.Address, remote.ApiKey, remote.Kind));
        Assert.Equal(BrainConfig.Default, BrainConfig.FromJson(null));
        // An older K-Desktop does not send them: one model at a time, with vision, the usual half hour.
        var bare = BrainConfig.FromJson(new JsonObject());
        Assert.Equal((true, true, BrainConfig.DefaultKeepMinutes), (bare.OneAtATime, bare.Vision, bare.KeepMinutes));
    }

    [Fact]
    public void EachAssistantsModelTravelsToo()
    {
        var config = BrainConfig.Default with { Models = new Dictionary<string, string> { ["lander"] = "r1/gemma4-e4b" } };
        var back = BrainConfig.FromJson(config.ToJson());
        Assert.Equal("r1/gemma4-e4b", back.AssistantModels["lander"]);
        Assert.Empty(BrainConfig.Default.AssistantModels);
    }

    [Theory]
    [InlineData("qwen3.5:4b", "qwen3.5:4b:latest", true)]
    [InlineData("Qwen3.5:4B", "qwen3.5:4b", true)]
    [InlineData("qwen3.5:4b", "qwen3.5:2b", false)]
    public void ModelNamesMatchWithOrWithoutLatest(string a, string b, bool same) => Assert.Equal(same, BrainProviders.SameModel(a, b));

    /// <summary>5.0.107: a model is named by where it is, so two servers with «qwen3:4b» are two models.</summary>
    [Fact]
    public void ModelsAreNamedByWhereTheyAre()
    {
        Assert.True(ModelRefs.IsLocal(@"E:\m\b.gguf"));
        Assert.False(ModelRefs.IsLocal("r1/b.gguf"));
        Assert.Equal(("r1", "unsloth/qwen:4b"), ModelRefs.SplitRemote("r1/unsloth/qwen:4b"));
        Assert.Null(ModelRefs.SplitRemote(@"E:\m\b.gguf"));
        Assert.Null(ModelRefs.SplitRemote("qwen3:4b"));
        Assert.Equal("b", ModelRefs.Label(@"E:\m\b.gguf"));
        Assert.Equal("qwen3:4b", ModelRefs.Label("r2/qwen3:4b"));
        Assert.True(ModelRefs.Same("r1/qwen3:4b", "r1/qwen3:4b:latest"));
        Assert.False(ModelRefs.Same("r1/qwen3:4b", "r2/qwen3:4b"));
        Assert.True(ModelRefs.Same(@"E:\m\B.gguf", @"e:\m\b.gguf"));
        string[] there = ["r1/qwen3:4b", "r2/qwen3:4b", @"E:\m\Bonsai.gguf"];
        Assert.Equal("r2/qwen3:4b", ModelRefs.Find(there, "r2/qwen3:4b:latest"));
        // A bare name (settings and messages of before) finds the first one with that name.
        Assert.Equal("r1/qwen3:4b", ModelRefs.Find(there, "qwen3:4b"));
        Assert.Equal(@"E:\m\Bonsai.gguf", ModelRefs.Find(there, "Bonsai"));
        Assert.Null(ModelRefs.Find(there, "r3/qwen3:4b"));
        Assert.Null(ModelRefs.Find(there, ModelRefs.None));
    }

    // ── the service against fake servers ────────────────────────────────

    private static readonly BrainRemote Ollama = new("r1", RemoteKind.Ollama);

    private static BrainConfig WithOllama => new(Remotes: [Ollama]);

    /// <summary>Stage 3.2: the Lander thinks with its own model; K-Desktop (no assistant in the process) with its own under its id, or the general one.</summary>
    [Fact]
    public async Task EachAssistantThinksWithItsOwnModelOrTheGeneralOne()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B", "gemma4-e4b:latest|7.5B"] };
        using var brain = new BrainService(() => 12, ollama);
        var own = new Dictionary<string, string> { ["lander"] = "r1/gemma4-e4b", ["dot"] = "r1/not-there:1b", ["blob"] = "gemma4-e4b" };
        await ConfigureAsync(brain, WithOllama with { Models = own });
        Assert.Equal("r1/gemma4-e4b:latest", brain.ModelFor("lander"));
        Assert.Equal("r1/qwen3.5-4b:latest", brain.ModelFor("dot"));
        Assert.Equal("r1/gemma4-e4b:latest", brain.ModelFor("blob"));
        Assert.Equal("r1/qwen3.5-4b:latest", brain.ModelFor(null));
        brain.Configure(WithOllama with { Models = new Dictionary<string, string>(own) { [AssistantTargeting.KDesktop] = "r1/gemma4-e4b" } });
        Assert.Equal("r1/gemma4-e4b:latest", brain.ModelFor(null));

        // Changing only an assistant's model takes effect without looking at the server again.
        brain.Configure(WithOllama with { Models = new Dictionary<string, string>() });
        Assert.Equal("r1/qwen3.5-4b:latest", brain.ModelFor("lander"));
    }

    /// <summary>
    /// Stage 13.10: an assistant thinks harder with its own model when the servers have it; else with the one of «Para
    /// pensar más», else with its usual one; never when thinking harder is off.
    /// </summary>
    [Fact]
    public async Task EachAssistantThinksHarderWithItsOwnModelOrTheGeneralOne()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B", "gemma4-e4b:latest|7.5B", "qwen3.5-9b:latest|9B"] };
        using var brain = new BrainService(() => 12, ollama);
        var think = new Dictionary<string, string> { ["lander"] = "r1/qwen3.5-9b", ["dot"] = "r1/not-there:1b" };
        var config = WithOllama with { Model = "r1/qwen3.5-4b", ThinkModels = think };
        Assert.Equal("r1/qwen3.5-9b", BrainConfig.FromJson(config.ToJson()).AssistantThinkModels["lander"]);
        await ConfigureAsync(brain, config);
        Assert.Equal("r1/qwen3.5-9b:latest", brain.ThinkerFor("lander")?.Model);
        Assert.Equal("r1/qwen3.5-4b:latest", brain.ThinkerFor("dot")?.Model);
        Assert.Equal("r1/qwen3.5-4b:latest", brain.ThinkerFor(null)?.Model);

        brain.Configure(config with { ThinkModel = "r1/gemma4-e4b" });
        Assert.Equal("r1/qwen3.5-9b:latest", brain.ThinkerFor("lander")?.Model);
        Assert.Equal("r1/gemma4-e4b:latest", brain.ThinkerFor("dot")?.Model);
        brain.Configure(config with { ThinkModel = ModelRefs.None });
        Assert.Null(brain.ThinkerFor("lander"));
    }

    [Fact]
    public async Task ItIsReadyWithTheModelItPicks()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B", "Qwen3.8-27b:latest|26.9B"], Loaded = ["qwen3.5-4b:latest"] };
        using var brain = new BrainService(() => 12, ollama);
        var state = await ConfigureAsync(brain, WithOllama);
        Assert.Equal("ready", state.State);
        Assert.Equal("r1/qwen3.5-4b:latest", state.Model);
        Assert.True(state.Automatic);
        Assert.Equal(["r1/qwen3.5-4b:latest"], state.Loaded);
        Assert.Equal(2, state.Models!.Count);
        Assert.All(state.Models, m => Assert.Equal("r1", m.Remote));
        Assert.Equal("ok", Assert.Single(state.Remotes!).State);
        Assert.Equal("r1/qwen3.5-4b:latest", brain.Model);
    }

    /// <summary>Two servers with a model of the same name: two models, each sent to its server.</summary>
    [Fact]
    public async Task TwoServersWithTheSameModelAreNotMixedUp()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B"], Ports = [11434, 11500] };
        using var brain = new BrainService(() => 12, ollama);
        var state = await ConfigureAsync(brain, new BrainConfig("r2/qwen3.5-4b", Remotes: [Ollama, new BrainRemote("r2", RemoteKind.Ollama, "http://localhost:11500")]));
        Assert.Equal(["r1/qwen3.5-4b:latest", "r2/qwen3.5-4b:latest"], state.Models!.Select(m => m.Name));
        Assert.Equal("r2/qwen3.5-4b:latest", state.Model);
        await brain.LoadAsync();
        Assert.Equal(11500, ollama.LastChatPort);
    }

    [Fact]
    public async Task AChosenModelThatIsNotThereIsSaid()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B"] };
        using var brain = new BrainService(() => 12, ollama);
        var state = await ConfigureAsync(brain, WithOllama with { Model = "r1/llama9:70b" });
        Assert.Equal("modelMissing", state.State);
        Assert.Equal("r1/llama9:70b", state.Model);
        Assert.Null(brain.Model);
    }

    [Fact]
    public async Task WithoutModelsItSaysSo()
    {
        using var brain = new BrainService(() => 12, new FakeOllama { Models = ["embeddinggemma:300m|300M|embedding"] });
        var state = await ConfigureAsync(brain, WithOllama);
        Assert.Equal("noModels", state.State);
        Assert.Null(brain.Model);
    }

    [Fact]
    public async Task AServerThatDoesNotAnswerIsSaidButDoesNotStopTheRest()
    {
        using var brain = new BrainService(() => null, new FakeOllama { Down = true });
        var state = await ConfigureAsync(brain, WithOllama);
        Assert.Equal("noModels", state.State);
        var remote = Assert.Single(state.Remotes!);
        Assert.Equal("unreachable", remote.State);
        Assert.NotNull(remote.Message);
    }

    [Fact]
    public async Task NoAIIsOff()
    {
        using var brain = new BrainService(() => null, new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B"] });
        var state = await ConfigureAsync(brain, WithOllama with { Model = ModelRefs.None });
        Assert.Equal("off", state.State);
        Assert.Null(brain.Client);
        Assert.Null(brain.ModelFor(null));
    }

    [Fact]
    public async Task FreeingTheCardUnloadsWhatIsLoaded()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B"], Loaded = ["qwen3.5-4b:latest"] };
        using var brain = new BrainService(() => 12, ollama);
        await ConfigureAsync(brain, WithOllama);
        await brain.HandleAsync(new BrainMessage("unload"));
        Assert.Equal(["qwen3.5-4b:latest"], ollama.Unloaded);
        Assert.Empty(brain.State.Loaded!);
    }

    /// <summary>One model at a time: loading another one unloads what the servers of this PC have loaded.</summary>
    [Fact]
    public async Task OneModelAtATimeUnloadsTheOthers()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B", "gemma4-e4b:latest|7.5B"], Loaded = ["qwen3.5-4b:latest"] };
        using var brain = new BrainService(() => 12, ollama);
        await ConfigureAsync(brain, WithOllama);
        await brain.HandleAsync(new BrainMessage("load", "r1/gemma4-e4b:latest"));
        Assert.Equal(["qwen3.5-4b:latest"], ollama.Unloaded);

        var many = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B", "gemma4-e4b:latest|7.5B"], Loaded = ["qwen3.5-4b:latest"] };
        using var several = new BrainService(() => 12, many);
        await ConfigureAsync(several, WithOllama with { OneAtATime = false });
        await several.HandleAsync(new BrainMessage("load", "r1/gemma4-e4b:latest"));
        Assert.Empty(many.Unloaded);
    }

    /// <summary>Configures and waits for the first state that is not «checking».</summary>
    private static async Task<BrainStateMessage> ConfigureAsync(BrainService brain, BrainConfig config)
    {
        var done = new TaskCompletionSource<BrainStateMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(BrainStateMessage s)
        {
            if (s.State != "checking") done.TrySetResult(s);
        }
        brain.Changed += OnChanged;
        brain.Configure(config);
        try { return await done.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { brain.Changed -= OnChanged; }
    }

    /// <summary>
    /// Ollama's API in memory: <c>/api/tags</c>, <c>/api/ps</c> and <c>/api/chat</c> (keep_alive 0 unloads, any other
    /// loads). Models are «name|size|capability,…». It answers on <see cref="Ports"/>; any other place does not answer.
    /// </summary>
    private sealed class FakeOllama : HttpMessageHandler
    {
        public List<string> Models { get; init; } = [];

        public List<string> Loaded { get; init; } = [];

        public List<string> Unloaded { get; } = [];

        public bool Down { get; init; }

        public int[] Ports { get; init; } = [11434];

        public int LastChatPort { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down || !Ports.Contains(request.RequestUri!.Port)) throw new HttpRequestException("No connection could be made");
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            switch (request.RequestUri.AbsolutePath)
            {
                case "/api/tags":
                    lock (Models) return Json(new { models = Models.Select(ToModel).ToArray() });
                case "/api/ps":
                    lock (Loaded) return Json(new { models = Loaded.Select(n => new { name = n }).ToArray() });
                case "/api/chat":
                    LastChatPort = request.RequestUri.Port;
                    using (var json = JsonDocument.Parse(body))
                    {
                        var model = json.RootElement.GetProperty("model").GetString()!;
                        lock (Loaded)
                        {
                            if (json.RootElement.GetProperty("keep_alive").GetString() == "0")
                            {
                                Unloaded.Add(model);
                                Loaded.Remove(model);
                            }
                            else if (!Loaded.Contains(model))
                            {
                                Loaded.Add(model);
                            }
                        }
                    }
                    return Json(new { done = true });
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static object ToModel(string spec)
        {
            var parts = spec.Split('|');
            var caps = parts.Length > 2 ? parts[2].Split(',') : ["completion", "tools"];
            return new { name = parts[0], size = 1_000_000_000L, details = new { parameter_size = parts.Length > 1 ? parts[1] : "" }, capabilities = caps };
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
