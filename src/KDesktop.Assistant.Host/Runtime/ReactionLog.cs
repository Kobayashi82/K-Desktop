using System.Text.Json;
using System.Text.Json.Serialization;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The context of each assistant's reactions (stage 13.3): how many of the same kind in a row and whether it is the first of
/// the day. Kept in <c>Assistant\reactions.json</c>, so restarting K-Desktop in the afternoon is not «the first of the day»
/// again. Thread-safe.
/// </summary>
internal sealed class ReactionLog(string? file, Func<DateTimeOffset>? clock = null)
{
    /// <summary>After this long without reactions, a streak starts again.</summary>
    public static readonly TimeSpan StreakGap = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private readonly object _gate = new();
    private Dictionary<string, Last>? _last;

    private sealed record Last(Reaction Reaction, int Streak, DateTimeOffset At);

    /// <summary>The context of a reaction of <paramref name="assistant"/> happening now, and it is counted.</summary>
    public ReactionContext Next(string assistant, Reaction reaction, ReactionSource source, string? what)
    {
        lock (_gate)
        {
            var all = _last ??= Read();
            var now = _clock();
            var before = all.GetValueOrDefault(assistant);
            var first = before is null || before.At.LocalDateTime.Date != now.LocalDateTime.Date;
            var streak = before is not null && before.Reaction == reaction && now - before.At <= StreakGap ? before.Streak + 1 : 1;
            all[assistant] = new Last(reaction, streak, now);
            Write(all);
            return new ReactionContext(source, streak, first, what);
        }
    }

    private Dictionary<string, Last> Read()
    {
        try
        {
            if (file is not null && File.Exists(file))
                return JsonSerializer.Deserialize<Dictionary<string, Last>>(File.ReadAllText(file), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Asistente: no se ha podido leer {file}: {ex.Message}");
        }
        return [];
    }

    private void Write(Dictionary<string, Last> all)
    {
        if (file is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(all, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar {file}: {ex.Message}");
        }
    }
}
