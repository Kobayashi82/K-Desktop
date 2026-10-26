using KDesktop.Assistant.Voice;
using KDesktop.Assistant.Voice.Kokoro;
using KDesktop.Assistant.Voice.Piper;
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
        // Nor Piper.
        Assert.Equal(expected, speech.Resolve("es", new VoiceSettings(SpeechEngineKind.Piper, PiperVoice: "es_ES-davefx-medium")));
        var german = speech.HasWindows("de") ? SpeechEngineKind.Windows : SpeechEngineKind.Beeps;
        Assert.Equal(german, speech.Resolve("de", new VoiceSettings(SpeechEngineKind.Piper)));
    }

    [Fact]
    public void ForPiperALoneShortSentenceGoesWithTheNextOne()
    {
        var splitter = new SentenceSplitter { JoinShort = true };
        Assert.Empty(splitter.Feed("Recibido. Abro"));
        Assert.Equal(["Recibido. Abro la papelera."], splitter.Feed("Recibido. Abro la papelera. Ya"));
        Assert.Equal(["Ya está."], splitter.Finish("Recibido. Abro la papelera. Ya está.", out _));

        // Nothing comes after it: it goes alone at the end.
        var alone = new SentenceSplitter { JoinShort = true };
        Assert.Empty(alone.Feed("Recibido. "));
        Assert.Equal(["Recibido."], alone.Finish("Recibido.", out _));

        // Kokoro does not wait.
        Assert.Equal(["Recibido."], new SentenceSplitter().Feed("Recibido. Abro"));
    }

    [Fact]
    public void TheCatalogHasPiperVoicesInEveryLanguage()
    {
        foreach (var language in new[] { "es", "en", "fr", "de", "it", "pt" })
        {
            Assert.True(PiperModels.Supports(language), language);
            var voice = PiperModels.DefaultVoice(language);
            Assert.Contains(PiperModels.Voices(language), v => v.Key == voice);
            Assert.All(PiperModels.Voices(language), v => Assert.Equal(language, v.Language));
            var files = PiperModels.Files(voice);
            Assert.Equal(PiperModels.ModelFile(voice), files[0].Path);
            Assert.Equal(PiperModels.ConfigFile(voice), files[1].Path);
            Assert.All(files.Take(2), f => Assert.StartsWith("https://huggingface.co/rhasspy/piper-voices/resolve/", f.Url, StringComparison.Ordinal));
            Assert.All(files.Take(2), f => Assert.Equal(64, f.Sha256.Length));
            // Every Piper voice needs espeak-ng (the tests' folder has none): it comes with the first one.
            Assert.Equal(EspeakFiles.Download, files[2]);
        }
        Assert.Equal("Jenny Dioco", PiperModels.Find("en_GB-jenny_dioco-medium")!.Name);
        Assert.Equal(22050, PiperModels.Find("de_DE-thorsten-medium")!.SampleRate);
        // A name that is not plain in a URL.
        Assert.Contains("tug%C3%A3o", PiperModels.Files("pt_PT-tugão-medium")[0].Url, StringComparison.Ordinal);
        Assert.Empty(PiperModels.Files("nobody"));
    }

    [Fact]
    public void APiperVoiceIsOfItsLanguageAndItsSpeakerOfItsVoice()
    {
        Assert.Equal("es_ES-davefx-medium", PiperModels.VoiceOrDefault("es", "de_DE-thorsten-medium"));
        Assert.Equal("es_ES-davefx-medium", PiperModels.VoiceOrDefault("es", ""));
        Assert.Equal("es_MX-claude-high", PiperModels.VoiceOrDefault("es", "es_MX-claude-high"));
        Assert.Equal("es_ES-sharvard-medium#F", PiperModels.VoiceOrDefault("es", "es_ES-sharvard-medium#F"));
        Assert.Equal("es_ES-sharvard-medium", PiperModels.VoiceOrDefault("es", "es_ES-sharvard-medium#nadie"));
        Assert.Equal(["M", "F"], PiperModels.Find("es_ES-sharvard-medium#F")!.Speakers);
        Assert.Equal("F", PiperModels.SpeakerOf("es_ES-sharvard-medium#F"));
        Assert.Null(PiperModels.SpeakerOf("es_ES-sharvard-medium"));
        Assert.Equal("de_DE-thorsten_emotional-medium#angry", PiperModels.Spec("de_DE-thorsten_emotional-medium", "angry"));
        Assert.Equal("es_ES-davefx-medium", PiperModels.Spec("es_ES-davefx-medium", null));
    }

    [Fact]
    public void PiperNumbersItsPhonemesAsPiperPhonemize()
    {
        // From es_ES-davefx-medium.onnx.json and, for the vowel pair, en_US-mike-medium (piper 1.5).
        const string map = """{ "_": [0], "^": [1], "$": [2], " ": [3], ",": [8], "a": [14], "h": [20], "l": [24], "o": [27], "ɪ": [74], "ˈ": [120], "aɪ": [161] }""";
        static PiperIds Ids(string json) => new(System.Text.Json.JsonDocument.Parse(json).RootElement);
        var davefx = Ids($$"""{ "phoneme_id_map": {{map}} }""");
        Assert.Equal([1, 0, 120, 0, 27, 0, 24, 0, 14, 0, 8, 0, 2], davefx.Of("ˈola,"));
        // Unknown phonemes are left out; without vowel_clusters a pair is two phonemes.
        Assert.Equal([1, 0, 20, 0, 14, 0, 74, 0, 2], davefx.Of("hxaɪ"));
        var mike = Ids($$"""{ "phoneme_id_map": {{map}}, "vowel_clusters": [["a", "ɪ"]] }""");
        Assert.Equal([1, 0, 20, 0, 161, 0, 2], mike.Of("haɪ"));
    }

    [Fact]
    public void PiperSaysWhatFitsInOneGo()
    {
        Assert.Equal(["ɹˈɛsɪbˌiðo. ˈabɾo la papelˈeɾa."], PiperEngine.Pieces("ɹˈɛsɪbˌiðo. ˈabɾo la papelˈeɾa."));
        var sentence = string.Join(' ', Enumerable.Repeat("ˈuna fɾˈase kˈoɾta.", 60));
        var pieces = PiperEngine.Pieces(sentence);
        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.InRange(p.Length, 1, PiperEngine.PieceLength + 1));
        Assert.All(pieces, p => Assert.EndsWith(".", p, StringComparison.Ordinal));
        Assert.Equal(sentence.Replace(" ", "", StringComparison.Ordinal), string.Concat(pieces).Replace(" ", "", StringComparison.Ordinal));
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
    public void AProfileMadeInCodeWithWholeNumbersIsRead()
    {
        // «Nuevo al azar» makes it in code: «1» there is an int, not a double (it broke before 5.0.280).
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = "Al azar",
            ["engine"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "kokoro", ["voice"] = "default", ["speed"] = 1, ["volume"] = 1 },
        };
        var profile = VoiceProfile.Parse(json, string.Empty);
        Assert.Equal(1.0, profile.Speed);
        Assert.Equal(1.0, profile.Volume);
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
    public void ProfilesAtRandomAlwaysFitTheSchemaAndBuild()
    {
        // Stage 9.4: «Aleatorio» and «Nuevo al azar» never make what the schema refuses or the effects cannot play.
        for (var seed = 0; seed < 200; seed++)
        {
            var chain = VoiceEffectCatalog.Random(new Random(seed));
            Assert.InRange(chain.Count, 2, 4);
            var profile = VoiceProfile.Default with { Id = "x", Name = "Prueba", Chain = chain };
            Assert.Empty(VoiceProfiles.Check(profile));
            Assert.False(Voice.Effects.VoiceChain.Build(profile, [], 24000, null, seed).IsEmpty);
        }
        // Every effect as the editor adds it, too.
        foreach (var fx in VoiceEffectCatalog.Names)
            Assert.Empty(VoiceProfiles.Check(VoiceProfile.Default with { Id = "x", Chain = [VoiceEffectCatalog.New(fx)] }));
    }

    [Fact]
    public void TheUsersProfilesAreKeptFoundExportedAndImported()
    {
        var folder = VoiceProfiles.Folder;
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        // A new one gets its id from its name; never one of K-Desktop's.
        var id = VoiceProfiles.Save(VoiceProfile.Default with { Id = string.Empty, Name = "Mi Róbot", Chain = [VoiceEffectCatalog.New("ring")] });
        Assert.Equal("mi-robot", id);
        Assert.Equal("natural-2", VoiceProfiles.Save(VoiceProfile.Default with { Name = "Natural" }));
        var found = VoiceProfiles.Find(id);
        Assert.NotNull(found);
        Assert.Equal("Mi Róbot", found!.Name);
        Assert.Equal("ring", found.Chain[0]!["fx"]!.GetValue<string>());
        Assert.Contains(VoiceProfiles.All, p => p.Id == id);

        // Changed on disk: read again.
        VoiceProfiles.Save(found with { Name = "Otro nombre" });
        Assert.Equal("Otro nombre", VoiceProfiles.Find(id)!.Name);

        // Exported and imported: a copy with a new id.
        var copy = VoiceProfiles.Import(VoiceProfiles.Export(VoiceProfiles.Find(id)!), out var errors);
        Assert.Empty(errors);
        Assert.Equal("otro-nombre", copy);
        Assert.Null(VoiceProfiles.Import("""{ "name": "Mal", "engine": { "type": "kokoro" }, "chain": [ { "fx": "pitch" } ] }""", out errors));
        Assert.NotEmpty(errors);

        VoiceProfiles.Delete(id);
        Assert.Null(VoiceProfiles.Find(id));
        VoiceProfiles.Delete("natural");
        Assert.NotNull(VoiceProfiles.Find("natural"));
        Directory.Delete(folder, true);
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
    public void TheUsersKokoroVoicesAreReadFromTheirFoldersEachOnce()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kdesktop-kokoro-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "v1_0"));
        // A .pt as PyTorch writes it: a zip with the pack in «<name>/data/0».
        static void Pt(string path, byte value)
        {
            using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
            using (var pickle = zip.CreateEntry("voice/data.pkl").Open()) pickle.Write([0x80, 2]);
            using var data = zip.CreateEntry("voice/data/0").Open();
            data.Write(Enumerable.Repeat(value, (int)KokoroModels.VoiceSize).ToArray());
        }
        Pt(Path.Combine(folder, "v1_0", "af_ellen.pt"), 1);
        Pt(Path.Combine(folder, "af_bella.pt"), 2);
        File.WriteAllBytes(Path.Combine(folder, "notes.pt"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "zf_xiaobei.bin"), new byte[KokoroModels.VoiceSize]);
        try
        {
            KokoroModels.UseFolders([folder]);
            var english = KokoroModels.Voices("en");
            // Hers is listed after Kokoro's; one of the catalog that is also there, once; Chinese (not spoken here) and other files, not at all.
            Assert.Equal("af_ellen", english[^1]);
            Assert.Single(english, v => v == "af_bella");
            Assert.DoesNotContain("zf_xiaobei", english);
            Assert.True(KokoroModels.IsInFolders("af_ellen"));
            Assert.True(KokoroModels.Installed("en", "af_ellen") || !KokoroModels.ModelInstalled);
            Assert.DoesNotContain(KokoroModels.Files("en", "af_ellen"), f => f.Path.Contains("af_ellen", StringComparison.Ordinal));
            Assert.Equal(KokoroModels.VoiceSize, KokoroModels.ReadPack(KokoroModels.PackFile("af_ellen")).Length);
            Assert.Equal("en", KokoroModels.LanguageOf("af_ellen"));
            Assert.Null(KokoroModels.LanguageOf("notes"));
        }
        finally
        {
            KokoroModels.UseFolders([]);
            Directory.Delete(folder, true);
        }
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

    /// <summary>Stage 12: Piper voices the user already has in a folder count as downloaded; nothing there is touched.</summary>
    [Fact]
    public void PiperVoicesInTheUsersFoldersAreUsedAsTheyAre()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "piper-" + Guid.NewGuid().ToString("N")[..6], "voces");
        var deeper = Path.Combine(folder, "es");
        Directory.CreateDirectory(deeper);
        // One not in the catalog, with two speakers, one level down; and one of the catalog.
        File.WriteAllBytes(Path.Combine(deeper, "es_AR-prueba-high.onnx"), new byte[1234]);
        File.WriteAllText(Path.Combine(deeper, "es_AR-prueba-high.onnx.json"),
            """{"audio":{"sample_rate":44100,"quality":"high"},"espeak":{"voice":"es-419"},"language":{"code":"es_AR","family":"es","region":"AR"},"speaker_id_map":{"ana":0,"luis":1}}""");
        File.WriteAllBytes(Path.Combine(folder, "es_ES-davefx-medium.onnx"), new byte[10]);
        File.WriteAllText(Path.Combine(folder, "es_ES-davefx-medium.onnx.json"), """{"audio":{"sample_rate":22050},"language":{"code":"es_ES"}}""");
        // Not a voice: no .onnx.json.
        File.WriteAllBytes(Path.Combine(folder, "otra-cosa.onnx"), new byte[5]);
        try
        {
            PiperModels.UseFolders([folder]);
            var own = Assert.Single(PiperModels.Voices("es"), v => v.Key == "es_AR-prueba-high");
            Assert.Equal(("es", "AR", "high", 44100, "es-419"), (own.Language, own.Region, own.Quality, own.SampleRate, own.Espeak));
            Assert.Equal(["ana", "luis"], own.Speakers);
            Assert.Equal(44100, PiperModels.Find("es_AR-prueba-high#luis")!.SampleRate);
            Assert.True(PiperModels.IsDownloaded("es_AR-prueba-high"));
            Assert.True(PiperModels.IsInFolders("es_AR-prueba-high"));
            Assert.Equal(Path.Combine(deeper, "es_AR-prueba-high.onnx"), PiperModels.ModelFile("es_AR-prueba-high"));
            Assert.Equal(2, PiperModels.CountIn(folder));
            Assert.DoesNotContain(PiperModels.Voices("en"), v => v.Key == "es_AR-prueba-high");

            // The catalog's, read from the folder: nothing to download but (maybe) espeak-ng, and the folder is read.
            Assert.True(PiperModels.IsDownloaded("es_ES-davefx-medium"));
            Assert.Equal(Path.Combine(folder, "es_ES-davefx-medium.onnx"), PiperModels.ModelFile("es_ES-davefx-medium"));
            Assert.DoesNotContain(PiperModels.Files("es_ES-davefx-medium"), f => f.Path.EndsWith(".onnx", StringComparison.Ordinal) || f.Path.EndsWith(".onnx.json", StringComparison.Ordinal));
            Assert.Single(PiperModels.Voices("es"), v => v.Key == "es_ES-davefx-medium");

            // Not K-Desktop's to delete.
            Assert.DoesNotContain("es_AR-prueba-high", PiperModels.AllInstalled);
            Assert.DoesNotContain("es_ES-davefx-medium", PiperModels.AllInstalled);
            PiperModels.Delete();
            Assert.True(File.Exists(Path.Combine(deeper, "es_AR-prueba-high.onnx")));
        }
        finally
        {
            PiperModels.UseFolders([]);
            Directory.Delete(Path.GetDirectoryName(folder)!, true);
        }
        Assert.Null(PiperModels.Find("es_AR-prueba-high"));
        Assert.Equal(PiperModels.Files("es_ES-davefx-medium")[0].Path, PiperModels.ModelFile("es_ES-davefx-medium"));
    }
}
