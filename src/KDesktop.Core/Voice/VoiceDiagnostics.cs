using System.Diagnostics;
using System.Text;
using KDesktop.Core.Model;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KDesktop.Core.Voice;

/// <summary>
/// <c>KDesktop.exe --voice-test a.wav carpeta "abre la papelera" …</c>: transcribes each .wav (or every .wav of a
/// folder) and matches it, or matches a phrase given as text, against the real commands. No microphone, no UI; the
/// report goes to Logs\voice-test.txt.
/// </summary>
public static class VoiceDiagnostics
{
    public static string Run(IReadOnlyList<string> inputs, AppConfig config)
    {
        var report = new StringBuilder();
        var commands = VoiceService.VoiceCommands(config).ToList();
        report.AppendLine($"K-Desktop · prueba de voz · {DateTime.Now:g} · {commands.Count} comandos disponibles por voz");
        using var stt = new SpeechToText();
        foreach (var input in inputs)
        {
            var files = Directory.Exists(input) ? Directory.GetFiles(input, "*.wav").Order().ToArray()
                      : File.Exists(input) ? [input] : null;
            if (files is null)
            {
                report.AppendLine().AppendLine($"Frase: «{input}»");
                Describe(report, IntentMatcher.Plan(input, commands));
                continue;
            }
            foreach (var file in files)
            {
                report.AppendLine().AppendLine($"Audio: {Path.GetFileName(file)}");
                try
                {
                    var samples = ReadWav(file);
                    var watch = Stopwatch.StartNew();
                    var text = stt.Transcribe(samples);
                    report.AppendLine($"  Texto: «{text}» ({samples.Length / (double)MicCapture.SampleRate:0.0} s, {watch.ElapsedMilliseconds} ms)");
                    Describe(report, IntentMatcher.Plan(text, commands));
                }
                catch (Exception ex)
                {
                    report.AppendLine("  Error: " + ex.Message);
                }
            }
        }
        var output = Path.Combine(AppPaths.LogDir, "voice-test.txt");
        Directory.CreateDirectory(AppPaths.LogDir);
        File.WriteAllText(output, report.ToString(), new UTF8Encoding(true));
        return output;
    }

    /// <summary>
    /// <c>--wake-test "KDesktop" carpeta</c>: the wake word set-up and its verification from .wav files instead of the
    /// microphone. word*.wav = the recordings of the word, speech*.wav = normal talk without it, test*.wav = what to
    /// check (with the word or without it). Report in Logs\wake-test.txt.
    /// </summary>
    public static string RunWake(string name, string folder)
    {
        var report = new StringBuilder();
        report.AppendLine($"K-Desktop · prueba de la palabra de activación «{name}» · {DateTime.Now:g}");
        report.AppendLine($"Clave fonética del nombre: {PhoneticKey.Of(name)}");
        using var stt = new SpeechToText();
        string[] Wavs(string prefix) => Directory.GetFiles(folder, prefix + "*.wav").Order().ToArray();

        report.AppendLine().AppendLine("Grabaciones de la palabra:");
        var kept = new List<double>();
        var variants = new List<string>();
        var takes = new List<float[]>();
        foreach (var file in Wavs("word"))
        {
            var take = WakeWordRecorder.Check(ReadWav(file), noiseRms: 0, kept);
            if (take.Issue == TakeIssue.None) takes.Add(take.Speech);
            var text = take.Speech.Length > 0 ? WakeWordVerifier.Clean(stt.Transcribe(take.Speech)) : "";
            report.AppendLine($"  {Path.GetFileName(file)}: {take.Seconds:0.00} s, pico {take.Peak:0.00} → {take.Issue}; «{text}» → {PhoneticKey.Of(text)}");
            if (take.Issue != TakeIssue.None || text.Length == 0) continue;
            kept.Add(take.Seconds);
            variants.Add(text);
        }

        report.AppendLine().AppendLine("Habla normal (negativos):");
        var negatives = new List<string>();
        foreach (var file in Wavs("speech"))
            foreach (var part in WakeWordRecorder.Utterances(ReadWav(file)))
            {
                var text = stt.Transcribe(part);
                if (text.Length == 0) continue;
                negatives.Add(text);
                report.AppendLine($"  {Path.GetFileName(file)}: «{text}»");
            }

        var cal = WakeWordVerifier.Calibrate(name, variants, negatives);
        var word = new WakeWord { Name = name, Variants = variants.Distinct().ToList(), Threshold = cal.Threshold, NegativeScore = cal.Negative };
        report.AppendLine().AppendLine($"Calibración: umbral {cal.Threshold:0.000} · habla normal hasta {cal.Negative:0.000} · " +
                                       $"grabaciones entre sí {string.Join(" ", cal.Positives.Select(p => p.ToString("0.00")))}");

        // Phase 1: the detector, trained here from the same recordings.
        WakeDetector? detector = null;
        OwwFeatures? features = null;
        if (VoiceModels.HasWakeDetector && takes.Count > 0)
        {
            features = new OwwFeatures();
            var silence = Wavs("silence").Select(ReadWav).FirstOrDefault();
            var speech = Wavs("speech").Select(ReadWav).FirstOrDefault();
            (detector, var trained) = WakeDetector.Train(features, takes, silence, speech);
            report.AppendLine().AppendLine($"Detector (fase 1): {trained.Positives} positivos, {trained.Negatives} negativos, {trained.Milliseconds} ms; " +
                                           $"tomas ≥ {trained.PositiveScore:0.000}, habla normal ≤ {trained.NegativeScore:0.000}; umbral {detector.Threshold(0.5):0.000}; " +
                                           $"DTW: tomas {detector.PositiveDistance:0.000}, habla {detector.NegativeDistance:0.000}");

            // Port check: the streaming path must give (almost) the embeddings of the whole-clip path.
            var probe = ReadWav(Wavs("speech").Concat(Wavs("word")).First());
            var whole = features.EmbedClip(probe);
            var stream = new OwwStream(features);
            var streamed = new List<float[]>();
            for (var i = 0; i < probe.Length; i += 512) streamed.AddRange(stream.Accept(probe.AsSpan(i, Math.Min(512, probe.Length - i))));
            // Streamed embedding j ends at chunk j (sample 1280·(j+1)); whole-clip embedding k ends near WindowEnd(k).
            var sims = new List<double>();
            for (var k = 0; k < whole.Length; k++)
            {
                var j = (int)Math.Round(OwwFeatures.WindowEnd(k) / (double)OwwFeatures.Chunk) - 1;
                if (j >= 0 && j < streamed.Count) sims.Add(1 - Dtw.CosineDistance(whole[k], streamed[j]));
            }
            report.AppendLine($"Comprobación del port: {whole.Length} embeddings del clip entero frente a {streamed.Count} en streaming; " +
                              $"similitud coseno media {sims.DefaultIfEmpty(0).Average():0.0000}, mínima {sims.DefaultIfEmpty(0).Min():0.0000}");
        }

        report.AppendLine().AppendLine("Pruebas:");
        foreach (var file in Wavs("test"))
        {
            var audio = ReadWav(file);
            var text = stt.Transcribe(audio);
            var match = WakeWordVerifier.Find(text, word);
            var phase1 = "";
            if (detector is not null && features is not null)
            {
                var (best, bestDtw, bestCls) = detector.BestLive(features, audio);
                phase1 = $"fase 1 {(best >= detector.Threshold(0.5) ? "SÍ" : "no")} {best:0.000} (DTW {bestDtw:0.00}, clasif. {bestCls:0.00}) · ";
            }
            report.AppendLine($"  {Path.GetFileName(file)}: «{text}» → {phase1}fase 2 {(match is { Accepted: true } ? "SÍ" : "no")} " +
                              $"({match?.Score ?? 0:0.000}){(match is { Accepted: true, Rest.Length: > 0 } ? $" · orden: «{match.Rest}»" : "")}");
        }
        features?.Dispose();

        var output = Path.Combine(AppPaths.LogDir, "wake-test.txt");
        Directory.CreateDirectory(AppPaths.LogDir);
        File.WriteAllText(output, report.ToString(), new UTF8Encoding(true));
        return output;
    }

    private static void Describe(StringBuilder report, IntentPlan plan)
    {
        report.AppendLine($"  Comando: {(plan.Parts.Count == 0 ? "(ninguno)" : string.Join(" + ", plan.Parts.Select(p => p.Command!.Name)))}");
        foreach (var c in plan.Whole.Candidates) report.AppendLine($"    {c.Score:0.000}  {c.Command.Name}");
        if (plan.Parts.Count > 1 || plan.Unmatched.Count > 0)
        {
            foreach (var part in plan.Parts) report.AppendLine($"    parte «{part.Text}» → {part.Command!.Name} {part.Confidence:0.000}");
            foreach (var part in plan.Unmatched) report.AppendLine($"    parte «{part}» → nada");
        }
    }

    /// <summary>Any .wav as mono 16 kHz float.</summary>
    public static float[] ReadWav(string path)
    {
        using var reader = new WaveFileReader(path);
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels == 2) samples = samples.ToMono();
        else if (samples.WaveFormat.Channels > 2) throw new NotSupportedException("Más de dos canales");
        if (samples.WaveFormat.SampleRate != MicCapture.SampleRate) samples = new WdlResamplingSampleProvider(samples, MicCapture.SampleRate);
        var all = new List<float>();
        var buffer = new float[MicCapture.SampleRate];
        int read;
        while ((read = samples.Read(buffer)) > 0) all.AddRange(buffer.AsSpan(0, read));
        return all.ToArray();
    }
}
