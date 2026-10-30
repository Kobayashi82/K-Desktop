using System.Buffers.Binary;
using System.Collections.Concurrent;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;

namespace KDesktop.Core.Tests;

/// <summary>Telegram, phase 5: voice notes heard, answers with the assistant's voice, photos for it to look at.</summary>
public sealed partial class TelegramTests
{
    [Fact]
    public async Task VoiceNotesAreHeardAndPhotosGoToTheAssistant()
    {
        var downloads = Path.Combine(_folder, "Descargas");
        var voices = Path.Combine(_folder, "Voz");
        using var s = await Commanded(downloads);
        var c = s.Connection;
        var talked = new ConcurrentQueue<(TelegramIncoming M, string Text, string? File)>();
        var heard = "pon música";
        s.Commands.VoiceFolder = () => voices;
        s.Commands.Transcribe = (path, _) => Task.FromResult<string?>(File.Exists(path) ? heard : null);
        s.Commands.Talk = (m, _, text, file) =>
        {
            talked.Enqueue((m, text, file));
            return Task.CompletedTask;
        };
        TelegramIncoming Voice(long from) =>
            new(1, from, TelegramChatKind.Private, null, 1, new TelegramUser(from, "X", null), DateTimeOffset.UtcNow, string.Empty, TelegramContent.Voice, new TelegramFile("v-" + from, null, "audio/ogg", 10));

        // The owner's: what is said is the order (shown back first), not a file kept in Downloads.
        c.Incoming.Add(Voice(42));
        await Until(() => talked.Count == 1);
        var said = talked.Last();
        Assert.Equal("pon música", said.Text);
        Assert.Null(said.File);
        Assert.Equal(TelegramContent.Voice, said.M.Content);
        Assert.Contains(c.Sent, x => x is { Chat: 42, Text: "🎤 pon música" });
        Assert.False(Directory.Exists(downloads) && Directory.EnumerateFiles(downloads).Any());
        Assert.Single(Directory.EnumerateFiles(voices));
        // The event sees what was said.
        Assert.Contains(s.Arrived, a => a.M.Text == "pon música" && a.Path is not null && a.Path.StartsWith(voices, StringComparison.Ordinal));

        // A command said is not run: «/comandos» spoken goes to the assistant like anything else.
        heard = "/comandos";
        c.Incoming.Add(Voice(42));
        await Until(() => talked.Count == 2);
        Assert.Equal("/comandos", talked.Last().Text);

        // Without «Decir lo que entendió»: no «🎤».
        s.Service.Update(x => x.ShowHeard = false);
        heard = "qué hora es";
        c.Incoming.Add(Voice(3));
        await Until(() => talked.Count == 3);
        Assert.DoesNotContain(c.Sent, x => x.Chat == 3 && x.Text.StartsWith("🎤", StringComparison.Ordinal));

        // Nobody speaks, or it cannot be heard: told, and nothing for the assistant.
        heard = "  ";
        var before = c.Sent.Count(x => x.Chat == 42);
        c.Incoming.Add(Voice(42));
        Assert.Equal(Loc.T("telegram.voice.empty"), await NextText(c, 42, before));
        s.Commands.Transcribe = (_, _) => Task.FromResult<string?>(null);
        before = c.Sent.Count(x => x.Chat == 42);
        c.Incoming.Add(Voice(42));
        Assert.Equal(Loc.T("telegram.voice.cannot"), await NextText(c, 42, before));
        // Only «Comandos elegidos»: told to use the commands.
        s.Commands.Transcribe = (_, _) => Task.FromResult<string?>("hola");
        before = c.Sent.Count(x => x.Chat == 2);
        c.Incoming.Add(Voice(2));
        Assert.Contains("/", await NextText(c, 2, before), StringComparison.Ordinal);
        await Task.Delay(100);
        Assert.Equal(3, talked.Count);

        // A photo alone, from a contact who may only talk: kept, and given to the assistant to look at.
        c.Incoming.Add(new TelegramIncoming(1, 3, TelegramChatKind.Private, null, 1, new TelegramUser(3, "Pepe", null), DateTimeOffset.UtcNow, string.Empty,
            TelegramContent.Photo, new TelegramFile("p-1", null, null, 10)));
        await Until(() => talked.Count == 4);
        var photo = talked.Last();
        Assert.Equal(Loc.T("telegram.talk.photo"), photo.Text);
        Assert.NotNull(photo.File);
        Assert.True(File.Exists(photo.File));
    }

    [Fact]
    public async Task AnswersBecomeVoiceNotes()
    {
        var folder = Path.Combine(_folder, "Notas");
        // Two sentences at the rates of Kokoro and Piper, arriving out of order: 1 s + 0.5 s.
        var voice = new TelegramVoice(folder);
        voice.Add(1, 22050, Pcm(Tone(22050, 0.5)));
        voice.Add(0, 24000, Pcm(Tone(24000, 1)));
        voice.End(ok: true);
        var file = await voice.File.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(file);
        var bytes = await File.ReadAllBytesAsync(file);
        Assert.True(bytes.AsSpan(0, 4).SequenceEqual("OggS"u8));
        var head = bytes.AsSpan().IndexOf("OpusHead"u8);
        Assert.True(head > 0);
        Assert.Equal(1, bytes[head + 9]); // mono
        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(head + 10));
        var last = bytes.AsSpan().LastIndexOf("OggS"u8);
        Assert.Equal(4, bytes[last + 5] & 4); // the last page says so
        var samples = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(last + 6)) - preSkip;
        Assert.InRange(samples, 48000 * 1.5 * 0.98, 48000 * 1.5 * 1.02);

        // Without voice (none set, or it failed): no note, the answer goes written.
        var none = new TelegramVoice(folder);
        none.Add(0, 24000, Pcm(Tone(24000, 0.2)));
        none.End(ok: false);
        Assert.Null(await none.File);
        var empty = new TelegramVoice(folder);
        empty.End(ok: true);
        Assert.Null(await empty.File);

        // How each one is answered: as they write by default; the contact's own choice wins.
        var settings = new TelegramSettings { Contacts = [new TelegramContact { Id = 2, Answer = TelegramAnswerKind.Both }, new TelegramContact { Id = 3 }] };
        Assert.Equal((true, false), TelegramVoice.AnswerWith(settings, 42, voiceNote: false));
        Assert.Equal((false, true), TelegramVoice.AnswerWith(settings, 42, voiceNote: true));
        Assert.Equal((true, true), TelegramVoice.AnswerWith(settings, 2, voiceNote: false));
        settings.Answer = TelegramAnswerKind.Voice;
        Assert.Equal((false, true), TelegramVoice.AnswerWith(settings, 3, voiceNote: false));
        settings.Answer = TelegramAnswerKind.Text;
        Assert.Equal((true, false), TelegramVoice.AnswerWith(settings, 42, voiceNote: true));
    }

    private static float[] Tone(int rate, double seconds) =>
        [.. Enumerable.Range(0, (int)(rate * seconds)).Select(i => 0.3f * MathF.Sin(2 * MathF.PI * 220 * i / rate))];

    private static byte[] Pcm(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2 * i), (short)(samples[i] * short.MaxValue));
        return bytes;
    }
}
