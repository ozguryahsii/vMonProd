using Microsoft.EntityFrameworkCore;
using vMonitor.Data;

namespace vMonitor.Services;

/// <summary>Her dakika uyanır; kontrol aralığı dolan servisleri paralel kontrol eder.
/// Günde bir kez de eski geçmiş kayıtlarını temizler.</summary>
public class MonitoringBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonitoringBackgroundService> _logger;
    private DateTime _lastCleanup = DateTime.MinValue;
    private DateTime _lastScheduledBackup = DateTime.MinValue;

    public MonitoringBackgroundService(IServiceScopeFactory scopeFactory, ILogger<MonitoringBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Uygulama tam ayağa kalksın
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunDueChecksAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "İzleme döngüsünde hata"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunDueChecksAsync(CancellationToken ct)
    {
        List<int> dueIds;
        MonitorSettings settings;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            settings = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(ct);

            var now = DateTime.UtcNow;
            var services = await db.Services.AsNoTracking()
                .Where(s => s.Enabled)
                .Select(s => new { s.Id, s.LastCheckedAt, s.IntervalMinutesOverride })
                .ToListAsync(ct);

            dueIds = services
                .Where(s =>
                {
                    var interval = TimeSpan.FromMinutes(s.IntervalMinutesOverride ?? settings.CheckIntervalMinutes);
                    return s.LastCheckedAt == null || now - s.LastCheckedAt.Value >= interval;
                })
                .Select(s => s.Id)
                .ToList();
        }

        if (dueIds.Count > 0)
        {
            _logger.LogInformation("{Count} servis kontrol ediliyor", dueIds.Count);

            // Her servis kendi scope'unda (SQLite + DbContext thread-safe değil), en fazla 5 paralel
            using var throttle = new SemaphoreSlim(5);
            var tasks = dueIds.Select(async id =>
            {
                await throttle.WaitAsync(ct);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<CheckRunner>();
                    await runner.RunCheckAsync(id, settings, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Servis kontrolü başarısız (Id={Id})", id);
                }
                finally { throttle.Release(); }
            });
            await Task.WhenAll(tasks);
        }

        // Zamanlanmış yedek (yalnızca SQLite): yerel saatte günde bir kez, ayarlanan saat:dakika geçilince
        if (settings.BackupEnabled && !string.IsNullOrWhiteSpace(settings.BackupPath))
        {
            var nowLocal = DateTime.Now;
            var dueToday = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, settings.BackupHour, settings.BackupMinute, 0);
            if (nowLocal >= dueToday && _lastScheduledBackup.Date < nowLocal.Date)
            {
                _lastScheduledBackup = nowLocal;
                using var scope = _scopeFactory.CreateScope();
                var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
                var pwd = string.IsNullOrWhiteSpace(settings.BackupPasswordEncrypted) ? null : CryptoHelper.Decrypt(settings.BackupPasswordEncrypted);
                var (file, err) = await backup.BackupNowAsync(settings.BackupPath, settings.BackupRetentionCount, settings.BackupEncrypt, pwd, ct);
                if (err != null) _logger.LogError("Zamanlanmış yedek başarısız: {Err}", err);
                else _logger.LogInformation("Zamanlanmış yedek alındı: {File}", file);
            }
        }

        // EOL verisi: açıksa ve cache 7 günden eskiyse (veya yoksa) haftada bir tazele
        if (settings.EolEnabled)
        {
            using var scope = _scopeFactory.CreateScope();
            var eol = scope.ServiceProvider.GetRequiredService<EolService>();
            if (eol.SyncedAt == null || (DateTime.UtcNow - eol.SyncedAt.Value).TotalDays >= 7)
            {
                var (ok, msg) = await eol.SyncAsync(settings.EolProxyUrl, ct);
                if (ok) _logger.LogInformation("EOL verisi tazelendi: {Msg}", msg);
                else _logger.LogWarning("EOL tazeleme başarısız: {Msg}", msg);
            }
        }

        // Günlük temizlik
        if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromHours(24))
        {
            _lastCleanup = DateTime.UtcNow;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-settings.HistoryRetentionDays);
            var deleted = await db.CheckResults.Where(r => r.CheckedAt < cutoff).ExecuteDeleteAsync(ct);
            deleted += await db.Outages.Where(o => o.EndedAt != null && o.EndedAt < cutoff).ExecuteDeleteAsync(ct);
            deleted += await db.HealthMetrics.Where(m => m.CheckedAt < cutoff).ExecuteDeleteAsync(ct);
            deleted += await db.JobRunHistories.Where(h => h.StartedAt < cutoff).ExecuteDeleteAsync(ct);
            if (deleted > 0) _logger.LogInformation("{Count} eski kayıt temizlendi", deleted);

            // Denetim kaydı saklama (PCI DSS 10.5.1: en az 1 yıl) — ayrı, daha uzun eşik
            var auditCutoff = DateTime.UtcNow.AddDays(-Math.Max(settings.AuditRetentionDays, 365));
            var auditDeleted = await db.AuditLogs.Where(a => a.At < auditCutoff).ExecuteDeleteAsync(ct);
            if (auditDeleted > 0) _logger.LogInformation("{Count} eski denetim kaydı temizlendi", auditDeleted);
        }
    }
}
