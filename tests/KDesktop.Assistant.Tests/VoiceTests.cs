using KDesktop.Assistant.Voice;
using KDesktop.Assistant.Voice.Kokoro;
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
        // The tests' data folder has no Kokoro: Windows' voices if this PC has one for the language, else beeps.
        var expected = speech.HasWindows("es") ? SpeechEngineKind.Windows : SpeechEngineKind.Beeps;
        Assert.Equal(expected, speech.Resolve("es", new VoiceSettings(SpeechEngineKind.Auto)));
        Assert.Equal(expected, speech.Resolve("es", new VoiceSettings(SpeechEngineKind.Kokoro)));
    }

    [Fact]
    public void ALongSentenceIsSaidInPieces()
    {
        var phonemes = SpanishPhonemes.ToPhonemes(string.Join(", ", Enumerable.Repeat("después busca la sección de sonido y elige el altavoz", 8)) + ".");
        var pieces = KokoroEngine.Pieces(phonemes);
        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.InRange(KokoroModel.Tokens(p).Count, 1, KokoroEngine.PieceLength));
        // Cut after a comma, and nothing lost.
        Assert.All(pieces.SkipLast(1), p => Assert.EndsWith(",", p, StringComparison.Ordinal));
        Assert.Equal(phonemes.Replace(" ", "", StringComparison.Ordinal), string.Concat(pieces).Replace(" ", "", StringComparison.Ordinal));
        Assert.Single(KokoroEngine.Pieces(SpanishPhonemes.ToPhonemes("Recibido, Houston.")));
    }

    [Fact]
    public void OnlyTheModelsSymbolsReachIt()
    {
        // Spaces and marks are symbols too; what it does not know («¿», digits) is left out.
        Assert.Equal(KokoroModel.Tokens("ˈola, kˈe tˈal?").Count, "ˈola, kˈe tˈal?".Length);
        Assert.Empty(KokoroModel.Tokens("¿¡123"));
    }

    [Fact]
    public void VoicesAreMixedByTheirWeights()
    {
        var folder = Directory.CreateTempSubdirectory("kokoro-voices").FullName;
        try
        {
            static byte[] Pack(float value) => System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                Enumerable.Repeat(value, KokoroModel.MaxPhonemes * KokoroModel.StyleSize).ToArray().AsSpan()).ToArray();
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), Pack(1));
            File.WriteAllBytes(Path.Combine(folder, "b.bin"), Pack(3));
            string File_(string name) => Path.Combine(folder, name + ".bin");
            Assert.Equal(1f, KokoroModel.Voice("a", File_)[0]);
            Assert.Equal(2f, KokoroModel.Voice("a,b", File_)[100], 4);
            Assert.Equal(1.5f, KokoroModel.Voice("a:0.75,b:0.25", File_)[^1], 4);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
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
    public void TheCatalogHasEveryLanguageKokoroSpeaks()
    {
        foreach (var language in new[] { "es", "en", "fr", "it", "pt" })
        {
            Assert.True(KokoroModels.Supports(language), language);
            var voice = KokoroModels.DefaultVoice(language);
            var files = KokoroModels.Files(language, voice);
            Assert.Equal(KokoroModels.ModelFile, files[0].Path);
            Assert.Equal(KokoroModels.VoiceFile(voice), files[1].Path);
            Assert.All(files, f => Assert.Equal(64, f.Sha256.Length));
            Assert.All(files.Take(2), f => Assert.StartsWith("https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/", f.Url, StringComparison.Ordinal));
            // Spanish has rules of its own; the others need espeak-ng, downloaded apart (the tests' folder has none).
            Assert.Equal(language != "es", KokoroModels.NeedsEspeak(language));
            Assert.Equal(language == "es" ? 2 : 3, files.Count);
        }
        Assert.False(KokoroModels.Supports("de"));
        Assert.Equal(["ef_dora", "em_alex", "em_santa"], KokoroModels.Voices("es"));
        Assert.Equal("en-gb", KokoroModels.EspeakVoice("en", "bm_george"));
        Assert.Equal("en-us", KokoroModels.EspeakVoice("en", "af_heart"));
        Assert.Null(KokoroModels.EspeakVoice("es", "ef_dora"));
    }

    [Fact]
    public void AVoiceOrAMixOfTheLanguageIsKept()
    {
        Assert.Equal("ef_dora", KokoroModels.VoiceOrDefault("es", "nobody"));
        Assert.Equal("ef_dora", KokoroModels.VoiceOrDefault("es", "af_heart"));
        Assert.Equal("em_alex", KokoroModels.VoiceOrDefault("es", "em_alex"));
        var mix = KokoroModels.Mix("ef_dora", "em_alex", 0.3);
        Assert.Equal("ef_dora:0.7,em_alex:0.3", mix);
        Assert.Equal(mix, KokoroModels.VoiceOrDefault("es", mix));
        Assert.Equal(["ef_dora", "em_alex"], KokoroModels.Names(mix));
        Assert.Equal("ef_dora", KokoroModels.Mix("ef_dora", "ef_dora", 0.5));
        Assert.Equal(3, KokoroModels.Files("es", mix).Count);
        Assert.Equal("Dora", KokoroModels.DisplayName("ef_dora"));
        Assert.True(KokoroModels.IsFemale("ef_dora"));
        Assert.False(KokoroModels.IsFemale("em_santa"));
    }
}
