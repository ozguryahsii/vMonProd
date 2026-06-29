using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Services;

public class MonitorSettings
{
    public int CheckIntervalMinutes { get; set; } = 5;
    public int FailureThreshold { get; set; } = 2;       // bu kadar ardışık hata sonrası mail
    public int HistoryRetentionDays { get; set; } = 365;

    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 25;
    public string MailFrom { get; set; } = "vmon@localhost";
    public string MailRecipients { get; set; } = "";      // virgül/noktalı virgül ile ayrılmış
    public bool EmailEnabled { get; set; } = false;

    // --- LDAP ile oturum açma (login) ---
    public bool AuthEnabled { get; set; } = false;
    public string LdapAuthHost { get; set; } = "";
    public int LdapAuthPort { get; set; } = 389;
    public bool LdapAuthUseSsl { get; set; } = false;
    /// <summary>NETBIOS domain veya UPN soneki (örn. SIRKET veya sirket.local).</summary>
    public string LdapAuthDomain { get; set; } = "";
    /// <summary>Kullanıcı aramasının yapılacağı temel DN (örn. DC=sirket,DC=local).</summary>
    public string LdapAuthBaseDn { get; set; } = "";
    /// <summary>Yalnızca bu güvenlik grubunun üyeleri giriş yapabilir (tam DN).</summary>
    public string LdapAuthGroupDn { get; set; } = "";

    /// <summary>Ayarlar sekmesine erişebilen admin kullanıcı adları (sAMAccountName),
    /// virgülle ayrılmış. Boşsa: giriş yapan herkes admindir (ilk kurulum kolaylığı).</summary>
    public string AdminUsers { get; set; } = "";

    /// <summary>Giriş ekranı logosu — Data klasöründeki dosya adı (admin yükler). Boşsa logo gösterilmez.</summary>
    public string LoginLogoFile { get; set; } = "";

    /// <summary>Müşteri/şirket adı (kurulum sihirbazında girilir; markalama/başlıkta kullanılır).</summary>
    public string CompanyName { get; set; } = "";

    /// <summary>İki faktörlü giriş (OTP) zorunlu mu? Açıkken şifreden sonra tek kullanımlık kod istenir.</summary>
    public bool OtpEnabled { get; set; } = false;
    /// <summary>OTP kodunun gönderileceği kanal: "Email" / "Sms" / "Whatsapp".</summary>
    public string OtpChannel { get; set; } = "Email";

    // --- Yedekleme (yalnızca SQLite) ---
    /// <summary>Zamanlanmış otomatik yedek açık mı?</summary>
    public bool BackupEnabled { get; set; } = false;
    /// <summary>Yedeklerin yazılacağı sunucu-tarafı klasör (örn. D:\vMon-Backups).</summary>
    public string BackupPath { get; set; } = "";
    /// <summary>Günlük otomatik yedek saati (0-23).</summary>
    public int BackupHour { get; set; } = 2;
    /// <summary>Günlük otomatik yedek dakikası (0-59).</summary>
    public int BackupMinute { get; set; } = 0;
    /// <summary>Saklanacak yedek sayısı (eskiler silinir). 0 = sınırsız.</summary>
    public int BackupRetentionCount { get; set; } = 14;

    /// <summary>Kullanıcı senkronizasyonunda kullanılacak kimlik bilgisi (Kimlik Bilgileri ekranından, Vault destekli olabilir).</summary>
    public int? LdapSyncCredentialId { get; set; }

    // --- Güvenlik / uyumluluk ayarları (PCI DSS / ISO 27001 / NIST) ---

    /// <summary>Vault ve LDAPS gibi giden bağlantılarda iç kurum CA'sıyla imzalı sertifikalara güven
    /// (sertifika zinciri doğrulamasını gevşetir). Varsayılan KAPALI = tam doğrulama (PCI DSS 4.2.1, NIST SC-8).
    /// Yalnızca kurum içi sertifikalar genel güven deposunda değilse ve bilinçli olarak açılır.</summary>
    public bool TrustInternalTlsCertificates { get; set; } = false;

    /// <summary>Hesap kilitleme eşiği: bu kadar ardışık başarısız girişten sonra kilitlenir
    /// (PCI DSS 8.3.4: en çok 10).</summary>
    public int MaxLoginAttempts { get; set; } = 10;

    /// <summary>Kilit süresi (dakika) — PCI DSS 8.3.4: en az 30.</summary>
    public int LockoutMinutes { get; set; } = 30;

    /// <summary>Denetim kaydı saklama süresi (gün) — PCI DSS 10.5.1: en az 365.</summary>
    public int AuditRetentionDays { get; set; } = 365;

    // --- SMS bildirimi (Faz 1: tek yön) ---
    /// <summary>SMS bildirimi genel anahtarı. Kapalıyken hiçbir servis SMS göndermez.</summary>
    public bool SmsEnabled { get; set; } = false;
    /// <summary>SMS sağlayıcısı (şimdilik "Twilio").</summary>
    public string SmsProvider { get; set; } = "Twilio";
    /// <summary>Twilio Account SID.</summary>
    public string SmsAccountSid { get; set; } = "";
    /// <summary>Twilio Auth Token — DPAPI ile şifreli saklanır.</summary>
    public string SmsAuthTokenEncrypted { get; set; } = "";
    /// <summary>Gönderen numara/başlık (Twilio From).</summary>
    public string SmsFrom { get; set; } = "";
    /// <summary>Varsayılan SMS alıcıları (E.164, virgül/noktalı virgül ile ayrılmış). örn. +905321234567</summary>
    public string SmsRecipients { get; set; } = "";

    // --- WhatsApp bildirimi (Faz 3: tek yön, Twilio) ---
    /// <summary>WhatsApp bildirimi genel anahtarı.</summary>
    public bool WhatsappEnabled { get; set; } = false;
    /// <summary>Twilio Account SID (WhatsApp). SMS ile aynı Twilio hesabıysa aynısını girin.</summary>
    public string WhatsappAccountSid { get; set; } = "";
    /// <summary>Twilio Auth Token (WhatsApp) — DPAPI ile şifreli.</summary>
    public string WhatsappAuthTokenEncrypted { get; set; } = "";
    /// <summary>Gönderen WhatsApp numarası (E.164; "whatsapp:" öneki otomatik eklenir).</summary>
    public string WhatsappFrom { get; set; } = "";
    /// <summary>Varsayılan WhatsApp alıcıları (E.164, virgül/noktalı virgül ile ayrılmış).</summary>
    public string WhatsappRecipients { get; set; } = "";
    /// <summary>Alarm şablonu Content SID (Twilio Content Template — {{1}}..{{4}} + butonlar). Boşsa serbest metin gönderilir.</summary>
    public string WhatsappAlarmTemplateSid { get; set; } = "";
    /// <summary>Gelen buton webhook'unu doğrulamak için gizli anahtar (URL'de ?key=... olarak verilir).</summary>
    public string WhatsappWebhookSecret { get; set; } = "";

    /// <summary>Yerleşik Twilio SMS/WhatsApp ayarları, "Bildirim Kanalları" entegrasyonlarına bir kez taşındı mı?
    /// (true ise tekrar oluşturulmaz.)</summary>
    public bool TwilioChannelsMigrated { get; set; } = false;

    // --- Mutabakat (envanter karşılaştırma) modülü ---
    /// <summary>Mutabakat sekmesi açık mı? Kapalıyken kimseye görünmez.</summary>
    public bool MutabakatEnabled { get; set; } = false;
    /// <summary>Mutabakatta "bizim firma" adı (görüntülenir).</summary>
    public string MutabakatOwnCompany { get; set; } = "";
    /// <summary>Mutabakatta "hizmet aldığımız firma" adı (görüntülenir).</summary>
    public string MutabakatVendorCompany { get; set; } = "";

    /// <summary>Verilen kullanıcı adı admin mi? Liste boşsa herkes admin sayılır.</summary>
    public bool IsAdmin(string? sam)
    {
        var list = (AdminUsers ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (list.Length == 0) return true;
        return !string.IsNullOrWhiteSpace(sam) &&
               list.Contains(sam, StringComparer.OrdinalIgnoreCase);
    }
}

public class SettingsService
{
    private readonly AppDbContext _db;
    public SettingsService(AppDbContext db) => _db = db;

    public async Task<MonitorSettings> GetAsync(CancellationToken ct = default)
    {
        // DB henüz hazır değilse (kurulum modu / erişilemez) varsayılan ayarlarla dön — istek anında 500 verme.
        Dictionary<string, string?> dict;
        try { dict = await _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct); }
        catch { return new MonitorSettings(); }
        var s = new MonitorSettings();
        if (dict.TryGetValue("CheckIntervalMinutes", out var v) && int.TryParse(v, out var i) && i > 0) s.CheckIntervalMinutes = i;
        if (dict.TryGetValue("FailureThreshold", out v) && int.TryParse(v, out i) && i > 0) s.FailureThreshold = i;
        if (dict.TryGetValue("HistoryRetentionDays", out v) && int.TryParse(v, out i) && i > 0) s.HistoryRetentionDays = i;
        if (dict.TryGetValue("SmtpHost", out v)) s.SmtpHost = v ?? "";
        if (dict.TryGetValue("SmtpPort", out v) && int.TryParse(v, out i) && i > 0) s.SmtpPort = i;
        if (dict.TryGetValue("MailFrom", out v) && !string.IsNullOrWhiteSpace(v)) s.MailFrom = v;
        if (dict.TryGetValue("MailRecipients", out v)) s.MailRecipients = v ?? "";
        if (dict.TryGetValue("EmailEnabled", out v)) s.EmailEnabled = v == "true";

        if (dict.TryGetValue("AuthEnabled", out v)) s.AuthEnabled = v == "true";
        if (dict.TryGetValue("LdapAuthHost", out v)) s.LdapAuthHost = v ?? "";
        if (dict.TryGetValue("LdapAuthPort", out v) && int.TryParse(v, out i) && i > 0) s.LdapAuthPort = i;
        if (dict.TryGetValue("LdapAuthUseSsl", out v)) s.LdapAuthUseSsl = v == "true";
        if (dict.TryGetValue("LdapAuthDomain", out v)) s.LdapAuthDomain = v ?? "";
        if (dict.TryGetValue("LdapAuthBaseDn", out v)) s.LdapAuthBaseDn = v ?? "";
        if (dict.TryGetValue("LdapAuthGroupDn", out v)) s.LdapAuthGroupDn = v ?? "";
        if (dict.TryGetValue("AdminUsers", out v)) s.AdminUsers = v ?? "";
        if (dict.TryGetValue("LoginLogoFile", out v)) s.LoginLogoFile = v ?? "";
        if (dict.TryGetValue("CompanyName", out v)) s.CompanyName = v ?? "";
        if (dict.TryGetValue("OtpEnabled", out v)) s.OtpEnabled = v == "true";
        if (dict.TryGetValue("OtpChannel", out v) && !string.IsNullOrWhiteSpace(v)) s.OtpChannel = v;
        if (dict.TryGetValue("BackupEnabled", out v)) s.BackupEnabled = v == "true";
        if (dict.TryGetValue("BackupPath", out v)) s.BackupPath = v ?? "";
        if (dict.TryGetValue("BackupHour", out v) && int.TryParse(v, out var bh)) s.BackupHour = Math.Clamp(bh, 0, 23);
        if (dict.TryGetValue("BackupMinute", out v) && int.TryParse(v, out var bm)) s.BackupMinute = Math.Clamp(bm, 0, 59);
        if (dict.TryGetValue("BackupRetentionCount", out v) && int.TryParse(v, out var br)) s.BackupRetentionCount = Math.Max(0, br);
        if (dict.TryGetValue("LdapSyncCredentialId", out v) && int.TryParse(v, out var ci) && ci > 0) s.LdapSyncCredentialId = ci;
        if (dict.TryGetValue("TrustInternalTlsCertificates", out v)) s.TrustInternalTlsCertificates = v == "true";
        if (dict.TryGetValue("MaxLoginAttempts", out v) && int.TryParse(v, out i) && i > 0) s.MaxLoginAttempts = Math.Min(i, 10);
        if (dict.TryGetValue("LockoutMinutes", out v) && int.TryParse(v, out i) && i > 0) s.LockoutMinutes = Math.Max(i, 30);
        if (dict.TryGetValue("AuditRetentionDays", out v) && int.TryParse(v, out i) && i > 0) s.AuditRetentionDays = Math.Max(i, 365);
        if (dict.TryGetValue("SmsEnabled", out v)) s.SmsEnabled = v == "true";
        if (dict.TryGetValue("SmsProvider", out v) && !string.IsNullOrWhiteSpace(v)) s.SmsProvider = v;
        if (dict.TryGetValue("SmsAccountSid", out v)) s.SmsAccountSid = v ?? "";
        if (dict.TryGetValue("SmsAuthTokenEncrypted", out v)) s.SmsAuthTokenEncrypted = v ?? "";
        if (dict.TryGetValue("SmsFrom", out v)) s.SmsFrom = v ?? "";
        if (dict.TryGetValue("SmsRecipients", out v)) s.SmsRecipients = v ?? "";
        if (dict.TryGetValue("WhatsappEnabled", out v)) s.WhatsappEnabled = v == "true";
        if (dict.TryGetValue("WhatsappAccountSid", out v)) s.WhatsappAccountSid = v ?? "";
        if (dict.TryGetValue("WhatsappAuthTokenEncrypted", out v)) s.WhatsappAuthTokenEncrypted = v ?? "";
        if (dict.TryGetValue("WhatsappFrom", out v)) s.WhatsappFrom = v ?? "";
        if (dict.TryGetValue("WhatsappRecipients", out v)) s.WhatsappRecipients = v ?? "";
        if (dict.TryGetValue("WhatsappAlarmTemplateSid", out v)) s.WhatsappAlarmTemplateSid = v ?? "";
        if (dict.TryGetValue("WhatsappWebhookSecret", out v)) s.WhatsappWebhookSecret = v ?? "";
        if (dict.TryGetValue("TwilioChannelsMigrated", out v)) s.TwilioChannelsMigrated = v == "true";
        if (dict.TryGetValue("MutabakatEnabled", out v)) s.MutabakatEnabled = v == "true";
        if (dict.TryGetValue("MutabakatOwnCompany", out v)) s.MutabakatOwnCompany = v ?? "";
        if (dict.TryGetValue("MutabakatVendorCompany", out v)) s.MutabakatVendorCompany = v ?? "";
        return s;
    }

    public async Task SaveAsync(MonitorSettings s, CancellationToken ct = default)
    {
        var pairs = new Dictionary<string, string?>
        {
            ["CheckIntervalMinutes"] = s.CheckIntervalMinutes.ToString(),
            ["FailureThreshold"] = s.FailureThreshold.ToString(),
            ["HistoryRetentionDays"] = s.HistoryRetentionDays.ToString(),
            ["SmtpHost"] = s.SmtpHost,
            ["SmtpPort"] = s.SmtpPort.ToString(),
            ["MailFrom"] = s.MailFrom,
            ["MailRecipients"] = s.MailRecipients,
            ["EmailEnabled"] = s.EmailEnabled ? "true" : "false",
            ["AuthEnabled"] = s.AuthEnabled ? "true" : "false",
            ["LdapAuthHost"] = s.LdapAuthHost,
            ["LdapAuthPort"] = s.LdapAuthPort.ToString(),
            ["LdapAuthUseSsl"] = s.LdapAuthUseSsl ? "true" : "false",
            ["LdapAuthDomain"] = s.LdapAuthDomain,
            ["LdapAuthBaseDn"] = s.LdapAuthBaseDn,
            ["LdapAuthGroupDn"] = s.LdapAuthGroupDn,
            ["AdminUsers"] = s.AdminUsers,
            ["LoginLogoFile"] = s.LoginLogoFile,
            ["CompanyName"] = s.CompanyName,
            ["OtpEnabled"] = s.OtpEnabled ? "true" : "false",
            ["OtpChannel"] = s.OtpChannel,
            ["BackupEnabled"] = s.BackupEnabled ? "true" : "false",
            ["BackupPath"] = s.BackupPath,
            ["BackupHour"] = s.BackupHour.ToString(),
            ["BackupMinute"] = s.BackupMinute.ToString(),
            ["BackupRetentionCount"] = s.BackupRetentionCount.ToString(),
            ["LdapSyncCredentialId"] = s.LdapSyncCredentialId?.ToString() ?? "",
            ["TrustInternalTlsCertificates"] = s.TrustInternalTlsCertificates ? "true" : "false",
            ["MaxLoginAttempts"] = s.MaxLoginAttempts.ToString(),
            ["LockoutMinutes"] = s.LockoutMinutes.ToString(),
            ["AuditRetentionDays"] = s.AuditRetentionDays.ToString(),
            ["SmsEnabled"] = s.SmsEnabled ? "true" : "false",
            ["SmsProvider"] = s.SmsProvider,
            ["SmsAccountSid"] = s.SmsAccountSid,
            ["SmsAuthTokenEncrypted"] = s.SmsAuthTokenEncrypted,
            ["SmsFrom"] = s.SmsFrom,
            ["SmsRecipients"] = s.SmsRecipients,
            ["WhatsappEnabled"] = s.WhatsappEnabled ? "true" : "false",
            ["WhatsappAccountSid"] = s.WhatsappAccountSid,
            ["WhatsappAuthTokenEncrypted"] = s.WhatsappAuthTokenEncrypted,
            ["WhatsappFrom"] = s.WhatsappFrom,
            ["WhatsappRecipients"] = s.WhatsappRecipients,
            ["WhatsappAlarmTemplateSid"] = s.WhatsappAlarmTemplateSid,
            ["WhatsappWebhookSecret"] = s.WhatsappWebhookSecret,
            ["TwilioChannelsMigrated"] = s.TwilioChannelsMigrated ? "true" : "false",
            ["MutabakatEnabled"] = s.MutabakatEnabled ? "true" : "false",
            ["MutabakatOwnCompany"] = s.MutabakatOwnCompany,
            ["MutabakatVendorCompany"] = s.MutabakatVendorCompany
        };

        foreach (var (key, value) in pairs)
        {
            var existing = await _db.Settings.FindAsync(new object[] { key }, ct);
            if (existing == null) _db.Settings.Add(new AppSetting { Key = key, Value = value });
            else existing.Value = value;
        }
        await _db.SaveChangesAsync(ct);
    }
}
