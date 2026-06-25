using System.ComponentModel.DataAnnotations;

namespace vMonitor.Models;

public enum CredentialSource
{
    Manual = 0,
    Vault = 1
}

/// <summary>Servis testlerinde kullanılan yetkili kullanıcı tanımı.
/// Manuel şifre veya Vault token'ı DPAPI (LocalMachine) ile şifrelenmiş saklanır.</summary>
public class Credential
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    /// <summary>Manuel kimlikte zorunlu; Vault kimliğinde kullanıcı adı secret'tan çekilir.</summary>
    [MaxLength(300)]
    public string Username { get; set; } = "";

    /// <summary>DPAPI ile şifrelenmiş, Base64.</summary>
    public string PasswordEncrypted { get; set; } = "";

    /// <summary>Windows/AD hesapları için domain (opsiyonel).</summary>
    [MaxLength(200)]
    public string? Domain { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    // --- Şifre kaynağı: Manuel (PasswordEncrypted) veya HashiCorp Vault ---
    public CredentialSource SourceType { get; set; } = CredentialSource.Manual;

    /// <summary>Vault secret adresi, örn. vault-prod.firma.com/v1/vmon/data/secrets (KV v2).</summary>
    [MaxLength(1000)]
    public string? VaultUrl { get; set; }

    /// <summary>Vault token — DPAPI ile şifrelenmiş, Base64.</summary>
    public string? VaultTokenEncrypted { get; set; }

    /// <summary>Secret içindeki şifre alanının anahtar adı, örn. "password".</summary>
    [MaxLength(200)]
    public string? VaultKey { get; set; }

    /// <summary>Secret içindeki kullanıcı adı alanının anahtar adı, örn. "username".</summary>
    [MaxLength(200)]
    public string? VaultUserKey { get; set; }
}
