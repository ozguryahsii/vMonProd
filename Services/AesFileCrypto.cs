using System.Security.Cryptography;
using System.Text;

namespace vMonitor.Services;

/// <summary>Yedek dosyalarını parolayla AES-256 şifreler/çözer (PCI 3.x/9.4.1). Dosya kopyalansa bile parola
/// olmadan açılamaz. Biçim: "VMENC1" + 16-bayt salt + 16-bayt IV + AES-CBC şifreli içerik. Anahtar PBKDF2-SHA256 (100k).</summary>
public static class AesFileCrypto
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VMENC1");

    public static void EncryptFile(string src, string dest, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        using (var kdf = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256))
            aes.Key = kdf.GetBytes(32);
        aes.GenerateIV();

        using var outFs = File.Create(dest);
        outFs.Write(Magic);
        outFs.Write(salt);
        outFs.Write(aes.IV);
        using var cs = new CryptoStream(outFs, aes.CreateEncryptor(), CryptoStreamMode.Write);
        using var inFs = File.OpenRead(src);
        inFs.CopyTo(cs);
    }

    public static void DecryptFile(string src, string dest, string password)
    {
        using var inFs = File.OpenRead(src);
        var magic = new byte[6]; inFs.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Geçersiz şifreli yedek dosyası.");
        var salt = new byte[16]; inFs.ReadExactly(salt);
        var iv = new byte[16]; inFs.ReadExactly(iv);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        using (var kdf = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256))
            aes.Key = kdf.GetBytes(32);
        aes.IV = iv;

        using var outFs = File.Create(dest);
        using var cs = new CryptoStream(inFs, aes.CreateDecryptor(), CryptoStreamMode.Read);
        cs.CopyTo(outFs);
    }

    public static bool IsEncrypted(string file) =>
        file.EndsWith(".enc", StringComparison.OrdinalIgnoreCase);
}
