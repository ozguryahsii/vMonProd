using System.ComponentModel.DataAnnotations;

namespace vMonitor.Models;

/// <summary>Zamanlanmış görev KOŞU GEÇMİŞİ — vMon'un KENDİ veritabanında birikir.
/// Kaynak sistem geçmiş tutmasa bile (Windows Task, systemd, MySQL Event) her kontrolde
/// algılanan YENİ koşu buraya yazılır; detay çekmecesindeki geçmiş tablosu buradan okur.
/// Saklama: HistoryRetentionDays (günlük temizlikte purge edilir).</summary>
public class JobRunHistory
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    [MaxLength(300)] public string JobName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    /// <summary>Koşu süresi; kaynak bildirmiyorsa (Windows Task, MySQL Event) null kalır.</summary>
    public int? DurationSec { get; set; }
    [MaxLength(10)] public string Status { get; set; } = "ok"; // ok / fail
    [MaxLength(500)] public string? Info { get; set; }
}

/// <summary>Kontrol anında checker'ın gördüğü tek görev koşusu (DB'ye YAZILMAZ —
/// CheckRunner geçmişe yeni koşu ekleme/güncelleme kararını bununla verir).</summary>
public sealed record JobRunSnapshot(string JobName, DateTime? StartedAt, int? DurSec, bool Failed, string? FailText);
