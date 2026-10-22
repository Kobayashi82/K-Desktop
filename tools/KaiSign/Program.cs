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
                return Fail($"Ya hay una clave en {keyFile} (--force para sustituirla: lo firmado con ella dejará de reconocerse).");
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                Save(key, args[1]);
                Console.WriteLine($"Clave nueva de «{args[1]}» en {keyFile}");
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
                    if (check.State != SignatureState.Verified) return Fail($"{file}: la firma no se comprueba ({check.State})");
                    Console.WriteLine($"Firmado: {file} ({publisher}, {check.Fingerprint})");
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
                    SignatureState.Verified => $"verificado ({check.Publisher})",
                    SignatureState.Unknown => $"firmado por «{check.Publisher}», clave desconocida {check.Fingerprint}",
                    SignatureState.Broken => "FIRMA NO VÁLIDA: el paquete se cambió después de firmarlo",
                    _ => "sin firma",
                }}");
            }
            return bad ? 1 : 0;

        case "export" when args.Length >= 3:
            using (var key = Load(out var publisher))
            {
                var pem = key.ExportEncryptedPkcs8PrivateKeyPem(args[2], new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
                File.WriteAllText(args[1], $"# {publisher}\n{pem}\n");
                Console.WriteLine($"Copia con contraseña en {args[1]}");
            }
            return 0;

        case "import" when args.Length >= 4:
            if (File.Exists(keyFile) && !args.Contains("--force")) return Fail($"Ya hay una clave en {keyFile} (--force para sustituirla).");
            using (var key = ECDsa.Create())
            {
                var text = File.ReadAllText(args[1]);
                key.ImportFromEncryptedPem(text.AsSpan(text.IndexOf("-----BEGIN", StringComparison.Ordinal)), args[2]);
                Save(key, args[3]);
                Console.WriteLine($"Clave de «{args[3]}» importada en {keyFile}");
                PrintTrusted(key, args[3]);
            }
            return 0;

        default:
            Console.WriteLine("""
                kaisign: firma paquetes de asistentes (.kai) con tu propia clave.

                  kaisign keygen <publicador> [--force]   crea la clave (en este usuario de Windows, con DPAPI)
                  kaisign public                          la clave pública, como va en trusted-publishers.json
                  kaisign sign <paquete.kai>...           firma (sustituye una firma anterior)
                  kaisign verify <paquete.kai>...         comprueba lo que diría K-Desktop
                  kaisign export <archivo> <contraseña>   copia de la clave con contraseña, para guardarla aparte
                  kaisign import <archivo> <contraseña> <publicador> [--force]
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
    if (!File.Exists(keyFile)) throw new IOException($"No hay clave: crea una con «kaisign keygen <publicador>» (se busca en {keyFile}).");
    publisher = File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : "?";
    var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(ProtectedData.Unprotect(File.ReadAllBytes(keyFile), entropy, DataProtectionScope.CurrentUser), out _);
    return key;
}

static void PrintTrusted(ECDsa key, string publisher)
{
    var publicKey = key.ExportSubjectPublicKeyInfo();
    Console.WriteLine($"Huella: {PackageSignature.FingerprintOf(publicKey)}");
    Console.WriteLine("Para trusted-publishers.json:");
    Console.WriteLine(new JsonObject { ["name"] = publisher, ["key"] = Convert.ToBase64String(publicKey) }
        .ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
