using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly CheckRunner _runner;
    private readonly EmailService _email;
    private readonly AuditService _audit;
    private readonly LicenseService _lic;

    public ApiController(AppDbContext db, SettingsService settings, CheckRunner runner, EmailService email, AuditService audit, LicenseService lic)
    {
        _db = db;
        _settings = settings;
        _runner = runner;
        _email = email;
        _audit = audit;
        _lic = lic;
    }

    /// <summary>Dashboard'un periyodik yenilemesi için tüm servislerin anlık durumu.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var services = await _db.Services.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                Type = s.Type.ToString(),
                s.Target,
                s.Port,
                s.Extra,
                s.Enabled,
                s.LastCheckedAt,
                s.LastIsUp,
                s.LastResponseTimeMs,
                s.LastError,
                s.ConsecutiveFailures,
                s.ResponseTimeThresholdMs,
                s.LastCpuPercent,
                s.LastRamPercent,
                s.LastMaxDiskPercent,
                s.CapacityInfo,
                s.LastStatus,
                s.Description
            })
            .ToListAsync(ct);

        // Açık kesinti süreleri
        var openOutages = await _db.Outages.AsNoTracking()
            .Where(o => o.EndedAt == null)
            .ToDictionaryAsync(o => o.ServiceId, o => o.StartedAt, ct);

        return Ok(new
        {
            now = DateTime.UtcNow,
            services = services.Select(s => new
            {
                s.Id, s.Name, s.Type, s.Target, s.Port, s.Extra, s.Enabled,
                s.LastCheckedAt, s.LastIsUp, s.LastResponseTimeMs, s.LastError,
                s.ConsecutiveFailures, s.ResponseTimeThresholdMs,
                s.LastCpuPercent, s.LastRamPercent, s.LastMaxDiskPercent, s.CapacityInfo,
                s.LastStatus, s.Description,
                isError = s.LastStatus == (int)Models.CheckStatus.Error,
                slow = s.LastIsUp == true && s.ResponseTimeThresholdMs.HasValue
                       && s.LastResponseTimeMs > s.ResponseTimeThresholdMs,
                downSince = openOutages.TryGetValue(s.Id, out var since) ? since : (DateTime?)null
            })
        });
    }

    /// <summary>React SPA dashboard için tek çağrıda toplu veri: KPI'lar, 24s uptime serisi, durum dağılımı, son servisler.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();

        var svc = await _db.Services.AsNoTracking()
            .Select(s => new
            {
                s.Id, s.Name, s.Type, s.Enabled, s.LastIsUp, s.LastStatus,
                s.LastResponseTimeMs, s.ResponseTimeThresholdMs, s.LastCheckedAt
            })
            .ToListAsync(ct);

        bool Slow(long? ms, int? thr) => thr.HasValue && ms.HasValue && ms > thr;

        int total = svc.Count;
        int slow = svc.Count(s => s.LastIsUp == true && Slow(s.LastResponseTimeMs, s.ResponseTimeThresholdMs));
        int upTotal = svc.Count(s => s.LastIsUp == true);
        int running = upTotal - slow;
        int down = total - upTotal;                       // kapalı + hata + hiç kontrol edilmemiş
        var upMs = svc.Where(s => s.LastIsUp == true && s.LastResponseTimeMs.HasValue)
                      .Select(s => s.LastResponseTimeMs!.Value).ToList();
        double? avgMs = upMs.Count > 0 ? Math.Round(upMs.Average(), 0) : (double?)null;

        // Son 24 saat: saatlik ortalama erişilebilirlik + servis bazlı uptime (eşik aşımı=ERROR up sayılır)
        var since = DateTime.UtcNow.AddHours(-24);
        var checks = await _db.CheckResults.AsNoTracking()
            .Where(r => r.CheckedAt >= since)
            .Select(r => new { r.ServiceId, r.CheckedAt, r.IsUp, r.Status })
            .ToListAsync(ct);

        bool IsUpRow(bool isUp, int status) => isUp || status == (int)Models.CheckStatus.Error;

        var uptime24h = checks
            .GroupBy(r => new DateTime(r.CheckedAt.Year, r.CheckedAt.Month, r.CheckedAt.Day, r.CheckedAt.Hour, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new { t = g.Key, uptime = Math.Round(100.0 * g.Count(r => IsUpRow(r.IsUp, r.Status)) / g.Count(), 2) })
            .ToList();

        var upBySvc = checks.GroupBy(r => r.ServiceId)
            .ToDictionary(g => g.Key, g => Math.Round(100.0 * g.Count(r => IsUpRow(r.IsUp, r.Status)) / g.Count(), 2));

        var services = svc.OrderBy(s => s.Name).Take(12).Select(s => new
        {
            id = s.Id,
            name = s.Name,
            type = s.Type.ToString(),
            status = s.LastStatus == (int)Models.CheckStatus.Error ? "error"
                     : s.LastIsUp != true ? "down"
                     : Slow(s.LastResponseTimeMs, s.ResponseTimeThresholdMs) ? "slow" : "up",
            ms = s.LastIsUp == true ? s.LastResponseTimeMs : (long?)null,
            uptime = upBySvc.TryGetValue(s.Id, out var u) ? u : (double?)null,
            lastCheckedAt = s.LastCheckedAt
        }).ToList();

        return Ok(new
        {
            kpis = new { total, up = upTotal, problem = down, avgMs },
            distribution = new { running, slow, down },
            uptime24h,
            services
        });
    }

    /// <summary>SPA için antiforgery token — mutasyonlarda X-CSRF-TOKEN header'ında gönderilir. (GET güvenli, doğrulama gerektirmez.)</summary>
    [HttpGet("antiforgery")]
    public IActionResult Antiforgery([FromServices] Microsoft.AspNetCore.Antiforgery.IAntiforgery af)
    {
        var tokens = af.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    // ---- Servisler (React ekranı) ----

    /// <summary>Servis düzenleme/liste için tam alanlar + kimlik adı + anlık durum.</summary>
    [HttpGet("services")]
    public async Task<IActionResult> Services(CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        var list = await _db.Services.AsNoTracking().Include(s => s.Credential)
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id, s.Name, Type = s.Type.ToString(), s.Target, s.Port, s.Extra,
                s.UseSsl, s.IgnoreCertErrors, s.CredentialId, credentialName = s.Credential != null ? s.Credential.Name : null,
                s.Enabled, s.IntervalMinutesOverride, s.ResponseTimeThresholdMs, s.TimeoutSeconds,
                s.CpuThresholdPercent, s.RamThresholdPercent, s.DiskThresholdPercent,
                s.Keyword, s.Description, s.AlertMail, s.AlertSms, s.AlertWhatsapp, s.AlertCall,
                s.LastCheckedAt, s.LastIsUp, s.LastStatus, s.LastResponseTimeMs, s.LastError,
                slow = s.LastIsUp == true && s.ResponseTimeThresholdMs.HasValue && s.LastResponseTimeMs > s.ResponseTimeThresholdMs
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    /// <summary>Form için meta: servis tipleri + kimlik bilgileri listesi.</summary>
    [HttpGet("services/meta")]
    public async Task<IActionResult> ServicesMeta(CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        var creds = await _db.Credentials.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        return Ok(new { types = Enum.GetNames<ServiceType>(), credentials = creds });
    }

    public record ServiceInput(
        string Name, string Type, string Target, int? Port, string? Extra,
        bool UseSsl, bool IgnoreCertErrors, int? CredentialId, bool Enabled,
        int? IntervalMinutesOverride, int? ResponseTimeThresholdMs, int TimeoutSeconds,
        int? CpuThresholdPercent, int? RamThresholdPercent, int? DiskThresholdPercent,
        string? Keyword, string? Description,
        bool AlertMail, bool AlertSms, bool AlertWhatsapp, bool AlertCall);

    private static string? ValidateInput(ServiceInput m, out ServiceType type)
    {
        if (!Enum.TryParse(m.Type, true, out type)) return "Geçersiz servis tipi.";
        if (string.IsNullOrWhiteSpace(m.Name)) return "Ad zorunlu.";
        if (string.IsNullOrWhiteSpace(m.Target)) return "Hedef zorunlu.";
        if ((type == ServiceType.WindowsServiceControl || type == ServiceType.LinuxServiceControl)
            && !string.IsNullOrWhiteSpace(m.Extra)
            && !System.Text.RegularExpressions.Regex.IsMatch(m.Extra, @"^[A-Za-z0-9._@\-]+$"))
            return "Servis adı yalnızca harf, rakam, nokta, tire ve alt çizgi içerebilir.";
        return null;
    }

    private static void Apply(MonitoredService s, ServiceInput m, ServiceType type)
    {
        s.Name = m.Name.Trim(); s.Type = type; s.Target = m.Target.Trim(); s.Port = m.Port;
        s.Extra = string.IsNullOrWhiteSpace(m.Extra) ? null : m.Extra.Trim();
        s.UseSsl = m.UseSsl; s.IgnoreCertErrors = m.IgnoreCertErrors; s.CredentialId = m.CredentialId;
        s.Enabled = m.Enabled; s.IntervalMinutesOverride = m.IntervalMinutesOverride;
        s.ResponseTimeThresholdMs = m.ResponseTimeThresholdMs; s.TimeoutSeconds = m.TimeoutSeconds <= 0 ? 15 : m.TimeoutSeconds;
        s.CpuThresholdPercent = m.CpuThresholdPercent; s.RamThresholdPercent = m.RamThresholdPercent; s.DiskThresholdPercent = m.DiskThresholdPercent;
        s.Keyword = string.IsNullOrWhiteSpace(m.Keyword) ? null : m.Keyword.Trim();
        s.Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim();
        s.AlertMail = m.AlertMail; s.AlertSms = m.AlertSms; s.AlertWhatsapp = m.AlertWhatsapp; s.AlertCall = m.AlertCall;
    }

    [HttpPost("services")]
    public async Task<IActionResult> ServiceCreate([FromBody] ServiceInput m, CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        // Lisans limiti: Basic 40 / Standard 200 / Enterprise sınırsız izleme
        if (_lic.Current is { } lim)
        {
            var total = await _db.Services.CountAsync(ct);
            if (total >= lim.MaxMonitors)
                return BadRequest($"Lisans limiti: {lim.Edition} paket en fazla {lim.MaxMonitors} izleme destekler (şu an {total}). Daha fazla izleme için üst pakete geçin.");
        }
        var err = ValidateInput(m, out var type);
        if (err != null) return BadRequest(err);
        var s = new MonitoredService();
        Apply(s, m, type);
        _db.Services.Add(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("service.create", s.Name, $"Tip: {s.Type}", ct: ct);
        return Ok(new { s.Id });
    }

    [HttpPut("services/{id:int}")]
    public async Task<IActionResult> ServiceUpdate(int id, [FromBody] ServiceInput m, CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        var err = ValidateInput(m, out var type);
        if (err != null) return BadRequest(err);
        var s = await _db.Services.FindAsync(new object[] { id }, ct);
        if (s == null) return NotFound("Servis bulunamadı");
        Apply(s, m, type);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("service.update", s.Name, $"Tip: {s.Type}", ct: ct);
        return Ok(new { s.Id });
    }

    [HttpDelete("services/{id:int}")]
    public async Task<IActionResult> ServiceDelete(int id, CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        var s = await _db.Services.FindAsync(new object[] { id }, ct);
        if (s == null) return NotFound("Servis bulunamadı");
        await _db.CheckResults.Where(r => r.ServiceId == id).ExecuteDeleteAsync(ct);
        await _db.Outages.Where(o => o.ServiceId == id).ExecuteDeleteAsync(ct);
        await _db.HealthMetrics.Where(x => x.ServiceId == id).ExecuteDeleteAsync(ct);
        _db.Services.Remove(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("service.delete", s.Name, ct: ct);
        return Ok(new { ok = true });
    }

    /// <summary>Seçili servisleri toplu sil (React ekranı).</summary>
    public record BulkIds(int[] Ids);

    [HttpPost("services/bulk-delete")]
    public async Task<IActionResult> ServicesBulkDelete([FromBody] BulkIds m, CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        var idList = (m.Ids ?? Array.Empty<int>()).Distinct().ToList();
        if (idList.Count == 0) return BadRequest("Silinecek servis seçilmedi.");
        await _db.CheckResults.Where(r => idList.Contains(r.ServiceId)).ExecuteDeleteAsync(ct);
        await _db.Outages.Where(o => idList.Contains(o.ServiceId)).ExecuteDeleteAsync(ct);
        await _db.HealthMetrics.Where(x => idList.Contains(x.ServiceId)).ExecuteDeleteAsync(ct);
        var deleted = await _db.Services.Where(s => idList.Contains(s.Id)).ExecuteDeleteAsync(ct);
        await _audit.LogAsync("service.delete-many", null, $"{deleted} servis toplu silindi.", ct: ct);
        return Ok(new { deleted });
    }

    /// <summary>Toplu düzenleme — klasik EditMany ile aynı semantik: kanallar "on"/"off"/null(dokunma);
    /// set*=true ise ilgili sayısal alan yazılır (null = özelliği temizle).</summary>
    public record BulkEditInput(
        int[] Ids,
        string? AlertMail, string? AlertSms, string? AlertWhatsapp, string? AlertCall, string? Enabled,
        bool SetInterval, int? Interval,
        bool SetSlow, int? Slow,
        bool SetCpu, int? Cpu,
        bool SetRam, int? Ram,
        bool SetDisk, int? Disk,
        string? AddKeywords);

    [HttpPost("services/bulk-edit")]
    public async Task<IActionResult> ServicesBulkEdit([FromBody] BulkEditInput m, CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        var idList = (m.Ids ?? Array.Empty<int>()).Distinct().ToList();
        if (idList.Count == 0) return BadRequest("Düzenlenecek servis seçilmedi.");

        var services = await _db.Services.Where(s => idList.Contains(s.Id)).ToListAsync(ct);
        var changes = new List<string>();
        void Apply(string? v, string label, Action<bool> set)
        {
            if (v == "on") { set(true); changes.Add(label + "=açık"); }
            else if (v == "off") { set(false); changes.Add(label + "=kapalı"); }
        }
        var newKws = MonitoredService.SplitKeywords(m.AddKeywords);
        int? Clamp(int? v, int min, int max) => v.HasValue ? Math.Clamp(v.Value, min, max) : (int?)null;
        var iv = Clamp(m.Interval, 1, 1440);
        var sl = Clamp(m.Slow, 1, 600000);
        var cp = Clamp(m.Cpu, 1, 100);
        var rm = Clamp(m.Ram, 1, 100);
        var dk = Clamp(m.Disk, 1, 100);

        foreach (var s in services)
        {
            Apply(m.AlertMail, "Mail", b => s.AlertMail = b);
            Apply(m.AlertSms, "SMS", b => s.AlertSms = b);
            Apply(m.AlertWhatsapp, "WhatsApp", b => s.AlertWhatsapp = b);
            Apply(m.AlertCall, "Arama", b => s.AlertCall = b);
            Apply(m.Enabled, "Aktif", b => s.Enabled = b);

            if (m.SetInterval) s.IntervalMinutesOverride = iv;
            if (m.SetSlow) s.ResponseTimeThresholdMs = sl;
            if (m.SetCpu) s.CpuThresholdPercent = cp;
            if (m.SetRam) s.RamThresholdPercent = rm;
            if (m.SetDisk) s.DiskThresholdPercent = dk;

            if (newKws.Count > 0)
            {
                var existing = MonitoredService.SplitKeywords(s.Keyword);
                s.Keyword = string.Join(", ", existing.Concat(newKws).Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }
        if (m.SetInterval) changes.Add(iv.HasValue ? $"Aralık={iv}dk" : "Aralık=global");
        if (m.SetSlow) changes.Add(sl.HasValue ? $"Yavaşlık={sl}ms" : "Yavaşlık=kapalı");
        if (m.SetCpu) changes.Add(cp.HasValue ? $"CPU={cp}%" : "CPU=kapalı");
        if (m.SetRam) changes.Add(rm.HasValue ? $"RAM={rm}%" : "RAM=kapalı");
        if (m.SetDisk) changes.Add(dk.HasValue ? $"Disk={dk}%" : "Disk=kapalı");
        if (newKws.Count > 0) changes.Add("Keyword+(" + string.Join("/", newKws) + ")");
        if (changes.Count == 0) return BadRequest("Değiştirilecek bir alan seçmediniz.");

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("service.bulk-edit", null, $"{services.Count} servis: {string.Join(", ", changes.Distinct())}", ct: ct);
        return Ok(new { updated = services.Count, changes = changes.Distinct() });
    }

    /// <summary>Servisleri import formatında CSV indir. ids verilirse YALNIZ seçilenler, yoksa tümü.</summary>
    [HttpGet("services/export")]
    public async Task<IActionResult> ServicesExport([FromQuery] string? ids, CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().ToList();
        var query = _db.Services.AsNoTracking().Include(s => s.Credential).AsQueryable();
        if (idList.Count > 0) query = query.Where(s => idList.Contains(s.Id));
        var services = await query.OrderBy(s => s.Name).ToListAsync(ct);
        var bytes = ServiceCsvHelper.BuildExportCsv(services);
        var suffix = idList.Count > 0 ? $"secili-{idList.Count}" : "tum";
        return File(bytes, "text/csv; charset=utf-8", $"vmon-servisler-{suffix}_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    /// <summary>CSV'den toplu servis ekleme (React) — JSON özet döner.</summary>
    [HttpPost("services/import")]
    public async Task<IActionResult> ServicesImport(IFormFile? csvFile, CancellationToken ct)
    {
        if (!Can(Perms.ServicesManage)) return Forbid403();
        if (csvFile == null || csvFile.Length == 0) return BadRequest("Dosya seçilmedi.");
        string content;
        using (var reader = new StreamReader(csvFile.OpenReadStream(), System.Text.Encoding.UTF8, true))
            content = await reader.ReadToEndAsync(ct);
        // Lisans limiti: dosyadaki satırlar limiti aşacaksa içe aktarım baştan reddedilir
        if (_lic.Current is { } lim && lim.MaxMonitors != int.MaxValue)
        {
            var total = await _db.Services.CountAsync(ct);
            var rows = content.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l)) - 1;   // başlık hariç kaba sayım
            if (rows > 0 && total + rows > lim.MaxMonitors)
                return BadRequest($"Lisans limiti: {lim.Edition} paket en fazla {lim.MaxMonitors} izleme destekler (şu an {total}, dosyada ~{rows} satır). Daha fazlası için üst pakete geçin.");
        }
        var result = await ServiceCsvHelper.ImportAsync(_db, content, ct);
        if (result.Added > 0)
            await _audit.LogAsync("service.import", null, $"{result.Added} servis CSV ile eklendi, {result.Skipped} atlandı.", ct: ct);
        return Ok(new { added = result.Added, skipped = result.Skipped, errors = result.Errors });
    }

    /// <summary>Tek servisi şimdi kontrol et.</summary>
    [HttpPost("check/{id:int}")]
    public async Task<IActionResult> CheckNow(int id, CancellationToken ct)
    {
        if (!Can(Perms.ServicesCheck)) return Forbid403();
        var settings = await _settings.GetAsync(ct);
        var outcome = await _runner.RunCheckAsync(id, settings, ct);
        return Ok(new { isUp = outcome.IsUp, responseTimeMs = outcome.ResponseTimeMs, error = outcome.Error });
    }

    /// <summary>Tüm aktif servisleri şimdi kontrol et.</summary>
    [HttpPost("check-all")]
    public async Task<IActionResult> CheckAll(CancellationToken ct)
    {
        if (!Can(Perms.ServicesCheck)) return Forbid403();
        var settings = await _settings.GetAsync(ct);
        var ids = await _db.Services.Where(s => s.Enabled).Select(s => s.Id).ToListAsync(ct);
        var results = new List<object>();
        foreach (var id in ids)
        {
            try
            {
                var outcome = await _runner.RunCheckAsync(id, settings, ct);
                results.Add(new { id, isUp = outcome.IsUp, responseTimeMs = outcome.ResponseTimeMs, error = outcome.Error });
            }
            catch (Exception ex)
            {
                results.Add(new { id, isUp = false, error = ex.Message });
            }
        }
        return Ok(results);
    }

    /// <summary>Verilen id listesindeki servisleri şimdi kontrol et (dashboard'a özel toplu kontrol).</summary>
    [HttpPost("check-ids")]
    public async Task<IActionResult> CheckIds([FromForm] string ids, CancellationToken ct)
    {
        if (!Can(Perms.ServicesCheck)) return Forbid403();
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().ToList();
        var settings = await _settings.GetAsync(ct);
        var results = new List<object>();
        foreach (var id in idList)
        {
            try
            {
                var outcome = await _runner.RunCheckAsync(id, settings, ct);
                results.Add(new { id, isUp = outcome.IsUp, responseTimeMs = outcome.ResponseTimeMs, error = outcome.Error });
            }
            catch (Exception ex) { results.Add(new { id, isUp = false, error = ex.Message }); }
        }
        return Ok(results);
    }

    /// <summary>Uzaktan servis kontrolü: yalnızca Windows/Linux Servis tiplerinde start/stop/restart.</summary>
    [HttpPost("service-action/{id:int}")]
    public async Task<IActionResult> ServiceAction(int id, [FromForm] string action, CancellationToken ct)
    {
        if (!Can(Perms.ServicesControl)) return Forbid403();
        var svc = await _db.Services.Include(s => s.Credential).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (svc == null) return NotFound("Servis bulunamadı");
        if (svc.Type != ServiceType.WindowsServiceControl && svc.Type != ServiceType.LinuxServiceControl)
            return BadRequest("Bu servis tipinde uzaktan kontrol yapılamaz.");
        if (action is not ("start" or "stop" or "restart")) return BadRequest("Geçersiz işlem.");

        ServiceControl.ActionResult res;
        if (svc.Type == ServiceType.WindowsServiceControl)
            res = await Task.Run(() => ServiceControl.WindowsAction(svc, svc.Credential, action), ct);
        else
        {
            if (svc.Credential == null) return Ok(new { ok = false, message = "Kimlik bilgisi tanımlı değil." });
            res = await Task.Run(() => ServiceControl.LinuxAction(svc, svc.Credential, action), ct);
        }

        await _audit.LogAsync("service.action", svc.Name, $"{action} → {res.Message}", res.Ok, ct: ct);

        // İşlemden sonra durumu hemen tazele
        if (res.Ok)
        {
            try { await _runner.RunCheckAsync(id, await _settings.GetAsync(ct), ct); } catch { }
        }
        return Ok(new { ok = res.Ok, message = res.Message });
    }

    /// <summary>Servis geçmişi: son kontroller + kesintiler.</summary>
    [HttpGet("history/{id:int}")]
    public async Task<IActionResult> History(int id, [FromQuery] int take = 100, [FromQuery] int minutes = 0, CancellationToken ct = default)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var svc = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (svc == null) return NotFound("Servis bulunamadı");

        // minutes > 0: zaman aralığına göre (detay grafiği aralık seçici); yoksa son N kontrol
        var q = _db.CheckResults.AsNoTracking().Where(r => r.ServiceId == id);
        if (minutes > 0)
        {
            minutes = Math.Clamp(minutes, 5, 60 * 24 * 62);
            var since = DateTime.UtcNow.AddMinutes(-minutes);
            q = q.Where(r => r.CheckedAt >= since);
            take = 10000;
        }
        var checks = await q
            .OrderByDescending(r => r.CheckedAt)
            .Take(Math.Clamp(take, 1, 10000))
            .Select(r => new { r.CheckedAt, r.IsUp, r.Status, r.ResponseTimeMs, r.Error })
            .ToListAsync(ct);

        // Uzun aralıkta sunucu tarafı özetleme (~400 kova): ms=maks, DOWN/HATA kova içinde korunur
        if (minutes > 0 && checks.Count > 600)
        {
            var bucketTicks = Math.Max(TimeSpan.TicksPerSecond, TimeSpan.FromMinutes(minutes).Ticks / 400);
            checks = checks
                .GroupBy(c => c.CheckedAt.Ticks / bucketTicks)
                .OrderByDescending(g => g.Key)
                .Select(g => new
                {
                    CheckedAt = new DateTime(g.Key * bucketTicks, DateTimeKind.Utc),
                    IsUp = !g.Any(x => !x.IsUp && x.Status != (int)Models.CheckStatus.Error),
                    Status = g.Any(x => x.Status == (int)Models.CheckStatus.Error) ? (int)Models.CheckStatus.Error
                           : g.Any(x => !x.IsUp) ? (int)Models.CheckStatus.Down : (int)Models.CheckStatus.Up,
                    ResponseTimeMs = g.Max(x => x.ResponseTimeMs),
                    Error = g.Select(x => x.Error).FirstOrDefault(e => e != null)
                })
                .ToList();
        }

        var outages = await _db.Outages.AsNoTracking()
            .Where(o => o.ServiceId == id)
            .OrderByDescending(o => o.StartedAt)
            .Take(50)
            .Select(o => new { o.StartedAt, o.EndedAt, o.FirstError })
            .ToListAsync(ct);

        return Ok(new { service = new { svc.Id, svc.Name, Type = svc.Type.ToString() }, checks, outages });
    }

    /// <summary>Canlı grafik için çok-servisli zaman serisi (yanıt süresi + durum).</summary>
    [HttpGet("timeseries")]
    public async Task<IActionResult> TimeSeries([FromQuery] string ids, [FromQuery] int minutes = 120, CancellationToken ct = default)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().Take(20).ToList();
        if (idList.Count == 0) return Ok(new { series = Array.Empty<object>() });

        var since = DateTime.UtcNow.AddMinutes(-Math.Clamp(minutes, 5, 60 * 24 * 31));

        var raw = await _db.CheckResults.AsNoTracking()
            .Where(r => idList.Contains(r.ServiceId) && r.CheckedAt >= since)
            .Select(r => new { r.ServiceId, r.CheckedAt, r.ResponseTimeMs, r.IsUp, r.Status })
            .ToListAsync(ct);

        var names = await _db.Services.AsNoTracking()
            .Where(s => idList.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // SUNUCU TARAFI ÖZETLEME: uzun aralıklarda ham satır sayısı (1 ay × 12 servis > 100k)
        // tarayıcıyı donduruyordu. Seri başına ~300 zaman kovası: ms=maks (spike korunur),
        // kova içinde herhangi bir DOWN/HATA varsa durum noktası korunur → özellik kaybı yok.
        const int targetPoints = 300;
        var bucketTicks = Math.Max(TimeSpan.TicksPerSecond,
            TimeSpan.FromMinutes(minutes).Ticks / targetPoints);

        return Ok(new
        {
            since,
            // st: 0=Up 1=Down 2=Error — grafiklerde kırmızı/sarı durum noktaları için
            series = idList.Where(id => names.ContainsKey(id)).Select(id => new
            {
                id,
                name = names[id],
                points = raw.Where(p => p.ServiceId == id)
                    .GroupBy(p => p.CheckedAt.Ticks / bucketTicks)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        t = new DateTime(g.Key * bucketTicks, DateTimeKind.Utc),
                        ms = g.Max(x => x.ResponseTimeMs),
                        up = !g.Any(x => !x.IsUp && x.Status != (int)Models.CheckStatus.Error),
                        st = g.Any(x => x.Status == (int)Models.CheckStatus.Error) ? 2
                           : g.Any(x => !x.IsUp) ? 1 : 0
                    })
            })
        });
    }

    /// <summary>Çok-servisli sağlık metrik serisi (dashboard CPU/RAM/Disk grafikleri) — tek çağrı, en çok 20 servis.</summary>
    [HttpGet("metrics-series")]
    public async Task<IActionResult> MetricsSeries([FromQuery] string ids, [FromQuery] int minutes = 180, CancellationToken ct = default)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => int.TryParse(x, out _)).Select(int.Parse).Distinct().Take(20).ToList();
        if (idList.Count == 0) return Ok(new { series = Array.Empty<object>() });

        minutes = Math.Clamp(minutes, 5, 60 * 24 * 31);
        var since = DateTime.UtcNow.AddMinutes(-minutes);
        var raw = await _db.HealthMetrics.AsNoTracking()
            .Where(m => idList.Contains(m.ServiceId) && m.CheckedAt >= since)
            .Select(m => new { m.ServiceId, m.CheckedAt, m.CpuPercent, m.RamPercent, m.MaxDiskPercent })
            .ToListAsync(ct);
        var names = await _db.Services.AsNoTracking().Where(s => idList.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // Sunucu tarafı özetleme (bkz. timeseries): seri başına ~300 kova, ortalama değerler
        const int targetPoints = 300;
        var bucketTicks = Math.Max(TimeSpan.TicksPerSecond, TimeSpan.FromMinutes(minutes).Ticks / targetPoints);

        return Ok(new
        {
            series = idList.Where(names.ContainsKey).Select(id => new
            {
                id,
                name = names[id],
                points = raw.Where(p => p.ServiceId == id)
                    .GroupBy(p => p.CheckedAt.Ticks / bucketTicks)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        t = new DateTime(g.Key * bucketTicks, DateTimeKind.Utc),
                        cpu = Avg(g.Select(x => x.CpuPercent)),
                        ram = Avg(g.Select(x => x.RamPercent)),
                        disk = Avg(g.Select(x => x.MaxDiskPercent))
                    })
            })
        });
    }

    /// <summary>Sağlık metrikleri zaman serisi (CPU/RAM/Disk grafiği için).</summary>
    [HttpGet("metrics/{id:int}")]
    public async Task<IActionResult> Metrics(int id, [FromQuery] int minutes = 1440, CancellationToken ct = default)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var since = DateTime.UtcNow.AddMinutes(-Math.Clamp(minutes, 5, 60 * 24 * 31));
        var points = await _db.HealthMetrics.AsNoTracking()
            .Where(m => m.ServiceId == id && m.CheckedAt >= since)
            .OrderBy(m => m.CheckedAt)
            .Select(m => new { t = m.CheckedAt, cpu = m.CpuPercent, ram = m.RamPercent, disk = m.MaxDiskPercent, diskDetail = m.DiskDetail })
            .ToListAsync(ct);
        return Ok(new { points });
    }

    // ---- Custom panolar (Dashboard'lar — React ekranı) ----

    private async Task<HashSet<int>> ResolveBoardIdsAsync(DashboardDef dash, CancellationToken ct)
    {
        var idSet = dash.GetServiceIds().ToHashSet();
        ServiceType type = default;
        bool hasType = !string.IsNullOrWhiteSpace(dash.TypeFilter) && Enum.TryParse(dash.TypeFilter, out type);
        bool hasKw = !string.IsNullOrWhiteSpace(dash.KeywordFilter);
        var kw = dash.KeywordFilter?.Trim() ?? "";
        if (hasType || hasKw)
        {
            var candidates = await _db.Services.AsNoTracking().Select(s => new { s.Id, s.Type, s.Keyword }).ToListAsync(ct);
            foreach (var s in candidates)
            {
                bool typeOk = !hasType || s.Type == type;
                bool kwOk = !hasKw || MonitoredService.SplitKeywords(s.Keyword).Contains(kw, StringComparer.OrdinalIgnoreCase);
                if (typeOk && kwOk) idSet.Add(s.Id);
            }
        }
        return idSet;
    }

    /// <summary>Tüm panolar + her birinin çözülmüş servis Id kümesi (React dashboard seçicisi).</summary>
    [HttpGet("dashboards")]
    public async Task<IActionResult> DashboardsList(CancellationToken ct)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var boards = await _db.Dashboards.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync(ct);
        var result = new List<object>();
        foreach (var d in boards)
            result.Add(new { d.Id, d.Name, d.SortOrder, d.TypeFilter, d.KeywordFilter, serviceIds = await ResolveBoardIdsAsync(d, ct) });
        return Ok(result);
    }

    /// <summary>Pano formu meta: tüm servisler + tipler + etiketler.</summary>
    [HttpGet("dashboards/meta")]
    public async Task<IActionResult> DashboardsMeta(CancellationToken ct)
    {
        if (!Can(Perms.DashboardsManage)) return Forbid403();
        var services = await _db.Services.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, Type = s.Type.ToString(), s.Keyword }).ToListAsync(ct);
        var keywords = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in services)
            foreach (var k in MonitoredService.SplitKeywords(s.Keyword)) keywords.Add(k);
        return Ok(new { services, types = Enum.GetNames<ServiceType>(), keywords });
    }

    public record DashboardInput(string Name, int[]? ServiceIds, string? TypeFilter, string? KeywordFilter, int SortOrder);

    private static void ApplyBoard(DashboardDef d, DashboardInput m)
    {
        d.Name = m.Name.Trim();
        d.ServiceIdsCsv = m.ServiceIds is { Length: > 0 } ? string.Join(",", m.ServiceIds) : null;
        d.TypeFilter = string.IsNullOrWhiteSpace(m.TypeFilter) ? null : m.TypeFilter;
        d.KeywordFilter = string.IsNullOrWhiteSpace(m.KeywordFilter) ? null : m.KeywordFilter.Trim();
        d.SortOrder = m.SortOrder;
    }

    [HttpPost("dashboards")]
    public async Task<IActionResult> DashboardCreate([FromBody] DashboardInput m, CancellationToken ct)
    {
        if (!Can(Perms.DashboardsManage)) return Forbid403();
        if (string.IsNullOrWhiteSpace(m.Name)) return BadRequest("Ad zorunlu.");
        // Lisans limiti: Basic en fazla 5 dashboard
        if (_lic.Current is { } lim)
        {
            var total = await _db.Dashboards.CountAsync(ct);
            if (total >= lim.MaxDashboards)
                return BadRequest($"Lisans limiti: {lim.Edition} paket en fazla {lim.MaxDashboards} dashboard destekler. Sınırsız dashboard için Standard/Enterprise pakete geçin.");
        }
        var d = new DashboardDef();
        ApplyBoard(d, m);
        _db.Dashboards.Add(d);
        await _db.SaveChangesAsync(ct);
        return Ok(new { d.Id });
    }

    [HttpPut("dashboards/{id:int}")]
    public async Task<IActionResult> DashboardUpdate(int id, [FromBody] DashboardInput m, CancellationToken ct)
    {
        if (!Can(Perms.DashboardsManage)) return Forbid403();
        if (string.IsNullOrWhiteSpace(m.Name)) return BadRequest("Ad zorunlu.");
        var d = await _db.Dashboards.FindAsync(new object[] { id }, ct);
        if (d == null) return NotFound("Pano bulunamadı");
        ApplyBoard(d, m);
        await _db.SaveChangesAsync(ct);
        return Ok(new { d.Id });
    }

    [HttpDelete("dashboards/{id:int}")]
    public async Task<IActionResult> DashboardDelete(int id, CancellationToken ct)
    {
        if (!Can(Perms.DashboardsManage)) return Forbid403();
        var d = await _db.Dashboards.FindAsync(new object[] { id }, ct);
        if (d == null) return NotFound("Pano bulunamadı");
        _db.Dashboards.Remove(d);
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>İstatistik widget düzeni (React panosu). Boşsa varsayılan set tohumlanır.</summary>
    [HttpGet("stat-widgets")]
    public async Task<IActionResult> StatWidgets(CancellationToken ct)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var widgets = await _db.StatWidgets.AsNoTracking()
            .OrderBy(w => w.Y).ThenBy(w => w.X).ThenBy(w => w.SortOrder).ToListAsync(ct);
        if (widgets.Count == 0)
        {
            widgets = StatisticsController.DefaultWidgets();
            _db.StatWidgets.AddRange(widgets);
            await _db.SaveChangesAsync(ct);
        }
        var isAdmin = (await _settings.GetAsync(ct)).IsAdmin(User.FindFirst("sam")?.Value) || User.IsAppAdmin();
        return Ok(new
        {
            canEdit = isAdmin,
            widgets = widgets.Select(w => new { w.Id, w.Type, w.Source, w.Title, w.ConfigJson, w.X, w.Y, w.W, w.H })
        });
    }

    /// <summary>Dashboard tanımına göre servis Id listesi (görüntüleme sayfası için).</summary>
    [HttpGet("dashboard-services/{id:int}")]
    public async Task<IActionResult> DashboardServices(int id, CancellationToken ct)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var dash = await _db.Dashboards.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dash == null) return NotFound("Dashboard bulunamadı");

        // Elle seçilen servisler her zaman dahildir.
        var idSet = dash.GetServiceIds().ToHashSet();

        ServiceType type = default;
        bool hasType = !string.IsNullOrWhiteSpace(dash.TypeFilter)
            && Enum.TryParse<ServiceType>(dash.TypeFilter, out type);
        bool hasKw = !string.IsNullOrWhiteSpace(dash.KeywordFilter);
        var kw = dash.KeywordFilter?.Trim() ?? "";

        if (hasType || hasKw)
        {
            // Filtre adaylarını çek; her ikisi de seçiliyse KESİŞİM (tip VE keyword), tek seçiliyse o filtre.
            var candidates = await _db.Services.AsNoTracking()
                .Select(s => new { s.Id, s.Type, s.Keyword }).ToListAsync(ct);
            foreach (var s in candidates)
            {
                bool typeOk = !hasType || s.Type == type;
                bool kwOk = !hasKw || MonitoredService.SplitKeywords(s.Keyword)
                    .Contains(kw, StringComparer.OrdinalIgnoreCase);
                if (typeOk && kwOk) idSet.Add(s.Id);
            }
        }
        return Ok(new { name = dash.Name, serviceIds = idSet });
    }

    /// <summary>Rapor özeti: tarih aralığında tüm servislerin erişilebilirlik istatistikleri.</summary>
    [HttpGet("report-summary")]
    public async Task<IActionResult> ReportSummary([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        if (to <= from) return BadRequest("Bitiş tarihi başlangıçtan büyük olmalı.");
        // Saat verilmemişse (00:00) bitiş gününü dahil et; saat verilmişse aynen kullan
        var toEnd = to.TimeOfDay == TimeSpan.Zero ? to.Date.AddDays(1) : to;

        var stats = await _db.CheckResults.AsNoTracking()
            .Where(r => r.CheckedAt >= from && r.CheckedAt < toEnd)
            .GroupBy(r => r.ServiceId)
            .Select(g => new
            {
                ServiceId = g.Key,
                Total = g.Count(),
                // Erişilebilirlik: eşik aşımı (ERROR) kesinti değildir — uptime'a "up" sayılır.
                Up = g.Count(r => r.IsUp || r.Status == (int)Models.CheckStatus.Error
                                  || (r.Error != null && r.Error.StartsWith("Eşik aşıldı"))),
                Errors = g.Count(r => r.Status == (int)Models.CheckStatus.Error
                                  || (r.Error != null && r.Error.StartsWith("Eşik aşıldı"))),
                AvgMs = g.Average(r => (double)r.ResponseTimeMs),
                MaxMs = g.Max(r => r.ResponseTimeMs)
            })
            .ToDictionaryAsync(x => x.ServiceId, ct);

        var outages = await _db.Outages.AsNoTracking()
            .Where(o => o.StartedAt < toEnd && (o.EndedAt == null || o.EndedAt > from))
            .ToListAsync(ct);

        // Sağlık servisleri için aralık içi ortalama/peak CPU-RAM-Disk
        var healthStats = await _db.HealthMetrics.AsNoTracking()
            .Where(m => m.CheckedAt >= from && m.CheckedAt < toEnd)
            .GroupBy(m => m.ServiceId)
            .Select(g => new
            {
                ServiceId = g.Key,
                AvgCpu = g.Average(x => x.CpuPercent),
                MaxCpu = g.Max(x => x.CpuPercent),
                AvgRam = g.Average(x => x.RamPercent),
                MaxRam = g.Max(x => x.RamPercent),
                AvgDisk = g.Average(x => x.MaxDiskPercent),
                MaxDisk = g.Max(x => x.MaxDiskPercent)
            })
            .ToDictionaryAsync(x => x.ServiceId, ct);

        var services = await _db.Services.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, Type = s.Type.ToString(), s.Target, s.Port, s.Enabled, s.CapacityInfo, s.Keyword, s.Description })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var result = services.Select(s =>
        {
            stats.TryGetValue(s.Id, out var st);
            var svcOutages = outages.Where(o => o.ServiceId == s.Id).ToList();
            // Kesinti sürelerini rapor aralığına kırp
            var downtimeMin = svcOutages.Sum(o =>
            {
                var start = o.StartedAt < from ? from : o.StartedAt;
                var end = (o.EndedAt ?? now) > toEnd ? toEnd : (o.EndedAt ?? now);
                return Math.Max(0, (end - start).TotalMinutes);
            });
            return new
            {
                s.Id, s.Name, s.Type, s.Target, s.Port, s.Enabled, s.Keyword, s.Description,
                checkCount = st?.Total ?? 0,
                upCount = st?.Up ?? 0,
                uptimePercent = st == null || st.Total == 0 ? (double?)null : Math.Round(100.0 * st.Up / st.Total, 3),
                avgResponseMs = st == null ? (double?)null : Math.Round(st.AvgMs, 0),
                maxResponseMs = st?.MaxMs,
                outageCount = svcOutages.Count,
                downtimeMinutes = Math.Round(downtimeMin, 1),
                errorCount = st?.Errors ?? 0,
                capacityInfo = s.CapacityInfo,
                health = healthStats.TryGetValue(s.Id, out var h) ? new
                {
                    avgCpu = h.AvgCpu.HasValue ? Math.Round(h.AvgCpu.Value, 1) : (double?)null,
                    maxCpu = h.MaxCpu,
                    avgRam = h.AvgRam.HasValue ? Math.Round(h.AvgRam.Value, 1) : (double?)null,
                    maxRam = h.MaxRam,
                    avgDisk = h.AvgDisk.HasValue ? Math.Round(h.AvgDisk.Value, 1) : (double?)null,
                    maxDisk = h.MaxDisk
                } : null
            };
        });

        return Ok(new { from, to = toEnd, services = result });
    }

    /// <summary>Servis bazlı detay rapor: günlük erişilebilirlik serisi + kesinti listesi.</summary>
    [HttpGet("report/{id:int}")]
    public async Task<IActionResult> Report(int id, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var svc = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (svc == null) return NotFound("Servis bulunamadı");
        if (to <= from) return BadRequest("Bitiş tarihi başlangıçtan büyük olmalı.");
        var toEnd = to.TimeOfDay == TimeSpan.Zero ? to.Date.AddDays(1) : to;

        var daily = await _db.CheckResults.AsNoTracking()
            .Where(r => r.ServiceId == id && r.CheckedAt >= from && r.CheckedAt < toEnd)
            .GroupBy(r => r.CheckedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Total = g.Count(),
                // Eşik aşımı (ERROR) kesinti değil → uptime'a "up" sayılır.
                Up = g.Count(r => r.IsUp || r.Status == (int)Models.CheckStatus.Error
                                  || (r.Error != null && r.Error.StartsWith("Eşik aşıldı"))),
                AvgMs = g.Average(r => (double)r.ResponseTimeMs)
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        var outages = await _db.Outages.AsNoTracking()
            .Where(o => o.ServiceId == id && o.StartedAt < toEnd && (o.EndedAt == null || o.EndedAt > from))
            .OrderByDescending(o => o.StartedAt)
            .Select(o => new { o.StartedAt, o.EndedAt, o.FirstError })
            .ToListAsync(ct);

        return Ok(new
        {
            service = new { svc.Id, svc.Name, Type = svc.Type.ToString(), svc.Target, svc.Port },
            from, to = toEnd,
            days = daily.Select(d => new
            {
                date = d.Date.ToString("yyyy-MM-dd"),
                checkCount = d.Total,
                upCount = d.Up,
                uptimePercent = d.Total == 0 ? (double?)null : Math.Round(100.0 * d.Up / d.Total, 2),
                avgResponseMs = Math.Round(d.AvgMs, 0)
            }),
            outages
        });
    }

    /// <summary>Vault bağlantı testi: secret'a erişim + anahtar varlığı doğrulanır (değer asla dönmez).</summary>
    [HttpPost("vault-test/{credentialId:int}")]
    public async Task<IActionResult> VaultTest(int credentialId, CancellationToken ct)
    {
        if (!Can(Perms.CredentialsManage)) return Forbid403();
        var cred = await _db.Credentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == credentialId, ct);
        if (cred == null) return NotFound("Kimlik bilgisi bulunamadı");
        if (cred.SourceType != Models.CredentialSource.Vault) return BadRequest("Bu kimlik Vault kaynaklı değil.");

        var (error, resolvedUsername) = await VaultClient.TestAsync(cred, ct);
        return error == null
            ? Ok(new { ok = true, message = $"Vault erişimi başarılı. Çözülen kullanıcı adı: {resolvedUsername}" })
            : Ok(new { ok = false, message = error });
    }

    /// <summary>Ters proxy IP teşhisi: uygulamanın gördüğü IP + gelen proxy başlıkları.
    /// X-Forwarded-For nginx'ten geliyor mu, middleware uyguluyor mu — buradan kesin görülür.</summary>
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        string H(string name) => Request.Headers.TryGetValue(name, out var v) ? v.ToString() : "(yok)";
        return Ok(new
        {
            uygulamaninGorduguIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            xForwardedFor = H("X-Forwarded-For"),
            xRealIp = H("X-Real-IP"),
            xForwardedProto = H("X-Forwarded-Proto"),
            host = H("Host"),
            aciklama = "xForwardedFor '(yok)' ise nginx başlığı GÖNDERMİYOR (config/reload). " +
                       "Dolu ama uygulamaninGorduguIp hâlâ nginx ise middleware sorunu."
        });
    }

    // ================= Faz H: oturum bilgisi (React yetki/tema/dil) =================

    /// <summary>Oturumdaki kullanıcı: yetkiler + tema/dil + admin bilgisi. React menü/aksiyon görünürlüğü buradan beslenir.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        var sam = User.FindFirst("sam")?.Value ?? User.Identity?.Name;
        var isAuth = User?.Identity?.IsAuthenticated == true;
        var isAdmin = !settings.AuthEnabled || !isAuth || settings.IsAdmin(sam) || (isAuth && User!.IsAppAdmin());

        string theme = Request.Cookies["vmon_theme"] == "light" ? "light" : "dark";
        string lang = Request.Cookies["vmon_lang"] == "en" ? "en" : "tr";
        string? displayName = null;
        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (isAuth && !string.IsNullOrWhiteSpace(sam))
        {
            var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Sam == sam, ct);
            if (u != null)
            {
                displayName = u.DisplayName;
                perms = u.Permissions();
                if (!string.IsNullOrEmpty(u.Theme)) theme = u.Theme;
                if (!string.IsNullOrEmpty(u.Language)) lang = u.Language;
            }
        }
        if (isAdmin) foreach (var p in Perms.All) perms.Add(p.Key);   // admin = tüm yetkiler
        if (!isAuth || !settings.AuthEnabled) foreach (var p in Perms.All) perms.Add(p.Key); // açık mod

        return Ok(new
        {
            sam,
            displayName = displayName ?? sam,
            isAdmin,
            authEnabled = settings.AuthEnabled,
            mutabakatEnabled = settings.MutabakatEnabled,
            perms,
            theme,
            lang,
            companyName = settings.CompanyName,
            // Lisans Fazı L1: sol üst rozet + Hakkında kalan gün bilgisi buradan beslenir
            license = _lic.Current is { } lic
                ? new { edition = lic.Edition.ToString(), company = lic.Company, expires = lic.ExpiresAt.ToString("yyyy-MM-dd"), daysLeft = lic.DaysLeft }
                : null
        });
    }

    /// <summary>Profil bilgisi (oturumdaki kullanıcı).</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> ProfileGet(CancellationToken ct)
    {
        var sam = User.FindFirst("sam")?.Value;
        if (string.IsNullOrWhiteSpace(sam)) return Forbid403();
        var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Sam == sam, ct);
        if (u == null) return NotFound("Kullanıcı bulunamadı");
        var s = await _settings.GetAsync(ct);
        return Ok(new
        {
            u.Sam, u.DisplayName, u.Email, u.Phone, u.IsLocal,
            minPasswordLength = s.MinPasswordLength,
            requireComplexity = s.RequirePasswordComplexity
        });
    }

    public record ProfileInput(string? Email, string? Phone, string? CurrentPassword, string? NewPassword, string? ConfirmPassword);

    /// <summary>Profil güncelle (+ yerel kullanıcı için şifre değişimi — klasik Profile POST semantiği).</summary>
    [HttpPost("profile")]
    public async Task<IActionResult> ProfileSave([FromBody] ProfileInput m, CancellationToken ct)
    {
        var sam = User.FindFirst("sam")?.Value;
        if (string.IsNullOrWhiteSpace(sam)) return Forbid403();
        var u = await _db.AppUsers.FirstOrDefaultAsync(x => x.Sam == sam, ct);
        if (u == null) return NotFound("Kullanıcı bulunamadı");

        u.Email = string.IsNullOrWhiteSpace(m.Email) ? null : m.Email.Trim();
        u.Phone = string.IsNullOrWhiteSpace(m.Phone) ? null : m.Phone.Trim();

        if (u.IsLocal && !string.IsNullOrEmpty(m.NewPassword))
        {
            if (!PasswordHasher.Verify(m.CurrentPassword ?? "", u.PasswordHash))
                return BadRequest("Mevcut şifre hatalı.");
            if (m.NewPassword != m.ConfirmPassword)
                return BadRequest("Yeni şifre ile tekrarı eşleşmiyor.");
            var s = await _settings.GetAsync(ct);
            var (ok, err) = PasswordHasher.ValidatePolicy(m.NewPassword, s.MinPasswordLength, s.RequirePasswordComplexity);
            if (!ok) return BadRequest(err);
            var history = (u.PasswordHistory ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (PasswordHasher.Verify(m.NewPassword, u.PasswordHash) || history.Any(h => PasswordHasher.Verify(m.NewPassword, h)))
                return BadRequest($"Yeni parola son {s.PasswordHistoryCount} parolanızdan farklı olmalı.");
            if (s.PasswordHistoryCount > 0 && !string.IsNullOrEmpty(u.PasswordHash))
            {
                history.Insert(0, u.PasswordHash!);
                u.PasswordHistory = string.Join("\n", history.Take(s.PasswordHistoryCount));
            }
            u.PasswordHash = PasswordHasher.Hash(m.NewPassword);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("user.password.change", u.Sam, "Yerel kullanıcı parolasını değiştirdi (React)", true, ct: ct);
            return Ok(new { ok = true, message = "Profil ve şifre güncellendi." });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, message = "Profil güncellendi." });
    }

    // ================= Faz G: Ayarlar (React) =================

    /// <summary>Tüm ayarlar (yalnız admin). Sırlar asla dönmez — yalnız has* bayrakları.</summary>
    /// <summary>Mevcut lisans durumu + bu makinenin kodu (Ayarlar > Lisans kartı). Yalnız admin.</summary>
    [HttpGet("license")]
    public async Task<IActionResult> LicenseGet(CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        return Ok(new
        {
            machineCode = LicenseService.MachineCode,
            status = _lic.Status.ToString(),
            license = _lic.Current is { } l
                ? new
                {
                    edition = l.Edition.ToString(), company = l.Company,
                    issued = l.IssuedAt.ToString("yyyy-MM-dd"), expires = l.ExpiresAt.ToString("yyyy-MM-dd"),
                    daysLeft = l.DaysLeft,
                    maxMonitors = l.MaxMonitors == int.MaxValue ? (int?)null : l.MaxMonitors,
                    maxUsers = l.MaxUsers == int.MaxValue ? (int?)null : l.MaxUsers,
                    maxDashboards = l.MaxDashboards == int.MaxValue ? (int?)null : l.MaxDashboards,
                    sqliteOnly = l.SqliteOnly, emailOnly = l.EmailOnlyNotifications, siem = l.SiemAllowed
                }
                : null
        });
    }

    /// <summary>Çalışan uygulamadan lisans key değiştir — paket yükseltme/düşürme (Ayarlar > Lisans). Yalnız admin.
    /// Basic'e DÜŞÜŞTE mevcut veri (Oracle vb.) SQLite'a taşınmaz; key kabul edilir ama uyarı döner.</summary>
    public record LicenseApplyInput(string Key);

    [HttpPost("license")]
    public async Task<IActionResult> LicenseApply([FromBody] LicenseApplyInput input, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();

        var prev = _lic.Current?.Edition;
        var (ok, err) = _lic.Apply(input?.Key ?? "");
        if (!ok) return BadRequest(err);

        await _audit.LogAsync("license.change", _lic.Current!.Company,
            $"Lisans değişti: {prev?.ToString() ?? "—"} → {_lic.Current.Edition}, bitiş {_lic.Current.ExpiresAt:yyyy-MM-dd}", ct: ct);

        // Basic'e geçişte, mevcut kurulum SQLite değilse: key geçerli ama Basic yalnız SQLite destekler → bilgilendir.
        string? warn = null;
        if (_lic.Current.SqliteOnly && _lic.Current.Edition != prev)
            warn = "Basic paket yalnız SQLite'ı destekler. Mevcut veritabanınız farklıysa çalışmaya devam eder ancak yeni Basic kurulumlar SQLite ile yapılmalıdır. Ayrıca Basic limitlerini (40 izleme, 1 kullanıcı, 5 dashboard, yalnız e-posta, SIEM kapalı) aşan yapılandırmalar için ilgili işlemler kısıtlanır.";

        return Ok(new
        {
            ok = true,
            edition = _lic.Current.Edition.ToString(),
            expires = _lic.Current.ExpiresAt.ToString("dd.MM.yyyy"),
            message = $"Lisans güncellendi: {_lic.Current.Edition} (bitiş {_lic.Current.ExpiresAt:dd.MM.yyyy}).",
            warn
        });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> SettingsGet(CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        return Ok(new
        {
            // İzleme
            s.CheckIntervalMinutes, s.FailureThreshold, s.HistoryRetentionDays,
            // E-posta
            s.EmailEnabled, s.SmtpHost, s.SmtpPort, s.MailFrom, s.MailRecipients,
            // LDAP + genel
            s.AuthEnabled, s.LdapAuthHost, s.LdapAuthPort, s.LdapAuthUseSsl, s.LdapAuthDomain,
            s.LdapAuthBaseDn, s.LdapAuthGroupDn, s.AdminUsers, s.CompanyName, s.LdapSyncCredentialId,
            // OTP
            s.OtpEnabled, s.OtpChannel,
            // Yedekleme
            s.BackupEnabled, s.BackupPath, s.BackupHour, s.BackupMinute, s.BackupRetentionCount, s.BackupEncrypt,
            hasBackupPassword = !string.IsNullOrEmpty(s.BackupPasswordEncrypted),
            // EOL
            s.EolEnabled, s.EolWarnDays, s.EolProxyUrl,
            // Güvenlik
            s.MinPasswordLength, s.RequirePasswordComplexity, s.PasswordHistoryCount,
            s.TrustInternalTlsCertificates, s.MaxLoginAttempts, s.LockoutMinutes, s.AuditRetentionDays,
            // SIEM
            s.SyslogEnabled, s.SyslogHost, s.SyslogPort, s.SyslogTcp,
            // SMS / WhatsApp (global Twilio)
            s.SmsEnabled, s.SmsProvider, s.SmsAccountSid, s.SmsFrom, s.SmsRecipients,
            hasSmsToken = !string.IsNullOrEmpty(s.SmsAuthTokenEncrypted),
            s.WhatsappEnabled, s.WhatsappAccountSid, s.WhatsappFrom, s.WhatsappRecipients,
            s.WhatsappAlarmTemplateSid, s.WhatsappWebhookSecret,
            hasWhatsappToken = !string.IsNullOrEmpty(s.WhatsappAuthTokenEncrypted),
            // Mutabakat
            s.MutabakatEnabled, s.MutabakatOwnCompany, s.MutabakatVendorCompany,
            // Logo (yalnız görüntüleme)
            s.LoginLogoFile
        });
    }

    public record SettingsSaveInput(MonitorSettings Model, string? NewSmsToken, string? NewWhatsappToken, string? NewBackupPassword);

    /// <summary>Ayarları kaydet — klasik Save ile aynı semantik (OTP kilitlenme koruması, sır koruma, syslog/TLS anında uygula).</summary>
    [HttpPost("settings")]
    public async Task<IActionResult> SettingsSave([FromBody] SettingsSaveInput input,
        [FromServices] SyslogService syslog, CancellationToken ct)
    {
        var current = await _settings.GetAsync(ct);
        if (!AdminAllowed(current)) return Forbid403();
        var model = input.Model ?? new MonitorSettings();

        // OTP kilitlenme koruması (klasik Save'dekiyle birebir)
        if (model.OtpEnabled)
        {
            var needEmail = string.Equals(model.OtpChannel, "Email", StringComparison.OrdinalIgnoreCase);
            var adminList = (model.AdminUsers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var users = await _db.AppUsers.AsNoTracking().Where(u => u.IsActive).ToListAsync(ct);
            bool IsAdminSam(string sam) => adminList.Length == 0 || adminList.Any(a => string.Equals(a, sam, StringComparison.OrdinalIgnoreCase));
            var hasContact = users.Any(u => IsAdminSam(u.Sam) &&
                (needEmail ? !string.IsNullOrWhiteSpace(u.Email) : !string.IsNullOrWhiteSpace(u.Phone)));
            if (!hasContact)
                return BadRequest(needEmail
                    ? "OTP açılamadı: E-posta kanalı için en az bir admin kullanıcının e-posta adresi olmalı."
                    : "OTP açılamadı: SMS/WhatsApp kanalı için en az bir admin kullanıcının telefon numarası olmalı.");
        }

        // Lisans: SIEM/Syslog aktarımı Basic pakette kapalı
        if (_lic.Current is { SiemAllowed: false } && model.SyslogEnabled)
            return BadRequest("Lisans: SIEM/Syslog log aktarımı Standard ve Enterprise paketlerde kullanılabilir. Basic pakette bu ayar açılamaz.");

        // Sistem alanları formdan taşınmaz — mevcut değerler korunur
        model.LoginLogoFile = current.LoginLogoFile;
        model.TwilioChannelsMigrated = current.TwilioChannelsMigrated;
        model.SmsAuthTokenEncrypted = string.IsNullOrWhiteSpace(input.NewSmsToken)
            ? current.SmsAuthTokenEncrypted : CryptoHelper.Encrypt(input.NewSmsToken.Trim());
        model.WhatsappAuthTokenEncrypted = string.IsNullOrWhiteSpace(input.NewWhatsappToken)
            ? current.WhatsappAuthTokenEncrypted : CryptoHelper.Encrypt(input.NewWhatsappToken.Trim());
        model.BackupPasswordEncrypted = string.IsNullOrWhiteSpace(input.NewBackupPassword)
            ? current.BackupPasswordEncrypted : CryptoHelper.Encrypt(input.NewBackupPassword.Trim());

        await _settings.SaveAsync(model, ct);
        syslog.Configure(model);
        VaultClient.TrustInternalCertificates = model.TrustInternalTlsCertificates;
        await _audit.LogAsync("settings.save", null,
            $"TLS güven={model.TrustInternalTlsCertificates}, kilit={model.MaxLoginAttempts}/{model.LockoutMinutes}dk, denetim saklama={model.AuditRetentionDays}g, auth={model.AuthEnabled}", ct: ct);
        return Ok(new { ok = true });
    }

    /// <summary>Kayıtlı ayarlarla syslog test mesajı (yalnız admin).</summary>
    [HttpPost("syslog-test")]
    public async Task<IActionResult> SyslogTest([FromServices] SyslogService syslog, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        if (!s.SyslogEnabled || string.IsNullOrWhiteSpace(s.SyslogHost))
            return Ok(new { ok = false, message = "Önce SIEM/Syslog ayarlarını doldurup kaydedin." });
        syslog.Configure(s);
        syslog.Test();
        await _audit.LogAsync("syslog.test", null, $"Test mesajı gönderildi → {s.SyslogHost}:{s.SyslogPort}", ct: ct);
        return Ok(new { ok = true, message = $"Test mesajı gönderildi → {s.SyslogHost}:{s.SyslogPort} ({(s.SyslogTcp ? "TCP" : "UDP")})" });
    }

    /// <summary>EOL verisini dosyadan içe aktar (kapalı ağ için — klasik EolImport paritesi).</summary>
    [HttpPost("eol-import")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> EolImport(IFormFile? eolFile, [FromServices] EolService eol, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        if (eolFile == null || eolFile.Length == 0) return BadRequest("Dosya seçilmedi.");
        using var sr = new StreamReader(eolFile.OpenReadStream());
        var json = await sr.ReadToEndAsync(ct);
        var (ok, msg) = eol.Import(json);
        await _audit.LogAsync("eol.import", null, msg, ok, ct: ct);
        return Ok(new { ok, message = msg });
    }

    /// <summary>EOL verisini şimdi senkronize et (yalnız admin).</summary>
    [HttpPost("eol-sync")]
    public async Task<IActionResult> EolSync([FromServices] EolService eol, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var (ok, msg) = await eol.SyncAsync(s.EolProxyUrl, ct);
        await _audit.LogAsync("eol.sync", null, msg, ok, ct: ct);
        return Ok(new { ok, message = msg });
    }

    // ================= Faz G2: Bildirim Kanalları + Yedekleme + Logo (React) =================

    /// <summary>Bildirim kanalları (entegrasyonlar) — sırlar dönmez.</summary>
    [HttpGet("channels")]
    public async Task<IActionResult> ChannelsList(CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var list = await _db.SmsProviders.AsNoTracking().OrderBy(p => p.Kind).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Kind, p.Recipients, p.TemplateSid, p.Method, p.Url, p.ContentType,
                p.Body, p.Headers, p.AuthType, p.Username, p.Sender, p.SuccessContains, p.Enabled,
                hasPassword = p.PasswordEncrypted != "", hasApiKey = p.ApiKeyEncrypted != ""
            }).ToListAsync(ct);
        return Ok(list);
    }

    public record ChannelInput(string Name, string Kind, string? Recipients, string? TemplateSid,
        string Method, string Url, string ContentType, string? Body, string? Headers,
        string AuthType, string? Username, string? Sender, string? SuccessContains, bool Enabled,
        string? NewPassword, string? NewApiKey);

    private async Task<string?> ValidateChannelAsync(int id, ChannelInput m, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return "Entegrasyon adı zorunlu.";
        if (string.Equals(m.Name.Trim(), "Twilio", StringComparison.OrdinalIgnoreCase)) return "'Twilio' adı yerleşiktir; farklı bir ad kullanın.";
        if (string.IsNullOrWhiteSpace(m.Url)) return "URL zorunlu.";
        if (await _db.SmsProviders.AnyAsync(p => p.Id != id && p.Name == m.Name, ct)) return "Bu adda bir sağlayıcı zaten var.";
        return null;
    }

    private static void ApplyChannel(SmsProvider p, ChannelInput m)
    {
        var kind = string.Equals(m.Kind, "Voice", StringComparison.OrdinalIgnoreCase) ? "Ivr" : m.Kind;
        var allowed = new[] { "Sms", "Whatsapp", "Ivr" };
        p.Kind = allowed.Contains(kind, StringComparer.OrdinalIgnoreCase) ? kind : "Sms";
        p.Name = m.Name.Trim();
        p.Recipients = m.Recipients; p.TemplateSid = m.TemplateSid;
        p.Method = m.Method; p.Url = m.Url; p.ContentType = m.ContentType;
        p.Body = m.Body; p.Headers = m.Headers; p.AuthType = m.AuthType;
        p.Username = m.Username ?? ""; p.Sender = m.Sender ?? ""; p.SuccessContains = m.SuccessContains;
        p.Enabled = m.Enabled;
        if (!string.IsNullOrEmpty(m.NewPassword)) p.PasswordEncrypted = CryptoHelper.Encrypt(m.NewPassword);
        if (!string.IsNullOrEmpty(m.NewApiKey)) p.ApiKeyEncrypted = CryptoHelper.Encrypt(m.NewApiKey);
    }

    [HttpPost("channels")]
    public async Task<IActionResult> ChannelCreate([FromBody] ChannelInput m, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        // Lisans: Basic paket yalnız e-posta bildirimi destekler — SMS/WhatsApp/IVR kanalı eklenemez
        if (_lic.Current is { EmailOnlyNotifications: true })
            return BadRequest("Lisans: Basic paket yalnız e-posta bildirimi destekler. SMS/WhatsApp/IVR entegrasyonları için Standard veya Enterprise pakete geçin.");
        var err = await ValidateChannelAsync(0, m, ct);
        if (err != null) return BadRequest(err);
        var p = new SmsProvider { PasswordEncrypted = "", ApiKeyEncrypted = "" };
        ApplyChannel(p, m);
        _db.SmsProviders.Add(p);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("integration.create", p.Name, p.Kind, ct: ct);
        return Ok(new { p.Id });
    }

    [HttpPut("channels/{id:int}")]
    public async Task<IActionResult> ChannelUpdate(int id, [FromBody] ChannelInput m, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var p = await _db.SmsProviders.FindAsync(new object[] { id }, ct);
        if (p == null) return NotFound("Entegrasyon bulunamadı");
        var err = await ValidateChannelAsync(id, m, ct);
        if (err != null) return BadRequest(err);
        ApplyChannel(p, m);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("integration.update", p.Name, p.Kind, ct: ct);
        return Ok(new { p.Id });
    }

    [HttpDelete("channels/{id:int}")]
    public async Task<IActionResult> ChannelDelete(int id, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var p = await _db.SmsProviders.FindAsync(new object[] { id }, ct);
        if (p == null) return NotFound("Entegrasyon bulunamadı");
        _db.SmsProviders.Remove(p);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("integration.delete", p.Name, ct: ct);
        return Ok(new { ok = true });
    }

    [HttpPost("channels/{id:int}/toggle")]
    public async Task<IActionResult> ChannelToggle(int id, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var p = await _db.SmsProviders.FindAsync(new object[] { id }, ct);
        if (p == null) return NotFound("Entegrasyon bulunamadı");
        p.Enabled = !p.Enabled;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("integration.toggle", p.Name, p.Enabled ? "aktif" : "pasif", ct: ct);
        return Ok(new { enabled = p.Enabled });
    }

    public record ChannelTestInput(string To);

    [HttpPost("channels/{id:int}/test")]
    public async Task<IActionResult> ChannelTest(int id, [FromBody] ChannelTestInput m,
        [FromServices] SmsService sms, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var p = await _db.SmsProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return NotFound("Entegrasyon bulunamadı");
        if (string.IsNullOrWhiteSpace(m.To)) return BadRequest("Test için bir alıcı girin.");
        var (ok, msg) = await sms.SendViaIntegrationAsync(p, new[] { m.To.Trim() }, "vMon test mesajı ✅");
        await _audit.LogAsync("integration.test", p.Name, msg, ok, ct: ct);
        return Ok(new { ok, message = msg });
    }

    /// <summary>Yedek listesi (yalnız SQLite'ta anlamlı).</summary>
    [HttpGet("backups")]
    public async Task<IActionResult> BackupsList([FromServices] BackupService backup, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        return Ok(new
        {
            isSqlite = backup.IsSqlite,
            path = s.BackupPath,
            files = backup.List(s.BackupPath).Select(f => new { f.Name, f.SizeMb, f.ModifiedUtc })
        });
    }

    [HttpPost("backups/now")]
    public async Task<IActionResult> BackupNowApi([FromServices] BackupService backup, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var pwd = string.IsNullOrWhiteSpace(s.BackupPasswordEncrypted) ? null : CryptoHelper.Decrypt(s.BackupPasswordEncrypted);
        var (file, error) = await backup.BackupNowAsync(s.BackupPath, s.BackupRetentionCount, s.BackupEncrypt, pwd, ct);
        if (error != null) return Ok(new { ok = false, message = "Yedek alınamadı: " + error });
        await _audit.LogAsync("backup.create", null, file, ct: ct);
        return Ok(new { ok = true, message = "Yedek alındı: " + file });
    }

    public record BackupFileInput(string File);

    [HttpPost("backups/delete")]
    public async Task<IActionResult> BackupDelete([FromBody] BackupFileInput m, [FromServices] BackupService backup, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var path = backup.SafeBackupPath(s.BackupPath, m.File);
        if (path == null) return NotFound("Yedek bulunamadı");
        try { System.IO.File.Delete(path); await _audit.LogAsync("backup.delete", null, Path.GetFileName(path), ct: ct); } catch { }
        return Ok(new { ok = true });
    }

    /// <summary>Yedeği geri yükle — başarılıysa uygulama YENİDEN BAŞLAR (birkaç sn erişilemez).</summary>
    [HttpPost("backups/restore")]
    public async Task<IActionResult> BackupRestore([FromBody] BackupFileInput m,
        [FromServices] BackupService backup, [FromServices] IHostApplicationLifetime life, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var path = backup.SafeBackupPath(s.BackupPath, m.File);
        if (path == null) return NotFound("Yedek bulunamadı");
        var pwd = string.IsNullOrWhiteSpace(s.BackupPasswordEncrypted) ? null : CryptoHelper.Decrypt(s.BackupPasswordEncrypted);
        var (ok, error) = await backup.RestoreAsync(path, pwd, ct);
        if (!ok) return Ok(new { ok = false, message = "Geri yükleme başarısız: " + error });
        await _audit.LogAsync("backup.restore", null, Path.GetFileName(path), true, ct: ct);
        if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
            _ = Task.Run(async () => { await Task.Delay(1500); Environment.Exit(1); });
        else
            _ = Task.Run(async () => { await Task.Delay(1500); life.StopApplication(); });
        return Ok(new { ok = true, message = "Geri yükleme tamam. Uygulama yeniden başlatılıyor — birkaç saniye içinde sayfayı yenileyin." });
    }

    /// <summary>Giriş ekranı logosu yükle (PNG/JPG/GIF/WEBP, ≤2 MB — SVG XSS riski nedeniyle kabul edilmez).</summary>
    [HttpPost("logo")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> LogoUpload(IFormFile? logoFile, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        if (logoFile == null || logoFile.Length == 0) return BadRequest("Dosya seçilmedi.");
        var ext = Path.GetExtension(logoFile.FileName).ToLowerInvariant();
        var allowed = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
        if (!allowed.Contains(ext)) return BadRequest("Geçersiz dosya türü. İzin verilenler: PNG, JPG, GIF, WEBP.");
        if (logoFile.Length > 2 * 1024 * 1024) return BadRequest("Logo en fazla 2 MB olabilir.");

        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDir);
        foreach (var old in Directory.GetFiles(dataDir, "login-logo.*"))
            try { System.IO.File.Delete(old); } catch { }
        var fileName = "login-logo" + ext;
        using (var fs = System.IO.File.Create(Path.Combine(dataDir, fileName)))
            await logoFile.CopyToAsync(fs, ct);
        s.LoginLogoFile = fileName;
        await _settings.SaveAsync(s, ct);
        await _audit.LogAsync("settings.logo", fileName, "Giriş ekranı logosu güncellendi.", ct: ct);
        return Ok(new { ok = true, file = fileName });
    }

    [HttpDelete("logo")]
    public async Task<IActionResult> LogoRemove([FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!AdminAllowed(s)) return Forbid403();
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        foreach (var old in Directory.GetFiles(dataDir, "login-logo.*"))
            try { System.IO.File.Delete(old); } catch { }
        s.LoginLogoFile = "";
        await _settings.SaveAsync(s, ct);
        await _audit.LogAsync("settings.logo", null, "Giriş ekranı logosu kaldırıldı.", ct: ct);
        return Ok(new { ok = true });
    }

    // ================= Faz F: Denetim + Kullanıcılar + Kimlik Bilgileri (React) =================

    /// <summary>Denetim kaydı (yalnız admin) — EF tabanlı, SAĞLAYICI-BAĞIMSIZ (klasik ekrandaki ham SQLite
    /// sorgusunun aksine Oracle/MSSQL/PG/MySQL'de de çalışır).</summary>
    [HttpGet("audit")]
    public async Task<IActionResult> AuditList([FromQuery] string? q, [FromQuery] string? act,
        [FromQuery] int days = 0, [FromQuery] int take = 500,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
    {
        if (!User.IsAppAdmin()) return Forbid403();
        take = Math.Clamp(take, 50, 20000);
        Response.Headers["Cache-Control"] = "no-store";

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();
        // Tarih aralığı (dışa aktarım için): from/to doluysa days yok sayılır; to günü DAHİL
        if (from.HasValue) query = query.Where(a => a.At >= from.Value);
        if (to.HasValue)
        {
            var toEnd = to.Value.TimeOfDay == TimeSpan.Zero ? to.Value.Date.AddDays(1) : to.Value;
            query = query.Where(a => a.At < toEnd);
        }
        if (days > 0 && !from.HasValue && !to.HasValue)
        {
            var since = DateTime.UtcNow.AddDays(-Math.Min(days, 3650));
            query = query.Where(a => a.At >= since);
        }
        if (!string.IsNullOrWhiteSpace(act))
        {
            var a0 = act.Trim();
            query = query.Where(a => a.Action == a0);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(a => a.User.Contains(t)
                || (a.Target != null && a.Target.Contains(t))
                || (a.Detail != null && a.Detail.Contains(t))
                || (a.Ip != null && a.Ip.Contains(t)));
        }
        var rows = await query.OrderByDescending(a => a.Id).Take(take)
            .Select(a => new { a.Id, a.At, a.User, a.Ip, a.Action, a.Target, a.Detail, a.Success })
            .ToListAsync(ct);
        var actions = await _db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().ToListAsync(ct);
        actions.Sort(StringComparer.OrdinalIgnoreCase);

        // Denetim kaydına erişim de loglanır (PCI DSS 10.2.1.3)
        await _audit.LogAsync("audit.view", null,
            (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(act) && days <= 0)
                ? "Denetim kaydı görüntülendi" : $"Denetim kaydı görüntülendi (filtre: q='{q}', act='{act}', gün={days})", ct: ct);

        return Ok(new { rows, actions });
    }

    /// <summary>Hash-zincir bütünlük doğrulaması (yalnız admin).</summary>
    [HttpPost("audit/verify")]
    public async Task<IActionResult> AuditVerify(CancellationToken ct)
    {
        if (!User.IsAppAdmin()) return Forbid403();
        var (ok, msg, _) = await AuditService.VerifyChainAsync(_db);
        await _audit.LogAsync("audit.verify", null, msg, ok, ct: ct);
        return Ok(new { ok, message = msg });
    }

    /// <summary>Kullanıcı listesi + yetki kataloğu (yalnız admin).</summary>
    [HttpGet("users")]
    public async Task<IActionResult> UsersList(CancellationToken ct)
    {
        if (!User.IsAppAdmin()) return Forbid403();
        var users = await _db.AppUsers.AsNoTracking().OrderBy(u => u.Sam)
            .Select(u => new { u.Id, u.Sam, u.DisplayName, u.Email, u.Phone, u.PermissionsCsv, u.IsActive, u.IsLocal, u.LastLogin })
            .ToListAsync(ct);
        var settings = await _settings.GetAsync(ct);
        return Ok(new
        {
            users,
            adminUsers = settings.AdminUsers,
            allPerms = Perms.All.Select(p => new { key = p.Key, label = p.Label })
        });
    }

    public record UserUpdateInput(string[]? Perms, string? Phone, string? Email);

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UserUpdate(int id, [FromBody] UserUpdateInput m, CancellationToken ct)
    {
        if (!User.IsAppAdmin()) return Forbid403();
        var u = await _db.AppUsers.FindAsync(new object[] { id }, ct);
        if (u == null) return NotFound("Kullanıcı bulunamadı");
        var valid = Perms.All.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        u.PermissionsCsv = string.Join(",", (m.Perms ?? Array.Empty<string>()).Where(p => valid.Contains(p)).Distinct());
        u.Phone = string.IsNullOrWhiteSpace(m.Phone) ? null : m.Phone.Trim();
        u.Email = string.IsNullOrWhiteSpace(m.Email) ? null : m.Email.Trim();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("user.permissions", u.Sam, "Yetkiler: " + (string.IsNullOrEmpty(u.PermissionsCsv) ? "(yok)" : u.PermissionsCsv), ct: ct);
        return Ok(new { ok = true });
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> UserDelete(int id, CancellationToken ct)
    {
        if (!User.IsAppAdmin()) return Forbid403();
        var u = await _db.AppUsers.FindAsync(new object[] { id }, ct);
        if (u == null) return NotFound("Kullanıcı bulunamadı");
        _db.AppUsers.Remove(u);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("user.delete", u.Sam, ct: ct);
        return Ok(new { ok = true });
    }

    /// <summary>LDAP grup senkronizasyonu (klasik Sync ile aynı davranış; JSON döner).</summary>
    [HttpPost("users/sync")]
    public async Task<IActionResult> UsersSync([FromServices] LdapAuthService ldap, CancellationToken ct)
    {
        if (!User.IsAppAdmin()) return Forbid403();
        var settings = await _settings.GetAsync(ct);
        Credential? syncCred = settings.LdapSyncCredentialId.HasValue
            ? await _db.Credentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == settings.LdapSyncCredentialId.Value, ct)
            : null;
        var (error, members) = await Task.Run(() => ldap.ListGroupMembers(settings, syncCred), ct);
        if (error != null) return Ok(new { ok = false, message = "Senkronizasyon başarısız: " + error });

        // Lisans limiti: Basic 1 / Standard 5 / Enterprise sınırsız kullanıcı
        if (_lic.Current is { } lim && lim.MaxUsers != int.MaxValue && members.Count > lim.MaxUsers)
            return Ok(new { ok = false, message = $"Lisans limiti: {lim.Edition} paket en fazla {lim.MaxUsers} kullanıcı destekler; LDAP grubunda {members.Count} üye var. Daha fazla kullanıcı için üst pakete geçin." });

        var existing = await _db.AppUsers.ToDictionaryAsync(u => u.Sam, StringComparer.OrdinalIgnoreCase, ct);
        var inGroup = members.Select(m => m.Sam).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0, reactivated = 0;
        foreach (var m in members)
        {
            if (existing.TryGetValue(m.Sam, out var u))
            {
                if (!string.IsNullOrWhiteSpace(m.DisplayName)) u.DisplayName = m.DisplayName;
                if (!string.IsNullOrWhiteSpace(m.Email)) u.Email = m.Email;
                if (!u.IsActive) { u.IsActive = true; reactivated++; }
            }
            else
            {
                _db.AppUsers.Add(new AppUser { Sam = m.Sam, DisplayName = m.DisplayName, Email = m.Email, PermissionsCsv = Perms.DashboardsView, IsActive = true });
                added++;
            }
        }
        int deactivated = 0;
        foreach (var u in existing.Values)
            if (u.IsActive && !u.IsLocal && !inGroup.Contains(u.Sam)) { u.IsActive = false; deactivated++; }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("user.sync", null,
            $"{members.Count} grup üyesi; {added} yeni, {reactivated} yeniden etkin, {deactivated} pasifleştirildi.", ct: ct);
        return Ok(new { ok = true, message = $"LDAP senkronizasyonu tamam: {members.Count} üye ({added} yeni, {deactivated} pasifleştirildi)." });
    }

    /// <summary>Kimlik bilgileri listesi (sırlar asla dönmez; yalnız var/yok bilgisi).</summary>
    [HttpGet("credentials")]
    public async Task<IActionResult> CredentialsList(CancellationToken ct)
    {
        if (!Can(Perms.CredentialsManage)) return Forbid403();
        var list = await _db.Credentials.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id, c.Name, c.Username, c.Domain, c.Description,
                sourceType = c.SourceType.ToString(),
                c.VaultUrl, c.VaultKey, c.VaultUserKey,
                hasPassword = c.PasswordEncrypted != null && c.PasswordEncrypted != "",
                hasToken = c.VaultTokenEncrypted != null && c.VaultTokenEncrypted != ""
            }).ToListAsync(ct);
        return Ok(list);
    }

    public record CredentialInput(string Name, string Username, string? Domain, string? Description,
        string SourceType, string? NewPassword, string? VaultUrl, string? NewVaultToken, string? VaultKey, string? VaultUserKey);

    private static string? ApplyCredential(Credential c, CredentialInput m, ISecretProtector secrets)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return "Ad zorunlu.";
        if (!Enum.TryParse<CredentialSource>(m.SourceType, true, out var src)) return "Geçersiz kaynak türü.";
        c.Name = m.Name.Trim();
        c.Username = m.Username?.Trim() ?? "";
        c.Domain = string.IsNullOrWhiteSpace(m.Domain) ? null : m.Domain.Trim();
        c.Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim();
        c.SourceType = src;
        c.VaultUrl = string.IsNullOrWhiteSpace(m.VaultUrl) ? null : m.VaultUrl.Trim();
        c.VaultKey = string.IsNullOrWhiteSpace(m.VaultKey) ? null : m.VaultKey.Trim();
        c.VaultUserKey = string.IsNullOrWhiteSpace(m.VaultUserKey) ? null : m.VaultUserKey.Trim();
        if (!string.IsNullOrEmpty(m.NewPassword)) c.PasswordEncrypted = secrets.Protect(m.NewPassword);
        if (!string.IsNullOrEmpty(m.NewVaultToken)) c.VaultTokenEncrypted = secrets.Protect(m.NewVaultToken);
        return null;
    }

    [HttpPost("credentials")]
    public async Task<IActionResult> CredentialCreate([FromBody] CredentialInput m, [FromServices] ISecretProtector secrets, CancellationToken ct)
    {
        if (!Can(Perms.CredentialsManage)) return Forbid403();
        var c = new Credential();
        var err = ApplyCredential(c, m, secrets);
        if (err != null) return BadRequest(err);
        _db.Credentials.Add(c);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("credential.create", c.Name, ct: ct);
        return Ok(new { c.Id });
    }

    [HttpPut("credentials/{id:int}")]
    public async Task<IActionResult> CredentialUpdate(int id, [FromBody] CredentialInput m, [FromServices] ISecretProtector secrets, CancellationToken ct)
    {
        if (!Can(Perms.CredentialsManage)) return Forbid403();
        var c = await _db.Credentials.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound("Kimlik bilgisi bulunamadı");
        var err = ApplyCredential(c, m, secrets);
        if (err != null) return BadRequest(err);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("credential.update", c.Name, ct: ct);
        return Ok(new { c.Id });
    }

    [HttpDelete("credentials/{id:int}")]
    public async Task<IActionResult> CredentialDelete(int id, CancellationToken ct)
    {
        if (!Can(Perms.CredentialsManage)) return Forbid403();
        var c = await _db.Credentials.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound("Kimlik bilgisi bulunamadı");
        _db.Credentials.Remove(c);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("credential.delete", c.Name, ct: ct);
        return Ok(new { ok = true });
    }

    /// <summary>Nullable double ortalaması (kova özetleme yardımcıcısı).</summary>
    private static double? Avg(IEnumerable<double?> xs)
    {
        var l = xs.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return l.Count > 0 ? Math.Round(l.Average(), 1) : (double?)null;
    }

    private bool Can(string perm) => User.Can(perm);
    private IActionResult Forbid403()
    {
        // Yetkisiz API erişimi denetim kaydına yazılır (PCI DSS 10.2.1.4, NIST AU-2)
        try { _audit.LogAsync("access.denied", Request.Path, "API yetki reddi", false).GetAwaiter().GetResult(); } catch { }
        return StatusCode(403, "Bu işlem için yetkiniz yok.");
    }

    /// <summary>Oturum açık modda yalnızca admin erişebilen yardımcılar için kontrol.</summary>
    private bool AdminAllowed(MonitorSettings settings) =>
        User?.Identity?.IsAuthenticated != true
        || settings.IsAdmin(User.FindFirst("sam")?.Value);

    /// <summary>Ayarlar ekranındaki "Test Girişi" — kayıtlı LDAP ayarlarıyla doğrular (oturum açmaz).</summary>
    [HttpPost("test-ldap-login")]
    public async Task<IActionResult> TestLdapLogin([FromForm] string username, [FromForm] string password,
        [FromServices] LdapAuthService ldap, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        if (!AdminAllowed(settings)) return StatusCode(403, "Yetki yok.");
        var result = ldap.Validate(settings, username, password);
        return Ok(new
        {
            ok = result.Success,
            message = result.Success
                ? $"Giriş başarılı. Yetkili kullanıcı: {result.DisplayName}"
                : result.Error
        });
    }

    /// <summary>Ayarlar ekranındaki "Test SMS gönder" butonu.</summary>
    [HttpPost("test-sms")]
    public async Task<IActionResult> TestSms([FromForm] string? to, [FromServices] SmsService sms, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        if (!AdminAllowed(settings)) return StatusCode(403, "Yetki yok.");
        if (_lic.Current is { EmailOnlyNotifications: true })
            return BadRequest("Lisans: Basic paket yalnız e-posta bildirimi destekler.");
        var recipients = string.IsNullOrWhiteSpace(to)
            ? SmsService.ParseRecipients(settings.SmsRecipients)
            : new[] { to.Trim() };
        var (ok, message) = await sms.SendAsync(settings, recipients, "vMon test mesajı ✅", ct);
        await _audit.LogAsync("sms.test", null, ok ? "Test SMS gönderildi" : "Test SMS başarısız: " + message, ok);
        return Ok(new { ok, message });
    }

    /// <summary>Ayarlar ekranındaki "Test WhatsApp gönder" butonu.</summary>
    [HttpPost("test-whatsapp")]
    public async Task<IActionResult> TestWhatsapp([FromForm] string? to, [FromServices] WhatsappService wa, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        if (!AdminAllowed(settings)) return StatusCode(403, "Yetki yok.");
        if (_lic.Current is { EmailOnlyNotifications: true })
            return BadRequest("Lisans: Basic paket yalnız e-posta bildirimi destekler.");
        var recipients = string.IsNullOrWhiteSpace(to)
            ? WhatsappService.ParseRecipients(settings.WhatsappRecipients)
            : new[] { to.Trim() };
        var (ok, message) = await wa.SendAsync(settings, recipients, "vMon WhatsApp test mesajı ✅", ct);
        await _audit.LogAsync("whatsapp.test", null, ok ? "Test WhatsApp gönderildi" : "Test WhatsApp başarısız: " + message, ok);
        return Ok(new { ok, message });
    }

    /// <summary>Ayarlar ekranındaki "Test maili gönder" butonu.</summary>
    [HttpPost("test-email")]
    public async Task<IActionResult> TestEmail(CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        if (!AdminAllowed(settings)) return StatusCode(403, "Yetki yok.");
        await _email.SendAsync(settings, "[vMon] Test maili",
            "<p>Bu bir test mailidir. SMTP ayarlarınız çalışıyor. ✅</p>", ct);
        return Ok(new { ok = true });
    }
}
