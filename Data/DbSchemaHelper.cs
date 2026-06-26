using Microsoft.EntityFrameworkCore;

namespace vMonitor.Data;

/// <summary>Yalnızca idempotent VERİ DÜZELTMELERİ (UPDATE/DELETE). Şema artık EnsureCreated (sıfır kurulum) +
/// SchemaSync (eksik kolon ekleme, tüm sağlayıcılar) ile yönetilir; eski körleme ALTER'lar kaldırıldı
/// (kolon zaten var olduğu için EF Error 20102 logluyordu). Bu adımlar SQLite legacy veride güvenle çalışır.</summary>
public static class DbSchemaHelper
{
    public static void EnsureSchema(AppDbContext db, ILogger logger)
    {
        var fixes = new[]
        {
            // "Voice" türü "Ivr" ile birleştirildi → eski Voice kayıtlarını taşı (idempotent; 0 satırda no-op)
            "UPDATE SmsProviders SET Kind = 'Ivr' WHERE Kind = 'Voice'",
            // Eşik aşımı (CPU/RAM/Disk) kesinti DEĞİLDİR — eski yanlış kayıtları temizle/ERROR işaretle (idempotent)
            "DELETE FROM Outages WHERE FirstError LIKE 'Eşik aşıldı%'",
            "UPDATE CheckResults SET Status = 2 WHERE Status <> 2 AND Error LIKE 'Eşik aşıldı%'"
        };

        foreach (var sql in fixes)
        {
            try { db.Database.ExecuteSqlRaw(sql); }
            catch (Exception ex) { logger.LogDebug(ex, "Veri düzeltmesi atlandı: {Sql}", sql); }
        }
    }
}
