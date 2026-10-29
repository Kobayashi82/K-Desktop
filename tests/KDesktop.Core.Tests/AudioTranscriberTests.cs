using System.Runtime.InteropServices;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Voice;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KDesktop.Core.Tests;

/// <summary>
/// D5 (5.0.373): a recording to text with Parakeet, in each format «Grabar audio» makes. A Spanish voice of Windows says
/// it; without one, or without the voice models, there is nothing to try. The models are the real ones, hard-linked into
/// the tests' data folder (removing a link leaves the file).
/// </summary>
public partial class AudioTranscriberTests
{
    public AudioTranscriberTests() => LinkModels();

    private static readonly string RealModels = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-Desktop 5", "Models");

    private static void LinkModels()
    {
        foreach (var file in new[] { VoiceModels.VadFile, VoiceModels.ParakeetEncoder, VoiceModels.ParakeetDecoder, VoiceModels.ParakeetJoiner, VoiceModels.ParakeetTokens })
        {
            var real = Path.Combine(RealModels, Path.GetRelativePath(AppPaths.ModelsDir, file));
            if (File.Exists(file) || !File.Exists(real)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (!CreateHardLink(file, real, IntPtr.Zero)) return;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, IntPtr security);

    private const string Said = "Mañana por la mañana tengo que llamar al médico para pedir una cita.";

    [Fact]
    public async Task WavOpusAndMp3RecordingsAreTranscribed()
    {
        if (!VoiceModels.Ready) return;
        var dir = Directory.CreateTempSubdirectory("kd-transcribe-").FullName;
        try
        {
            if (await SpeakAsync(Path.Combine(dir, "voz.wav")) is not { } spoken) return;
            // As «Grabar audio» records: 48 kHz, 16 bits, stereo, with silence around.
            var wav = Path.Combine(dir, "grabación.wav");
            using (var reader = new WaveFileReader(spoken))
            {
                var stereo = new MonoToStereoSampleProvider(new WdlResamplingSampleProvider(reader.ToSampleProvider().ToMono(), 48000));
                var padded = new OffsetSampleProvider(stereo) { DelayBy = TimeSpan.FromSeconds(1), LeadOut = TimeSpan.FromSeconds(1) };
                WaveFileWriter.CreateWaveFile16(wav, padded);
            }
            File.Copy(wav, Path.Combine(dir, "a.wav"));
            File.Copy(wav, Path.Combine(dir, "b.wav"));
            var files = new[] { wav, AudioEncoders.Convert(Path.Combine(dir, "a.wav"), AudioFileFormat.Opus), AudioEncoders.Convert(Path.Combine(dir, "b.wav"), AudioFileFormat.Mp3) };
            Assert.Equal([".wav", ".opus", ".mp3"], files.Select(Path.GetExtension));

            using var speech = new SpeechToText();
            foreach (var file in files)
            {
                var text = await AudioTranscriber.TranscribeAsync(speech, file, CancellationToken.None);
                // Its first word too (Silero starts a piece a little late).
                Assert.StartsWith("Mañana", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("médico", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("cita", text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void OnlyARecordingThatEndsGivesItsText()
    {
        Assert.False(new AudioRecordAction().GivesResult);
        Assert.True(new AudioRecordAction { Transcribe = true }.GivesResult);
        Assert.True(new AudioRecordAction { Transcribe = true, Mode = RecordMode.SaveReplay }.GivesResult);
        Assert.False(new AudioRecordAction { Transcribe = true, Mode = RecordMode.Start }.GivesResult);
        Assert.EndsWith("transcribir", new AudioRecordAction { Transcribe = true, Mode = RecordMode.Stop }.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SilenceGivesNothing()
    {
        if (!VoiceModels.Ready) return;
        var dir = Directory.CreateTempSubdirectory("kd-transcribe-").FullName;
        try
        {
            var wav = Path.Combine(dir, "silencio.wav");
            WaveFileWriter.CreateWaveFile16(wav, new OffsetSampleProvider(new SilenceProvider(new WaveFormat(48000, 16, 2)).ToSampleProvider()) { Take = TimeSpan.FromSeconds(3) });
            using var speech = new SpeechToText();
            Assert.Equal(string.Empty, await AudioTranscriber.TranscribeAsync(speech, wav, CancellationToken.None));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary><see cref="Said"/> in a Spanish voice of Windows into a WAV; null without one.</summary>
    private static async Task<string?> SpeakAsync(string path)
    {
        var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase));
        if (voice is null) return null;
        using var synthesizer = new Windows.Media.SpeechSynthesis.SpeechSynthesizer { Voice = voice };
        using var stream = await synthesizer.SynthesizeTextToStreamAsync(Said);
        await using (var file = File.Create(path))
            await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
        return path;
    }
}
