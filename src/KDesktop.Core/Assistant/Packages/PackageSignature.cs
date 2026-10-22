using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>What the signature of a package says.</summary>
public enum SignatureState
{
    /// <summary>No <c>signature.json</c>: nobody vouches for it (like every package before stage 12).</summary>
    NotSigned,

    /// <summary>Signed and whole, by a key K-Desktop knows: «verificado».</summary>
    Verified,

    /// <summary>Signed and whole, by a key K-Desktop does not know: the name it gives is only its own word.</summary>
    Unknown,

    /// <summary>It has a signature that does not match: something was changed, added or taken out after signing.</summary>
    Broken,
}

/// <summary>A package's signature, checked. <paramref name="Fingerprint"/>: of its key (null unsigned or broken beyond reading).</summary>
public sealed record SignatureInfo(SignatureState State, string? Publisher = null, string? Fingerprint = null)
{
    public static SignatureInfo None { get; } = new(SignatureState.NotSigned);

    /// <summary>Whole and signed, known or not.</summary>
    public bool IsSigned => State is SignatureState.Verified or SignatureState.Unknown;
}

/// <summary>A key K-Desktop trusts: packages it signs are «verificados».</summary>
public sealed record TrustedPublisher(string Name, string Key)
{
    public string Fingerprint => PackageSignature.FingerprintOf(Convert.FromBase64String(Key));
}

/// <summary>
/// Signed packages without paying anyone (stage 12): the publisher's own ECDSA P-256 key, no certificates. Inside the
/// <c>.kai</c>, <c>signature.json</c> lists every other file with its SHA-256, the publisher's name and public key, and
/// the signature of all of it; nothing may be missing, added or changed. A key in <c>trusted-publishers.json</c> (built
/// into K-Desktop) makes the package «verificado». The private key stays with the publisher (<c>tools/KaiSign</c>).
/// </summary>
public static class PackageSignature
{
    public const string FileName = "signature.json";

    public const string Algorithm = "ECDSA-P256-SHA256";

    private const string Header = "kai-signature-v1";

    private static readonly Lazy<IReadOnlyList<TrustedPublisher>> BuiltIn = new(LoadTrusted);

    /// <summary>The publishers K-Desktop trusts.</summary>
    public static IReadOnlyList<TrustedPublisher> Trusted => BuiltIn.Value;

    /// <summary>Short and stable name of a public key (its SubjectPublicKeyInfo): the first 16 bytes of its SHA-256, in hex.</summary>
    public static string FingerprintOf(byte[] publicKey) => Convert.ToHexStringLower(SHA256.HashData(publicKey).AsSpan(0, 16));

    /// <summary>Checks the signature of a package already opened (its files by path, as <see cref="AssistantPackages"/> reads them).</summary>
    public static SignatureInfo Check(IReadOnlyDictionary<string, ZipArchiveEntry> entries, IReadOnlyList<TrustedPublisher>? trusted = null)
    {
        if (!entries.TryGetValue(FileName, out var signatureEntry)) return SignatureInfo.None;
        JsonObject json;
        try
        {
            if (signatureEntry.Length > 1L << 20) return new SignatureInfo(SignatureState.Broken);
            using var stream = signatureEntry.Open();
            json = JsonNode.Parse(stream)?.AsObject() ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or InvalidDataException)
        {
            return new SignatureInfo(SignatureState.Broken);
        }
        var publisher = Text(json["publisher"]);
        var key = Text(json["key"]);
        byte[] publicKey;
        try { publicKey = Convert.FromBase64String(key); }
        catch (FormatException) { return new SignatureInfo(SignatureState.Broken, publisher); }
        var fingerprint = FingerprintOf(publicKey);
        if (Text(json["alg"]) != Algorithm || json["files"] is not JsonObject listed) return new SignatureInfo(SignatureState.Broken, publisher, fingerprint);

        // Exactly the files it signed, each as it was.
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, hash) in listed) files[path] = Text(hash);
        var present = entries.Keys.Where(p => !p.Equals(FileName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (present.Count != files.Count) return new SignatureInfo(SignatureState.Broken, publisher, fingerprint);
        foreach (var path in present)
        {
            if (!files.TryGetValue(path, out var expected)) return new SignatureInfo(SignatureState.Broken, publisher, fingerprint);
            using var stream = entries[path].Open();
            if (!Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                return new SignatureInfo(SignatureState.Broken, publisher, fingerprint);
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            var signature = Convert.FromBase64String(Text(json["sig"]));
            if (!ecdsa.VerifyData(Payload(publisher, key, Text(json["signed"]), files), signature, HashAlgorithmName.SHA256))
                return new SignatureInfo(SignatureState.Broken, publisher, fingerprint);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return new SignatureInfo(SignatureState.Broken, publisher, fingerprint);
        }

        var known = (trusted ?? Trusted).FirstOrDefault(t => t.Fingerprint == fingerprint);
        return known is null
            ? new SignatureInfo(SignatureState.Unknown, publisher, fingerprint)
            : new SignatureInfo(SignatureState.Verified, known.Name, fingerprint);
    }

    /// <summary>Checks a <c>.kai</c> file.</summary>
    public static SignatureInfo Check(string file, IReadOnlyList<TrustedPublisher>? trusted = null)
    {
        using var zip = ZipFile.OpenRead(file);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
            if (!entry.FullName.EndsWith('/')) entries[entry.FullName.Replace('\\', '/')] = entry;
        return Check(entries, trusted);
    }

    /// <summary>
    /// The signature of an installed assistant (its folder has the <c>signature.json</c> of its package): only who signed
    /// it, to compare with an update (the files are not checked again).
    /// </summary>
    public static SignatureInfo OfInstalled(string folder, IReadOnlyList<TrustedPublisher>? trusted = null)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) return SignatureInfo.None;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            var fingerprint = FingerprintOf(Convert.FromBase64String(Text(json?["key"])));
            var known = (trusted ?? Trusted).FirstOrDefault(t => t.Fingerprint == fingerprint);
            return known is null ? new SignatureInfo(SignatureState.Unknown, Text(json?["publisher"]), fingerprint) : new SignatureInfo(SignatureState.Verified, known.Name, fingerprint);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException or InvalidOperationException)
        {
            return new SignatureInfo(SignatureState.Broken);
        }
    }

    /// <summary>
    /// Signs a <c>.kai</c> in place: <c>signature.json</c> (a new one, if it had one) with every other file. The key is the
    /// publisher's private ECDSA P-256 key.
    /// </summary>
    public static void Sign(string file, ECDsa key, string publisher, DateTimeOffset? when = null)
    {
        if (key.KeySize != 256) throw new ArgumentException("La clave debe ser ECDSA P-256", nameof(key));
        using var zip = ZipFile.Open(file, ZipArchiveMode.Update);
        zip.Entries.Where(e => e.FullName.Replace('\\', '/').Equals(FileName, StringComparison.OrdinalIgnoreCase)).ToList().ForEach(e => e.Delete());
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            using var stream = entry.Open();
            files[entry.FullName.Replace('\\', '/')] = Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var signed = (when ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        var signature = key.SignData(Payload(publisher, publicKey, signed, files), HashAlgorithmName.SHA256);
        var json = new JsonObject
        {
            ["v"] = 1,
            ["alg"] = Algorithm,
            ["publisher"] = publisher,
            ["key"] = publicKey,
            ["signed"] = signed,
            ["files"] = new JsonObject(files.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))),
            ["sig"] = Convert.ToBase64String(signature),
        };
        var created = zip.CreateEntry(FileName, CompressionLevel.Optimal);
        using var output = created.Open();
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteTo(writer);
    }

    /// <summary>What is signed: the header, who, the key, when, and each file with its hash, in order.</summary>
    private static byte[] Payload(string publisher, string key, string signed, SortedDictionary<string, string> files)
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n')
            .Append("publisher:").Append(publisher).Append('\n')
            .Append("key:").Append(key).Append('\n')
            .Append("signed:").Append(signed).Append('\n');
        foreach (var (path, hash) in files) text.Append(path).Append('\t').Append(hash.ToLowerInvariant()).Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static IReadOnlyList<TrustedPublisher> LoadTrusted()
    {
        using var stream = typeof(PackageSignature).Assembly.GetManifestResourceStream("AssistantPackages/trusted-publishers.json");
        if (stream is null) return [];
        var list = JsonNode.Parse(stream)?["publishers"] as JsonArray ?? [];
        return [.. list.OfType<JsonObject>().Select(p => new TrustedPublisher(Text(p["name"]), Text(p["key"]))).Where(p => p.Name.Length > 0 && p.Key.Length > 0)];
    }

    private static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;
}
