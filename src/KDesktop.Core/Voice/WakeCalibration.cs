namespace KDesktop.Core.Voice;

/// <summary>
/// What recording a wake word measures that does not depend on the word (5.0.109): 3 s of the room's silence and 10 s of
/// the user talking normally (with what Parakeet understood of it), with the microphone they were recorded with. Kept
/// after each recording, so the next word only asks for the word itself while it is the same microphone and recent.
/// </summary>
public sealed record WakeCalibration(float[] Silence, float[] Speech, IReadOnlyList<string> Negatives, string? Microphone, DateTime Recorded)
{
    /// <summary>Older than this, the room or the voice may have changed: measured again.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);

    public static string Folder => Path.Combine(AppPaths.DataDir, "Voice", "WakeCalibration");

    private sealed record Info(string? Microphone, DateTime Recorded, List<string> Negatives);

    /// <summary>The one kept, when it is of this microphone (its id; null = the default one) and recent; otherwise null.</summary>
    public static WakeCalibration? Load(string? microphone, DateTime? now = null)
    {
        try
        {
            var info = Path.Combine(Folder, "calibration.json");
            if (!File.Exists(info)) return null;
            var read = Json.Deserialize<Info>(File.ReadAllText(info));
            if (read is null || read.Microphone != microphone || (now ?? DateTime.Now) - read.Recorded > KeepFor) return null;
            var silence = VoiceDiagnostics.ReadWav(Path.Combine(Folder, "silence.wav"));
            var speech = VoiceDiagnostics.ReadWav(Path.Combine(Folder, "speech.wav"));
            return silence.Length > 0 && speech.Length > 0 ? new WakeCalibration(silence, speech, read.Negatives, read.Microphone, read.Recorded) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            Log.Warn($"No se ha podido leer la calibración de las palabras de activación: {ex.Message}");
            return null;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            WakeWordStore.WriteWav(Path.Combine(Folder, "silence.wav"), Silence);
            WakeWordStore.WriteWav(Path.Combine(Folder, "speech.wav"), Speech);
            File.WriteAllText(Path.Combine(Folder, "calibration.json"), Json.Serialize(new Info(Microphone, Recorded, [.. Negatives])));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se ha podido guardar la calibración de las palabras de activación: {ex.Message}");
        }
    }
}
