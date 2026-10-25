using System.Diagnostics;
using System.Globalization;
using KDesktop.Assistant.Voice.Kokoro;

// Experiment: Kokoro as K-Desktop says things today, to compare with Ming-3.0-tts (see tools\tts\README.md).
//   KokoroBench <frases.txt> <out folder> [voice=ef_dora]
// Writes kokoro_<n>.wav (24 kHz) per sentence, ref.wav + ref.txt (the reference Ming clones) and kokoro.csv.
if (args.Length < 2)
{
    Console.Error.WriteLine("KokoroBench <frases.txt> <out folder> [voice]");
    return 2;
}
var sentences = File.ReadAllLines(args[0]).Where(s => s.Trim().Length > 0).ToArray();
var outDir = Directory.CreateDirectory(args[1]).FullName;
var voice = args.Length > 2 ? args[2] : "ef_dora";
const string Reference = "Hola, soy tu asistente. Puedo abrir programas, mover ventanas y contarte qué tiempo va a hacer mañana.";

using var engine = new KokoroEngine();
var load = Stopwatch.StartNew();
await engine.SayAsync("Hola.", "es", voice, 1, CancellationToken.None);
Console.WriteLine($"carga+primera: {load.ElapsedMilliseconds} ms");

Wav.Write(Path.Combine(outDir, "ref.wav"), await engine.SayAsync(Reference, "es", voice, 1, CancellationToken.None));
File.WriteAllText(Path.Combine(outDir, "ref.txt"), Reference);

var rows = new List<string> { "n,first_ms,total_ms,audio_s,rtf" };
for (var i = 0; i < sentences.Length; i++)
{
    var watch = Stopwatch.StartNew();
    long first = -1;
    var audio = await engine.SayAsync(sentences[i], "es", voice, 1, CancellationToken.None,
        _ => { if (first < 0) first = watch.ElapsedMilliseconds; });
    var total = watch.ElapsedMilliseconds;
    var seconds = audio.Length / (double)KokoroModel.SampleRate;
    Wav.Write(Path.Combine(outDir, $"kokoro_{i + 1}.wav"), audio);
    rows.Add(string.Create(CultureInfo.InvariantCulture, $"{i + 1},{first},{total},{seconds:0.00},{total / 1000.0 / seconds:0.000}"));
    Console.WriteLine(rows[^1]);
}
File.WriteAllLines(Path.Combine(outDir, "kokoro.csv"), rows);
return 0;

internal static class Wav
{
    public static void Write(string file, float[] samples)
    {
        using var writer = new BinaryWriter(File.Create(file));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(KokoroModel.SampleRate);
        writer.Write(KokoroModel.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var s in samples)
            writer.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }
}
