using System.Security.Cryptography;
using System.Text;

namespace KDesktop.Core.Platform;

public enum ClipKind { Text, Image, Files }

/// <summary>Something copied, as the watcher read it: a text (with its formats), a picture (PNG) or files.</summary>
public sealed record ClipCapture(ClipKind Kind, string Text, string? Rtf = null, string? Html = null, IReadOnlyList<string>? Files = null,
    byte[]? Png = null, int Width = 0, int Height = 0, string? Source = null);

/// <summary>One entry of the clipboard history. A picture's PNG is kept apart (<see cref="ClipboardHistory.ImageBytes"/>).</summary>
public sealed class ClipboardEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public ClipKind Kind { get; set; }

    /// <summary>The text; for files, their paths one per line; for a picture, empty.</summary>
    public string Text { get; set; } = string.Empty;
    public string? Rtf { get; set; }
    public string? Html { get; set; }
    public List<string> Files { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The program it was copied from (lowercase, without .exe), when known.</summary>
    public string? Source { get; set; }
    public DateTime Copied { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }

    /// <summary>What makes two copies the same (the second one only moves the first to the top).</summary>
    public string Hash { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasFormat => Rtf is not null || Html is not null;
}

/// <summary>
/// The clipboard history (Opciones › Portapapeles): what was copied, newest first, encrypted with DPAPI for this Windows
/// user in <c>Clipboard\</c> of the data folder (outside the backups: it only opens on this PC). The same thing copied
/// again goes back to the top; pinned entries never go away by themselves; beyond the limits the oldest go. Thread-safe.
/// </summary>
public sealed class ClipboardHistory
{
    private const string IndexFile = "history.dat";
    private static readonly byte[] Entropy = "K-Desktop 5 clipboard"u8.ToArray();

    /// <summary>Longer texts are not kept (a whole book copied).</summary>
    public const int MaxText = 1_000_000;

    /// <summary>Bigger pictures (as PNG) are not kept.</summary>
    public const int MaxImageBytes = 20 * 1024 * 1024;

    private readonly string _folder;
    private readonly Lock _gate = new();
    private List<ClipboardEntry> _entries = [];

    public ClipboardHistory(string folder) => _folder = folder;

    public static string DefaultFolder => Path.Combine(AppPaths.DataDir, "Clipboard");

    /// <summary>How many entries are kept (pinned ones aside); and after how many days they go (0: never).</summary>
    public int MaxItems { get; set; } = 200;
    public int MaxDays { get; set; }

    /// <summary>Raised (any thread) when something is added, removed or changed.</summary>
    public event Action? Changed;

    /// <summary>A copy, newest first.</summary>
    public IReadOnlyList<ClipboardEntry> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    /// <summary>The <paramref name="n"/>-th text copied (1: the last one), pictures and files aside; null when there are fewer.</summary>
    public string? Text(int n)
    {
        lock (_gate) return n < 1 ? null : _entries.Where(e => e.Kind == ClipKind.Text).Skip(n - 1).FirstOrDefault()?.Text;
    }

    public ClipboardEntry? Find(string id)
    {
        lock (_gate) return _entries.FirstOrDefault(e => e.Id == id);
    }

    public void Load()
    {
        var path = Path.Combine(_folder, IndexFile);
        if (!File.Exists(path)) return;
        try
        {
            var entries = Json.Deserialize<List<ClipboardEntry>>(Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(path)))) ?? [];
            lock (_gate) _entries = entries;
            Trim();
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or System.Text.Json.JsonException)
        {
            // Another user or PC, or a damaged file: it starts again empty.
            Log.Warn($"Historial del portapapeles: no se pudo leer ({ex.Message}); empieza vacío");
        }
    }

    /// <summary>Adds what was copied; the same as an entry already there moves that one to the top. Returns it, or null when not kept.</summary>
    public ClipboardEntry? Add(ClipCapture capture)
    {
        if (capture.Kind == ClipKind.Text && (capture.Text.Trim().Length == 0 || capture.Text.Length > MaxText)) return null;
        if (capture.Kind == ClipKind.Image && capture.Png is not { Length: > 0 and <= MaxImageBytes }) return null;
        if (capture.Kind == ClipKind.Files && capture.Files is not { Count: > 0 }) return null;
        var hash = HashOf(capture);
        ClipboardEntry entry;
        lock (_gate)
        {
            var same = _entries.FindIndex(e => e.Hash == hash);
            if (same >= 0)
            {
                entry = _entries[same];
                _entries.RemoveAt(same);
                entry.Copied = DateTime.Now;
                entry.Source = capture.Source ?? entry.Source;
                // The formats of the latest copy (the same text copied from Word, then from Notepad).
                if (capture.Kind == ClipKind.Text) (entry.Rtf, entry.Html) = (capture.Rtf, capture.Html);
            }
            else
            {
                entry = new ClipboardEntry
                {
                    Kind = capture.Kind,
                    Text = capture.Kind == ClipKind.Files ? string.Join(Environment.NewLine, capture.Files!) : capture.Kind == ClipKind.Image ? string.Empty : capture.Text,
                    Rtf = capture.Rtf,
                    Html = capture.Html,
                    Files = [.. capture.Files ?? []],
                    Width = capture.Width,
                    Height = capture.Height,
                    Source = capture.Source,
                    Hash = hash,
                };
                if (capture.Png is { } png) WriteImage(entry.Id, png);
            }
            _entries.Insert(0, entry);
        }
        Trim();
        Save();
        return entry;
    }

    /// <summary>An entry used again (pasted from the history) goes back to the top.</summary>
    public void Touch(string id)
    {
        lock (_gate)
        {
            var i = _entries.FindIndex(e => e.Id == id);
            if (i <= 0) return;
            var entry = _entries[i];
            _entries.RemoveAt(i);
            entry.Copied = DateTime.Now;
            _entries.Insert(0, entry);
        }
        Save();
    }

    public void SetPinned(string id, bool pinned)
    {
        lock (_gate)
        {
            if (_entries.FirstOrDefault(e => e.Id == id) is not { } entry || entry.Pinned == pinned) return;
            entry.Pinned = pinned;
        }
        Trim();
        Save();
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) == 0) return;
        }
        DeleteImage(id);
        Save();
    }

    /// <summary>Empties the history; the pinned entries stay unless <paramref name="pinnedToo"/>.</summary>
    public void Clear(bool pinnedToo = false)
    {
        List<ClipboardEntry> gone;
        lock (_gate)
        {
            gone = _entries.Where(e => pinnedToo || !e.Pinned).ToList();
            _entries = _entries.Except(gone).ToList();
        }
        foreach (var e in gone) DeleteImage(e.Id);
        Save();
    }

    /// <summary>The PNG of a picture entry (null: not a picture, or its file is gone).</summary>
    public byte[]? ImageBytes(string id)
    {
        var path = ImagePath(id);
        try { return File.Exists(path) ? Unprotect(File.ReadAllBytes(path)) : null; }
        catch (Exception ex) when (ex is CryptographicException or IOException) { return null; }
    }

    /// <summary>Applies the limits now (after changing them, and every so often).</summary>
    public void Trim()
    {
        List<ClipboardEntry> gone;
        lock (_gate)
        {
            var limit = DateTime.Now.AddDays(-MaxDays);
            var kept = new List<ClipboardEntry>();
            gone = [];
            var count = 0;
            foreach (var e in _entries)
            {
                var keep = e.Pinned || ((MaxDays <= 0 || e.Copied >= limit) && count < Math.Max(1, MaxItems));
                if (keep && !e.Pinned) count++;
                (keep ? kept : gone).Add(e);
            }
            if (gone.Count == 0) return;
            _entries = kept;
        }
        foreach (var e in gone) DeleteImage(e.Id);
        Save();
    }

    private void Save()
    {
        List<ClipboardEntry> copy;
        lock (_gate) copy = [.. _entries];
        try
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, IndexFile);
            File.WriteAllBytes(path + ".tmp", Protect(Encoding.UTF8.GetBytes(Json.Serialize(copy))));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Warn($"Historial del portapapeles: no se pudo guardar ({ex.Message})");
        }
        try { Changed?.Invoke(); }
        catch (Exception ex) { Log.Error("Error en un manejador del historial del portapapeles", ex); }
    }

    private string ImagePath(string id) => Path.Combine(_folder, id + ".img");

    private void WriteImage(string id, byte[] png)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(ImagePath(id), Protect(png));
    }

    private void DeleteImage(string id)
    {
        try { File.Delete(ImagePath(id)); }
        catch (IOException) { /* in use: it goes with the next clean-up */ }
    }

    private static string HashOf(ClipCapture c)
    {
        var data = c.Kind switch
        {
            ClipKind.Image => c.Png!,
            ClipKind.Files => Encoding.UTF8.GetBytes("F" + string.Join("\n", c.Files!)),
            _ => Encoding.UTF8.GetBytes("T" + c.Text),
        };
        return Convert.ToHexString(SHA256.HashData(data))[..32];
    }

    private static byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    private static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
