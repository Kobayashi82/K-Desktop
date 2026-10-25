using System.Diagnostics;
using System.Globalization;
using System.Text;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

/// <summary>What a dictated piece asks for besides its text (said alone, or at the end of it for the line breaks).</summary>
public enum DictationOrder { None, NewLine, NewParagraph, Undo, Stop }

/// <summary>
/// Stage 10.5, «Dictar»: what is said is written where the cursor is, without the AI. The microphone stays open and every
/// pause closes a piece, transcribed with Parakeet and typed at once (Unicode keystrokes, the clipboard is not touched).
/// It ends when the key is let go (held), after a few seconds of silence, with the key or the action again, Escape
/// (what was not written yet is dropped) or «deja de dictar». «nueva línea», «punto y aparte» and «borra eso» are orders.
/// </summary>
public sealed partial class VoiceService
{
    /// <summary>A pause that closes a piece: short enough to see the text come, long enough not to cut a sentence.</summary>
    private static readonly TimeSpan DictationPause = TimeSpan.FromMilliseconds(700);

    /// <summary>Silence after speaking that ends a dictation not held with the key.</summary>
    private static readonly TimeSpan DictationQuiet = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan DictationMax = TimeSpan.FromMinutes(5);

    private volatile bool _dictating;

    /// <summary>A dictation is going on.</summary>
    public bool IsDictating => _dictating;

    /// <summary>
    /// Starts a dictation (<paramref name="held"/>: while that returns true; a quick tap instead lasts until a silence),
    /// or ends the one going on. A voice order going on is not interrupted.
    /// </summary>
    public Task DictateAsync(Func<bool>? held = null)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            if (_dictating) _stopRequested = true;
            return Task.CompletedTask;
        }
        return DictationCoreAsync(held);
    }

    /// <summary>«Dictar» with its mode: toggle, start (if not dictating) or stop.</summary>
    public Task DictateAsync(SwitchMode mode)
    {
        switch (mode)
        {
            case SwitchMode.Off:
                if (_dictating) _stopRequested = true;
                return Task.CompletedTask;
            case SwitchMode.On when _dictating:
                return Task.CompletedTask;
            default:
                return DictateAsync(held: null);
        }
    }

    private async Task DictationCoreAsync(Func<bool>? held)
    {
        using var abort = new CancellationTokenSource();
        _abort = abort;
        _dictating = true;
        try
        {
            if (NotReadyText is { } notReady)
            {
                _ui.ShowOsd(notReady, OsdKind.Warning, TimeSpan.FromSeconds(4), new OsdLook("MicOff24", Topic: OsdTopic.Voice));
                return;
            }
            _stopRequested = false;
            StateChanged?.Invoke();
            await DictationSessionAsync(held, abort.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error en el dictado", ex);
        }
        finally
        {
            _abort = null;
            _dictating = false;
            _active = 0;
            StateChanged?.Invoke();
        }
    }

    private async Task DictationSessionAsync(Func<bool>? held, CancellationToken abort)
    {
        var settings = _config().Settings;
        PreloadModels(settings);

        var audio = new List<float>(MicCapture.SampleRate * 30);
        using var vad = new VoiceActivity(DictationPause);
        var gate = new object();
        void OnSamples(float[] samples)
        {
            lock (gate)
            {
                audio.AddRange(samples);
                vad.Accept(samples);
            }
        }

        var listening = Loc.T("dictation.listening");
        using var osd = _ui.ShowVoiceOsd(listening);
        ListenSound(settings);
        using var fake = new CancellationTokenSource();
        var simulated = Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_STREAM") is { Length: > 0 } || Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE") is { Length: > 0 };
        if (simulated)
        {
            if (Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_SESSION") is not { Length: > 0 } said || !File.Exists(said))
            {
                Log.Info("Dictado simulado: sin sesión que oír");
                osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
                return;
            }
            Log.Info($"Dictado simulado: oye {said}");
            PlayFake(VoiceDiagnostics.ReadWav(said), OnSamples, fake.Token);
        }
        else _mic.Samples += OnSamples;
        using var duck = new DuckScope(Duck(settings));
        try
        {
            _mic.EchoCancel = settings.VoiceEchoCancel;
            if (!simulated) _mic.Start(settings.VoiceMicId);
        }
        catch (Exception ex)
        {
            _mic.Samples -= OnSamples;
            Log.Error("No se pudo abrir el micrófono para dictar", ex);
            osd.Finish(Loc.T("voice.micError"), OsdKind.Warning);
            return;
        }

        var writer = new DictationWriter(DictationOutput.Create());
        var pieces = Task.CompletedTask;
        var taken = 0;
        var cancelled = false;
        // Each closed piece is transcribed and written in order, while the microphone keeps listening.
        void TakePieces(bool all)
        {
            var clips = new List<float[]>();
            lock (gate)
            {
                if (all) vad.Flush();
                for (; taken < vad.Segments.Count; taken++) clips.Add(Piece(audio, vad.Segments[taken]));
            }
            foreach (var clip in clips)
            {
                pieces = pieces.ContinueWith(_ =>
                {
                    if (abort.IsCancellationRequested || clip.Length < MicCapture.SampleRate / 5) return;
                    string text;
                    try { text = SpeechToText.Transcribe(clip); }
                    catch (Exception ex)
                    {
                        Log.Error("Error transcribiendo un trozo del dictado", ex);
                        return;
                    }
                    if (abort.IsCancellationRequested) return;
                    if (writer.Write(text) == DictationOrder.Stop) _stopRequested = true;
                    if (writer.Last is { Length: > 0 } last) osd.SetVoiceText($"{listening} {Shorten(last, 70)}");
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        var watch = Stopwatch.StartNew();
        var lastSpeech = TimeSpan.Zero;
        try
        {
            while (true)
            {
                await Task.Delay(40, CancellationToken.None).ConfigureAwait(false);
                osd.SetLevel(_mic.Level);
                if (abort.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                TakePieces(all: false);
                if (_stopRequested) break;
                bool speaking, heard;
                lock (gate) (speaking, heard) = (vad.Speaking, vad.HeardSpeech);
                if (speaking) lastSpeech = watch.Elapsed;
                if (watch.Elapsed > DictationMax) break;
                if (held is not null)
                {
                    if (held()) continue;
                    // A tap rather than a hold: dictate until a silence.
                    if (watch.Elapsed < TapLimit) held = null;
                    else break;
                }
                if (heard && watch.Elapsed - lastSpeech > DictationQuiet) break;
                if (!heard && watch.Elapsed > NothingSaid) break;
            }
        }
        finally
        {
            fake.Cancel();
            _mic.Samples -= OnSamples;
            if (!simulated) _mic.Stop();
        }

        if (!cancelled) TakePieces(all: true);
        osd.SetText(Loc.T("dictation.finishing"));
        await pieces.ConfigureAwait(false);
        duck.Dispose();
        cancelled |= abort.IsCancellationRequested;
        Log.Info($"Dictado: {writer.Pieces} trozos, {writer.Words} palabras en {watch.Elapsed.TotalSeconds:0.0} s{(cancelled ? " (cancelado)" : "")}");
        if (cancelled) osd.Finish(Loc.T("dictation.cancelled"), OsdKind.Info);
        else if (writer.Words == 0) osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
        else osd.Finish(Loc.T("dictation.done", writer.Words), OsdKind.Success);
    }

    /// <summary>A piece of speech, a little silence around it.</summary>
    private static float[] Piece(List<float> audio, (int Start, int Length) segment)
    {
        var pad = MicCapture.SampleRate / 5;
        var from = Math.Clamp(segment.Start - pad, 0, audio.Count);
        var to = Math.Clamp(segment.Start + segment.Length + pad, from, audio.Count);
        return audio.GetRange(from, to - from).ToArray();
    }

    private static string Shorten(string text, int max) => text.Length <= max ? $"«{text}»" : $"«…{text[^max..].TrimStart()}»";
}

/// <summary>Where dictated text goes: the window in front (keystrokes), or a file in tests (<c>KDESKTOP_DICTATION_SINK</c>).</summary>
public interface IDictationOutput
{
    void Type(string text);

    void Erase(int characters);
}

public static class DictationOutput
{
    /// <summary>The window in front, or the file of <c>KDESKTOP_DICTATION_SINK</c> (tests never press real keys).</summary>
    public static IDictationOutput Create() =>
        Environment.GetEnvironmentVariable("KDESKTOP_DICTATION_SINK") is { Length: > 0 } file ? new FileOutput(file) : new Keys();

    private sealed class Keys : IDictationOutput
    {
        public void Type(string text) => InputSimulator.SendText(text);

        public void Erase(int characters)
        {
            InputSimulator.ReleaseModifiers();
            for (var i = 0; i < characters; i++) InputSimulator.KeyPress(KeyNames.VK_BACK);
        }
    }

    /// <summary>Appends what would be typed; an erase is written as «⌫n».</summary>
    private sealed class FileOutput(string file) : IDictationOutput
    {
        public void Type(string text) => File.AppendAllText(file, text);

        public void Erase(int characters) => File.AppendAllText(file, $"⌫{characters}");
    }
}

/// <summary>
/// Turns the dictated pieces into what is typed: a space between pieces, the line-break orders said alone or at the end
/// of a piece, «borra eso» (the last piece, again and again) and «deja de dictar». Public for tests.
/// </summary>
public sealed class DictationWriter(IDictationOutput output)
{
    /// <summary>Each piece written: how many characters, and whether a line began before it (what «borra eso» goes back to).</summary>
    private readonly Stack<(int Count, bool LineStart)> _written = new();
    private bool _lineStart = true;

    public int Pieces { get; private set; }

    public int Words { get; private set; }

    /// <summary>The text of the last piece written (for the OSD), or null.</summary>
    public string? Last { get; private set; }

    /// <summary>Writes a transcribed piece; returns the order it carried.</summary>
    public DictationOrder Write(string piece)
    {
        var text = piece.Trim();
        if (text.Length == 0) return DictationOrder.None;
        Pieces++;
        var (rest, order) = Orders.Split(text);
        switch (order)
        {
            case DictationOrder.Stop when rest.Length == 0:
                return DictationOrder.Stop;
            case DictationOrder.Undo when rest.Length == 0:
                if (_written.TryPop(out var last))
                {
                    output.Erase(last.Count);
                    _lineStart = last.LineStart;
                }
                Last = null;
                return DictationOrder.Undo;
        }

        var typed = new StringBuilder();
        if (rest.Length > 0)
        {
            if (!_lineStart) typed.Append(' ');
            typed.Append(_lineStart ? UpperFirst(rest) : rest);
            Words += rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Last = rest;
        }
        if (order == DictationOrder.NewLine) typed.Append('\n');
        else if (order == DictationOrder.NewParagraph) typed.Append("\n\n");
        if (typed.Length == 0) return order;
        output.Type(typed.ToString());
        _written.Push((typed.Length, _lineStart));
        _lineStart = order is DictationOrder.NewLine or DictationOrder.NewParagraph;
        return order;
    }

    private static string UpperFirst(string text) => char.IsLower(text[0]) ? char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..] : text;

    /// <summary>The spoken orders of every language (<c>dictation.say.*</c>, «|» between ways of saying them).</summary>
    public static class Orders
    {
        private static readonly Lazy<List<(string Words, DictationOrder Order)>> All = new(Build);

        /// <summary>
        /// A piece without its order: «borra eso» and «deja de dictar» only said alone; the line breaks also at the end
        /// («…mañana punto y aparte»).
        /// </summary>
        public static (string Text, DictationOrder Order) Split(string text)
        {
            var plain = Plain(text);
            foreach (var (words, order) in All.Value)
            {
                if (plain == words) return (string.Empty, order);
                if (order is DictationOrder.NewLine or DictationOrder.NewParagraph && plain.EndsWith(" " + words, StringComparison.Ordinal))
                    return (CutEnd(text, words.Split(' ').Length), order);
            }
            return (text, DictationOrder.None);
        }

        /// <summary>The text without its last <paramref name="words"/> words and the punctuation around them.</summary>
        private static string CutEnd(string text, int words)
        {
            var end = text.Length;
            for (var i = 0; i < words; i++)
            {
                while (end > 0 && !char.IsLetterOrDigit(text[end - 1])) end--;
                while (end > 0 && char.IsLetterOrDigit(text[end - 1])) end--;
            }
            var rest = text[..end].TrimEnd();
            // «…mañana. Punto y aparte.»: the sentence keeps its full stop; «…mañana, nueva línea»: the comma goes.
            return rest.TrimEnd(',', ';', ':', ' ');
        }

        /// <summary>Lower case, without accents or punctuation, single spaces.</summary>
        public static string Plain(string text)
        {
            var b = new StringBuilder(text.Length);
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
                else if (b.Length > 0 && b[^1] != ' ') b.Append(' ');
            }
            return b.ToString().Trim();
        }

        private static List<(string, DictationOrder)> Build()
        {
            var list = new List<(string Words, DictationOrder Order)>();
            foreach (var language in Loc.Available())
                foreach (var (key, value) in Loc.Strings(language.Code, "dictation.say."))
                {
                    if (!Enum.TryParse<DictationOrder>(key["dictation.say.".Length..], ignoreCase: true, out var order)) continue;
                    foreach (var way in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (Plain(way) is { Length: > 0 } words) list.Add((words, order));
                }
            // Longest first: «nuevo párrafo» before a shorter way that ends the same.
            list.Sort((a, b) => b.Words.Length - a.Words.Length);
            return list;
        }
    }
}
