using Microsoft.EntityFrameworkCore;

namespace vMonitor.Data;

/// <summary>EnsureCreated sonrası, ileriki sürümlerde eklenen kolonlar için
/// manuel ALTER TABLE adımları. Kolon zaten varsa hata yutulur (SLLTracker kalıbı).</summary>
public static class DbSchemaHelper
{
    public static void EnsureSchema(AppDbContext db, ILogger logger)
    {
        var alters = new[]
        {
            // v1 şeması EnsureCreated ile geliyor; ileride kolon eklenirse buraya yazılacak.
            // Örnek: "ALTER TABLE Services ADD COLUMN YeniKolon TEXT"
            "CREATE INDEX IF NOT EXISTS IX_CheckResults_CheckedAt ON CheckResults(CheckedAt)",
            // v2: çoklu dashboard tanımları — mevcut prod DB'lere EnsureCreated eklemediği için manuel
            @"CREATE TABLE IF NOT EXISTS Dashboards (
                Id INTEGER NOT NULL CONSTRAINT PK_Dashboards PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ServiceIdsCsv TEXT NULL,
                TypeFilter TEXT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0)",
            // v3: sunucu sağlığı izleme (CPU/RAM/Disk) — mevcut prod DB'ler için kolon + tablo
            "ALTER TABLE Services ADD COLUMN CpuThresholdPercent INTEGER NULL",
            "ALTER TABLE Services ADD COLUMN RamThresholdPercent INTEGER NULL",
            "ALTER TABLE Services ADD COLUMN DiskThresholdPercent INTEGER NULL",
            "ALTER TABLE Services ADD COLUMN LastCpuPercent REAL NULL",
            "ALTER TABLE Services ADD COLUMN LastRamPercent REAL NULL",
            "ALTER TABLE Services ADD COLUMN LastMaxDiskPercent REAL NULL",
            "ALTER TABLE Services ADD COLUMN CapacityInfo TEXT NULL",
            // v4: HashiCorp Vault entegrasyonu — kimlik bilgisi şifresi Vault'tan çekilebilir
            "ALTER TABLE Credentials ADD COLUMN SourceType INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Credentials ADD COLUMN VaultUrl TEXT NULL",
            "ALTER TABLE Credentials ADD COLUMN VaultTokenEncrypted TEXT NULL",
            "ALTER TABLE Credentials ADD COLUMN VaultKey TEXT NULL",
            "ALTER TABLE Credentials ADD COLUMN VaultUserKey TEXT NULL",
            // v5: servis keyword/etiket + dashboard keyword filtresi
            "ALTER TABLE Services ADD COLUMN Keyword TEXT NULL",
            "ALTER TABLE Dashboards ADD COLUMN KeywordFilter TEXT NULL",
            // v6: Down/Error ayrımı
            "ALTER TABLE Services ADD COLUMN LastStatus INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE CheckResults ADD COLUMN Status INTEGER NOT NULL DEFAULT 0",
            // v7: granüler kullanıcı yetkileri
            @"CREATE TABLE IF NOT EXISTS AppUsers (
                Id INTEGER NOT NULL CONSTRAINT PK_AppUsers PRIMARY KEY AUTOINCREMENT,
                Sam TEXT NOT NULL,
                DisplayName TEXT NULL,
                PermissionsCsv TEXT NOT NULL DEFAULT '',
                LastLogin TEXT NULL)",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AppUsers_Sam ON AppUsers(Sam)",
            @"CREATE TABLE IF NOT EXISTS HealthMetrics (
                Id INTEGER NOT NULL CONSTRAINT PK_HealthMetrics PRIMARY KEY AUTOINCREMENT,
                ServiceId INTEGER NOT NULL,
                CheckedAt TEXT NOT NULL,
                CpuPercent REAL NULL,
                RamPercent REAL NULL,
                MaxDiskPercent REAL NULL,
                DiskDetail TEXT NULL)",
            "CREATE INDEX IF NOT EXISTS IX_HealthMetrics_ServiceId_CheckedAt ON HealthMetrics(ServiceId, CheckedAt)",
            // v8: güvenlik denetim kaydı (PCI DSS 10.2/10.3, ISO 27001 A.8.15, NIST AU-2/AU-3)
            @"CREATE TABLE IF NOT EXISTS AuditLogs (
                Id INTEGER NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY AUTOINCREMENT,
                At TEXT NOT NULL,
                User TEXT NOT NULL DEFAULT '',
                Ip TEXT NULL,
                Action TEXT NOT NULL DEFAULT '',
                Target TEXT NULL,
                Detail TEXT NULL,
                Success INTEGER NOT NULL DEFAULT 1)",
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_At ON AuditLogs(At)",
            // v8: AD güvenlik grubundan düşen kullanıcıları pasifleştirme (PCI DSS 8.2.4-8.2.5, ISO A.5.18)
            "ALTER TABLE AppUsers ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1",
            // v9: servis açıklaması
            "ALTER TABLE Services ADD COLUMN Description TEXT NULL",
            // v10: servis bazlı alarm kanalları (mail varsayılan açık, diğerleri kapalı)
            "ALTER TABLE Services ADD COLUMN AlertMail INTEGER NOT NULL DEFAULT 1",
            "ALTER TABLE Services ADD COLUMN AlertSms INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Services ADD COLUMN AlertWhatsapp INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE Services ADD COLUMN AlertCall INTEGER NOT NULL DEFAULT 0",
            // v11: UI'dan tanımlanabilen genel HTTP SMS sağlayıcıları
            @"CREATE TABLE IF NOT EXISTS SmsProviders (
                Id INTEGER NOT NULL CONSTRAINT PK_SmsProviders PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL DEFAULT '',
                Method TEXT NOT NULL DEFAULT 'GET',
                Url TEXT NOT NULL DEFAULT '',
                ContentType TEXT NOT NULL DEFAULT 'form',
                Body TEXT NULL,
                Headers TEXT NULL,
                AuthType TEXT NOT NULL DEFAULT 'none',
                Username TEXT NOT NULL DEFAULT '',
                PasswordEncrypted TEXT NOT NULL DEFAULT '',
                ApiKeyEncrypted TEXT NOT NULL DEFAULT '',
                Sender TEXT NOT NULL DEFAULT '',
                SuccessContains TEXT NULL,
                Kind TEXT NOT NULL DEFAULT 'Sms',
                Recipients TEXT NULL,
                Enabled INTEGER NOT NULL DEFAULT 1)",
            // v12: interaktif alarm (WhatsApp/IVR) — kullanıcı telefonu + alarm oturumları
            "ALTER TABLE AppUsers ADD COLUMN Phone TEXT NULL",
            @"CREATE TABLE IF NOT EXISTS AlarmSessions (
                Id INTEGER NOT NULL CONSTRAINT PK_AlarmSessions PRIMARY KEY AUTOINCREMENT,
                ServiceId INTEGER NOT NULL,
                Phone TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                HandledAt TEXT NULL,
                Action TEXT NULL,
                Result TEXT NULL)",
            "CREATE INDEX IF NOT EXISTS IX_AlarmSessions_Phone ON AlarmSessions(Phone, CreatedAt)",
            // v13: bildirim kanallarını tek modelde birleştir — entegrasyon türü + kendi alıcıları
            "ALTER TABLE SmsProviders ADD COLUMN Kind TEXT NOT NULL DEFAULT 'Sms'",
            "ALTER TABLE SmsProviders ADD COLUMN Recipients TEXT NULL",
            // v14: kullanıcı bazlı tema + dil tercihi (sonraki girişte hatırlanır)
            "ALTER TABLE AppUsers ADD COLUMN Theme TEXT NOT NULL DEFAULT 'light'",
            "ALTER TABLE AppUsers ADD COLUMN Language TEXT NOT NULL DEFAULT 'tr'",
            // v15: WhatsApp entegrasyonuna onaylı şablon (Content SID) — butonlu interaktif alarm
            "ALTER TABLE SmsProviders ADD COLUMN TemplateSid TEXT NULL",
            // v16: "Voice" ve "Ivr" türleri birleştirildi → mevcut Voice kayıtlarını Ivr'a taşı (idempotent)
            "UPDATE SmsProviders SET Kind = 'Ivr' WHERE Kind = 'Voice'",
            // v9 veri düzeltmesi: eşik aşımı (CPU/RAM/Disk) kesinti DEĞİLDİR. Eski sürümlerde eşik aşımı
            // kesinti olarak kaydedilmiş olabilir; bunları temizle ve kontrolleri ERROR olarak işaretle.
            // (idempotent — her açılışta güvenle çalışır)
            "DELETE FROM Outages WHERE FirstError LIKE 'Eşik aşıldı%'",
            "UPDATE CheckResults SET Status = 2 WHERE Status <> 2 AND Error LIKE 'Eşik aşıldı%'"
        };

        foreach (var sql in alters)
        {
            try { db.Database.ExecuteSqlRaw(sql); }
            catch (Exception ex) { logger.LogDebug(ex, "Şema adımı atlandı: {Sql}", sql); }
        }
    }
}
