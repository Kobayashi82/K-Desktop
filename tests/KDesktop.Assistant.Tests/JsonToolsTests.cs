using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Assistant.Tools.Declarative;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 9.1: JSON tools (http, kdesktop, routine, script), their secrets and the folder that reloads them.</summary>
public sealed class JsonToolsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-json-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly HttpListener _listener = new();
    private readonly string _address;
    private readonly List<string> _requests = [];
    private readonly List<AskMessage> _asked = [];
    private readonly HashSet<string> _trusted = new(StringComparer.Ordinal);
    private readonly Bridge _bridge = new();
    private readonly List<IDisposable> _owned = [];
    private string _answer = "yes";
    private string _policy = AskPolicy.Risky;

    public JsonToolsTests()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _address = $"http://localhost:{port}/";
        _listener.Prefixes.Add(_address);
        _listener.Start();
        _ = Task.Run(ServeAsync);
        Directory.CreateDirectory(ToolsFolder);
    }

    private string ToolsFolder => Path.Combine(_folder, "Tools");

    private string SecretsFile => Path.Combine(_folder, "secrets.json");

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        _listener.Close();
        try { Directory.Delete(_folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A pretend Home Assistant: switches lights, has a sensor, says back the Authorization it got, fails.</summary>
    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            var request = context.Request;
            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync();
            lock (_requests) _requests.Add($"{request.HttpMethod} {request.Url!.AbsolutePath}{request.Url.Query} | {request.Headers["Authorization"]} | {body}");
            var path = request.Url.AbsolutePath;
            var (status, answer) = path switch
            {
                "/api/services/light/turn_on" or "/api/services/light/turn_off" =>
                    (200, $$"""[{"entity_id":"light.salon","state":"{{(path.EndsWith("on", StringComparison.Ordinal) ? "on" : "off")}}"}]"""),
                "/api/states/sensor.salon_temperatura" => (200, """{"entity_id":"sensor.salon_temperatura","state":"21.5","attributes":{"unit_of_measurement":"°C"}}"""),
                "/echo" => (200, "Me llegó: " + request.Headers["Authorization"]),
                _ => (500, "base caída"),
            };
            var bytes = Encoding.UTF8.GetBytes(answer);
            context.Response.StatusCode = status;
            context.Response.ContentType = answer.StartsWith('[') || answer.StartsWith('{') ? "application/json" : "text/plain; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private (ToolRunner Runner, JsonTools Json, ToolRegistry Registry) Make()
    {
        var registry = new ToolRegistry();
        var builtin = new BuiltinTools(_folder);
        builtin.AddTo(registry);
        var secrets = new ToolSecrets(SecretsFile);
        var json = new JsonTools(ToolsFolder, registry, secrets, _bridge);
        var runner = new ToolRunner(registry, () => _trusted, (ask, _) =>
        {
            lock (_asked) _asked.Add(ask);
            return Task.FromResult(_answer);
        }, _ => { }, Path.Combine(_folder, "undo.json"), () => _policy, Path.Combine(_folder, "approved.json"));
        json.Runner = runner;
        _owned.Add(builtin);
        _owned.Add(json);
        json.Start();
        return (runner, json, registry);
    }

    private void Write(string file, string json) => File.WriteAllText(Path.Combine(ToolsFolder, file), json);

    private void Secrets(string json) => File.WriteAllText(SecretsFile, json);

    /// <summary>The living room light of the plan (§7.2), pointing at the pretend Home Assistant.</summary>
    private string Light => $$$"""
        {
          "name": "luz_salon",
          "type": "http",
          "description": "Turns the living room light on or off.",
          "examples": ["enciende la luz del salón"],
          "parameters": { "estado": { "type": "string", "enum": ["on", "off"] } },
          "request": {
            "method": "POST",
            "url": "{{{_address}}}api/services/light/turn_{{estado}}",
            "headers": { "Authorization": "Bearer {{secret:ha_token}}" },
            "body": { "entity_id": "light.salon" }
          },
          "intent": "poner la luz del salón en {{estado}}",
          "say": { "ok": "Luz del salón: {{estado}}.", "error": "La base no responde." }
        }
        """;

    private static JsonElement Args(string json) => ToolArguments.Parse(json);

    private static readonly ToolContext Lander = new("lander", "enciende la luz del salón");

    private string[] Requests()
    {
        lock (_requests) return [.. _requests];
    }

    [Fact]
    public void TheExamplesOfTheGuideAreValidAndLoad()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Contracts.DocsDir, "herramientas")))
            File.Copy(file, Path.Combine(ToolsFolder, Path.GetFileName(file)));
        var (_, json, registry) = Make();
        Assert.All(json.Files, f => Assert.True(f.Errors.Count == 0, Path.GetFileName(f.File) + ": " + string.Join("; ", f.Errors)));
        Assert.Equal(7, json.Files.Count);
        foreach (var name in new[] { "luz_salon", "temperatura_salon", "send_telegram", "send_telegram_file", "modo_cine", "buenas_noches", "comprobar_conexion" })
            Assert.Equal(JsonTools.Group, registry.GroupOf(name));
        Assert.Equal(ToolRegistry.Builtin, registry.GroupOf("calculate"));
    }

    [Fact]
    public async Task TheLivingRoomLightWorksWithItsSecretKeptEncrypted()
    {
        Secrets("""{ "ha_token": "abc123-token-secreto" }""");
        Write("luz_salon.json", Light);
        var (runner, _, registry) = Make();

        var tool = registry.Find("luz_salon")!;
        Assert.Equal("poner la luz del salón en on", tool.Intent(Args("""{"estado":"on"}""")));
        Assert.Contains("\"enum\":[\"on\",\"off\"]", tool.ParametersSchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"estado\"]", tool.ParametersSchemaJson, StringComparison.Ordinal);

        var outcome = await runner.RunAsync("luz_salon", Args("""{"estado":"ON"}"""), Lander, Lander.Heard!, "ai", default);
        var result = outcome.Result!;
        Assert.False(result.Failed, result.Text);
        Assert.True(result.Acted);
        Assert.Equal("Luz del salón: on.", result.Reply);
        Assert.StartsWith("Done.", result.Text, StringComparison.Ordinal);
        Assert.Equal("POST /api/services/light/turn_on | Bearer abc123-token-secreto | {\"entity_id\":\"light.salon\"}", Assert.Single(Requests()));
        Assert.Empty(_asked);

        // Written in clear by hand: encrypted the first time it was read.
        var kept = File.ReadAllText(SecretsFile);
        Assert.DoesNotContain("abc123", kept, StringComparison.Ordinal);
        Assert.Contains("dpapi:", kept, StringComparison.Ordinal);
        Assert.Equal("abc123-token-secreto", new ToolSecrets(SecretsFile).Get("ha_token"));
    }

    [Fact]
    public async Task ASecretInTheAnswerNeverReachesTheModel()
    {
        Secrets("""{ "ha_token": "abc123-token-secreto" }""");
        Write("eco.json", $$$"""
            { "name": "eco", "type": "http", "description": "Echo.", "readsOnly": true,
              "request": { "url": "{{{_address}}}echo", "headers": { "Authorization": "Bearer {{secret:ha_token}}" } },
              "say": { "ok": "Dice: {{result}}" } }
            """);
        var (runner, _, _) = Make();
        var result = (await runner.RunAsync("eco", Args("{}"), Lander, "eco", "ai", default)).Result!;
        Assert.False(result.Failed, result.Text);
        Assert.Equal("Me llegó: Bearer •••", result.Text);
        Assert.Equal("Dice: Me llegó: Bearer •••", result.Reply);
        Assert.False(result.Acted);
    }

    [Fact]
    public async Task AMissingSecretIsSaidAndNothingIsSent()
    {
        Write("luz_salon.json", Light);
        var (runner, _, _) = Make();
        var result = (await runner.RunAsync("luz_salon", Args("""{"estado":"on"}"""), Lander, "luz", "ai", default)).Result!;
        Assert.True(result.Failed);
        Assert.Contains("ha_token", result.Text, StringComparison.Ordinal);
        Assert.Equal("La base no responde.", result.Reply);
        Assert.Empty(Requests());
    }

    [Fact]
    public async Task WrongArgumentsAreToldToTheModel()
    {
        Secrets("""{ "ha_token": "abc123-token-secreto" }""");
        Write("luz_salon.json", Light);
        var (runner, _, _) = Make();
        var result = (await runner.RunAsync("luz_salon", Args("""{"estado":"dim"}"""), Lander, "luz", "ai", default)).Result!;
        Assert.True(result.Failed);
        Assert.Contains("on, off", result.Text, StringComparison.Ordinal);
        result = (await runner.RunAsync("luz_salon", Args("{}"), Lander, "luz", "ai", default)).Result!;
        Assert.Contains("Missing parameter «estado»", result.Text, StringComparison.Ordinal);
        Assert.Empty(Requests());
    }

    [Fact]
    public async Task AReadingGivesThePickedPartAndAnErrorItsWords()
    {
        Write("temperatura.json", $$$"""
            { "name": "temperatura", "type": "http", "description": "Reads the temperature.", "readsOnly": true,
              "request": { "url": "{{{_address}}}api/states/sensor.salon_temperatura", "pick": "state" },
              "say": { "ok": "Hay {{result}} {{result.attributes.unit_of_measurement}}." } }
            """);
        Write("roto.json", $$$"""
            { "name": "roto", "type": "http", "description": "Fails.", "request": { "url": "{{{_address}}}nada" },
              "say": { "error": "La base no responde ({{status}})." } }
            """);
        var (runner, _, _) = Make();
        var reading = (await runner.RunAsync("temperatura", Args("{}"), Lander, "temperatura", "ai", default)).Result!;
        Assert.Equal("21.5", reading.Text);
        Assert.Equal("Hay 21.5 °C.", reading.Reply);

        var failed = (await runner.RunAsync("roto", Args("{}"), Lander, "roto", "ai", default)).Result!;
        Assert.True(failed.Failed);
        Assert.Contains("HTTP 500", failed.Text, StringComparison.Ordinal);
        Assert.Contains("base caída", failed.Text, StringComparison.Ordinal);
        Assert.Equal("La base no responde (500).", failed.Reply);
    }

    [Fact]
    public async Task SavingAFileReloadsTheToolsAndABrokenOneIsTold()
    {
        var (_, json, registry) = Make();
        Assert.Empty(json.Files);
        var broken = new TaskCompletionSource<JsonToolFile>(TaskCreationOptions.RunContinuationsAsynchronously);
        json.Broken += f => broken.TrySetResult(f);

        // Without a type: the schema says so.
        Write("mal.json", """{ "name": "mal", "description": "x" }""");
        var told = await broken.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.EndsWith("mal.json", told.File, StringComparison.Ordinal);
        Assert.Contains(told.Errors, e => e.Contains("type", StringComparison.Ordinal));
        Assert.Null(registry.Find("mal"));

        // A name of a built-in tool, and a name with a dot: refused.
        Assert.NotEmpty(Contracts.Validate("tool", """{ "name": "luz.salon", "type": "kdesktop", "description": "x", "command": "y" }"""));
        Assert.NotEmpty(Contracts.Validate("tool", """{ "name": "mcp_algo", "type": "kdesktop", "description": "x", "command": "y" }"""));
        var loaded = Changed(json);
        Write("mal.json", """{ "name": "calculate", "type": "kdesktop", "description": "x", "command": "y" }""");
        await loaded.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("incluida", Assert.Single(json.Files).Errors[0], StringComparison.Ordinal);
        Assert.IsType<CalculateTool>(registry.Find("calculate"));

        // Fixed: there it is. Switched off: gone, and not an error.
        loaded = Changed(json);
        Write("mal.json", """{ "name": "modo_cine", "type": "kdesktop", "description": "Film mode.", "command": "Modo cine" }""");
        await loaded.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsType<CommandJsonTool>(registry.Find("modo_cine"));
        loaded = Changed(json);
        Write("mal.json", """{ "name": "modo_cine", "type": "kdesktop", "description": "Film mode.", "command": "Modo cine", "enabled": false }""");
        await loaded.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(registry.Find("modo_cine"));
        Assert.Empty(Assert.Single(json.Files).Errors);
    }

    /// <summary>The next load whose result differs (the watcher may load more than once per save).</summary>
    private static Task Changed(JsonTools json)
    {
        var before = string.Join("|", json.Files.Select(f => f.Name + string.Join(",", f.Errors) + f.Enabled));
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        json.Loaded += () =>
        {
            if (string.Join("|", json.Files.Select(f => f.Name + string.Join(",", f.Errors) + f.Enabled)) != before) done.TrySetResult();
        };
        return done.Task;
    }

    [Fact]
    public async Task AKDesktopToolRunsItsCommandWithItsOwnWords()
    {
        Write("escena.json", """
            { "name": "escena", "type": "kdesktop", "description": "Sets a scene.",
              "parameters": { "cual": { "type": "string", "enum": ["cine", "juego"] } },
              "command": "Escena {{cual}}", "say": { "ok": "Escena {{cual}} puesta." } }
            """);
        Write("falta.json", """{ "name": "falta", "type": "kdesktop", "description": "Missing.", "command": "Escena playa" }""");
        var (runner, _, _) = Make();
        var result = (await runner.RunAsync("escena", Args("""{"cual":"cine"}"""), Lander, "pon la escena de cine", "ai", default)).Result!;
        Assert.False(result.Failed, result.Text);
        Assert.Equal("Escena cine puesta.", result.Reply);
        Assert.Equal("run:id-Escena cine:lander", Assert.Single(_bridge.Done));

        // A command that is not there by that exact name is not swapped for one like it.
        result = (await runner.RunAsync("falta", Args("{}"), Lander, "x", "ai", default)).Result!;
        Assert.True(result.Failed);
        Assert.Contains("no command called «Escena playa»", result.Text, StringComparison.Ordinal);
        Assert.Contains("Escena cine", result.Text, StringComparison.Ordinal);
        Assert.Single(_bridge.Done);

        _bridge.Status = "declined";
        result = (await runner.RunAsync("escena", Args("""{"cual":"juego"}"""), Lander, "x", "ai", default)).Result!;
        Assert.True(result.Failed);
    }

    [Fact]
    public async Task ARoutineRunsItsStepsInOrderButNotAnotherRoutine()
    {
        Secrets("""{ "ha_token": "abc123-token-secreto" }""");
        Write("luz_salon.json", Light);
        Write("noches.json", """
            { "name": "buenas_noches", "type": "routine", "description": "Bedtime.",
              "steps": [ { "tool": "luz_salon", "arguments": { "estado": "off" } }, { "wait": 0.1 }, { "command": "Apagar pantalla" } ],
              "say": { "ok": "Buenas noches." } }
            """);
        Write("otra.json", """
            { "name": "otra", "type": "routine", "description": "Nested.", "steps": [ { "tool": "buenas_noches" } ] }
            """);
        var (runner, _, _) = Make();
        var result = (await runner.RunAsync("buenas_noches", Args("{}"), Lander, "buenas noches", "ai", default)).Result!;
        Assert.False(result.Failed, result.Text);
        Assert.Equal("Buenas noches.", result.Reply);
        Assert.StartsWith("POST /api/services/light/turn_off", Assert.Single(Requests()), StringComparison.Ordinal);
        Assert.Equal("run:id-Apagar pantalla:lander", Assert.Single(_bridge.Done));
        Assert.Contains("3. Apagar pantalla", result.Text, StringComparison.Ordinal);

        var nested = (await runner.RunAsync("otra", Args("{}"), Lander, "x", "ai", default)).Result!;
        Assert.True(nested.Failed);
        Assert.Contains("cannot run another routine", nested.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScriptAsksUntilItIsApprovedEvenWhenNothingAsksAndAgainWhenItChanges()
    {
        _policy = AskPolicy.Never;
        Secrets("""{ "clave": "zz-secreto-99" }""");
        var script = Path.Combine(ToolsFolder, "saluda.ps1");
        File.WriteAllText(script, "param([string]$Name)\nWrite-Output \"Hola, $Name. Clave: $env:CLAVE. Ñandú\"\n", new UTF8Encoding(true));
        Write("saluda.json", """
            { "name": "saluda", "type": "script", "description": "Says hello.", "readsOnly": true,
              "parameters": { "name": { "type": "string" } },
              "script": { "file": "saluda.ps1", "args": ["{{name}}"], "env": { "CLAVE": "{{secret:clave}}" } } }
            """);
        var (runner, _, _) = Make();

        var outcome = await runner.RunAsync("saluda", Args("""{"name":"Ana"}"""), Lander, "saluda", "ai", default);
        Assert.False(outcome.Result!.Failed, outcome.Result.Text);
        Assert.Equal("Hola, Ana. Clave: •••. Ñandú", outcome.Result.Text);
        var asked = Assert.Single(_asked);
        Assert.True(asked.CanTrust);
        Assert.Contains("no has aprobado", asked.Text, StringComparison.Ordinal);

        // «Yes» once: it asks again; «always»: no more.
        await runner.RunAsync("saluda", Args("""{"name":"Ana"}"""), Lander, "saluda", "ai", default);
        Assert.Equal(2, _asked.Count);
        _answer = "always";
        await runner.RunAsync("saluda", Args("""{"name":"Ana"}"""), Lander, "saluda", "ai", default);
        _answer = "no";
        outcome = await runner.RunAsync("saluda", Args("""{"name":"Ana"}"""), Lander, "saluda", "ai", default);
        Assert.Equal(3, _asked.Count);
        Assert.True(outcome.Ran);

        // The script changed: it asks again (and «no» is a no).
        File.AppendAllText(script, "Write-Output 'otra cosa'\n");
        outcome = await runner.RunAsync("saluda", Args("""{"name":"Ana"}"""), Lander, "saluda", "ai", default);
        Assert.Equal(4, _asked.Count);
        Assert.False(outcome.Ran);
    }

    [Fact]
    public async Task ABatDoesNotGetCharactersThatWouldMakeCmdDoSomethingElse()
    {
        _policy = AskPolicy.Never;
        File.WriteAllText(Path.Combine(ToolsFolder, "eco.bat"), "@echo off\r\necho Recibido: %~1\r\n");
        Write("eco.json", """
            { "name": "eco_bat", "type": "script", "description": "Echo.", "readsOnly": true,
              "parameters": { "text": { "type": "string" } }, "script": { "file": "eco.bat", "args": ["{{text}}"] } }
            """);
        var (runner, _, _) = Make();
        var good = (await runner.RunAsync("eco_bat", Args("""{"text":"hola mundo"}"""), Lander, "eco", "ai", default)).Result!;
        Assert.Equal("Recibido: hola mundo", good.Text);
        var bad = (await runner.RunAsync("eco_bat", Args("""{"text":"x & del *"}"""), Lander, "eco", "ai", default)).Result!;
        Assert.True(bad.Failed);
        Assert.Contains("caracteres", bad.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("buenas noches", true)]
    [InlineData("Lander, buenas noches.", true)]
    [InlineData("Buenas noches, por favor", true)]
    [InlineData("buenas nochez", true)]
    [InlineData("buenas noches a todos", false)]
    [InlineData("noches", false)]
    public void APhraseOfAToolIsSaidOnlyWhenItIsAllThatWasSaid(string said, bool says) =>
        Assert.Equal(says, KDesktop.Assistant.Brain.Talk.QuickRules.SaysPhrase(said, "buenas noches", ["Lander"]));

    private sealed class Bridge : ICommandBridge
    {
        public List<string> Done { get; } = [];

        public string Status { get; set; } = "ok";

        /// <summary>Every text names a command of that name (id «id-» + name), and «Escena» something else too.</summary>
        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult<MatchedMessage?>(new MatchedMessage("x", 0.9, [], [new MatchedPart(text, true,
                text.StartsWith("Escena", StringComparison.Ordinal) && text != "Escena cine" && text != "Escena juego"
                    ? [new MatchedCandidate("id-Escena cine", "Escena cine", 0.8, null)]
                    : [new MatchedCandidate("id-" + text, text, 0.95, null), new MatchedCandidate("id-otro", "Otro", 0.5, null)])]));

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken)
        {
            Done.Add($"run:{command}:{assistant}");
            return Task.FromResult<EndMessage?>(new EndMessage("r", Status, []));
        }

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }
}
