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
                Describe(report, IntentMatcher.Match(input, commands));
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
                    Describe(report, IntentMatcher.Match(text, commands));
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

    private static void Describe(StringBuilder report, IntentResult result)
    {
        report.AppendLine($"  Comando: {result.Command?.Name ?? "(ninguno)"}");
        foreach (var c in result.Candidates) report.AppendLine($"    {c.Score:0.000}  {c.Command.Name}");
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
