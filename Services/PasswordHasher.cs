using System.Security.Cryptography;

namespace vMonitor.Services;

/// <summary>Yerel kullanıcı şifreleri için PBKDF2 (SHA-256, 100k tur, 16-bayt salt). Düz şifre asla saklanmaz.
/// Biçim: pbkdf2.sha256.{tur}.{saltB64}.{hashB64}</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2.sha256.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>Parola politikasını doğrular (PCI 8.3.6 / NIST 800-63B). (geçerli, hata mesajı) döner.</summary>
    public static (bool ok, string? error) ValidatePolicy(string? password, int minLength, bool requireComplexity)
    {
        password ??= "";
        if (password.Length < minLength)
            return (false, $"Parola en az {minLength} karakter olmalı.");
        if (requireComplexity)
        {
            bool upper = password.Any(char.IsUpper), lower = password.Any(char.IsLower), digit = password.Any(char.IsDigit);
            if (!(upper && lower && digit))
                return (false, "Parola en az bir büyük harf, bir küçük harf ve bir rakam içermeli.");
        }
        return (true, null);
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        try
        {
            var p = stored.Split('.');
            if (p.Length != 5 || p[0] != "pbkdf2") return false;
            int iter = int.Parse(p[2]);
            byte[] salt = Convert.FromBase64String(p[3]);
            byte[] hash = Convert.FromBase64String(p[4]);
            byte[] test = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, hash.Length);
            return CryptographicOperations.FixedTimeEquals(test, hash);
        }
        catch { return false; }
    }
}
