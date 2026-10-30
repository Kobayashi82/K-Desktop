using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>
/// An answer's voice for Telegram (phase 5.2): the pieces the assistant makes with its voice (16-bit mono PCM, as for the
/// phone) gathered in order, and once it is over, a voice note (Opus in Ogg) in <paramref name="folder"/>. Thread-safe.
/// </summary>
public sealed class TelegramVoice(string folder)
{
    private readonly SortedDictionary<int, (float[] Samples, int Rate)> _pieces = [];
    private readonly TaskCompletionSource<string?> _file = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The voice note, or null when there is no voice (none set, or it failed).</summary>
    public Task<string?> File => _file.Task;

    /// <summary>A piece (<paramref name="index"/> from 0), as <c>speechAudio</c> brings it.</summary>
    public void Add(int index, int rate, byte[] pcm)
    {
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f;
        lock (_pieces) _pieces[index] = (samples, rate);
    }

    /// <summary>It is over (<paramref name="ok"/>: said whole): the note is written, off the caller's thread.</summary>
    public void End(bool ok)
    {
        List<(float[] Samples, int Rate)> pieces;
        lock (_pieces) pieces = [.. _pieces.Values];
        if (!ok || pieces.Count == 0)
        {
            _file.TrySetResult(null);
            return;
        }
        _ = Task.Run(() =>
        {
            var path = Path.Combine(folder, $"voz {DateTime.Now:yyyy-MM-dd HH.mm.ss} {Guid.NewGuid().ToString("N")[..6]}.ogg");
            try
            {
                Directory.CreateDirectory(folder);
                TimeSpan length;
                using (var stream = System.IO.File.Create(path)) length = AudioEncoders.VoiceNote(pieces, stream);
                Log.Info($"Telegram: nota de voz de {length.TotalSeconds:0.0} s ({new FileInfo(path).Length / 1024} KB)");
                _file.TrySetResult(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                Log.Warn($"Telegram: no se ha podido hacer la nota de voz: {ex.Message}");
                _file.TrySetResult(null);
            }
        });
    }

    /// <summary>How someone is answered: in text, with a voice note, or both (a voice note sent is answered as it came).</summary>
    public static (bool Text, bool Voice) AnswerWith(TelegramSettings s, long from, bool voiceNote) =>
        (s.Contacts.FirstOrDefault(c => c.Id == from)?.Answer ?? s.Answer) switch
        {
            TelegramAnswerKind.Text => (true, false),
            TelegramAnswerKind.Voice => (false, true),
            TelegramAnswerKind.Both => (true, true),
            _ => (!voiceNote, voiceNote),
        };
}
