namespace vMonitor.Models;

/// <summary>Güvenlik denetim kaydı (append-only). Kimin, ne zaman, hangi IP'den,
/// hangi eylemi, hangi hedef üzerinde ve sonucuyla yaptığını tutar.
/// PCI DSS 10.2/10.3, ISO 27001 A.8.15, NIST AU-2/AU-3 gereği.
/// UI'dan değiştirilemez/silinemez; yalnızca saklama süresi dolanları arka plan temizler.</summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    /// <summary>Eylemi yapan kullanıcı (sAMAccountName) veya "sistem" / "anonim".</summary>
    public string User { get; set; } = "";
    public string? Ip { get; set; }
    /// <summary>Eylem türü (örn. login.success, settings.save, service.action, user.permissions).</summary>
    public string Action { get; set; } = "";
    /// <summary>Eylemin hedefi (örn. servis adı, kullanıcı adı, kimlik adı).</summary>
    public string? Target { get; set; }
    /// <summary>İnsan-okunur açıklama. Asla şifre/secret/token içermez.</summary>
    public string? Detail { get; set; }
    public bool Success { get; set; } = true;

    /// <summary>Bu kaydın değiştirilemezlik (tamper-evident) hash'i: SHA-256(öncekiHash + kanonik alanlar).
    /// PCI DSS 10.3.x, NIST AU-9, ISO 27001 A.8.15 — kayıtların sonradan değiştirilmediğini doğrulamak için.</summary>
    public string? Hash { get; set; }
    /// <summary>Bir önceki denetim kaydının Hash'i (hash-zinciri). İlk kayıtta/retention sonrası boş olabilir.</summary>
    public string? PrevHash { get; set; }
}
