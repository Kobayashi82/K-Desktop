using System.Text.Json;
using KDesktop.Core.Model;

namespace KDesktop.Core.Remote.Web;

/// <summary>Something sent to the phone (B11): a text, a link and/or a small file, for some paired devices.</summary>
/// <param name="Devices">The devices it is for (their ids), chosen when it was sent.</param>
/// <param name="Delivered">The devices that already got it (they said so with <c>api/inbox/ack</c>).</param>
/// <param name="File">The file's name, kept in the inbox's folder under <paramref name="Id"/>.</param>
public sealed record PhoneMessage(string Id, DateTime At, string Title, string Text, string? Url, string? File, long FileSize, string From,
    IReadOnlyList<string> Devices, List<string> Delivered);

/// <summary>What <see cref="PhoneInbox.Send"/> did: to whom (their names), or why not.</summary>
/// <param name="Reason">Not sent: <c>empty</c>, <c>noDevice</c> (none paired, or none with that name), <c>fileMissing</c>, <c>fileTooBig</c>.</param>
public sealed record PhoneSent(PhoneMessage? Message, IReadOnlyList<string> Names, string? Reason = null);

/// <summary>
/// «Mándamelo al móvil» (B11, 5.0.353): what K-Desktop sends to the paired phones and browsers, kept until each of them
/// has it (phone-inbox.json in the data folder, files in Phone\). The open web app and the Android app's service hear
/// «inbox» in api/events and ask api/inbox; nothing goes through anybody's servers. A week at most, the last 100.
/// </summary>
public sealed class PhoneInbox
{
    /// <summary>The biggest file sent.</summary>
    public const long MaxFile = 25L * 1024 * 1024;

    public const int MaxKept = 100;

    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(7);

    private const int MaxText = 4000;

    private readonly Lock _lock = new();
    private readonly string _folder;
    private List<PhoneMessage> _messages = [];

    /// <summary>The one of K-Desktop (actions, the assistants, the web server).</summary>
    public static PhoneInbox Current { get; set; } = new(AppPaths.DataDir);

    /// <summary>The devices that can get things: the paired ones, without this PC's browsers (set by the web server).</summary>
    public Func<IReadOnlyList<WebDevice>> Devices { get; set; } = () => [];

    /// <summary>Raised (on any thread) when something new is waiting.</summary>
    public event Action? Added;

    public PhoneInbox(string folder)
    {
        _folder = folder;
        Load();
    }

    private string FilePath => Path.Combine(_folder, "phone-inbox.json");

    private string FilesFolder => Path.Combine(_folder, "Phone");

    /// <summary>The paired phones and browsers it can send to (not this PC's own browsers).</summary>
    public IReadOnlyList<WebDevice> Targets() => [.. Devices().Where(d => !d.ThisPc && d.Id != WebDevices.Local.Id)];

    /// <summary>
    /// Sends it to the device named (part of its name, any case) or, with none, to all of them. The file is copied, so it
    /// can change or go away afterwards.
    /// </summary>
    public PhoneSent Send(string title, string text, string? url, string? file, string from, string? device = null)
    {
        title = Clip(title, 120);
        text = Clip(text, MaxText);
        url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        file = string.IsNullOrWhiteSpace(file) ? null : Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));
        if (title.Length == 0 && text.Length == 0 && url is null && file is null) return new PhoneSent(null, [], "empty");
        if (file is not null)
        {
            if (!File.Exists(file)) return new PhoneSent(null, [], "fileMissing");
            if (new FileInfo(file).Length > MaxFile) return new PhoneSent(null, [], "fileTooBig");
        }
        var targets = Targets();
        if (!string.IsNullOrWhiteSpace(device))
        {
            var words = device.Trim();
            targets = [.. targets.Where(d => d.Name.Contains(words, StringComparison.CurrentCultureIgnoreCase))];
        }
        if (targets.Count == 0) return new PhoneSent(null, [], "noDevice");

        var id = Ids.New();
        string? kept = null;
        long size = 0;
        if (file is not null)
        {
            var folder = Path.Combine(FilesFolder, id);
            Directory.CreateDirectory(folder);
            kept = Path.GetFileName(file);
            File.Copy(file, Path.Combine(folder, kept));
            size = new FileInfo(file).Length;
        }
        var message = new PhoneMessage(id, DateTime.Now, title, text, url, kept, size, from, [.. targets.Select(d => d.Id)], []);
        lock (_lock)
        {
            _messages.Add(message);
            Prune(DateTime.Now);
            Save();
        }
        Log.Info($"Móvil: «{(title.Length > 0 ? title : text.Length > 0 ? Clip(text, 40) : url ?? kept)}» para {string.Join(", ", targets.Select(d => d.Name))} ({from})");
        Added?.Invoke();
        return new PhoneSent(message, [.. targets.Select(d => d.Name)]);
    }

    /// <summary>What is waiting for <paramref name="deviceId"/>, oldest first.</summary>
    public IReadOnlyList<PhoneMessage> For(string deviceId)
    {
        lock (_lock)
        {
            Prune(DateTime.Now);
            return [.. _messages.Where(m => m.Devices.Contains(deviceId) && !m.Delivered.Contains(deviceId))];
        }
    }

    /// <summary>The device has these now: not given to it again.</summary>
    public void Ack(string deviceId, IEnumerable<string> ids)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        lock (_lock)
        {
            var changed = false;
            foreach (var message in _messages.Where(m => set.Contains(m.Id) && m.Devices.Contains(deviceId) && !m.Delivered.Contains(deviceId)))
            {
                message.Delivered.Add(deviceId);
                changed = true;
            }
            if (!changed) return;
            Prune(DateTime.Now);
            Save();
        }
    }

    /// <summary>The file of a message, if that device may have it.</summary>
    public string? FileOf(string id, string deviceId)
    {
        lock (_lock)
        {
            var message = _messages.FirstOrDefault(m => m.Id == id && m.Devices.Contains(deviceId));
            if (message?.File is not { } name) return null;
            var path = Path.Combine(FilesFolder, id, name);
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>Old ones, too many, or already with everybody: out (their files too). Inside the lock.</summary>
    private void Prune(DateTime now)
    {
        var gone = _messages.Where(m => now - m.At > KeptFor || m.Devices.All(m.Delivered.Contains) && now - m.At > TimeSpan.FromDays(1)).ToList();
        if (_messages.Count - gone.Count > MaxKept) gone.AddRange(_messages.Except(gone).OrderBy(m => m.At).Take(_messages.Count - gone.Count - MaxKept));
        foreach (var message in gone)
        {
            _messages.Remove(message);
            if (message.File is null) continue;
            try { Directory.Delete(Path.Combine(FilesFolder, message.Id), true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next time */ }
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath)) _messages = JsonSerializer.Deserialize<List<PhoneMessage>>(File.ReadAllText(FilePath), Json.Options) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Móvil: no se pudo leer phone-inbox.json: " + ex.Message);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_messages, Json.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Móvil: no se pudo guardar phone-inbox.json: " + ex.Message);
        }
    }

    private static string Clip(string? text, int most)
    {
        var clean = (text ?? string.Empty).Trim();
        return clean.Length <= most ? clean : clean[..most].TrimEnd() + "…";
    }
}
