using System.Security.Cryptography;
using System.Text;
using KDesktop.Core.Model;

namespace KDesktop.Core.Remote.Web;

/// <summary>A phone or browser paired with the web app. Only the hash of its token is stored.</summary>
public sealed class WebDevice
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Tokens of the same device at its other addresses (a browser keeps one per address: the local IP and the
    /// proxy's domain are different sites to it). See <see cref="WebDevices.NewLinkCode"/>.
    /// </summary>
    public List<string> LinkedTokenHashes { get; set; } = [];
    /// <summary>The browsers of this same PC, paired by «Abrir en el navegador» (see <see cref="WebDevices.NewOpenCode"/>).</summary>
    public bool ThisPc { get; set; }

    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime LastSeen { get; set; } = DateTime.Now;
    public string? LastIp { get; set; }
}

/// <summary>An active pairing code (shown in Settings as a QR and as text).</summary>
public sealed record WebPairingCode(string Code, DateTime Expires);

/// <summary>
/// Paired devices (web-devices.json in the data folder) and pairing codes. Nothing reaches the API without pairing:
/// a one-time code shown in Settings (valid for a few minutes) is exchanged for a long random token.
/// </summary>
public sealed class WebDevices
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>No 0/O, 1/I/L: easy to type from the screen.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;
    private const int MaxFailures = 5;
    private const int MaxLinkedTokens = 8;

    private readonly Lock _lock = new();
    private List<WebDevice> _devices = [];
    private WebPairingCode? _code;
    private int _failures;

    /// <summary>One-time codes that pair an already paired device at another address: code → device id.</summary>
    private readonly Dictionary<string, (string DeviceId, DateTime Expires)> _linkCodes = [];
    private DateTime _lastSave = DateTime.MinValue;

    private static string FilePath => Path.Combine(AppPaths.DataDir, "web-devices.json");

    /// <summary>Raised (on any thread) when a device is paired, revoked or renamed.</summary>
    public event Action? Changed;

    /// <summary>Raised (on any thread) when the current pairing code is used.</summary>
    public event Action<WebDevice>? Paired;

    /// <summary>The browser of this same PC when it needs no pairing (see <see cref="WebRequest.IsLocal"/>).</summary>
    public static WebDevice Local { get; } = new() { Id = "local" };

    public WebDevices() => Load();

    public IReadOnlyList<WebDevice> List()
    {
        lock (_lock) return _devices.OrderByDescending(d => d.LastSeen).ToList();
    }

    /// <summary>Creates a new pairing code (the previous one stops working).</summary>
    public WebPairingCode NewCode()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        lock (_lock)
        {
            _code = new WebPairingCode(new string(chars), DateTime.Now + CodeLifetime);
            _failures = 0;
            return _code;
        }
    }

    /// <summary>
    /// A one-time code for <paramref name="device"/> to pair itself at another address of the app (it opens
    /// <c>other-address#link=CODE</c>): the same device, not a new one.
    /// </summary>
    public string NewLinkCode(WebDevice device)
    {
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        lock (_lock)
        {
            foreach (var old in _linkCodes.Where(c => c.Value.Expires < DateTime.Now).Select(c => c.Key).ToList()) _linkCodes.Remove(old);
            _linkCodes[code] = (device.Id, DateTime.Now + CodeLifetime);
        }
        return code;
    }

    /// <summary>
    /// A one-time code for K-Desktop to open the app in this PC's browser already paired (<c>localhost#link=CODE</c>):
    /// it only works from this PC, and every browser of the PC is the same device, <paramref name="name"/>.
    /// </summary>
    public string NewOpenCode(string name)
    {
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        lock (_lock)
        {
            foreach (var old in _linkCodes.Where(c => c.Value.Expires < DateTime.Now).Select(c => c.Key).ToList()) _linkCodes.Remove(old);
            _linkCodes[code] = (ThisPcPrefix + name, DateTime.Now + CodeLifetime);
        }
        return code;
    }

    /// <summary>Marks a link code made by <see cref="NewOpenCode"/> (the rest of the value is the device's name).</summary>
    private const string ThisPcPrefix = "\u0001";

    public void CancelCode()
    {
        lock (_lock) _code = null;
    }

    /// <summary>Exchanges a pairing code for a device token; null when the code is wrong or expired.</summary>
    public (WebDevice Device, string Token)? Pair(string? code, string? name, string? ip, bool local = false)
    {
        if (Link(code, ip, local) is { } linked) return linked;
        var typed = new string((code ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        WebDevice device;
        string token;
        lock (_lock)
        {
            if (_code is null || DateTime.Now > _code.Expires) return null;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(typed), Encoding.ASCII.GetBytes(_code.Code)))
            {
                // Guessing is pointless (31^8 combinations) but cut it short anyway.
                if (++_failures >= MaxFailures)
                {
                    _code = null;
                    Log.Warn($"Web: código de emparejamiento anulado tras {MaxFailures} intentos fallidos (último desde {ip})");
                }
                return null;
            }
            _code = null;
            token = Base64Url(RandomNumberGenerator.GetBytes(32));
            device = new WebDevice
            {
                Name = CleanName(name),
                TokenHash = Hash(token),
                LastIp = ip,
            };
            _devices.Add(device);
            SaveLocked();
        }
        Log.Info($"Web: dispositivo emparejado «{device.Name}» ({ip})");
        Paired?.Invoke(device);
        Changed?.Invoke();
        return (device, token);
    }

    /// <summary>A link code (see <see cref="NewLinkCode"/>): a new token for the device it belongs to.</summary>
    private (WebDevice Device, string Token)? Link(string? code, string? ip, bool local)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 40) return null;
        WebDevice? device;
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        bool thisPc;
        lock (_lock)
        {
            if (!_linkCodes.TryGetValue(code, out var link) || DateTime.Now > link.Expires) return null;
            thisPc = link.DeviceId.StartsWith(ThisPcPrefix, StringComparison.Ordinal);
            // Opening from K-Desktop pairs only a browser of this PC: the code is useless anywhere else.
            if (thisPc && !local) return null;
            _linkCodes.Remove(code);
            device = thisPc ? _devices.FirstOrDefault(d => d.ThisPc) : _devices.FirstOrDefault(d => d.Id == link.DeviceId);
            if (device is null && thisPc)
            {
                device = new WebDevice { Name = CleanName(link.DeviceId[ThisPcPrefix.Length..]), TokenHash = Hash(token), ThisPc = true };
                _devices.Add(device);
            }
            else if (device is null) return null;
            else
            {
                device.LinkedTokenHashes.Add(Hash(token));
                if (device.LinkedTokenHashes.Count > MaxLinkedTokens) device.LinkedTokenHashes.RemoveAt(0);
            }
            device.LastSeen = DateTime.Now;
            device.LastIp = ip ?? device.LastIp;
            SaveLocked();
        }
        Log.Info(thisPc ? $"Web: emparejado un navegador de este PC («{device.Name}»)" : $"Web: «{device.Name}» emparejado también en otra dirección ({ip})");
        Changed?.Invoke();
        return (device, token);
    }

    /// <summary>The device that owns <paramref name="token"/>, or null.</summary>
    public WebDevice? Authenticate(string? token, string? ip)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
        var hash = Hash(token);
        lock (_lock)
        {
            var bytes = Encoding.ASCII.GetBytes(hash);
            var device = _devices.FirstOrDefault(d => d.LinkedTokenHashes.Prepend(d.TokenHash)
                .Any(h => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), bytes)));
            if (device is null) return null;
            device.LastSeen = DateTime.Now;
            device.LastIp = ip ?? device.LastIp;
            // "Last used" does not need to hit the disk on every tap.
            if (DateTime.Now - _lastSave > TimeSpan.FromMinutes(1)) SaveLocked();
            return device;
        }
    }

    public void Revoke(string id)
    {
        lock (_lock)
        {
            if (_devices.RemoveAll(d => d.Id == id) == 0) return;
            SaveLocked();
        }
        Log.Info("Web: dispositivo revocado");
        Changed?.Invoke();
    }

    public void Rename(string id, string? name)
    {
        lock (_lock)
        {
            if (_devices.FirstOrDefault(d => d.Id == id) is not { } device) return;
            device.Name = CleanName(name);
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public void Flush()
    {
        lock (_lock) SaveLocked();
    }

    private static string CleanName(string? name)
    {
        var clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? "?" : clean.Length > 60 ? clean[..60] : clean;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath)) _devices = Json.Deserialize<List<WebDevice>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception ex) { Log.Error("Web: no se pudieron leer los dispositivos emparejados", ex); }
    }

    private void SaveLocked()
    {
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(_devices));
            File.Move(tmp, FilePath, overwrite: true);
            _lastSave = DateTime.Now;
        }
        catch (Exception ex) { Log.Error("Web: no se pudieron guardar los dispositivos emparejados", ex); }
    }
}
