using System.Security.Cryptography;
using System.Text;

namespace vMonitor.Services;

/// <summary>DPAPI (LocalMachine) ile şifre saklama. Şifreler yalnızca bu
/// sunucuda çözülebilir; DB başka makineye taşınırsa yeniden girilmeleri gerekir.</summary>
public static class CryptoHelper
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("vMonitor.v1");

    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(bytes);
    }

    public static string Decrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }
}
