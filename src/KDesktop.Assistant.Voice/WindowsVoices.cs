using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using KDesktop.Core;

namespace KDesktop.Assistant.Voice;

/// <summary>
/// Windows' own voices (SAPI 5: «Microsoft Helena», «Zira»…), the fallback without downloads (stage 5.4). Rendered into
/// memory, never to the speakers: the host plays it like any other sound. One at a time.
/// </summary>
public static class WindowsVoices
{
    public const int SampleRate = 22_050;

    private static readonly Lock Gate = new();

    /// <summary>The installed voices that speak this language («es»), by name.</summary>
    public static IReadOnlyList<string> For(string language)
    {
        try
        {
            lock (Gate)
            {
                using var synth = new SpeechSynthesizer();
                return Of(synth, language);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"Asistente: no se han podido leer las voces de Windows: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// <paramref name="text"/> said by a voice of <paramref name="language"/> (<paramref name="voice"/> if it is one of
    /// them), at <paramref name="speed"/> (1 = normal): 22 kHz mono. Empty when there is no voice for it.
    /// </summary>
    public static float[] Say(string text, string language, string? voice, double speed)
    {
        lock (Gate)
        {
            using var synth = new SpeechSynthesizer();
            var voices = Of(synth, language);
            if (voices.Count == 0) return [];
            synth.SelectVoice(voice is not null && voices.Contains(voice) ? voice : voices[0]);
            // SAPI's rate goes from −10 (a third as fast) to 10 (three times as fast).
            synth.Rate = (int)Math.Clamp(Math.Round(Math.Log(Math.Clamp(speed, 0.34, 3)) / Math.Log(3) * 10), -10, 10);
            using var stream = new MemoryStream();
            synth.SetOutputToAudioStream(stream, new SpeechAudioFormatInfo(SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synth.Speak(text);
            var bytes = stream.ToArray();
            var samples = new float[bytes.Length / 2];
            for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            return samples;
        }
    }

    /// <summary>The enabled voices whose culture is of that language (any country: «es-ES», «es-MX»…).</summary>
    private static List<string> Of(SpeechSynthesizer synth, string language) =>
    [
        // All of them, whatever the user's culture: they are filtered by language here.
#pragma warning disable CA1304
        .. synth.GetInstalledVoices()
#pragma warning restore CA1304
            .Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase))
            .Select(v => v.VoiceInfo.Name),
    ];
}
