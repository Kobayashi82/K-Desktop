using System.Diagnostics;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

/// <summary>
/// Voice orders: listen (while a key is held, or until the speaker goes quiet) → transcribe with Parakeet → pick the
/// command → run it. Nothing listens in between: the microphone is only open during a session.
/// </summary>
public sealed class VoiceService : IDisposable
{
    private static readonly TimeSpan EndSilence = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan TapLimit = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan NothingSaid = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxSession = TimeSpan.FromSeconds(15);

    private readonly IUiServices _ui;
    private readonly Func<AppConfig> _config;
    private readonly Func<Command, Task> _run;
    private readonly MicCapture _mic = new();
    private volatile bool _stopRequested;
    private int _active;

    /// <param name="run">Runs a chosen command (with the voice trigger source).</param>
    public VoiceService(IUiServices ui, Func<AppConfig> config, Func<Command, Task> run)
    {
        _ui = ui;
        _config = config;
        _run = run;
    }

    public SpeechToText SpeechToText { get; } = new();

    public bool IsListening => _active != 0;

    /// <summary>Raised (any thread) when a session starts or ends.</summary>
    public event Action? StateChanged;

    /// <summary>The commands a voice order can run.</summary>
    public IEnumerable<Command> Commands() => VoiceCommands(_config());

    public static IEnumerable<Command> VoiceCommands(AppConfig config) =>
        config.Commands.Where(c => c.Enabled
            && !c.Actions.Any(a => a is VoiceListenAction)
            // Until voice confirmations exist, nothing that switches the PC off or logs out runs by voice.
            && !c.Actions.Any(a => a is PowerAction));

    /// <summary>
    /// One listening session. With <paramref name="held"/> it lasts while that returns true (a quick tap instead
    /// switches to «until silence»); without it, until the speaker goes quiet. A second call during a session ends
    /// the first one early. <paramref name="stop"/> ends it and still processes what was said;
    /// <paramref name="abort"/> drops it (Escape).
    /// </summary>
    public async Task ListenAsync(Func<bool>? held, CancellationToken stop, CancellationToken abort)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            _stopRequested = true;
            return;
        }
        try
        {
            if (!VoiceModels.Ready)
            {
                _ui.ShowOsd(Loc.T("voice.noModels"), OsdKind.Warning, TimeSpan.FromSeconds(4));
                return;
            }
            _stopRequested = false;
            StateChanged?.Invoke();
            await SessionAsync(held, stop, abort).ConfigureAwait(false);
        }
        finally
        {
            _active = 0;
            StateChanged?.Invoke();
        }
    }

    private async Task SessionAsync(Func<bool>? held, CancellationToken stop, CancellationToken abort)
    {
        var settings = _config().Settings;
        SpeechToText.IdleUnload = TimeSpan.FromMinutes(Math.Max(1, settings.VoiceUnloadMinutes));
        SpeechToText.Preload(); // loads while the user speaks

        var audio = new List<float>(MicCapture.SampleRate * 6);
        using var vad = new VoiceActivity(EndSilence);
        var gate = new object();
        void OnSamples(float[] samples)
        {
            lock (gate)
            {
                audio.AddRange(samples);
                vad.Accept(samples);
            }
        }

        using var osd = _ui.ShowVoiceOsd(Loc.T("voice.listening"));
        _mic.Samples += OnSamples;
        try
        {
            _mic.Start(settings.VoiceMicId);
        }
        catch (Exception ex)
        {
            _mic.Samples -= OnSamples;
            Log.Error("No se pudo abrir el micrófono", ex);
            osd.Finish(Loc.T("voice.micError"), OsdKind.Warning);
            return;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                await Task.Delay(40, CancellationToken.None).ConfigureAwait(false);
                osd.SetLevel(_mic.Level);
                if (abort.IsCancellationRequested)
                {
                    osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
                    return;
                }
                if (stop.IsCancellationRequested || _stopRequested) break;
                if (held is not null)
                {
                    if (held()) continue;
                    // A tap rather than a hold: keep listening until the speaker goes quiet.
                    if (watch.Elapsed < TapLimit) held = null;
                    else break;
                }
                bool ended, heard;
                lock (gate) (ended, heard) = (vad.Ended, vad.HeardSpeech);
                if (ended || watch.Elapsed > MaxSession) break;
                if (!heard && watch.Elapsed > NothingSaid) break;
            }
        }
        finally
        {
            _mic.Samples -= OnSamples;
            _mic.Stop();
        }

        float[] clip;
        lock (gate)
        {
            vad.Flush();
            clip = Trim(audio, vad.Segments, vad.HeardSpeech);
        }
        if (clip.Length < MicCapture.SampleRate / 5)
        {
            osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
            return;
        }

        osd.SetText(Loc.T("voice.thinking"));
        string text;
        try
        {
            text = await Task.Run(() => SpeechToText.Transcribe(clip), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error transcribiendo la orden de voz", ex);
            osd.Finish(Loc.T("voice.sttError"), OsdKind.Warning);
            return;
        }
        if (abort.IsCancellationRequested)
        {
            osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
            return;
        }
        if (text.Length == 0)
        {
            osd.Finish(Loc.T("voice.notUnderstood"), OsdKind.Warning);
            return;
        }

        var result = IntentMatcher.Match(text, Commands());
        Log.Info($"Voz → {(result.Command?.Name ?? "nada")} · " +
                 string.Join(", ", result.Candidates.Select(c => $"{c.Command.Name} {c.Score:0.00}")));
        if (result.Command is { } command)
        {
            osd.Finish(Loc.T("voice.running", text, command.Name), OsdKind.Success);
            await _run(command).ConfigureAwait(false);
        }
        else if (result.Candidates.Count > 0 && result.Confidence >= 0.6)
            osd.Finish(Loc.T("voice.unsure", text, result.Candidates[0].Command.Name), OsdKind.Warning);
        else
            osd.Finish(Loc.T("voice.noMatch", text), OsdKind.Warning);
    }

    /// <summary>
    /// The audio to transcribe: from a little before the first speech to a little after the last. Without detected
    /// speech, all of it when the key was held (VAD may miss a single short word), else nothing.
    /// </summary>
    private static float[] Trim(List<float> audio, List<(int Start, int Length)> segments, bool heard)
    {
        if (segments.Count == 0) return heard ? audio.ToArray() : [];
        var pad = MicCapture.SampleRate * 3 / 10;
        var start = Math.Clamp(segments[0].Start - pad, 0, audio.Count);
        var end = Math.Clamp(segments[^1].Start + segments[^1].Length + pad, start, audio.Count);
        return audio.GetRange(start, end - start).ToArray();
    }

    public void Dispose()
    {
        _mic.Dispose();
        SpeechToText.Dispose();
    }
}
