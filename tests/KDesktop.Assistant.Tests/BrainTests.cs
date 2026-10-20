using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Setup;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 3.1: the providers' clients, choosing the model, and the brain service against a fake Ollama.</summary>
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
        var (content, finished) = OpenAICompatibleChatModel.ParseEvent("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"ra","arguments":"1}"}}]},"finish_reason":"tool_calls"}]}""", pending);
        Assert.True(finished);
        Assert.Equal("", content);
        Assert.Equal("hora", pending[0].Name);
        Assert.Equal("""{"a":1}""", pending[0].Arguments.ToString());
    }

    [Fact]
    public void PullProgressAddsTheLayers()
    {
        var tracker = new PullTracker(expectedBytes: 0);
        tracker.Feed("""{"status":"pulling manifest"}""");
        tracker.Feed("""{"status":"pulling a","digest":"a","total":100,"completed":50}""");
        var step = tracker.Feed("""{"status":"pulling b","digest":"b","total":300,"completed":30}""")!;
        Assert.Equal(80, step.Completed);
        Assert.Equal(400, step.Total);
        Assert.False(tracker.Done);
        var done = tracker.Feed("""{"status":"success"}""")!;
        Assert.True(tracker.Done);
        Assert.Equal(400, done.Completed);
        Assert.Throws<ChatModelException>(() => new PullTracker(0).Feed("""{"error":"pull model manifest: file does not exist"}"""));
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
        var config = new BrainConfig(BrainProvider.LlamaSwap, "", "secret", "qwen");
        var back = BrainConfig.FromJson(config.ToJson());
        Assert.Equal(BrainProvider.LlamaSwap, back.Provider);
        Assert.Equal("http://localhost:8080", back.Address);
        Assert.Equal("secret", back.ApiKey);
        Assert.Equal("qwen", back.Model);
        Assert.Equal(BrainConfig.Default, BrainConfig.FromJson(null));
    }

    [Fact]
    public void EachAssistantsModelTravelsToo()
    {
        var config = BrainConfig.Default with { Models = new Dictionary<string, string> { ["lander"] = "gemma4-e4b" } };
        var back = BrainConfig.FromJson(config.ToJson());
        Assert.Equal("gemma4-e4b", back.AssistantModels["lander"]);
        Assert.Empty(BrainConfig.Default.AssistantModels);
    }

    [Theory]
    [InlineData("qwen3.5:4b", "qwen3.5:4b:latest", true)]
    [InlineData("Qwen3.5:4B", "qwen3.5:4b", true)]
    [InlineData("qwen3.5:4b", "qwen3.5:2b", false)]
    public void ModelNamesMatchWithOrWithoutLatest(string a, string b, bool same) => Assert.Equal(same, BrainProviders.SameModel(a, b));

    // ── the service against a fake Ollama ───────────────────────────────

    /// <summary>Stage 3.2: the Lander thinks with its own model and the module without a body with the general one.</summary>
    [Fact]
    public async Task EachAssistantThinksWithItsOwnModelOrTheGeneralOne()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B", "gemma4-e4b:latest|7.5B"] };
        using var brain = new BrainService(() => 12, ollama);
        var own = new Dictionary<string, string> { ["lander"] = "gemma4-e4b", ["dot"] = "not-there:1b" };
        await ConfigureAsync(brain, BrainConfig.Default with { Models = own });
        Assert.Equal("gemma4-e4b:latest", brain.ModelFor("lander"));
        Assert.Equal("qwen3.5-4b:latest", brain.ModelFor("dot"));
        Assert.Equal("qwen3.5-4b:latest", brain.ModelFor(null));

        // Changing only an assistant's model takes effect without looking at the server again.
        brain.Configure(BrainConfig.Default with { Models = new Dictionary<string, string>() });
        Assert.Equal("qwen3.5-4b:latest", brain.ModelFor("lander"));
    }


    [Fact]
    public async Task ItIsReadyWithTheModelItPicks()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B", "Qwen3.8-27b:latest|26.9B"], Loaded = ["qwen3.5-4b:latest"] };
        using var brain = new BrainService(() => 12, ollama);
        var state = await ConfigureAsync(brain, BrainConfig.Default);
        Assert.Equal("ready", state.State);
        Assert.Equal("qwen3.5-4b:latest", state.Model);
        Assert.True(state.Automatic);
        Assert.Equal(["qwen3.5-4b:latest"], state.Loaded);
        Assert.Equal(2, state.Models!.Count);
        Assert.Equal("qwen3.5-4b:latest", brain.Model);
    }

    [Fact]
    public async Task AChosenModelThatIsNotThereIsSaid()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B"] };
        using var brain = new BrainService(() => 12, ollama);
        var state = await ConfigureAsync(brain, BrainConfig.Default with { Model = "llama9:70b" });
        Assert.Equal("modelMissing", state.State);
        Assert.Equal("llama9:70b", state.Model);
        Assert.Null(brain.Model);
    }

    [Fact]
    public async Task WithoutModelsItSuggestsOneForTheVideoMemory()
    {
        using var small = new BrainService(() => 4, new FakeOllama());
        var state = await ConfigureAsync(small, BrainConfig.Default);
        Assert.Equal("noModels", state.State);
        Assert.Equal(ModelAdvice.Light.Name, state.Suggested!.Name);

        using var big = new BrainService(() => 12, new FakeOllama { Models = ["embeddinggemma:300m|300M|embedding"] });
        state = await ConfigureAsync(big, BrainConfig.Default);
        Assert.Equal("noModels", state.State);
        Assert.Equal(ModelAdvice.Standard.Name, state.Suggested!.Name);
    }

    [Fact]
    public async Task NoServerIsUnreachable()
    {
        using var brain = new BrainService(() => null, new FakeOllama { Down = true });
        var state = await ConfigureAsync(brain, BrainConfig.Default);
        Assert.Equal("unreachable", state.State);
        Assert.NotNull(state.Message);
    }

    [Fact]
    public async Task NoAIIsOff()
    {
        using var brain = new BrainService(() => null, new FakeOllama());
        var state = await ConfigureAsync(brain, new BrainConfig(BrainProvider.None));
        Assert.Equal("off", state.State);
        Assert.Null(brain.Client);
    }

    [Fact]
    public async Task ADownloadReportsItsProgressAndThenTheModelIsThere()
    {
        var ollama = new FakeOllama();
        using var brain = new BrainService(() => 12, ollama);
        await ConfigureAsync(brain, BrainConfig.Default);
        var jobs = new List<BrainJob>();
        var ready = new TaskCompletionSource<BrainStateMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        brain.Changed += s =>
        {
            if (s.Job is { } job) lock (jobs) jobs.Add(job);
            if (s is { State: "ready", Job.State: "done" }) ready.TrySetResult(s);
        };
        await brain.HandleAsync(new BrainMessage("pull", "qwen3.5:4b"));
        var state = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("qwen3.5:4b", state.Model);
        lock (jobs)
        {
            Assert.Contains(jobs, j => j is { State: "running", Kind: "pull" });
            Assert.Equal("done", jobs[^1].State);
        }
    }

    [Fact]
    public async Task FreeingTheCardUnloadsWhatIsLoaded()
    {
        var ollama = new FakeOllama { Models = ["qwen3.5-4b:latest|4.7B"], Loaded = ["qwen3.5-4b:latest"] };
        using var brain = new BrainService(() => 12, ollama);
        await ConfigureAsync(brain, BrainConfig.Default);
        await brain.HandleAsync(new BrainMessage("unload"));
        Assert.Equal(["qwen3.5-4b:latest"], ollama.Unloaded);
        Assert.Empty(brain.State.Loaded!);
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
        if (brain.State.State != "checking") done.TrySetResult(brain.State);
        try { return await done.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { brain.Changed -= OnChanged; }
    }

    /// <summary>
    /// Ollama's API in memory: <c>/api/tags</c>, <c>/api/ps</c>, <c>/api/pull</c> (two layers, then the model is there) and
    /// <c>/api/chat</c> with keep_alive 0 (unload). Models are «name|size|capability,…». Any other place does not answer.
    /// </summary>
    private sealed class FakeOllama : HttpMessageHandler
    {
        public List<string> Models { get; init; } = [];

        public List<string> Loaded { get; init; } = [];

        public List<string> Unloaded { get; } = [];

        public bool Down { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down || request.RequestUri!.Port != 11434) throw new HttpRequestException("No connection could be made");
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            switch (request.RequestUri.AbsolutePath)
            {
                case "/api/tags":
                    lock (Models) return Json(new { models = Models.Select(ToModel).ToArray() });
                case "/api/ps":
                    lock (Loaded) return Json(new { models = Loaded.Select(n => new { name = n }).ToArray() });
                case "/api/pull":
                    var name = JsonDocument.Parse(body).RootElement.GetProperty("model").GetString()!;
                    var lines = new StringBuilder()
                        .AppendLine("""{"status":"pulling manifest"}""")
                        .AppendLine("""{"status":"pulling","digest":"a","total":1000,"completed":400}""")
                        .AppendLine("""{"status":"pulling","digest":"a","total":1000,"completed":1000}""")
                        .AppendLine("""{"status":"success"}""");
                    lock (Models) Models.Add(name + "|4.7B");
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(lines.ToString()) };
                case "/api/chat":
                    using (var json = JsonDocument.Parse(body))
                    {
                        var model = json.RootElement.GetProperty("model").GetString()!;
                        if (json.RootElement.GetProperty("keep_alive").GetString() == "0")
                        {
                            Unloaded.Add(model);
                            lock (Loaded) Loaded.Remove(model);
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
