using System.ComponentModel.DataAnnotations;

namespace vMonitor.Models;

public enum ServiceType
{
    Http = 0,
    Tcp = 1,
    MySql = 2,
    MsSql = 3,
    Oracle = 4,
    Ldap = 5,
    Dns = 6,
    Sftp = 7,
    DhcpWindowsService = 8,
    Smtp = 9,
    Imap = 10,
    Ping = 11,
    WindowsHealth = 12,
    LinuxHealth = 13,
    WindowsServiceControl = 14,
    LinuxServiceControl = 15
}

/// <summary>Bir kontrolün sonucu: Up=sorunsuz, Down=ulaşılamıyor/bağlantı hatası,
/// Error=ulaşıldı ama eşik aşıldı/uyarı (kesinti değil).</summary>
public enum CheckStatus
{
    Up = 0,
    Down = 1,
    Error = 2
}

public class MonitoredService
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public ServiceType Type { get; set; }

    /// <summary>Host adı / IP. HTTP tipinde tam URL buraya yazılır.</summary>
    [Required, MaxLength(500)]
    public string Target { get; set; } = "";

    public int? Port { get; set; }

    /// <summary>Tipine göre ekstra ayar: Oracle service name, DNS test hostname,
    /// MSSQL/MySQL veritabanı adı, DHCP Windows servis adı, HTTP beklenen durum kodu vb.</summary>
    [MaxLength(500)]
    public string? Extra { get; set; }

    /// <summary>LDAPS / HTTPS gibi durumlarda SSL kullan.</summary>
    public bool UseSsl { get; set; }

    /// <summary>Sertifika hatalarını yoksay (iç CA'lar için). Varsayılan KAPALI — doğrulama açık
    /// (PCI DSS 4.2.1, NIST SC-8). Yalnızca bilinçli olarak gerekirse açılır.</summary>
    public bool IgnoreCertErrors { get; set; } = false;

    public int? CredentialId { get; set; }
    public Credential? Credential { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Serbest etiket(ler) — virgülle ayrılmış birden çok olabilir (örn. "uretim, kritik").
    /// Raporlarda gösterilir, dashboard ve rapor filtrelerinde tek tek eşleşir.</summary>
    [MaxLength(300)]
    public string? Keyword { get; set; }

    /// <summary>Serbest açıklama — sunucuda ne çalıştığı vb. Dashboard kartlarında ve raporlarda gösterilir.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    // --- Alarm kanalları (servis bazlı aç/kapa) ---
    /// <summary>E-posta alarmı (varsayılan açık — mevcut davranış korunur).</summary>
    public bool AlertMail { get; set; } = true;
    /// <summary>SMS alarmı.</summary>
    public bool AlertSms { get; set; } = false;
    /// <summary>WhatsApp alarmı (ileride; şimdilik yalnızca işaret).</summary>
    public bool AlertWhatsapp { get; set; } = false;
    /// <summary>Sesli arama alarmı (ileride; şimdilik yalnızca işaret).</summary>
    public bool AlertCall { get; set; } = false;

    /// <summary>Keyword alanını ayrı ayrı etiketlere böler (virgül ayraçlı, tekrarsız).</summary>
    public static List<string> SplitKeywords(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword)
            ? new List<string>()
            : keyword.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Dakika cinsinden servis bazlı kontrol aralığı. Boşsa global ayar geçerli.</summary>
    public int? IntervalMinutesOverride { get; set; }

    /// <summary>Yanıt süresi bu eşiği (ms) aşarsa uyarı olarak işaretle. Boşsa kontrol yok.</summary>
    public int? ResponseTimeThresholdMs { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    // --- Sunucu sağlığı (WindowsHealth/LinuxHealth) eşikleri: aşılırsa DOWN sayılır ---
    public int? CpuThresholdPercent { get; set; }
    public int? RamThresholdPercent { get; set; }
    public int? DiskThresholdPercent { get; set; }

    // --- Denormalize anlık durum (dashboard için) ---
    public DateTime? LastCheckedAt { get; set; }
    public bool? LastIsUp { get; set; }
    /// <summary>Son kontrol durumu: 0=Up, 1=Down, 2=Error. (LastIsUp ile uyumlu tutulur.)</summary>
    public int LastStatus { get; set; }
    public long? LastResponseTimeMs { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public bool DownAlertSent { get; set; }

    // --- Son sağlık metrikleri (sağlık tipleri için, dashboard kartında gösterilir) ---
    public double? LastCpuPercent { get; set; }
    public double? LastRamPercent { get; set; }
    public double? LastMaxDiskPercent { get; set; }

    /// <summary>Donanım kapasitesi, örn. "8 CPU · 16 GB RAM · C: 237 GB". Sağlık kontrolünde güncellenir.</summary>
    public string? CapacityInfo { get; set; }
}
