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
    LinuxServiceControl = 15,
    // Database Fazı: Oracle sağlık izlemeleri — sayısal sonuç (adet) ResponseTimeMs alanında saklanır,
    // YavaşlıkEşiği (ResponseTimeThresholdMs) bu sayıya eşik olarak uygulanabilir
    OracleSysdate = 16,          // SELECT SYSDATE FROM DUAL — bağlantı + gecikme + saat sapması
    OracleActiveSessions = 17,   // GV$SESSION: TYPE<>'BACKGROUND' AND STATUS='ACTIVE' adedi
    OracleBlockedSessions = 18,  // GV$SESSION: BLOCKING_SESSION IS NOT NULL adedi
    // DB İzleme Fazı 2: üç platformda simetrik metrik seti (Oracle tamamlama + MSSQL + MySQL)
    OracleLongQueries = 19,      // GV$SESSION: aktif + LAST_CALL_ET > 60 sn adedi
    OracleTablespaceStatus = 20, // DBA_TABLESPACES: STATUS='OFFLINE' adedi (>0 ERROR)
    OracleConnectionUsage = 21,  // V$RESOURCE_LIMIT processes doluluk yüzdesi
    MsSqlGetDate = 22,           // SELECT GETDATE() — bağlantı + gecikme + saat sapması
    MsSqlActiveSessions = 23,    // dm_exec_sessions: is_user_process=1 AND status='running' adedi
    MsSqlBlockedSessions = 24,   // dm_exec_requests: blocking_session_id<>0 adedi (>0 ERROR)
    MsSqlLongQueries = 25,       // dm_exec_requests: total_elapsed_time > 60 sn adedi
    MsSqlDbStatus = 26,          // sys.databases: state_desc<>'ONLINE' adedi (>0 ERROR)
    MsSqlConnectionUsage = 27,   // kullanıcı oturumu / user connections limiti yüzdesi
    MySqlNow = 28,               // SELECT NOW() — bağlantı + gecikme + saat sapması
    MySqlActiveSessions = 29,    // PROCESSLIST: COMMAND<>'Sleep' adedi
    MySqlBlockedSessions = 30,   // INNODB_TRX: trx_state='LOCK WAIT' adedi (>0 ERROR)
    MySqlLongQueries = 31,       // PROCESSLIST: COMMAND<>'Sleep' AND TIME>60 adedi
    MySqlReplication = 32,       // replika IO/SQL thread durumu + Seconds_Behind (sn)
    MySqlConnectionUsage = 33,   // PROCESSLIST / max_connections yüzdesi
    // Yol haritası (SLLTracker mirası): SSL sertifika izleme — iç/dış çift kontrol + thumbprint karşılaştırma
    SslCertificate = 34,         // kalan gün grafiğe; eşik gün/bitmiş/iç-dış farklı → ERROR
    // Zamanlanmış Görevler Fazı 1 (PULL): son koşu ne zaman/ne kadar sürdü/sonucu ne?
    // JobName = görev adı; MaxSilenceHours = "en geç şu aralıkta koşmalı" (yalnız bu tiplerde).
    OracleSchedulerJob = 35,     // DBA_SCHEDULER_JOB_RUN_DETAILS — süre (sn) grafiğe; FAILED → DOWN
    MsSqlAgentJob = 36,          // msdb sysjobs/sysjobhistory — süre (sn) grafiğe; başarısız → DOWN
    MySqlEventJob = 37,          // information_schema.EVENTS — sonuç/süre YOK; son koşudan bu yana (dk) grafiğe
    WindowsTaskJob = 38,         // Görev Zamanlayıcı (Schedule.Service COM) — son koşudan bu yana (dk) grafiğe; sonuç kodu ≠ 0 → DOWN
    SystemdTimerJob = 39         // systemd timer (SSH) — süre (sn) grafiğe; Result ≠ success → DOWN
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

    /// <summary>Login'siz herkese açık durum sayfasında (/durum) gösterilsin mi? (Opt-in — varsayılan kapalı.)
    /// Sayfada yalnızca izleme ADI ve durumu görünür; hedef/host/hata detayı asla sızmaz.</summary>
    public bool ShowOnStatusPage { get; set; } = false;

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

    // ---- SELF-HEALING (yalnız WindowsServiceControl/LinuxServiceControl) ----
    /// <summary>Down görülünce alarmdan ÖNCE otomatik yeniden başlatma dene.</summary>
    public bool SelfHealEnabled { get; set; } = false;
    /// <summary>Kaç ARDIŞIK down kontrolünden sonra iyileştirme başlasın (1-10).
    /// 1 = ilk down'da hemen dene; 2+ = false-positive koruması (tek seferlik dalgalanmada restart atılmaz).</summary>
    public int SelfHealAfterFailures { get; set; } = 1;
    /// <summary>Sorun döngüsü başına en fazla kaç yeniden başlatma denemesi yapılır (1-10).</summary>
    public int SelfHealMaxRetries { get; set; } = 1;
    /// <summary>Süregelen sorun döngüsünde harcanan deneme sayısı (UP olunca sıfırlanır).</summary>
    public int SelfHealAttemptsUsed { get; set; } = 0;
    /// <summary>Son otomatik iyileştirme müdahalesinin zamanı (UTC).</summary>
    public DateTime? LastSelfHealAt { get; set; }
    /// <summary>Son müdahale döngüsünde GERÇEKTE yapılan deneme sayısı — gösterim için kalıcı
    /// (SelfHealAttemptsUsed UP olunca sıfırlanır, bu alan silinmez).</summary>
    public int? LastSelfHealAttempts { get; set; }
    /// <summary>Son self-healing müdahalesi servisi ayağa kaldırdı mı?</summary>
    public bool? LastSelfHealOk { get; set; }

    /// <summary>Keyword alanını ayrı ayrı etiketlere böler (virgül ayraçlı, tekrarsız).</summary>
    public static List<string> SplitKeywords(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword)
            ? new List<string>()
            : keyword.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // ---- Zamanlanmış Görevler (yalnız job tiplerinde kullanılır; diğer tipleri ETKİLEMEZ) ----
    /// <summary>İzlenen görev(ler): ';' ile ayrılmış BİRDEN ÇOK görev olabilir — tek izleme bir ortamın
    /// job setini topluca izler (örn. "HR.JOB1;HR.JOB2"). Biçimler: Oracle "OWNER.JOB_NAME", MSSQL Agent
    /// job adı, MySQL "schema.event", Windows görev yolu "\Klasör\Ad", systemd timer birimi "ad.timer".</summary>
    [MaxLength(4000)]
    public string? JobName { get; set; }

    /// <summary>Sessizlik eşiği (saat): görev en geç bu aralıkta bir koşmuş olmalı; aşılırsa HATA üretilir.
    /// Boş = kontrol yok. Süre eşiğinden (ResponseTimeThresholdMs) BAĞIMSIZDIR.</summary>
    public int? MaxSilenceHours { get; set; }

    /// <summary>Son kontroldeki GÖREV-BAŞINA durumlar (dashboard job kartı mini kutuları):
    /// "ad|durum|değer" girişleri ';' ile ayrılır. durum: ok/fail/dis/sil/nf;
    /// değer: süre tiplerinde sn, diğerlerinde son koşudan bu yana dk (-1 = bilinmiyor).
    /// MaxLength YOK — her sağlayıcıda sınırsız metin kolonu (TEXT/nvarchar(max)/NCLOB) oluşur.</summary>
    public string? LastJobStates { get; set; }

    /// <summary>Kontrol anındaki görev koşuları — DB'ye MAPLENMEZ. Checker (JobCommon.Evaluate) doldurur,
    /// CheckRunner bunlardan JobRunHistory kayıtlarını üretir (koşu geçmişi vMon DB'sinde birikir).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<JobRunSnapshot>? PendingJobRuns { get; set; }

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

    /// <summary>Son sağlık kontrolündeki disk-başına doluluk: "mount|kullanılanGb|toplamGb|yüzde" (';' ayraçlı).
    /// Detay panelinde her disk kapasite + doluluk çubuğuyla gösterilir (Windows + Linux).</summary>
    [MaxLength(2000)]
    public string? LastDiskInfo { get; set; }

    // --- İstatistikler için yapısal son değerler (sağlık kontrolünde güncellenir) ---
    /// <summary>İşletim sistemi tam adı, örn. "Windows Server 2019 Standard (10.0.17763)" / "Ubuntu 22.04.3 LTS (kernel 5.15.0)".</summary>
    public string? OsName { get; set; }
    /// <summary>İşletim sistemi ailesi: "Windows" / "Linux". İstatistik gruplaması için.</summary>
    public string? OsKind { get; set; }
    public int? LastCpuCores { get; set; }
    public double? LastRamTotalGb { get; set; }
    public double? LastRamUsedGb { get; set; }
    public double? LastDiskTotalGb { get; set; }
    public double? LastDiskUsedGb { get; set; }
}
