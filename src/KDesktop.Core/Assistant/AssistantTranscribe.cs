using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant;

/// <summary>
/// <c>transcribe</c> (transcribe_audio, D5, 5.0.373): an audio file to text with K-Desktop's Parakeet, without the AI. A .txt
/// next to it at least as new as the audio (what «Al terminar, transcribir» or an earlier transcription left) is read
/// instead; otherwise the text is saved in one. Without a file: the newest recording in the folder of «Grabar audio».
/// </summary>
public static class AssistantTranscribe
{
    /// <param name="recordings">The folder of «Grabar audio» (Opciones › Grabación de audio).</param>
    /// <param name="recording">The file being recorded now (never «the latest»: it is not over).</param>
    /// <param name="ready">The file once a recording just stopped has been converted (MP3, Opus).</param>
    public static async Task<TranscribedMessage> HandleAsync(TranscribeMessage ask, SpeechToText? speech, string recordings, string? recording,
        Func<string, Task<string>> ready, CancellationToken token)
    {
        var file = ask.File.Trim().Trim('"');
        if (file.Length == 0)
        {
            if (Newest(recordings, recording) is not { } newest) return new TranscribedMessage(ask.Id, false, Reason: "notFound");
            file = newest;
        }
        file = await ready(file).ConfigureAwait(false);
        if (!File.Exists(file)) return new TranscribedMessage(ask.Id, false, File: file, Reason: "notFound");
        if (!AudioTranscriber.Extensions.Contains(Path.GetExtension(file))) return new TranscribedMessage(ask.Id, false, File: file, Reason: "notAudio");

        var txt = Path.ChangeExtension(file, ".txt");
        try
        {
            if (File.Exists(txt) && File.GetLastWriteTimeUtc(txt) >= File.GetLastWriteTimeUtc(file))
            {
                var kept = (await File.ReadAllTextAsync(txt, token).ConfigureAwait(false)).Trim();
                if (kept.Length > 0)
                {
                    Log.Info($"Asistente: transcripción de {Path.GetFileName(file)} ya hecha ({Path.GetFileName(txt)})");
                    return new TranscribedMessage(ask.Id, true, kept, file, Saved: txt);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Asistente: no se pudo leer {txt} ({ex.Message})"); }

        if (speech is null || !VoiceModels.Ready) return new TranscribedMessage(ask.Id, false, File: file, Reason: "noModels");
        string text;
        try
        {
            text = await AudioTranscriber.TranscribeAsync(speech, file, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"Asistente: no se pudo transcribir {file} ({ex.Message})");
            return new TranscribedMessage(ask.Id, false, File: file, Reason: "unreadable");
        }
        if (text.Length == 0) return new TranscribedMessage(ask.Id, true, File: file, Reason: "noSpeech");
        string? saved = txt;
        try { await File.WriteAllTextAsync(txt, text, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se pudo guardar {txt} ({ex.Message})");
            saved = null;
        }
        return new TranscribedMessage(ask.Id, true, text, file, Saved: saved);
    }

    /// <summary>The newest audio file in <paramref name="folder"/> (not in its subfolders), other than <paramref name="recording"/>.</summary>
    public static string? Newest(string folder, string? recording = null)
    {
        if (!Directory.Exists(folder)) return null;
        // A recording still being converted: its MP3 or Opus is the newest, and «ready» waits for it.
        return new DirectoryInfo(folder).EnumerateFiles()
            .Where(f => AudioTranscriber.Extensions.Contains(f.Extension) && !string.Equals(f.FullName, recording, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.CreationTimeUtc)
            .FirstOrDefault()?.FullName;
    }
}
