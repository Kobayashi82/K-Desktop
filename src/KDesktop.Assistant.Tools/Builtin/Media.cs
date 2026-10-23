using System.Globalization;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «¿qué suena?», «¿cómo se llama esta canción?» (stage 10.2, <c>que_suena</c>): what each program is playing, from
/// Windows' media controls (<see cref="MediaSessions"/>).
/// </summary>
public sealed class WhatsPlayingTool(Func<CancellationToken, Task<IReadOnlyList<MediaNow>>>? list = null) : ITool
{
    public string Name => "whats_playing";

    public string Description => "What is playing on the PC right now (music, videos, podcasts, in any program that shows it in Windows' media controls): the program, the title, the artist or channel, whether it is paused and how far it is. Use it before control_media to know what there is.";

    public string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.whats_playing.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var now = await (list ?? MediaSessions.ListAsync)(cancellationToken).ConfigureAwait(false);
        if (now.Count > 0) context.Show?.Invoke(ToolGesture.GoTo(ToolSpot.Tray));
        return new ToolResult(Describe(now)) { Done = Loc.T("tool.whats_playing.done"), Reply = Say(now) };
    }

    /// <summary>For the model, in English: one line per program.</summary>
    public static string Describe(IReadOnlyList<MediaNow> now)
    {
        if (now.Count == 0) return "Nothing is playing: no program shows anything in Windows' media controls.";
        var text = new StringBuilder();
        foreach (var m in now)
        {
            var state = m.State switch { MediaState.Playing => "playing", MediaState.Paused => "paused", MediaState.Stopped => "stopped", _ => "ready" };
            text.Append(CultureInfo.InvariantCulture, $"{m.App}: {state}");
            if (m.Title is { } title) text.Append(CultureInfo.InvariantCulture, $" «{title}»");
            if (m.Artist is { } artist) text.Append(CultureInfo.InvariantCulture, $" by {artist}");
            if (m.Album is { } album) text.Append(CultureInfo.InvariantCulture, $" (album {album})");
            if (m.Position is { } at && m.Length is { } length) text.Append(CultureInfo.InvariantCulture, $", {Clock(at)} of {Clock(length)}");
            if (m.Current) text.Append(" (the media keys act on it)");
            text.AppendLine(".");
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>What the assistant says without the AI, in the user's language: the first one (playing before paused).</summary>
    public static string Say(IReadOnlyList<MediaNow> now)
    {
        if (now.FirstOrDefault(m => m.State is MediaState.Playing or MediaState.Paused) is not { } m) return Loc.T("tool.whats_playing.nothing");
        if (m.Title is not { } title) return Loc.T("tool.whats_playing.untitled", m.App);
        var paused = m.State == MediaState.Paused;
        return m.Artist is { } artist
            ? Loc.T(paused ? "tool.whats_playing.paused" : "tool.whats_playing.playing", title, artist, m.App)
            : Loc.T(paused ? "tool.whats_playing.pausedNoArtist" : "tool.whats_playing.playingNoArtist", title, m.App);
    }

    internal static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}

/// <summary>
/// «pausa Spotify», «la siguiente», «para el vídeo» (stage 10.2): play, pause, skip in the program that plays (or the one
/// named), with Windows' media controls. Without a program, a pause stops every program playing.
/// </summary>
public sealed class ControlMediaTool(Func<MediaOp, string?, CancellationToken, Task<IReadOnlyList<MediaNow>>>? control = null) : ITool
{
    public string Name => "control_media";

    public string Description => "Plays, pauses, skips to the next or previous track, or stops what a program plays (music, videos, podcasts), with Windows' media controls. Give app only when the user names a program; without it, pause stops everything playing and the rest act on what plays now.";

    private static readonly string[] Actions = ["play", "pause", "toggle", "next", "previous", "stop"];

    public string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"action":{"type":"string","enum":[{{{string.Join(",", Actions.Select(a => $"\"{a}\""))}}}],"description":"What to do (toggle: play if paused, pause if playing)"},"app":{"type":"string","description":"The program, as the user named it (only if they named one)"}},"required":["action"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.control_media.intent." + OpOf(arguments).ToString().ToLowerInvariant());

    private static MediaOp OpOf(JsonElement arguments) => arguments.GetString("action")?.Trim().ToLowerInvariant() switch
    {
        "play" or "resume" => MediaOp.Play,
        "pause" => MediaOp.Pause,
        "next" or "skip" => MediaOp.Next,
        "previous" or "back" => MediaOp.Previous,
        "stop" => MediaOp.Stop,
        _ => MediaOp.Toggle,
    };

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var op = OpOf(arguments);
        var app = arguments.GetString("app") is { Length: > 0 } named ? named.Trim() : null;
        var done = await (control ?? MediaSessions.ControlAsync)(op, app, cancellationToken).ConfigureAwait(false);
        if (done.Count == 0)
        {
            var reply = app is null ? Loc.T("tool.whats_playing.nothing") : Loc.T("tool.control_media.noApp", app);
            return new ToolResult(app is null ? "Nothing to act on: nothing is playing." : $"No program called «{app}» shows anything in Windows' media controls.")
            { Failed = true, Reply = reply, Done = reply };
        }
        var apps = string.Join(", ", done.Select(m => m.App).Distinct());
        var key = op.ToString().ToLowerInvariant();
        context.Show?.Invoke(ToolGesture.GoTo(ToolSpot.Tray));
        return new ToolResult($"Done: {key} in {apps}.") { Acted = true, Reply = Loc.T("tool.control_media.done." + key, apps), Done = Loc.T("tool.control_media.done." + key, apps) };
    }
}
