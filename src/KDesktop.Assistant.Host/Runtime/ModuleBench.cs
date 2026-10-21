using System.Text.Json;
using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Brain.Bench;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Core;
using KDesktop.Core.Assistant.Bench;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// Options → Brain → Test the model (stage 3.9): runs the bench with the model chosen there, one at a time, telling
/// K-Desktop after each order (<c>benchState</c>), and keeps the last score of each model
/// (<c>Assistant\bench-results.json</c>).
/// </summary>
internal sealed class ModuleBench(KDesktopLink link, BrainService brain, ModuleTools tools, ToolChooser chooser)
{
    /// <summary>Scores kept (the last of each model).</summary>
    private const int KeptScores = 30;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private CancellationTokenSource? _running;
    private BenchStateMessage _state = new("idle", null, 0, 0, 0, 0, false, [], []);

    private static string HistoryFile => Path.Combine(AppPaths.DataDir, "Assistant", "bench-results.json");

    public void OnBench(BenchMessage request)
    {
        switch (request.Op)
        {
            case "start" when request.Model is { Length: > 0 } model:
                Start(model);
                break;
            case "cancel":
                lock (_gate) _running?.Cancel();
                break;
            default:
                Send(_state with { History = History() });
                break;
        }
    }

    private void Start(string model)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_running is not null) return;
            _running = cts = new CancellationTokenSource();
        }
        _ = Task.Run(() => RunAsync(model, cts));
    }

    private async Task RunAsync(string model, CancellationTokenSource cts)
    {
        var (cases, own) = BenchFile.Load(Loc.Instance.Language);
        var done = new List<BenchCaseResult>();
        var history = History();
        Send(new BenchStateMessage("running", model, 0, cases.Count, 0, 0, own, [], history));
        try
        {
            if (brain.Client is not { } client) throw new InvalidOperationException(Loc.T("bench.noBrain"));
            Log.Info($"Asistente: banco de pruebas con {model} ({cases.Count} órdenes{(own ? ", el propio" : "")})");
            var runner = new BenchRunner(tools.Runner.Registry, chooser);
            await runner.RunAsync(client, model, cases, result =>
            {
                lock (done) done.Add(result);
                Send(State("running"));
            }, cts.Token).ConfigureAwait(false);
            var score = new BenchScore(model, done.Count(r => r.Passed), done.Count, Mean(done), DateTimeOffset.Now, own);
            history = Save(score);
            Log.Info($"Asistente: banco de pruebas con {model}: {score.Passed}/{score.Total}, {score.MeanMs} ms de media");
            Send(State("done"));
        }
        catch (OperationCanceledException)
        {
            Send(State("cancelled"));
        }
        catch (Exception ex)
        {
            Log.Error("Asistente: el banco de pruebas ha fallado", ex);
            Send(State("failed") with { Message = ex.Message });
        }
        finally
        {
            lock (_gate) _running = null;
            cts.Dispose();
        }

        BenchStateMessage State(string state)
        {
            lock (done)
                return new BenchStateMessage(state, model, done.Count, cases.Count, done.Count(r => r.Passed), Mean(done), own,
                    state == "running" ? [] : [.. done.Where(r => !r.Passed)], history);
        }
    }

    private static int Mean(List<BenchCaseResult> done)
    {
        lock (done) return done.Count == 0 ? 0 : (int)done.Average(r => r.Ms);
    }

    private void Send(BenchStateMessage state)
    {
        lock (_gate) _state = state;
        link.Send(state);
    }

    private static List<BenchScore> History()
    {
        try
        {
            return File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<BenchScore>>(File.ReadAllText(HistoryFile), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The model's new score replaces its last one; newest first.</summary>
    private static List<BenchScore> Save(BenchScore score)
    {
        var history = History().Where(h => h.Model != score.Model).Prepend(score).Take(KeptScores).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(history, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se han podido guardar los resultados del banco: {ex.Message}");
        }
        return history;
    }
}
