using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services.Checkers;

namespace vMonitor.Services;

/// <summary>Tek bir servisin kontrolünü çalıştırır, sonucu/kesintiyi/uyarıyı işler.
/// Hem BackgroundService hem "Şimdi Kontrol Et" API'si bunu kullanır.</summary>
public class CheckRunner
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly SmsService _sms;
    private readonly WhatsappService _whatsapp;
    private readonly ILogger<CheckRunner> _logger;
    private readonly Dictionary<ServiceType, IServiceChecker> _checkers;

    public CheckRunner(AppDbContext db, EmailService email, SmsService sms, WhatsappService whatsapp, IEnumerable<IServiceChecker> checkers, ILogger<CheckRunner> logger, LicenseService license, AuditService audit)
    {
        _db = db;
        _email = email;
        _sms = sms;
        _whatsapp = whatsapp;
        _logger = logger;
        _license = license;
        _audit = audit;
        _checkers = checkers.ToDictionary(c => c.Type);
    }

    private readonly LicenseService _license;
    private readonly AuditService _audit;

    /// <summary>Lisans: Basic paket yalnız e-posta bildirimi destekler — SMS/WhatsApp gönderimi atlanır.</summary>
    private bool EmailOnly => _license.Current?.EmailOnlyNotifications == true;

    public async Task<CheckOutcome> RunCheckAsync(int serviceId, MonitorSettings settings, CancellationToken ct = default)
    {
        var svc = await _db.Services.Include(s => s.Credential).FirstOrDefaultAsync(s => s.Id == serviceId, ct)
                  ?? throw new InvalidOperationException($"Servis bulunamadı: {serviceId}");

        if (!_checkers.TryGetValue(svc.Type, out var checker))
            throw new InvalidOperationException($"Checker yok: {svc.Type}");

        var outcome = await checker.CheckAsync(svc, svc.Credential, ct);

        // ---- SELF-HEALING (yol haritası #1): yalnız Windows/Linux servis kontrol tiplerinde ----
        // Kural: X ARDIŞIK kontrol down görülürse (false-positive koruması) → Y kez yeniden başlatma
        // dene → hâlâ down ise normal alarm akışı. Başarılıysa döngü "kendini iyileştirdi" olarak kayda geçer.
        // Lisans: Self-Healing yalnız Standard/Enterprise — Basic'e düşen kurulumda açık bayraklar da işlemez.
        bool healRetryPending = false;
        if (outcome.Status == CheckStatus.Down && svc.SelfHealEnabled
            && _license.Current is { SelfHealAllowed: true }
            && svc.Type is ServiceType.WindowsServiceControl or ServiceType.LinuxServiceControl)
        {
            var maxRetries = Math.Clamp(svc.SelfHealMaxRetries, 1, 10);
            var startAfter = Math.Clamp(svc.SelfHealAfterFailures, 1, 10);
            var consecutiveNow = svc.ConsecutiveFailures + 1;   // bu kontrol dahil ardışık down sayısı

            if (consecutiveNow < startAfter)
            {
                // Henüz iyileştirme eşiğine gelinmedi: restart atılmaz, alarm da bekletilir
                // (kullanıcı kuralı: önce dene, alarm en son). Sonraki down kontrolünde tekrar bakılır.
                healRetryPending = true;
                _logger.LogInformation("Self-healing bekliyor: {Svc} down {N}/{X} (eşik dolunca restart denenecek)",
                    svc.Name, consecutiveNow, startAfter);
            }
            else if (svc.SelfHealAttemptsUsed < maxRetries)
            {
                svc.SelfHealAttemptsUsed++;
                var attemptNo = svc.SelfHealAttemptsUsed;
                svc.LastSelfHealAt = DateTime.UtcNow;
                _logger.LogWarning("Self-healing: {Svc} down — otomatik yeniden başlatma {N}/{Max}", svc.Name, attemptNo, maxRetries);

                var act = svc.Type == ServiceType.WindowsServiceControl
                    ? await Task.Run(() => ServiceControl.WindowsAction(svc, svc.Credential, "restart"), ct)
                    : svc.Credential == null
                        ? new ServiceControl.ActionResult(false, "Kimlik bilgisi tanımlı değil")
                        : await Task.Run(() => ServiceControl.LinuxAction(svc, svc.Credential, "restart"), ct);

                if (act.Ok)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);   // servisin ayağa kalkması için kısa bekleme
                    outcome = await checker.CheckAsync(svc, svc.Credential, ct);
                }

                // Gerçek müdahale bilgisi kalıcı tutulur (dashboard notu bunu gösterir; UP olunca silinmez)
                svc.LastSelfHealAttempts = attemptNo;
                svc.LastSelfHealOk = outcome.IsUp;

                if (outcome.IsUp)
                {
                    await _audit.LogAsync("selfheal.success", svc.Name,
                        $"Servis down görüldü; otomatik yeniden başlatma ile düzeldi (deneme {attemptNo}/{maxRetries}).",
                        true, user: "self-healing");
                }
                else
                {
                    // Deneme hakkı kaldıysa bu döngüde alarm bastırılır (sonraki kontrolde tekrar denenir)
                    healRetryPending = svc.SelfHealAttemptsUsed < maxRetries;
                    await _audit.LogAsync("selfheal.fail", svc.Name,
                        $"Otomatik yeniden başlatma {attemptNo}/{maxRetries} başarısız" +
                        (act.Ok ? " (servis hâlâ down)." : $": {act.Message}"),
                        false, user: "self-healing");
                }
            }
        }

        // Yanıt süresi eşiği aşıldıysa UP olsa bile uyarı say (DOWN yapmaz, sadece işaretler)
        var slow = outcome.IsUp && svc.ResponseTimeThresholdMs.HasValue
                   && outcome.ResponseTimeMs > svc.ResponseTimeThresholdMs.Value;

        var now = DateTime.UtcNow;
        var status = outcome.Status;            // Up / Down / Error
        var isDown = status == CheckStatus.Down;
        var isError = status == CheckStatus.Error;

        _db.CheckResults.Add(new CheckResult
        {
            ServiceId = svc.Id,
            CheckedAt = now,
            IsUp = outcome.IsUp,
            Status = (int)status,
            ResponseTimeMs = outcome.ResponseTimeMs,
            Error = outcome.Error ?? (slow ? $"Yavaş yanıt: {outcome.ResponseTimeMs} ms (eşik {svc.ResponseTimeThresholdMs} ms)" : null)
        });

        // Sağlık metriklerini kaydet (CPU/RAM/Disk geçmişi + kartlarda son değerler)
        if (outcome.Metrics != null)
        {
            _db.HealthMetrics.Add(new HealthMetric
            {
                ServiceId = svc.Id,
                CheckedAt = now,
                CpuPercent = outcome.Metrics.Cpu,
                RamPercent = outcome.Metrics.Ram,
                MaxDiskPercent = outcome.Metrics.MaxDisk,
                DiskDetail = outcome.Metrics.DiskDetail,
                CpuCores = outcome.Metrics.CpuCores,
                RamTotalGb = outcome.Metrics.RamTotalGb,
                RamUsedGb = outcome.Metrics.RamUsedGb,
                DiskTotalGb = outcome.Metrics.DiskTotalGb,
                DiskUsedGb = outcome.Metrics.DiskUsedGb
            });
            svc.LastCpuPercent = outcome.Metrics.Cpu;
            svc.LastRamPercent = outcome.Metrics.Ram;
            svc.LastMaxDiskPercent = outcome.Metrics.MaxDisk;
            if (outcome.Metrics.Capacity != null) svc.CapacityInfo = outcome.Metrics.Capacity;
            if (outcome.Metrics.Disks != null) svc.LastDiskInfo = outcome.Metrics.Disks;
            // İstatistikler için yapısal son değerler + OS
            if (outcome.Metrics.CpuCores.HasValue) svc.LastCpuCores = outcome.Metrics.CpuCores;
            if (outcome.Metrics.RamTotalGb.HasValue) svc.LastRamTotalGb = outcome.Metrics.RamTotalGb;
            if (outcome.Metrics.RamUsedGb.HasValue) svc.LastRamUsedGb = outcome.Metrics.RamUsedGb;
            if (outcome.Metrics.DiskTotalGb.HasValue) svc.LastDiskTotalGb = outcome.Metrics.DiskTotalGb;
            if (outcome.Metrics.DiskUsedGb.HasValue) svc.LastDiskUsedGb = outcome.Metrics.DiskUsedGb;
            if (!string.IsNullOrWhiteSpace(outcome.Metrics.OsName)) svc.OsName = outcome.Metrics.OsName;
            if (!string.IsNullOrWhiteSpace(outcome.Metrics.OsKind)) svc.OsKind = outcome.Metrics.OsKind;
        }

        svc.LastCheckedAt = now;
        svc.LastIsUp = outcome.IsUp;
        svc.LastStatus = (int)status;
        svc.LastResponseTimeMs = outcome.ResponseTimeMs;
        svc.LastError = outcome.Error;

        // Kesinti (outage) YALNIZCA DOWN için açılır/kapanır. ERROR (eşik aşımı) kesinti sayılmaz.
        var openOutage = await _db.Outages
            .Where(o => o.ServiceId == svc.Id && o.EndedAt == null)
            .OrderByDescending(o => o.StartedAt)
            .FirstOrDefaultAsync(ct);
        TimeSpan? duration = null;

        if (isDown)
        {
            if (openOutage == null)
                _db.Outages.Add(new OutageRecord { ServiceId = svc.Id, StartedAt = now, FirstError = outcome.Error });
        }
        else if (openOutage != null) // UP veya ERROR → açık kesinti kapanır
        {
            openOutage.EndedAt = now;
            duration = now - openOutage.StartedAt;
        }

        if (outcome.IsUp)
        {
            svc.ConsecutiveFailures = 0;
            svc.SelfHealAttemptsUsed = 0;   // self-healing deneme hakkı yeni sorun döngüsü için tazelenir
            if (svc.DownAlertSent)
            {
                svc.DownAlertSent = false;
                // E-posta (servis bazlı aç/kapa)
                if (settings.EmailEnabled && svc.AlertMail)
                    await TrySendAsync(() => _email.SendRecoveredAlertAsync(settings, svc, duration, ct), svc);
                // SMS (servis bazlı aç/kapa)
                if (settings.SmsEnabled && svc.AlertSms)
                    await TrySendSmsAsync(settings, svc, SmsService.RecoveredText(svc), "recovered", ct);
                // WhatsApp (servis bazlı aç/kapa)
                if (settings.WhatsappEnabled && svc.AlertWhatsapp)
                    await TrySendWhatsappAsync(settings, svc, WhatsappService.RecoveredText(svc), ct);

                // Özel entegrasyonlar (Bildirim Kanalları kutusu) — yerleşik Twilio'dan bağımsız
                if (svc.AlertSms)
                    await FireIntegrationsAsync(new[] { "Sms" }, settings, svc, _ => SmsService.RecoveredText(svc), ct);
                if (svc.AlertWhatsapp)
                    await FireWhatsappIntegrationsAsync(settings, svc, recovered: true, isError: false, null, now, ct);
                if (svc.AlertCall)
                    await FireIntegrationsAsync(new[] { "Ivr" }, settings, svc, _ => SmsService.RecoveredText(svc), ct);
            }
        }
        else // Down veya Error → sorunlu
        {
            svc.ConsecutiveFailures++;

            // Eşik dolduktan sonra HER sorunlu kontrolde uyarı gider (servis düzelene kadar).
            // Self-healing deneme hakkı sürüyorsa alarm BASTIRILIR (kullanıcı kuralı: önce dene, sonra alarm).
            if (!healRetryPending && svc.ConsecutiveFailures >= settings.FailureThreshold)
            {
                svc.DownAlertSent = true;
                if (settings.EmailEnabled && svc.AlertMail)
                {
                    if (isError)
                        await TrySendAsync(() => _email.SendErrorAlertAsync(settings, svc, outcome.Error, ct), svc);
                    else
                        await TrySendAsync(() => _email.SendDownAlertAsync(settings, svc, outcome.Error, ct), svc);
                }
                if (settings.SmsEnabled && svc.AlertSms)
                {
                    var text = isError ? SmsService.ErrorText(svc, outcome.Error) : SmsService.DownText(svc, outcome.Error);
                    await TrySendSmsAsync(settings, svc, text, isError ? "error" : "down", ct);
                }
                if (settings.WhatsappEnabled && svc.AlertWhatsapp)
                {
                    if (!string.IsNullOrWhiteSpace(settings.WhatsappAlarmTemplateSid))
                    {
                        var err = outcome.Error ?? "-";
                        if (err.Length > 150) err = err[..150];
                        var vars = new Dictionary<string, string>
                        {
                            ["1"] = svc.Name,
                            ["2"] = (isError ? "Eşik aşıldı — " : "Erişilemiyor — ") + err,
                            ["3"] = now.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                            ["4"] = "-"
                        };
                        await TrySendWhatsappTemplateAsync(settings, svc, vars, ct);
                    }
                    else
                    {
                        var wtext = isError ? WhatsappService.ErrorText(svc, outcome.Error) : WhatsappService.DownText(svc, outcome.Error);
                        await TrySendWhatsappAsync(settings, svc, wtext, ct);
                    }
                }

                // Özel entegrasyonlar (Bildirim Kanalları kutusu) — yerleşik Twilio'dan bağımsız
                if (svc.AlertSms)
                    await FireIntegrationsAsync(new[] { "Sms" }, settings, svc,
                        _ => isError ? SmsService.ErrorText(svc, outcome.Error) : SmsService.DownText(svc, outcome.Error), ct);
                if (svc.AlertWhatsapp)
                    await FireWhatsappIntegrationsAsync(settings, svc, recovered: false, isError, outcome.Error, now, ct);
                if (svc.AlertCall)
                    await FireIntegrationsAsync(new[] { "Ivr" }, settings, svc,
                        _ => isError ? SmsService.ErrorText(svc, outcome.Error) : SmsService.DownText(svc, outcome.Error), ct);
            }
        }

        // ---- Zamanlanmış Görev koşu GEÇMİŞİ: vMon'un KENDİ veritabanında birikir ----
        // Checker görev başına SON koşuyu bildirir; başlangıç zamanı değiştiyse YENİ satır eklenir,
        // aynıysa (koşu sürerken görülmüş olabilir) süre/sonuç netleşince mevcut satır güncellenir.
        // Böylece kaynak sistem geçmiş tutmasa bile (Windows Task, systemd, MySQL Event) geçmiş oluşur.
        if (svc.PendingJobRuns is { Count: > 0 })
        {
            foreach (var run in svc.PendingJobRuns)
            {
                if (run.StartedAt == null) continue;
                try
                {
                    var prev = await _db.JobRunHistories
                        .Where(h => h.ServiceId == svc.Id && h.JobName == run.JobName)
                        .OrderByDescending(h => h.StartedAt)
                        .FirstOrDefaultAsync(ct);
                    var info = run.FailText == null ? null
                        : run.FailText.Length > 500 ? run.FailText[..500] : run.FailText;

                    // ±5 sn tolerans: yaş sunucu tarafında her kontrolde yeniden hesaplandığından
                    // aynı koşunun başlangıcı saniye düzeyinde oynayabilir — mükerrer satır açılmaz.
                    if (prev != null && Math.Abs((run.StartedAt.Value - prev.StartedAt).TotalSeconds) <= 5)
                    {
                        if (run.DurSec != null && prev.DurationSec == null) prev.DurationSec = run.DurSec;
                        if (run.Failed && prev.Status != "fail") { prev.Status = "fail"; prev.Info = info; }
                    }
                    else if (prev == null || run.StartedAt.Value > prev.StartedAt)
                    {
                        _db.JobRunHistories.Add(new JobRunHistory
                        {
                            ServiceId = svc.Id,
                            JobName = run.JobName,
                            StartedAt = run.StartedAt.Value,
                            DurationSec = run.DurSec,
                            Status = run.Failed ? "fail" : "ok",
                            Info = info
                        });
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Görev geçmişi yazılamadı ({Svc}/{Job})", svc.Name, run.JobName); }
            }
        }

        await _db.SaveChangesAsync(ct);
        return outcome;
    }

    private async Task TrySendAsync(Func<Task> send, MonitoredService svc)
    {
        try { await send(); }
        catch (Exception ex) { _logger.LogError(ex, "Email gönderilemedi ({Service})", svc.Name); }
    }

    /// <summary>Verilen türlerdeki tüm AKTİF özel entegrasyonlara (SmsProviders tablosu) gönderir.
    /// Her entegrasyon kendi alıcı listesini kullanır; boşsa türüne göre global alıcılara düşer.
    /// Yerleşik SMS yolu zaten settings.SmsProvider'ı gönderdiyse o entegrasyon atlanır (çift gönderim olmaz).</summary>
    private async Task FireIntegrationsAsync(string[] kinds, MonitorSettings settings, MonitoredService svc,
        Func<SmsProvider, string> textFor, CancellationToken ct)
    {
        if (EmailOnly) return;   // Lisans: Basic yalnız e-posta — özel SMS/IVR entegrasyonları da göndermez
        List<SmsProvider> list;
        try
        {
            list = await _db.SmsProviders.AsNoTracking()
                .Where(p => p.Enabled && kinds.Contains(p.Kind)).ToListAsync(ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "Entegrasyonlar okunamadı"); return; }

        foreach (var p in list)
        {
            // SMS: yerleşik yol bu sağlayıcıyı zaten gönderdiyse atla
            if (p.Kind.Equals("Sms", StringComparison.OrdinalIgnoreCase)
                && settings.SmsEnabled && svc.AlertSms
                && p.Name.Equals(settings.SmsProvider, StringComparison.OrdinalIgnoreCase))
                continue;

            var fallback = p.Kind.Equals("Whatsapp", StringComparison.OrdinalIgnoreCase)
                ? settings.WhatsappRecipients : settings.SmsRecipients;
            var recips = SmsService.ParseRecipients(string.IsNullOrWhiteSpace(p.Recipients) ? fallback : p.Recipients);
            if (recips.Length == 0) continue;

            try
            {
                var (ok, msg) = await _sms.SendViaIntegrationAsync(p, recips, textFor(p), ct);
                if (!ok) _logger.LogWarning("Entegrasyon '{P}' gönderemedi ({Svc}): {Msg}", p.Name, svc.Name, msg);
            }
            catch (Exception ex) { _logger.LogError(ex, "Entegrasyon '{P}' hata ({Svc})", p.Name, svc.Name); }
        }
    }

    /// <summary>WhatsApp entegrasyonları: TemplateSid doluysa butonlu onaylı şablon (interaktif, AlarmSession açar),
    /// boşsa düz metin gönderir. Düzelme (recovered) bildirimi her zaman düz metindir.</summary>
    private async Task FireWhatsappIntegrationsAsync(MonitorSettings settings, MonitoredService svc,
        bool recovered, bool isError, string? error, DateTime now, CancellationToken ct)
    {
        if (EmailOnly) return;   // Lisans: Basic yalnız e-posta — özel WhatsApp entegrasyonları da göndermez
        List<SmsProvider> list;
        try { list = await _db.SmsProviders.AsNoTracking().Where(p => p.Enabled && p.Kind == "Whatsapp").ToListAsync(ct); }
        catch (Exception ex) { _logger.LogError(ex, "WhatsApp entegrasyonları okunamadı"); return; }

        foreach (var p in list)
        {
            var recips = SmsService.ParseRecipients(string.IsNullOrWhiteSpace(p.Recipients) ? settings.WhatsappRecipients : p.Recipients);
            if (recips.Length == 0) continue;
            try
            {
                if (!recovered && !string.IsNullOrWhiteSpace(p.TemplateSid))
                {
                    var err = error ?? "-"; if (err.Length > 150) err = err[..150];
                    var vars = new Dictionary<string, string>
                    {
                        ["1"] = svc.Name,
                        ["2"] = (isError ? "Eşik aşıldı — " : "Erişilemiyor — ") + err,
                        ["3"] = now.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                        ["4"] = "-"
                    };
                    var (ok, msg) = await _whatsapp.SendTemplateRawAsync(p.Username, p.PasswordEncrypted, p.Sender, recips, p.TemplateSid, vars, ct);
                    if (ok)
                    {
                        foreach (var r in recips)
                            _db.AlarmSessions.Add(new AlarmSession { ServiceId = svc.Id, Phone = WhatsappService.NormalizePhone(r), CreatedAt = DateTime.UtcNow });
                        await _db.SaveChangesAsync(ct);
                    }
                    else _logger.LogWarning("WhatsApp şablon '{P}' gönderilemedi ({Svc}): {Msg}", p.Name, svc.Name, msg);
                }
                else
                {
                    var text = recovered ? WhatsappService.RecoveredText(svc)
                        : (isError ? WhatsappService.ErrorText(svc, error) : WhatsappService.DownText(svc, error));
                    var (ok, msg) = await _sms.SendViaIntegrationAsync(p, recips, text, ct);
                    if (!ok) _logger.LogWarning("WhatsApp entegrasyon '{P}' gönderemedi ({Svc}): {Msg}", p.Name, svc.Name, msg);
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "WhatsApp entegrasyon '{P}' hata ({Svc})", p.Name, svc.Name); }
        }
    }

    private async Task TrySendSmsAsync(MonitorSettings settings, MonitoredService svc, string text, string kind, CancellationToken ct)
    {
        if (EmailOnly) return;
        try
        {
            var (ok, msg) = await _sms.SendAsync(settings, SmsService.ParseRecipients(settings.SmsRecipients), text, ct);
            if (!ok) _logger.LogWarning("SMS gönderilemedi ({Service}): {Msg}", svc.Name, msg);
        }
        catch (Exception ex) { _logger.LogError(ex, "SMS gönderilemedi ({Service})", svc.Name); }
    }

    private async Task TrySendWhatsappAsync(MonitorSettings settings, MonitoredService svc, string text, CancellationToken ct)
    {
        if (EmailOnly) return;
        try
        {
            var (ok, msg) = await _whatsapp.SendAsync(settings, WhatsappService.ParseRecipients(settings.WhatsappRecipients), text, ct);
            if (!ok) _logger.LogWarning("WhatsApp gönderilemedi ({Service}): {Msg}", svc.Name, msg);
        }
        catch (Exception ex) { _logger.LogError(ex, "WhatsApp gönderilemedi ({Service})", svc.Name); }
    }

    private async Task TrySendWhatsappTemplateAsync(MonitorSettings settings, MonitoredService svc, Dictionary<string, string> vars, CancellationToken ct)
    {
        if (EmailOnly) return;
        try
        {
            var recipients = WhatsappService.ParseRecipients(settings.WhatsappRecipients);
            var (ok, msg) = await _whatsapp.SendTemplateAsync(settings, recipients, settings.WhatsappAlarmTemplateSid, vars, ct);
            if (!ok) { _logger.LogWarning("WhatsApp şablon gönderilemedi ({Service}): {Msg}", svc.Name, msg); return; }

            // İnteraktif korelasyon: butona basılınca hangi servise işlem yapılacağını bulmak için oturum aç
            foreach (var r in recipients)
                _db.AlarmSessions.Add(new AlarmSession { ServiceId = svc.Id, Phone = WhatsappService.NormalizePhone(r), CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "WhatsApp şablon gönderilemedi ({Service})", svc.Name); }
    }
}
