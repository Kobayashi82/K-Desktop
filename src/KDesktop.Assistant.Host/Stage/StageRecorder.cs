using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Imaging;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Stage;

/// <summary>
/// The clips of an assistant's showcase (stage 16.2): on the stage, it does each thing it can show — the host's entrances,
/// exits, states, reactions, gestures and what it does to a window, and its own animations — and each one is recorded as a
/// short animated WebP (<see cref="AnimatedWebPWriter"/>, the stage's whole picture, 25 frames a second). What it cannot do
/// (the stage does it and the bubble says it) is not recorded. With <c>--manifest</c>, the <c>showcase</c> of that
/// assistant.json lists the clips (only that part of the file changes). Engine thread.
/// </summary>
internal sealed class StageRecorder(StageSession session, string folder, string? manifest)
{
    /// <summary>Frames a second of the clips.</summary>
    private const int FrameRate = 25;

    /// <summary>How long it is left alone before each clip, so it starts calm.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);

    /// <summary>What is still recorded after the thing ends.</summary>
    private static readonly TimeSpan Tail = TimeSpan.FromSeconds(0.6);

    /// <summary>The wait before trying again one of its own that it could not do then.</summary>
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(3);

    /// <summary>The longest a clip lasts (a longer one is stopped there).</summary>
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(10);

    /// <summary>The folder inside the package the manifest names the clips in.</summary>
    public const string PackageFolder = "showcase";

    private readonly AssistantRuntime _runtime = session.Runtime;
    private readonly StageScene _scene = session.Scene;

    public async Task<bool> RunAsync(string id)
    {
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder, "*.webp")) File.Delete(old);
        _runtime.StageFrameRate = FrameRate;
        var items = session.Showcase.List(id);
        var own = _runtime.ShowcaseOf(id).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var clips = new List<JsonObject>();
        Console.WriteLine($"Escenario: {items.Count} cosas de «{id}»");
        foreach (var item in items)
        {
            var (result, file, bytes) = await RecordAsync(id, item).ConfigureAwait(true);
            // One of its own it could not do right then (busy with something of its own): once more, a moment later.
            if (result == ShowcaseDoneMessage.NotPerformed && item.Own)
            {
                await Task.Delay(Retry).ConfigureAwait(true);
                (result, file, bytes) = await RecordAsync(id, item).ConfigureAwait(true);
            }
            Console.WriteLine($"  {item.Id,-24} {result,-13} {(file is null ? "" : $"{bytes / 1024.0:0.#} KB")}");
            Log.Info($"Escenario: clip de {item.Id}: {result}{(file is null ? "" : $" ({bytes} bytes)")}");
            if (file is null) continue;
            var clip = new JsonObject { ["item"] = item.Id, ["clip"] = $"{PackageFolder}/{file}" };
            if (item.Own)
            {
                if (NameOf(item, own) is { } name) clip["name"] = name;
                clip["group"] = item.Group;
            }
            clips.Add(clip);
        }
        var list = new JsonArray([.. clips]);
        // Without a manifest to write them into, the list goes next to the clips.
        if (manifest is not null) WriteManifest(manifest, list);
        else await File.WriteAllTextAsync(Path.Combine(folder, "showcase.json"), list.ToJsonString(Json)).ConfigureAwait(true);
        Console.WriteLine($"Escenario: {clips.Count} clips en {folder}");
        return clips.Count > 0;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Its own name by language, or K-Desktop's own name for that id (the animations of the assistants without code) in every language.</summary>
    private static JsonNode? NameOf(ShowcaseEntry item, IReadOnlyDictionary<string, KDesktop.Assistant.Sdk.ShowcaseItem> own)
    {
        if (own.TryGetValue(item.Id, out var known) && known.Name is { Count: > 0 } names)
            return new JsonObject([.. names.Select(n => KeyValuePair.Create(n.Key, (JsonNode?)n.Value))]);
        var key = "showcase.own." + item.Id;
        var texts = new JsonObject();
        foreach (var language in Loc.Available())
        {
            using var _ = Loc.Instance.Use(language.Code);
            var text = Loc.T(key);
            if (text != key) texts[language.Code] = text;
        }
        return texts.Count > 0 ? texts : JsonValue.Create(item.Id);
    }

    private async Task<(string Result, string? File, int Bytes)> RecordAsync(string id, ShowcaseEntry item)
    {
        // The stage as it started, the cursor only where a gesture needs it, and a moment to calm down.
        session.Showcase.Stop(id);
        await session.Settled.ConfigureAwait(true);
        // An entrance starts from an empty stage.
        if (item.Id.StartsWith("enter.", StringComparison.Ordinal) && !item.Own) await session.TakeAwayAsync(id).ConfigureAwait(true);
        _scene.Reset();
        var work = _scene.WorkArea;
        _scene.Cursor = item.Id is "gesture.announce" or "gesture.goTo"
            ? new System.Windows.Point(work.Left + work.Width * 0.72, work.Top + work.Height * 0.3)
            : null;
        await Task.Delay(Settle).ConfigureAwait(true);

        var writer = new AnimatedWebPWriter(StageScene.Width, StageScene.Height);
        var frames = Channel.CreateUnbounded<(byte[] Pixels, int Milliseconds)>(new UnboundedChannelOptions { SingleReader = true });
        var encoding = Task.Run(async () =>
        {
            await foreach (var (pixels, ms) in frames.Reader.ReadAllAsync().ConfigureAwait(false)) writer.Add(pixels, ms);
        });
        byte[]? pending = null;
        double pendingAt = 0;
        void OnFrame(StageCompositor picture, double now)
        {
            if (pending is not null) frames.Writer.TryWrite((pending, Milliseconds(now - pendingAt)));
            pending = (byte[])picture.Pixels.Clone();
            pendingAt = now;
        }
        _runtime.StageFramed += OnFrame;
        _runtime.RedrawStage();
        string result;
        try
        {
            var running = session.Showcase.RunAsync(id, item.Id, item.Own);
            if (await Task.WhenAny(running, Task.Delay(Longest)).ConfigureAwait(true) != running)
            {
                session.Showcase.Stop(id);
                await running.ConfigureAwait(true);
                result = ShowcaseDoneMessage.Done;
            }
            else result = await running.ConfigureAwait(true);
            // After an exit the stage has been empty for a moment already, and it is on its way back now.
            if (!item.Id.StartsWith("exit.", StringComparison.Ordinal) || item.Own) await Task.Delay(Tail).ConfigureAwait(true);
        }
        finally
        {
            _runtime.StageFramed -= OnFrame;
        }
        if (pending is not null) frames.Writer.TryWrite((pending, Milliseconds(session.Engine.Now - pendingAt)));
        frames.Writer.Complete();
        await encoding.ConfigureAwait(true);
        if (result != ShowcaseDoneMessage.Done || writer.FrameCount == 0) return (result, null, 0);
        var bytes = writer.ToArray();
        var file = FileName(item.Id);
        await File.WriteAllBytesAsync(Path.Combine(folder, file), bytes).ConfigureAwait(true);
        return (result, file, bytes.Length);
    }

    private static int Milliseconds(double seconds) => Math.Max(1, (int)Math.Round(seconds * 1000));

    /// <summary>A file name for an item's clip (its id, with what a file name cannot have changed).</summary>
    internal static string FileName(string item)
    {
        var name = new StringBuilder(item.Length);
        foreach (var c in item) name.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        return name + ".webp";
    }

    /// <summary>
    /// Writes the clips into the manifest's <c>showcase</c>: that part of the file is replaced (or added at the end), the rest
    /// is left exactly as it was.
    /// </summary>
    internal static void WriteManifest(string path, JsonArray clips)
    {
        var text = File.ReadAllText(path);
        var bytes = Encoding.UTF8.GetBytes(text);
        var array = Indent(clips.ToJsonString(Json), "  ");
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        int start = -1, end = -1, close = -1;
        while (reader.Read())
        {
            if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("showcase"u8))
            {
                reader.Read();
                start = (int)reader.TokenStartIndex;
                reader.Skip();
                end = (int)reader.BytesConsumed;
            }
            else if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject) close = (int)reader.TokenStartIndex;
        }
        string result;
        if (start >= 0)
            result = Encoding.UTF8.GetString(bytes, 0, start) + array + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);
        else
        {
            if (close < 0) throw new InvalidDataException($"{path} no es un objeto JSON");
            var before = Encoding.UTF8.GetString(bytes, 0, close).TrimEnd();
            result = before + ",\n  \"showcase\": " + array + "\n" + Encoding.UTF8.GetString(bytes, close, bytes.Length - close);
        }
        if (text.Contains("\r\n", StringComparison.Ordinal)) result = result.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(path, result, new UTF8Encoding(false));
    }

    private static string Indent(string json, string by) => json.Replace("\n", "\n" + by, StringComparison.Ordinal);
}
