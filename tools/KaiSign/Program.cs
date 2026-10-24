using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Packages;

// kaisign: the publisher's side of signed assistant packages (stage 12). The private key lives in this Windows user,
// protected with DPAPI; «export» makes a copy with a password, to keep it somewhere else.

Console.OutputEncoding = Encoding.UTF8;
var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "K-Desktop", "Signing");
var keyFile = Path.Combine(folder, "key.dpapi");
var nameFile = Path.Combine(folder, "publisher.txt");
var entropy = "K-Desktop package signing"u8.ToArray();

try
{
    switch (args.FirstOrDefault()?.ToLowerInvariant())
    {
        case "keygen" when args.Length >= 2:
            if (File.Exists(keyFile) && !args.Contains("--force"))
                return Fail($"There is already a key in {keyFile} (--force replaces it: what was signed with it will no longer be recognised).");
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                Save(key, args[1]);
                Console.WriteLine($"New key of «{args[1]}» in {keyFile}");
                PrintTrusted(key, args[1]);
            }
            return 0;

        case "public":
            using (var key = Load(out var publisher))
                PrintTrusted(key, publisher);
            return 0;

        case "sign" when args.Length >= 2:
            using (var key = Load(out var publisher))
                foreach (var file in args.Skip(1))
                {
                    PackageSignature.Sign(file, key, publisher);
                    var check = PackageSignature.Check(file, [new TrustedPublisher(publisher, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()))]);
                    if (check.State != SignatureState.Verified) return Fail($"{file}: the signature does not check ({check.State})");
                    Console.WriteLine($"Signed: {file} ({publisher}, {check.Fingerprint})");
                }
            return 0;

        case "verify" when args.Length >= 2:
            var bad = false;
            foreach (var file in args.Skip(1))
            {
                var check = PackageSignature.Check(file);
                bad |= check.State == SignatureState.Broken;
                Console.WriteLine($"{file}: {check.State switch
                {
                    SignatureState.Verified => $"verified ({check.Publisher})",
                    SignatureState.Unknown => $"signed by «{check.Publisher}», unknown key {check.Fingerprint}",
                    SignatureState.Broken => "INVALID SIGNATURE: the package changed after it was signed",
                    _ => "not signed",
                }}");
            }
            return bad ? 1 : 0;

        case "export" when args.Length >= 2:
            using (var key = Load(out var publisher))
            {
                // Asked, not written in the command: it would stay in the console's history.
                var password = args.Length >= 3 ? args[2] : AskPassword(twice: true);
                if (password.Length < 8) return Fail("The password needs 8 characters at least.");
                var pem = key.ExportEncryptedPkcs8PrivateKeyPem(password, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
                File.WriteAllText(args[1], $"# {publisher}\n{pem}\n");
                Console.WriteLine($"Copy with a password in {args[1]}");
            }
            return 0;

        case "import" when args.Length >= 3:
            if (File.Exists(keyFile) && !args.Contains("--force")) return Fail($"There is already a key in {keyFile} (--force replaces it).");
            using (var key = ECDsa.Create())
            {
                // import <file> <publisher> asks for the password; import <file> <password> <publisher> as before.
                var rest = args.Skip(2).Where(a => a != "--force").ToList();
                var (password, who) = rest.Count >= 2 ? (rest[0], rest[1]) : (AskPassword(twice: false), rest[0]);
                var text = File.ReadAllText(args[1]);
                key.ImportFromEncryptedPem(text.AsSpan(text.IndexOf("-----BEGIN", StringComparison.Ordinal)), password);
                Save(key, who);
                Console.WriteLine($"Key of «{who}» imported into {keyFile}");
                PrintTrusted(key, who);
            }
            return 0;

        default:
            Console.WriteLine("""
                kaisign: signs K-Desktop assistant packages (.kai) with your own key.

                  kaisign keygen <publisher> [--force]    makes the key (for this Windows user, protected with DPAPI)
                  kaisign public                          the public key, as it goes in trusted-publishers.json
                  kaisign sign <package.kai>...           signs (replaces an earlier signature)
                  kaisign verify <package.kai>...         checks what K-Desktop would say
                  kaisign export <file>                   a copy of the key with a password (asked), to keep elsewhere
                  kaisign import <file> <publisher> [--force]   puts it back for this user (asks for the password)
                """);
            return args.Length == 0 || args[0] is "help" or "-h" or "--help" ? 0 : 2;
    }
}
catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
{
    return Fail(ex.Message);
}

void Save(ECDsa key, string publisher)
{
    Directory.CreateDirectory(folder);
    File.WriteAllBytes(keyFile, ProtectedData.Protect(key.ExportPkcs8PrivateKey(), entropy, DataProtectionScope.CurrentUser));
    File.WriteAllText(nameFile, publisher.Trim(), Encoding.UTF8);
}

ECDsa Load(out string publisher)
{
    if (!File.Exists(keyFile)) throw new IOException($"There is no key: make one with «kaisign keygen <publisher>» (looked for in {keyFile}).");
    publisher = File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : "?";
    var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(ProtectedData.Unprotect(File.ReadAllBytes(keyFile), entropy, DataProtectionScope.CurrentUser), out _);
    return key;
}

static void PrintTrusted(ECDsa key, string publisher)
{
    var publicKey = key.ExportSubjectPublicKeyInfo();
    Console.WriteLine($"Fingerprint: {PackageSignature.FingerprintOf(publicKey)}");
    Console.WriteLine("For trusted-publishers.json:");
    Console.WriteLine(new JsonObject { ["name"] = publisher, ["key"] = Convert.ToBase64String(publicKey) }
        .ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}

/// <summary>A password typed without showing it (twice: it must be the same both times).</summary>
static string AskPassword(bool twice)
{
    var first = Read("Password: ");
    if (twice && Read("Again: ") != first) throw new ArgumentException("The two passwords are not the same.");
    return first;

    static string Read(string prompt)
    {
        Console.Write(prompt);
        var text = new StringBuilder();
        for (var key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(intercept: true))
        {
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
        Console.WriteLine();
        return text.ToString();
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
