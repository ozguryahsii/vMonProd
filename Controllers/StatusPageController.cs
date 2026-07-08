using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>HERKESE AÇIK durum sayfası (yol haritası #5): login'siz /durum (EN alias /status).
/// Yalnızca ShowOnStatusPage işaretli izlemeler görünür. Sayfa İÇ BİLGİ SIZDIRMAZ:
/// hedef/host/IP/port/hata metni asla yanıtta yer almaz — sadece ad, durum ve günlük uptime yüzdeleri.</summary>
[EnableRateLimiting("public")]
public class StatusPageController : Controller
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly LicenseService _lic;
    private readonly IMemoryCache _cache;

    private const int Days = 90;

    public StatusPageController(AppDbContext db, SettingsService settings, LicenseService lic, IMemoryCache cache)
    {
        _db = db; _settings = settings; _lic = lic; _cache = cache;
    }

    [HttpGet("/durum")]
    [HttpGet("/status")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!s.StatusPageEnabled) return NotFound();          // kapalıyken varlığını belli etme
        ViewBag.Unavailable = !_lic.IsUsable;                 // lisans dolmuşsa nötr "kullanılamıyor" (iç ekrana yönlendirme YOK)
        ViewBag.Title = PageTitle(s);
        return View("Index");
    }

    [HttpGet("/durum/data")]
    [HttpGet("/status/data")]
    public async Task<IActionResult> Data(CancellationToken ct)
    {
        var s = await _settings.GetAsync(ct);
        if (!s.StatusPageEnabled) return NotFound();
        if (!_lic.IsUsable) return StatusCode(503);

        // Herkese açık uç: 60 sn önbellek — sayfa kaç kişi açarsa açsın DB'ye dakikada en çok 1 sorgu gider.
        var payload = await _cache.GetOrCreateAsync("statuspage.data", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await BuildAsync(s, ct);
        });
        return Ok(payload);
    }

    private static string PageTitle(MonitorSettings s) =>
        !string.IsNullOrWhiteSpace(s.StatusPageTitle) ? s.StatusPageTitle.Trim()
        : !string.IsNullOrWhiteSpace(s.CompanyName) ? s.CompanyName.Trim() + " — Sistem Durumu"
        : "Sistem Durumu";

    private async Task<object> BuildAsync(MonitorSettings settings, CancellationToken ct)
    {
        var services = await _db.Services.AsNoTracking()
            .Where(x => x.Enabled && x.ShowOnStatusPage)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Description, x.LastStatus, x.LastIsUp, x.LastCheckedAt })
            .ToListAsync(ct);

        var startDay = DateTime.UtcNow.Date.AddDays(-(Days - 1));
        var ids = services.Select(x => x.Id).ToList();

        // Günlük özet: toplam / down / degraded adetleri (tüm sağlayıcılarda DATE'e çevrilerek gruplanır)
        var daily = ids.Count == 0
            ? new List<DayAgg>()
            : await _db.CheckResults.AsNoTracking()
                .Where(r => r.CheckedAt >= startDay && ids.Contains(r.ServiceId))
                .GroupBy(r => new { r.ServiceId, Day = r.CheckedAt.Date })
                .Select(g => new DayAgg(g.Key.ServiceId, g.Key.Day, g.Count(),
                    g.Count(r => r.Status == (int)CheckStatus.Down),
                    g.Count(r => r.Status == (int)CheckStatus.Error)))
                .ToListAsync(ct);
        var byService = daily.ToLookup(d => d.ServiceId);

        var rows = new List<object>(services.Count);
        bool anyDown = false, anyWarn = false;
        foreach (var svc in services)
        {
            var bars = new char[Days];
            var pcts = new double[Days];
            long total = 0, down = 0;
            for (int i = 0; i < Days; i++) { bars[i] = 'n'; pcts[i] = -1; }
            foreach (var d in byService[svc.Id])
            {
                var idx = (int)(d.Day.Date - startDay).TotalDays;
                if (idx < 0 || idx >= Days || d.Total == 0) continue;
                total += d.Total; down += d.Down;
                var upPct = (d.Total - d.Down) * 100.0 / d.Total;
                pcts[idx] = Math.Round(upPct, 2);
                bars[idx] = d.Down == 0
                    ? (d.Degraded == 0 ? 'g' : 'a')     // yeşil / sarı (yalnız uyarı-eşik)
                    : (upPct >= 95 ? 'o' : 'r');        // turuncu (kısmi) / kırmızı (büyük kesinti)
            }

            // Anlık durum — yalnız kategorik değer; hata metni/detay bilinçli olarak YOK
            string status = svc.LastIsUp == null ? "unknown"
                : svc.LastStatus == (int)CheckStatus.Down || svc.LastIsUp == false ? "down"
                : svc.LastStatus == (int)CheckStatus.Error ? "warn"
                : "up";
            anyDown |= status == "down";
            anyWarn |= status == "warn";

            rows.Add(new
            {
                name = svc.Name,
                description = string.IsNullOrWhiteSpace(svc.Description) ? null : svc.Description.Trim(),
                status,
                uptimePct = total == 0 ? (double?)null : Math.Round((total - down) * 100.0 / total, 2),
                bars = new string(bars),
                pcts
            });
        }

        return new
        {
            title = PageTitle(settings),
            updatedAt = DateTime.UtcNow,
            days = Days,
            startDay = startDay.ToString("yyyy-MM-dd"),
            overall = anyDown ? "down" : anyWarn ? "warn" : rows.Count == 0 ? "unknown" : "up",
            services = rows
        };
    }

    private sealed record DayAgg(int ServiceId, DateTime Day, int Total, int Down, int Degraded);
}
