using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Declarative;

/// <summary>What the JSON tools reach: the secrets, K-Desktop's commands, the other tools (a routine) and the web.</summary>
public sealed class JsonToolEnvironment
{
    /// <param name="folder">The folder of the tool files (a script's relative path starts there).</param>
    public JsonToolEnvironment(string folder, ToolSecrets? secrets, ICommandBridge? commands, HttpClient http)
    {
        Folder = folder;
        Secrets = secrets;
        Commands = commands;
        Http = http;
    }

    public string Folder { get; }

    public ToolSecrets? Secrets { get; }

    public ICommandBridge? Commands { get; }

    public HttpClient Http { get; }

    /// <summary>Runs the steps of a routine (asking and writing the activity like any call); set once the runner exists.</summary>
    public ToolRunner? Runner { get; set; }
}

/// <summary>
/// A tool written as a JSON file (stage 9.1, plan §7.2): <see cref="JsonToolDefinition"/> says what it is; each type
/// does it its own way. The model sees its name, description and parameters; never a secret: what it gets back and
/// what is said have them masked.
/// </summary>
public abstract class JsonTool : ITool
{
    /// <summary>How much of an answer the model gets.</summary>
    public const int MaxResult = 3000;

    /// <summary>How much of it <c>{{result}}</c> puts in what is said.</summary>
    private const int MaxSaid = 200;

    private readonly string _schema;

    protected JsonTool(JsonToolDefinition definition, string file, JsonToolEnvironment environment)
    {
        Definition = definition;
        File = file;
        Environment = environment;
        _schema = definition.ParametersSchema();
    }

    public JsonToolDefinition Definition { get; }

    /// <summary>Its file.</summary>
    public string File { get; }

    protected JsonToolEnvironment Environment { get; }

    public string Name => Definition.Name;

    public string Description => Definition.Description;

    public string ParametersSchemaJson => _schema;

    public IReadOnlyList<string> Examples => Definition.Examples ?? [];

    public ToolRisk Risk => Definition.RiskLevel;

    public bool ReadsOnly => Definition.ReadsOnly;

    public virtual string? Fingerprint => null;

    /// <summary>Builds the tool of <paramref name="definition"/>'s type.</summary>
    public static JsonTool Create(JsonToolDefinition definition, string file, JsonToolEnvironment environment) => definition.Type switch
    {
        "http" => new HttpJsonTool(definition, file, environment),
        "kdesktop" => new CommandJsonTool(definition, file, environment),
        "routine" => new RoutineJsonTool(definition, file, environment),
        "script" => new ScriptJsonTool(definition, file, environment),
        _ => throw new ArgumentException($"Tipo desconocido: {definition.Type}"),
    };

    public string Intent(JsonElement arguments)
    {
        var values = Definition.Resolve(arguments, out _);
        return Definition.Intent is { Length: > 0 } intent
            ? new JsonTemplate(values, Environment.Secrets, withSecrets: false).Fill(intent)
            : Loc.T("tools.json.intent", Name.Replace('_', ' '));
    }

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var values = Definition.Resolve(arguments, out var problem);
        if (problem is not null) return ToolResult.Fail(problem + " Fix the call, or ask the user for it.");

        var template = new JsonTemplate(values, Environment.Secrets, withSecrets: true);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Definition.TimeLimit);
        try
        {
            var ran = await RunCoreAsync(template, context, limit.Token).ConfigureAwait(false);
            if (!ran.Ok) return Failure(template, ran.Text, ran.Reply);
            return new ToolResult(Mask(ran.Text))
            {
                Reply = Say(Definition.Say?.Ok, template) ?? ran.Reply,
                Acted = !ReadsOnly,
            };
        }
        catch (MissingSecretException ex)
        {
            Log.Warn($"Asistente: la herramienta {Name} necesita el secreto «{ex.Name}», que no está");
            return Failure(template, $"Not done: the secret «{ex.Name}» is not set. The user must add it in Options.", Loc.T("tools.json.noSecret", ex.Name));
        }
        catch (MissingProgramException ex)
        {
            return Failure(template, $"Not done: {ex.Program} is not installed on this PC.", Loc.T("tools.json.noProgram", ex.Program));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn($"Asistente: la herramienta {Name} ha tardado más de {Definition.TimeLimit.TotalSeconds:0} s");
            return Failure(template, $"Not done: it took more than {Definition.TimeLimit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or ArgumentException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            Log.Warn($"Asistente: la herramienta {Name} ha fallado: {Mask(ex.Message)}");
            return Failure(template, "Failed: " + ex.Message);
        }
    }

    /// <summary>Does it; <see cref="JsonTemplate.Result"/> and <see cref="JsonTemplate.Status"/> say what came back.</summary>
    protected abstract Task<Ran> RunCoreAsync(JsonTemplate template, ToolContext context, CancellationToken cancellationToken);

    /// <param name="Text">For the model.</param>
    /// <param name="Reply">What is said when the file says nothing (in the user's language), or null.</param>
    protected sealed record Ran(bool Ok, string Text, string? Reply = null);

    /// <summary>What a tool got back: <c>{{result}}</c> (the picked part) and <c>{{result.campo}}</c>.</summary>
    protected static string Answer(JsonTemplate template, string text, string? pick)
    {
        JsonNode? node = null;
        if (text.TrimStart() is ['{' or '[', ..])
        {
            try { node = JsonNode.Parse(text); }
            catch (JsonException) { node = null; }
        }
        template.Result = node ?? JsonValue.Create(text);
        var picked = node is null ? text : JsonTemplate.Pick(node, pick) is { } part ? part is JsonValue ? JsonToolDefinition.Text(part) : part.ToJsonString() : string.Empty;
        template.ResultText = Cut(picked.Trim(), MaxResult);
        return template.ResultText;
    }

    /// <summary>One of the user's commands, by name (a <c>kdesktop</c> tool, a step of a routine).</summary>
    protected static async Task<Ran> RunCommandAsync(JsonToolEnvironment environment, string command, JsonTemplate template, ToolContext context, CancellationToken cancellationToken)
    {
        if (environment.Commands is null) return new Ran(false, "Not done: K-Desktop is not there.");
        // By its exact name (K-Desktop runs commands by id): a file says which command, never «one like it».
        var matched = await environment.Commands.MatchAsync(command, cancellationToken).ConfigureAwait(false);
        if (matched is null) return new Ran(false, "Not done: K-Desktop did not answer.");
        var candidates = matched.Parts.SelectMany(p => p.Candidates).Where(c => c.Command is not null).ToList();
        if (candidates.FirstOrDefault(c => string.Equals(c.Name.Trim(), command.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } exact)
            return new Ran(false, $"Not done: the user has no command called «{command}»."
                + (candidates.Count > 0 ? $" The closest: {CommandTools.List(candidates.Take(3))}." : string.Empty));
        var end = await environment.Commands.RunAsync(exact.Command!, context.Assistant, cancellationToken).ConfigureAwait(false);
        template.Status = end?.Status == "ok" ? 0 : 1;
        if (end is { Messages.Count: > 0 }) Answer(template, string.Join(" ", end.Messages), null);
        var outcome = CommandTools.Outcome(end, command, "run_command");
        return new Ran(!outcome.Failed, outcome.Text, outcome.Reply);
    }

    protected string Mask(string text) => Environment.Secrets?.Mask(text) ?? text;

    private ToolResult Failure(JsonTemplate template, string text, string? reply = null) =>
        ToolResult.Fail(Mask(text)) with { Reply = Say(Definition.Say?.Error, template) ?? reply };

    private string? Say(string? said, JsonTemplate template)
    {
        if (string.IsNullOrWhiteSpace(said)) return null;
        var shown = template.ResultText;
        try
        {
            if (shown is not null) template.ResultText = Cut(shown, MaxSaid);
            return Mask(template.Fill(said));
        }
        catch (MissingSecretException) { return null; }
        finally { template.ResultText = shown; }
    }

    protected static string Cut(string text, int most) => text.Length <= most ? text : text[..most].TrimEnd() + "…";
}

/// <summary><c>http</c>: a request to any API (Home Assistant, a Telegram bot…).</summary>
public sealed class HttpJsonTool(JsonToolDefinition definition, string file, JsonToolEnvironment environment) : JsonTool(definition, file, environment)
{
    /// <summary>How much of an answer is read.</summary>
    private const int MaxRead = 512 * 1024;

    protected override async Task<Ran> RunCoreAsync(JsonTemplate template, ToolContext context, CancellationToken cancellationToken)
    {
        var request = Definition.Request!;
        var url = new StringBuilder(template.Fill(request.Url, UrlPart));
        foreach (var (name, value) in request.Query ?? [])
            url.Append(url.ToString().Contains('?', StringComparison.Ordinal) ? '&' : '?')
                .Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(template.Fill(value)));

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), url.ToString());
        string? contentType = request.ContentType;
        foreach (var (name, value) in request.Headers ?? [])
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = template.Fill(value);
            else message.Headers.TryAddWithoutValidation(name, template.Fill(value));
        }
        if (request.Body is { } body)
            message.Content = new StringContent(template.Fill(body)?.ToJsonString() ?? "null", Encoding.UTF8, MediaTypeHeaderValue.Parse(contentType ?? "application/json"));
        else if (request.BodyText is { } text)
            message.Content = new StringContent(template.Fill(text), Encoding.UTF8, MediaTypeHeaderValue.Parse(contentType ?? "text/plain"));

        using var response = await Environment.Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        template.Status = (int)response.StatusCode;
        var answer = Answer(template, await ReadAsync(response, cancellationToken).ConfigureAwait(false), request.Pick);
        if (!response.IsSuccessStatusCode)
            return new Ran(false, $"Failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}." + (answer.Length > 0 ? " Answer: " + answer : string.Empty));
        return new Ran(true, ReadsOnly ? (answer.Length > 0 ? answer : "(empty answer)") : "Done." + (answer.Length > 0 ? " Answer: " + answer : string.Empty));
    }

    /// <summary>A value put in the URL: encoded, but <c>:</c> and <c>@</c> stay (a bot token «123:abc» in the path).</summary>
    private static string UrlPart(string value) =>
        Uri.EscapeDataString(value).Replace("%3A", ":", StringComparison.Ordinal).Replace("%40", "@", StringComparison.Ordinal);

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaxRead];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0) total += read;
        var charset = response.Content.Headers.ContentType?.CharSet;
        Encoding encoding;
        try { encoding = charset is { Length: > 0 } ? Encoding.GetEncoding(charset.Trim('"')) : Encoding.UTF8; }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        return encoding.GetString(buffer, 0, total);
    }
}

/// <summary><c>kdesktop</c>: one of the user's commands, with phrases of its own («modo cine» → «Escena cine»).</summary>
public sealed class CommandJsonTool(JsonToolDefinition definition, string file, JsonToolEnvironment environment) : JsonTool(definition, file, environment)
{
    protected override async Task<Ran> RunCoreAsync(JsonTemplate template, ToolContext context, CancellationToken cancellationToken) =>
        await RunCommandAsync(Environment, template.Fill(Definition.Command!), template, context, cancellationToken).ConfigureAwait(false);
}

/// <summary><c>routine</c>: steps one after another (other tools, commands, waits), each asked and written like any call.</summary>
public sealed class RoutineJsonTool(JsonToolDefinition definition, string file, JsonToolEnvironment environment) : JsonTool(definition, file, environment)
{
    protected override async Task<Ran> RunCoreAsync(JsonTemplate template, ToolContext context, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var steps = Definition.Steps!;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            bool ok;
            string label, text;
            if (step.Wait is { } wait)
            {
                await Task.Delay(TimeSpan.FromSeconds(wait), cancellationToken).ConfigureAwait(false);
                lines.Add($"{i + 1}. waited {wait.ToString("0.#", CultureInfo.InvariantCulture)} s");
                continue;
            }
            if (step.Command is { } command)
            {
                label = template.Fill(command);
                var ran = await RunCommandAsync(Environment, label, template, context, cancellationToken).ConfigureAwait(false);
                (ok, text) = (ran.Ok, ran.Text);
            }
            else
            {
                label = step.Tool!;
                (ok, text) = await RunToolAsync(label, step, template, context, cancellationToken).ConfigureAwait(false);
            }
            lines.Add($"{i + 1}. {label}: {Cut(text, 300)}");
            if (!ok && !step.ContinueOnError)
                return new Ran(false, $"Stopped at step {i + 1} of {steps.Count}.\n" + string.Join("\n", lines));
        }
        return new Ran(true, "Done.\n" + string.Join("\n", lines));
    }

    private async Task<(bool Ok, string Text)> RunToolAsync(string name, JsonStep step, JsonTemplate template, ToolContext context, CancellationToken cancellationToken)
    {
        if (Environment.Runner is not { } runner) return (false, "Not done: no tools.");
        switch (runner.Registry.Find(name))
        {
            case null:
                return (false, $"Not done: there is no tool «{name}».");
            case RoutineJsonTool:
                return (false, "Not done: a routine cannot run another routine.");
        }
        var arguments = JsonSerializer.SerializeToElement(template.Fill(step.Arguments ?? []));
        var outcome = await runner.RunAsync(name, arguments, context, context.Heard ?? string.Empty, "routine", cancellationToken).ConfigureAwait(false);
        return outcome.Result is { } result ? (!result.Failed, result.Text) : (false, "Not done: " + outcome.Entry.Did);
    }
}

/// <summary>
/// <c>script</c>: one of the user's scripts. Before its first run, and again whenever it or its file change, it asks
/// whatever the user's policy (<see cref="Fingerprint"/>).
/// </summary>
public sealed class ScriptJsonTool(JsonToolDefinition definition, string file, JsonToolEnvironment environment) : JsonTool(definition, file, environment)
{
    /// <summary>Where the script is.</summary>
    public string ScriptPath => Path.GetFullPath(Path.IsPathRooted(Definition.Script!.File) ? Definition.Script.File : Path.Combine(Environment.Folder, Definition.Script.File));

    public override string? Fingerprint
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(System.IO.File.ReadAllBytes(File));
            hash.AppendData(Encoding.UTF8.GetBytes("\n" + ScriptPath.ToUpperInvariant() + "\n"));
            if (System.IO.File.Exists(ScriptPath)) hash.AppendData(System.IO.File.ReadAllBytes(ScriptPath));
            return "script:" + Convert.ToHexString(hash.GetHashAndReset());
        }
    }

    protected override async Task<Ran> RunCoreAsync(JsonTemplate template, ToolContext context, CancellationToken cancellationToken)
    {
        var script = Definition.Script!;
        var path = ScriptPath;
        if (!System.IO.File.Exists(path)) return new Ran(false, $"Not done: the script {Path.GetFileName(path)} does not exist.");
        var arguments = (script.Args ?? []).Select(a => template.Fill(a)).ToList();
        var environment = (script.Env ?? []).ToDictionary(p => p.Key, p => template.Fill(p.Value), StringComparer.OrdinalIgnoreCase);
        var outcome = await ScriptProcess.RunAsync(path, arguments, environment, cancellationToken).ConfigureAwait(false);
        template.Status = outcome.Exit;
        var output = Answer(template, outcome.Output, null);
        if (outcome.Exit != 0)
            return new Ran(false, $"Failed (exit code {outcome.Exit}). " + Cut(outcome.Error.Length > 0 ? outcome.Error : output, MaxResult));
        return new Ran(true, ReadsOnly ? (output.Length > 0 ? output : "(no output)") : "Done." + (output.Length > 0 ? " Output: " + output : string.Empty));
    }
}
