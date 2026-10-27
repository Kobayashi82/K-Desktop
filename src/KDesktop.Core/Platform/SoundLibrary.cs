using System.Text.RegularExpressions;

namespace KDesktop.Core.Platform;

/// <summary>What a sound is for, to sort them in the sound chooser.</summary>
public enum SoundKind { Alert, Alarm, Ring, System }

/// <summary>Where a sound comes from: built into K-Desktop, Windows' own (C:\Windows\Media) or the user's Sounds folder.</summary>
public enum SoundSource { KDesktop, Windows, Yours }

/// <summary>A sound of the chooser: <see cref="Value"/> is what is saved (a name, or the path of a Windows file).</summary>
public sealed record SoundEntry(string Value, string Name, SoundKind Kind, SoundSource Source);

/// <summary>
/// Every sound the chooser offers: the built-in ones (only K-Desktop's own: none is a copy of a Windows one), the user's
/// Sounds folder and Windows' (C:\Windows\Media and its theme folders).
/// </summary>
public static partial class SoundLibrary
{
    /// <summary>The formats that play (Media Foundation reads all but .wav).</summary>
    public static readonly string[] Extensions = [".wav", ".mp3", ".wma", ".m4a", ".aac", ".flac"];

    public static string WindowsMedia => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    /// <summary>
    /// Built-in sounds until 5.0.296 that were Windows' own files with another name (the same bytes): since 5.0.297 they
    /// are not built in, and those names mean the Windows file (<see cref="Upgrade"/>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FromWindows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Alarma 1"] = "Alarm02.wav", ["Alarma 2"] = "Alarm03.wav", ["Balloon"] = "Windows Balloon.wav",
        ["Battery Critical"] = "Windows Battery Critical.wav", ["Battery Low"] = "Windows Battery Low.wav", ["Chimes"] = "chimes.wav",
        ["Chord"] = "chord.wav", ["Connection"] = "Windows Proximity Connection.wav", ["Default"] = "Windows Default.wav",
        ["Ding"] = "Windows Ding.wav", ["Error"] = "Windows Error.wav", ["Exclamation"] = "Windows Exclamation.wav",
        ["LogOff"] = "Windows Logoff Sound.wav", ["LogOn"] = "Windows Logon.wav", ["Notification 1"] = "Windows Proximity Notification.wav",
        ["Notification 2"] = "notify.wav", ["Notification 3"] = "Windows Notify.wav", ["Print"] = "Windows Print complete.wav",
        ["Recycle"] = "Windows Recycle.wav", ["Ring 1"] = "Ring02.wav", ["Ring 2"] = "Ring06.wav", ["Ring 3"] = "Windows Ringout.wav",
        ["Shutdown"] = "Windows Shutdown.wav", ["Stop"] = "Windows Critical Stop.wav", ["Tada"] = "tada.wav", ["Unlock"] = "Windows Unlock.wav",
    };

    /// <summary>A name of <see cref="FromWindows"/> as the path of its Windows file; anything else as it is.</summary>
    public static string? Upgrade(string? sound) =>
        sound is not null && FromWindows.TryGetValue(sound, out var file) ? Path.Combine(WindowsMedia, file) : sound;

    public static bool IsPlayableFile(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Windows' sounds do not change while K-Desktop runs: read (and compared with the built-in ones) once.</summary>
    private static readonly Lazy<IReadOnlyList<SoundEntry>> WindowsSounds = new(ReadWindowsSounds);

    public static IReadOnlyList<SoundEntry> All()
    {
        var list = new List<SoundEntry>();
        foreach (var name in Sounds.BuiltInNames()) list.Add(new SoundEntry(name, name, KindOf(name), SoundSource.KDesktop));
        // A user file with the name of a built-in one replaces it (Sounds.FindFile looks there first).
        foreach (var name in Sounds.UserNames())
        {
            list.RemoveAll(e => e.Source == SoundSource.KDesktop && string.Equals(e.Value, name, StringComparison.OrdinalIgnoreCase));
            list.Add(new SoundEntry(name, name, KindOf(name), SoundSource.Yours));
        }
        list.AddRange(WindowsSounds.Value);
        return list;
    }

    /// <summary>The name to show for a saved sound: a built-in or user name as it is, a file by its name.</summary>
    public static string NameOf(string sound) =>
        !Path.IsPathRooted(sound) ? sound
        : IsWindowsFile(sound) ? WindowsName(sound)
        : Path.GetFileNameWithoutExtension(sound);

    private static bool IsWindowsFile(string path) =>
        path.StartsWith(WindowsMedia + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a sound is for, by the words in its name (in English: Windows', and most built-in ones).</summary>
    public static SoundKind KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("alarm", StringComparison.Ordinal)) return SoundKind.Alarm;
        if (n.Contains("ring", StringComparison.Ordinal)) return SoundKind.Ring;
        string[] alert = ["notif", "message", "messaging", "mail", "calendar", "balloon", "nudge", "chime", "chord", "ding", "tada",
                          "toc", "logro", "generic", "information", "feed", "pop-up", "default", "reminder"];
        return alert.Any(w => n.Contains(w, StringComparison.Ordinal)) ? SoundKind.Alert : SoundKind.System;
    }

    /// <summary>
    /// «Windows Notify Email.wav» → «Notify Email», «Alarm01.wav» → «Alarm 01», with its theme folder if it is in one. The
    /// «Windows» stays where taking it away would give the name of another file (ding.wav and Windows Ding.wav differ).
    /// </summary>
    private static string WindowsName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, name[8..] + Path.GetExtension(path))))
            name = name[8..];
        name = DigitsAfterLetters().Replace(name, " ");
        if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name[1..];
        var folder = Path.GetFileName(Path.GetDirectoryName(path));
        return string.Equals(Path.GetDirectoryName(path), WindowsMedia, StringComparison.OrdinalIgnoreCase) ? name : $"{name} · {folder}";
    }

    [GeneratedRegex(@"(?<=[A-Za-z])(?=\d)")]
    private static partial Regex DigitsAfterLetters();

    private static List<SoundEntry> ReadWindowsSounds()
    {
        var list = new List<SoundEntry>();
        try
        {
            if (!Directory.Exists(WindowsMedia)) return list;
            // «dm» holds other takes of the same system sounds: they would only repeat names.
            var folders = new[] { WindowsMedia }.Concat(Directory.GetDirectories(WindowsMedia)
                .Where(d => !string.Equals(Path.GetFileName(d), "dm", StringComparison.OrdinalIgnoreCase)));
            foreach (var folder in folders)
                foreach (var file in Directory.EnumerateFiles(folder).Where(IsPlayableFile).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var name = WindowsName(file);
                    list.Add(new SoundEntry(file, name, KindOf(name), SoundSource.Windows));
                }
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudieron leer los sonidos de Windows: {ex.Message}");
        }
        return list;
    }
}
