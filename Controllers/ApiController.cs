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

    public ApiController(AppDbContext db, SettingsService settings, CheckRunner runner, EmailService email, AuditService audit)
    {
        _db = db;
        _settings = settings;
        _runner = runner;
        _email = email;
        _audit = audit;
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
                s.Id, s.Name, s.Type, s.Target, s.Port, s.Enabled,
                s.LastCheckedAt, s.LastIsUp, s.LastResponseTimeMs, s.LastError,
                s.ConsecutiveFailures,
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
    public async Task<IActionResult> History(int id, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        if (!Can(Perms.DashboardsView)) return Forbid403();
        var svc = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (svc == null) return NotFound("Servis bulunamadı");

        var checks = await _db.CheckResults.AsNoTracking()
            .Where(r => r.ServiceId == id)
            .OrderByDescending(r => r.CheckedAt)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(r => new { r.CheckedAt, r.IsUp, r.Status, r.ResponseTimeMs, r.Error })
            .ToListAsync(ct);

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

        var points = await _db.CheckResults.AsNoTracking()
            .Where(r => idList.Contains(r.ServiceId) && r.CheckedAt >= since)
            .OrderBy(r => r.CheckedAt)
            .Select(r => new { r.ServiceId, r.CheckedAt, r.ResponseTimeMs, r.IsUp })
            .ToListAsync(ct);

        var names = await _db.Services.AsNoTracking()
            .Where(s => idList.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return Ok(new
        {
            since,
            series = idList.Where(id => names.ContainsKey(id)).Select(id => new
            {
                id,
                name = names[id],
                points = points.Where(p => p.ServiceId == id)
                    .Select(p => new { t = p.CheckedAt, ms = p.ResponseTimeMs, up = p.IsUp })
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
