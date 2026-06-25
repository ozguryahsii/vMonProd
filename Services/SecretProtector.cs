namespace vMonitor.Services;

/// <summary>Sır şifreleme soyutlaması. Şu an Windows DPAPI (CryptoHelper) ile; ileride Linux/cloud için
/// AES + harici anahtar (KMS/Vault) uygulaması eklenebilir. Bootstrap bağlantı sırrı + uygulama sırları buradan.</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string encrypted);
}

/// <summary>Varsayılan: Windows DPAPI (LocalMachine). Mevcut CryptoHelper'ı sarar.</summary>
public class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plain) => string.IsNullOrEmpty(plain) ? "" : CryptoHelper.Encrypt(plain);
    public string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        try { return CryptoHelper.Decrypt(encrypted); } catch { return ""; }
    }
}
