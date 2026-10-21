using KDesktop.Assistant.Voice;
using KDesktop.Assistant.Voice.Pocket;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 5.4: the answer said aloud sentence by sentence while it is written, and the voices it is said with.</summary>
public class VoiceTests
{
    [Fact]
    public void FinishedSentencesGoOutWhileTheAnswerIsWritten()
    {
        var splitter = new SentenceSplitter();
        Assert.Empty(splitter.Feed("Hola"));
        Assert.Empty(splitter.Feed("Hola, soy el Lander."));          // the mark has no space after it yet
        Assert.Equal(["Hola, soy el Lander."], splitter.Feed("Hola, soy el Lander. Hoy"));
        Assert.Empty(splitter.Feed("Hola, soy el Lander. Hoy hace 3.5 grados"));   // a decimal point does not end it
        Assert.Equal(["Hoy hace 3.5 grados?!"], splitter.Feed("Hola, soy el Lander. Hoy hace 3.5 grados?! Y"));
        Assert.Equal(["Y mañana, más."], splitter.Finish("Hola, soy el Lander. Hoy hace 3.5 grados?! Y mañana, más.", out var diverged));
        Assert.False(diverged);
    }

    [Fact]
    public void AnAnswerThatChangesIsSaidWhole()
    {
        var splitter = new SentenceSplitter();
        splitter.Feed("Voy a mirarlo. Un momento");
        var rest = splitter.Finish("Hecho. He puesto un temporizador de 5 minutos.", out var diverged);
        Assert.True(diverged);
        Assert.Equal(["Hecho.", "He puesto un temporizador de 5 minutos."], rest);
    }

    [Fact]
    public void ALongSentenceWithoutAnEndIsCutAtAComma()
    {
        var splitter = new SentenceSplitter();
        var text = "Primero abre el panel de control, después busca la sección de sonido y elige el altavoz que quieras usar, luego ajusta el volumen con mucha calma hasta que suene bien y, por último, guarda todos los cambios que hayas hecho y cierra la ventana";
        Assert.True(text.Length > SentenceSplitter.LongSentence);
        var out1 = splitter.Feed(text);
        Assert.Single(out1);
        Assert.EndsWith(",", out1[0], StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyWhatCanBeSaidIsSaid()
    {
        Assert.Equal("Hecho . Listo", SentenceSplitter.Speakable("Hecho 🎉. Listo ✅"));
        Assert.Null(SentenceSplitter.Speakable("🎉 ✅ ★"));
        Assert.True(SentenceSplitter.Speakable(new string('a', 900) + ". Fin.")!.Length <= SentenceSplitter.MaxSpokenLength);
    }

    [Fact]
    public async Task BeepsAlwaysSpeakTheSameWay()
    {
        var a = Beeps.Say("Hola, ¿qué tal?");
        var b = Beeps.Say("Hola, ¿qué tal?");
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        Assert.InRange(a.Length / (double)Beeps.SampleRate, 0.3, 4);
        Assert.All(a, s => Assert.InRange(s, -1f, 1f));

        using var speech = new Speech();
        var spoken = await speech.SayAsync("Hola, soy el Lander.", "es", new VoiceSettings(SpeechEngineKind.Beeps), CancellationToken.None);
        Assert.Equal(SpeechEngineKind.Beeps, spoken!.Engine);
        Assert.Null(await speech.SayAsync("Hola.", "es", new VoiceSettings(SpeechEngineKind.Off), CancellationToken.None));
    }

    [Fact]
    public void WithoutTheNaturalVoiceAutomaticFallsBack()
    {
        using var speech = new Speech();
        // The tests' data folder has no Pocket TTS: Windows' voices if this PC has one for the language, else beeps.
        var expected = speech.HasWindows("es") ? SpeechEngineKind.Windows : SpeechEngineKind.Beeps;
        Assert.Equal(expected, speech.Resolve("es", new VoiceSettings(SpeechEngineKind.Auto)));
        Assert.Equal(expected, speech.Resolve("es", new VoiceSettings(SpeechEngineKind.Pocket)));
    }

    [Fact]
    public void ASentenceThatRamblesOrStopsIsNoticed()
    {
        var (shortest, longest) = PocketEngine.Bounds("Recibido. He vaciado la papelera.");
        Assert.InRange(2.0, shortest, longest);
        Assert.True(6.4 > longest);   // what «javert» once did with it
        Assert.True(0.16 < shortest);
    }

    [Fact]
    public void TheSixProfilesAndTheirMoodsBuild()
    {
        Assert.Equal(["natural", "robot", "martian", "mission", "eightbit", "beeps"], VoiceProfile.BuiltIn.Select(p => p.Id));
        var example = VoiceProfile.Load(Path.Combine(Contracts.DocsDir, "ejemplos-paquete", "lander", "voice", "lander.json"), "lander");
        Assert.NotNull(example);
        var noise = new Random(1);
        var input = Enumerable.Range(0, 24000).Select(i => (float)(Math.Sin(i * 0.05) * 0.3 + (noise.NextDouble() - 0.5) * 0.05)).ToArray();
        foreach (var profile in VoiceProfile.BuiltIn.Append(example!))
            foreach (var moods in new[] { Array.Empty<string>(), ["error"], ["far", "happy"] })
            {
                var chain = Voice.Effects.VoiceChain.Build(profile, moods, 24000, speed: null, seed: 3);
                var output = new List<float>();
                for (var at = 0; at < input.Length; at += 1920) output.AddRange(chain.Process(input[at..Math.Min(input.Length, at + 1920)]));
                output.AddRange(chain.Finish());
                Assert.All(output, s => Assert.True(float.IsFinite(s) && Math.Abs(s) <= 1, $"{profile.Id}: {s}"));
                Assert.True(output.Count >= input.Length * 0.9, $"{profile.Id} {string.Join('+', moods)}: {output.Count}");
            }
    }

    [Fact]
    public void FasterIsShorter()
    {
        var input = Enumerable.Range(0, 24000).Select(i => (float)Math.Sin(i * 0.05) * 0.3f).ToArray();
        var chain = Voice.Effects.VoiceChain.Build(VoiceProfile.Default, [], 24000, speed: 1.25, seed: 1);
        var output = chain.Process(input).Length + chain.Finish().Length;
        Assert.InRange(output, 24000 / 1.25 * 0.95, 24000 / 1.25 * 1.05);
    }

    [Fact]
    public void TheCatalogHasEveryLanguageOfKDesktop()
    {
        foreach (var language in new[] { "es", "en", "fr", "de", "it", "pt" })
        {
            Assert.True(PocketModels.Supports(language), language);
            Assert.Contains(PocketModels.DefaultVoice, PocketModels.Voices(language));
            var files = PocketModels.Files(language, PocketModels.DefaultVoice);
            Assert.Equal(7, files.Count);
            Assert.All(files, f => Assert.StartsWith("https://huggingface.co/", f.Url, StringComparison.Ordinal));
            Assert.All(files.Where(f => f.Path.EndsWith(".onnx", StringComparison.Ordinal)), f => Assert.Equal(64, f.Sha256.Length));
        }
        Assert.Equal(PocketModels.DefaultVoice, PocketModels.VoiceOrDefault("es", "nobody"));
    }
}
