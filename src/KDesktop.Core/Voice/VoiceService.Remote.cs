using KDesktop.Core.Engine;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Voice;

/// <summary>How a voice order from the phone went: what was heard and the message the PC showed.</summary>
public sealed record RemoteVoiceResult(string Text, string Message, OsdKind Kind);

public sealed partial class VoiceService
{
    /// <summary>Longest order the phone may send.</summary>
    public static readonly TimeSpan RemoteMax = TimeSpan.FromSeconds(16);

    /// <summary>
    /// A voice order recorded on the phone (web app or Android app): 16 kHz mono samples. Same path as the PC's own
    /// orders from the transcription on (wake word at the start stripped, commands and media orders); the PC shows
    /// its usual voice OSD and the phone gets the final message.
    /// </summary>
    public async Task<RemoteVoiceResult> RunRemoteAsync(float[] samples)
    {
        if (!VoiceModels.Ready) return new("", Loc.T("voice.noModels"), OsdKind.Warning);
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) return new("", Loc.T("voice.remoteBusy"), OsdKind.Warning);
        try
        {
            StateChanged?.Invoke();
            var settings = _config().Settings;
            SpeechToText.IdleUnload = TimeSpan.FromMinutes(Math.Max(1, settings.VoiceUnloadMinutes));

            float[] clip;
            using (var vad = new VoiceActivity(EndSilence))
            {
                // In pieces, as the microphone delivers them (the detector gets them all wrong at once).
                const int piece = MicCapture.SampleRate / 10;
                for (var at = 0; at < samples.Length; at += piece)
                    vad.Accept(samples[at..Math.Min(samples.Length, at + piece)]);
                vad.Flush();
                clip = Trim([.. samples], vad.Segments, vad.HeardSpeech);
            }
            if (clip.Length < MicCapture.SampleRate / 5) return new("", Loc.T("voice.nothingHeard"), OsdKind.Info);

            using var osd = new RecordingOsd(_ui.ShowVoiceOsd(Loc.T("voice.thinking")));
            osd.SetText(Loc.T("voice.thinking"));
            string text;
            try
            {
                text = await Task.Run(() => SpeechToText.Transcribe(clip)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error("Error transcribiendo la orden de voz del móvil", ex);
                osd.Finish(Loc.T("voice.sttError"), OsdKind.Warning);
                return osd.Result("");
            }
            Log.Info($"Voz (móvil): «{text}»");
            await HandleTextAsync(text, osd).ConfigureAwait(false);
            return osd.Result(text);
        }
        catch (Exception ex)
        {
            Log.Error("Error en la orden de voz del móvil", ex);
            return new("", Loc.T("voice.sttError"), OsdKind.Warning);
        }
        finally
        {
            _active = 0;
            StateChanged?.Invoke();
        }
    }

    /// <summary>The PC's voice OSD, keeping its last message to answer the phone with it.</summary>
    private sealed class RecordingOsd(IVoiceOsd inner) : IVoiceOsd
    {
        private string _message = "";
        private OsdKind _kind = OsdKind.Info;

        public RemoteVoiceResult Result(string text) => new(text, _message, _kind);

        public void SetLevel(float level) => inner.SetLevel(level);

        public void SetText(string text) => inner.SetText(text);

        public void Finish(string text, OsdKind kind)
        {
            _message = text;
            _kind = kind;
            inner.Finish(text, kind);
        }

        public void Dispose() => inner.Dispose();
    }
}
