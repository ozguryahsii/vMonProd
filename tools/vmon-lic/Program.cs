using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// ============================================================================
// vmon-lic — vMon lisans üretme aracı (YALNIZ satıcıda durur, dağıtılmaz)
//
//   vmon-lic new  --out <klasör> --pass <parola>
//       Anahtar çifti üretir: private.vmonkey (parola-şifreli, AES+PBKDF2)
//       + public.txt (uygulamaya gömülecek açık anahtar).
//
//   vmon-lic issue --key <private.vmonkey> --pass <parola>
//                  --edition basic|standard|enterprise --company "Firma A.Ş." [--days 365]
//       İmzalı lisans key üretir: VMON1.<payload>.<imza>
//       KURAL: lisanslar YILLIK — --days üst sınırı 365'tir (one-time/süresiz lisans yok).
//
// Özel anahtar makineye değil DOSYAYA bağlıdır: private.vmonkey + parolayı taşıyan
// herkes lisans kesebilir. İki ayrı USB'de yedekleyin; asla repo/buluta koymayın.
// ============================================================================

return args.Length > 0 && args[0].ToLowerInvariant() switch
{
    "new" => CmdNew(args),
    "issue" => CmdIssue(args),
    _ => Usage()
} is int rc ? rc : 1;

static int Usage()
{
    Console.WriteLine("""
        vmon-lic — vMon lisans araci

        Komutlar:
          new   --out <klasor> --pass <parola>
          issue --key <private.vmonkey> --pass <parola>
                --edition basic|standard|enterprise --company "Firma" [--days 365]
        """);
    return 1;
}

static string? Arg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i].Equals("--" + name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static int CmdNew(string[] args)
{
    var outDir = Arg(args, "out") ?? ".";
    var pass = Arg(args, "pass");
    if (string.IsNullOrWhiteSpace(pass)) { Console.Error.WriteLine("HATA: --pass zorunlu."); return 1; }
    Directory.CreateDirectory(outDir);

    using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var priv = ecdsa.ExportPkcs8PrivateKey();
    var pub = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

    // Özel anahtarı parola ile şifrele: PBKDF2(SHA256, 200k) → AES-256-CBC
    var salt = RandomNumberGenerator.GetBytes(16);
    using var kdf = new Rfc2898DeriveBytes(pass, salt, 200_000, HashAlgorithmName.SHA256);
    using var aes = Aes.Create();
    aes.Key = kdf.GetBytes(32);
    aes.GenerateIV();
    var cipher = aes.CreateEncryptor().TransformFinalBlock(priv, 0, priv.Length);

    var keyFile = Path.Combine(outDir, "private.vmonkey");
    File.WriteAllText(keyFile, JsonSerializer.Serialize(new
    {
        v = 1,
        salt = Convert.ToBase64String(salt),
        iv = Convert.ToBase64String(aes.IV),
        data = Convert.ToBase64String(cipher)
    }, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(outDir, "public.txt"), pub);

    Console.WriteLine($"Ozel anahtar : {keyFile}   (PAROLA OLMADAN ISE YARAMAZ — ikisini ayri saklayin)");
    Console.WriteLine($"Acik anahtar : {Path.Combine(outDir, "public.txt")}");
    Console.WriteLine();
    Console.WriteLine("Uygulamaya gomulecek acik anahtar (LicenseService.PublicKeyB64):");
    Console.WriteLine(pub);
    return 0;
}

static ECDsa LoadPrivate(string keyFile, string pass)
{
    var j = JsonDocument.Parse(File.ReadAllText(keyFile)).RootElement;
    var salt = Convert.FromBase64String(j.GetProperty("salt").GetString()!);
    var iv = Convert.FromBase64String(j.GetProperty("iv").GetString()!);
    var data = Convert.FromBase64String(j.GetProperty("data").GetString()!);
    using var kdf = new Rfc2898DeriveBytes(pass, salt, 200_000, HashAlgorithmName.SHA256);
    using var aes = Aes.Create();
    aes.Key = kdf.GetBytes(32);
    aes.IV = iv;
    byte[] priv;
    try { priv = aes.CreateDecryptor().TransformFinalBlock(data, 0, data.Length); }
    catch (CryptographicException) { throw new Exception("Parola yanlis (ozel anahtar cozulemedi)."); }
    var ecdsa = ECDsa.Create();
    ecdsa.ImportPkcs8PrivateKey(priv, out _);
    return ecdsa;
}

static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static int CmdIssue(string[] args)
{
    var keyFile = Arg(args, "key");
    var pass = Arg(args, "pass");
    var edition = (Arg(args, "edition") ?? "").Trim().ToLowerInvariant();
    var company = Arg(args, "company") ?? "";
    var daysStr = Arg(args, "days") ?? "365";

    if (string.IsNullOrWhiteSpace(keyFile) || !File.Exists(keyFile)) { Console.Error.WriteLine("HATA: --key <private.vmonkey> bulunamadi."); return 1; }
    if (string.IsNullOrWhiteSpace(pass)) { Console.Error.WriteLine("HATA: --pass zorunlu."); return 1; }
    if (edition is not ("basic" or "standard" or "enterprise")) { Console.Error.WriteLine("HATA: --edition basic|standard|enterprise olmali."); return 1; }
    if (string.IsNullOrWhiteSpace(company)) { Console.Error.WriteLine("HATA: --company zorunlu."); return 1; }
    if (!int.TryParse(daysStr, out var days) || days < 1) { Console.Error.WriteLine("HATA: --days pozitif sayi olmali."); return 1; }
    if (days > 365)
    {
        // KESIN KURAL: lisanslar yillik yenilenir — one-time/suresiz lisans YOK.
        Console.Error.WriteLine("HATA: --days en fazla 365 olabilir (lisanslar YILLIK yenilenir).");
        return 1;
    }

    var ed = char.ToUpperInvariant(edition[0]) + edition[1..];   // Basic / Standard / Enterprise
    var payload = JsonSerializer.SerializeToUtf8Bytes(new
    {
        ed,
        co = company,
        iat = DateTime.Today.ToString("yyyy-MM-dd"),
        exp = DateTime.Today.AddDays(days).ToString("yyyy-MM-dd")
    });

    using var ecdsa = LoadPrivate(keyFile, pass);
    var sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
    // Ayraç '.' — base64url alfabesinde ('-','_') bulunmadığı için güvenle bölünebilir (JWT deseni)
    var key = $"VMON1.{B64Url(payload)}.{B64Url(sig)}";

    Console.WriteLine($"Paket    : {ed}");
    Console.WriteLine($"Firma    : {company}");
    Console.WriteLine($"Bitis    : {DateTime.Today.AddDays(days):yyyy-MM-dd}  ({days} gun)");
    Console.WriteLine();
    Console.WriteLine(key);
    return 0;
}
