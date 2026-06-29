using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Controllers;

/// <summary>İstatistikler: widget'lı (ECharts + Gridstack) ortak analitik panosu. v1 tek ortak düzen;
/// düzeni yalnız adminler değiştirir. Veriler /Statistics/Data'dan JSON olarak gelir.</summary>
public class StatisticsController : Controller
{
    private readonly AppDbContext _db;
    private readonly Services.SettingsService _settings;
    public StatisticsController(AppDbContext db, Services.SettingsService settings) { _db = db; _settings = settings; }

    private bool CanView() => User.Can(Perms.DashboardsView);
    private bool CanEdit() => User.IsAppAdmin();

    public async Task<IActionResult> Index()
    {
        if (!CanView()) { TempData["Error"] = "Bu sayfaya erişim yetkiniz yok."; return RedirectToAction("Index", "Home"); }
        var widgets = await _db.StatWidgets.AsNoTracking().OrderBy(w => w.SortOrder).ToListAsync();
        if (widgets.Count == 0)
        {
            widgets = DefaultWidgets();
            _db.StatWidgets.AddRange(widgets);
            await _db.SaveChangesAsync();
        }
        ViewBag.CanEdit = CanEdit();
        return View(widgets);
    }

    /// <summary>Tüm istatistik verisini tek seferde döner; widget'lar buradan beslenir.</summary>
    [HttpGet]
    public async Task<IActionResult> Data()
    {
        if (!CanView()) return Forbid();
        // TÜM istatistikler yalnız sağlık ile izlenen sunuculardan (Windows Health + Linux Health) gelir.
        var svc = await HealthServicesAsync();

        int total = svc.Count;
        int up = svc.Count(s => s.LastStatus == 0);
        int down = svc.Count(s => s.LastStatus == 1);
        int error = svc.Count(s => s.LastStatus == 2);

        // Kaynak toplamları (yalnız sağlık verisi olan sunucular)
        double allocCores = svc.Where(s => s.LastCpuCores.HasValue).Sum(s => s.LastCpuCores!.Value);
        double usedCores = svc.Where(s => s.LastCpuCores.HasValue && s.LastCpuPercent.HasValue)
            .Sum(s => s.LastCpuCores!.Value * s.LastCpuPercent!.Value / 100.0);
        double ramAlloc = svc.Where(s => s.LastRamTotalGb.HasValue).Sum(s => s.LastRamTotalGb!.Value);
        double ramUsed = svc.Where(s => s.LastRamUsedGb.HasValue).Sum(s => s.LastRamUsedGb!.Value);
        double diskAlloc = svc.Where(s => s.LastDiskTotalGb.HasValue).Sum(s => s.LastDiskTotalGb!.Value);
        double diskUsed = svc.Where(s => s.LastDiskUsedGb.HasValue).Sum(s => s.LastDiskUsedGb!.Value);

        double? AvgOrNull(IEnumerable<double?> xs) { var l = xs.Where(x => x.HasValue).Select(x => x!.Value).ToList(); return l.Count > 0 ? Math.Round(l.Average(), 1) : (double?)null; }

        var osKind = svc.Where(s => !string.IsNullOrWhiteSpace(s.OsKind))
            .GroupBy(s => s.OsKind!).Select(g => new { name = g.Key, value = g.Count() }).OrderByDescending(x => x.value).ToList();
        var osVersion = svc.Where(s => !string.IsNullOrWhiteSpace(s.OsName))
            .GroupBy(s => s.OsName!).Select(g => new { name = g.Key, value = g.Count() }).OrderByDescending(x => x.value).ToList();

        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in svc)
            foreach (var t in MonitoredService.SplitKeywords(s.Keyword))
                tagCounts[t] = tagCounts.GetValueOrDefault(t) + 1;
        var tags = tagCounts.OrderByDescending(kv => kv.Value).Select(kv => new { name = kv.Key, value = kv.Value }).ToList();

        var now = DateTime.UtcNow;
        var ids = svc.Select(s => s.Id).ToHashSet();
        var idName = svc.ToDictionary(s => s.Id, s => s.Name);

        // Top-10 kaynak tüketenler
        List<object> Top(Func<MonitoredService, double?> sel) => svc.Where(s => sel(s).HasValue)
            .OrderByDescending(s => sel(s)).Take(10)
            .Select(s => (object)new { name = s.Name, value = Math.Round(sel(s)!.Value, 1), os = s.OsName }).ToList();

        // Kritik
        var diskFull = svc.Count(s => s.LastMaxDiskPercent >= 85);
        var breach = svc.Count(s => s.LastStatus == 2);

        // Kullanım dağılımı histogramı (0-20,20-40,40-60,60-80,80-100)
        int[] Band(Func<MonitoredService, double?> sel)
        { var b = new int[5]; foreach (var s in svc) { var v = sel(s); if (v.HasValue) b[Math.Min(4, (int)(v.Value / 20))]++; } return b; }

        // OS EOL (destek sonu) — basit kalıp eşleşmesi
        var eol = svc.Where(s => !string.IsNullOrWhiteSpace(s.OsName) && EolPatterns.Any(p => s.OsName!.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(s => s.OsName!).Select(g => new { name = g.Key, value = g.Count() }).OrderByDescending(x => x.value).ToList();

        // Uptime (CheckResults, son 7g)
        var cr = await _db.CheckResults.AsNoTracking().Where(r => ids.Contains(r.ServiceId) && r.CheckedAt >= now.AddDays(-7))
            .Select(r => new { r.CheckedAt, r.Status }).ToListAsync();
        double Pct(List<int> st) => st.Count == 0 ? 100 : Math.Round(100.0 * st.Count(x => x == 0) / st.Count, 2);
        var since24 = now.AddDays(-1);

        // Kesinti özeti (Outages, son 7g)
        var outs = await _db.Outages.AsNoTracking()
            .Where(o => ids.Contains(o.ServiceId) && (o.EndedAt == null || o.EndedAt >= now.AddDays(-7) || o.StartedAt >= now.AddDays(-7)))
            .Select(o => new { o.ServiceId, o.StartedAt, o.EndedAt }).ToListAsync();
        double outMin = outs.Sum(o => ((o.EndedAt ?? now) - o.StartedAt).TotalMinutes);
        var outDaily = outs.GroupBy(o => o.StartedAt.ToLocalTime().Date).OrderBy(g => g.Key)
            .Select(g => new { day = g.Key.ToString("dd.MM"), value = g.Count() }).ToList();
        var worst = outs.GroupBy(o => o.ServiceId).Select(g => new { name = idName.GetValueOrDefault(g.Key, "?"), value = g.Count() })
            .OrderByDescending(x => x.value).Take(5).ToList();

        // Zaman serileri (HealthMetrics, son 30g) — filo trendi, kapasite, ısı haritası
        var hm = (await _db.HealthMetrics.AsNoTracking()
            .Where(m => ids.Contains(m.ServiceId) && m.CheckedAt >= now.AddDays(-30))
            .Select(m => new { m.ServiceId, m.CheckedAt, m.CpuPercent, m.RamPercent, m.MaxDiskPercent, m.CpuCores, m.RamUsedGb, m.RamTotalGb, m.DiskUsedGb, m.DiskTotalGb })
            .ToListAsync())
            .Select(m => new { m.ServiceId, Local = m.CheckedAt.ToLocalTime(), m.CpuPercent, m.RamPercent, m.MaxDiskPercent, m.CpuCores, m.RamUsedGb, m.RamTotalGb, m.DiskUsedGb, m.DiskTotalGb })
            .ToList();

        var fleet = hm.GroupBy(x => x.Local.Date).OrderBy(g => g.Key).Select(g => new
        {
            day = g.Key.ToString("dd.MM"),
            cpu = Math.Round(g.Where(x => x.CpuPercent.HasValue).Select(x => x.CpuPercent!.Value).DefaultIfEmpty(0).Average(), 1),
            ram = Math.Round(g.Where(x => x.RamPercent.HasValue).Select(x => x.RamPercent!.Value).DefaultIfEmpty(0).Average(), 1),
            disk = Math.Round(g.Where(x => x.MaxDiskPercent.HasValue).Select(x => x.MaxDiskPercent!.Value).DefaultIfEmpty(0).Average(), 1)
        }).ToList();

        // Kapasite: gün başına, her sunucunun O GÜNKÜ SON ölçümü → toplam
        var capacity = hm.GroupBy(x => new { x.ServiceId, Day = x.Local.Date })
            .Select(g => g.OrderByDescending(x => x.Local).First())
            .GroupBy(x => x.Local.Date).OrderBy(g => g.Key).Select(g => new
            {
                day = g.Key.ToString("dd.MM"),
                cpuUsed = Math.Round(g.Sum(x => (x.CpuCores ?? 0) * (x.CpuPercent ?? 0) / 100.0), 1),
                cpuAlloc = g.Sum(x => x.CpuCores ?? 0),
                ramUsed = Math.Round(g.Sum(x => x.RamUsedGb ?? 0), 1),
                ramAlloc = Math.Round(g.Sum(x => x.RamTotalGb ?? 0), 1),
                diskUsed = Math.Round(g.Sum(x => x.DiskUsedGb ?? 0), 1),
                diskAlloc = Math.Round(g.Sum(x => x.DiskTotalGb ?? 0), 1)
            }).ToList();

        // Kapasite kullanımı ARTAN: ~7 gün öncesine göre kullanım oranı +%10'dan fazla artan sunucular (metrik bazında)
        var recentSince = now.AddDays(-1).ToLocalTime();
        var pastFrom = now.AddDays(-8).ToLocalTime();
        var pastTo = now.AddDays(-6).ToLocalTime();
        List<object> RisingTriples(IEnumerable<(int id, DateTime t, double v)> rows)
        {
            var tmp = new List<(string name, double from, double to, double delta)>();
            foreach (var g in rows.GroupBy(r => r.id))
            {
                var recent = g.Where(r => r.t >= recentSince).Select(r => r.v).ToList();
                var past = g.Where(r => r.t >= pastFrom && r.t < pastTo).Select(r => r.v).ToList();
                if (recent.Count == 0 || past.Count == 0) continue;
                double rr = recent.Average(), pp = past.Average(), d = rr - pp;
                if (d >= 10) tmp.Add((idName.GetValueOrDefault(g.Key, "?"), Math.Round(pp, 1), Math.Round(rr, 1), Math.Round(d, 1)));
            }
            return tmp.OrderByDescending(x => x.delta).Select(x => (object)new { name = x.name, from = x.from, to = x.to, delta = x.delta }).ToList();
        }
        var rising = new
        {
            cpu = RisingTriples(hm.Where(x => x.CpuPercent.HasValue).Select(x => (x.ServiceId, x.Local, x.CpuPercent!.Value))),
            ram = RisingTriples(hm.Where(x => x.RamPercent.HasValue).Select(x => (x.ServiceId, x.Local, x.RamPercent!.Value))),
            disk = RisingTriples(hm.Where(x => x.MaxDiskPercent.HasValue).Select(x => (x.ServiceId, x.Local, x.MaxDiskPercent!.Value)))
        };

        // Isı haritası: son 24s, en yoğun 24 sunucu × saat (ort. CPU)
        var hm24 = hm.Where(x => x.Local >= since24.ToLocalTime() && x.CpuPercent.HasValue).ToList();
        var topIds = hm24.GroupBy(x => x.ServiceId).Select(g => new { id = g.Key, avg = g.Average(x => x.CpuPercent!.Value) })
            .OrderByDescending(x => x.avg).Take(24).Select(x => x.id).ToList();
        var heatRows = topIds.Select(id => idName.GetValueOrDefault(id, "?")).ToList();
        var heatData = new List<int[]>();
        for (int yi = 0; yi < topIds.Count; yi++)
            foreach (var hg in hm24.Where(x => x.ServiceId == topIds[yi]).GroupBy(x => x.Local.Hour))
                heatData.Add(new[] { hg.Key, yi, (int)Math.Round(hg.Average(x => x.CpuPercent!.Value)) });

        return Json(new
        {
            lastUpdated = now,
            counts = new { total, up, down, error },
            cpu = new { used = Math.Round(usedCores, 1), alloc = Math.Round(allocCores, 1), unit = "çekirdek" },
            ram = new { used = Math.Round(ramUsed, 1), alloc = Math.Round(ramAlloc, 1), unit = "GB" },
            disk = new { used = Math.Round(diskUsed, 1), alloc = Math.Round(diskAlloc, 1), unit = "GB" },
            avg = new { cpu = AvgOrNull(svc.Select(s => s.LastCpuPercent)), ram = AvgOrNull(svc.Select(s => s.LastRamPercent)), disk = AvgOrNull(svc.Select(s => s.LastMaxDiskPercent)) },
            osKind,
            osVersion,
            tags,
            top = new { cpu = Top(s => s.LastCpuPercent), ram = Top(s => s.LastRamPercent), disk = Top(s => s.LastMaxDiskPercent) },
            critical = new { diskFull, breach },
            histogram = new { cpu = Band(s => s.LastCpuPercent), ram = Band(s => s.LastRamPercent), disk = Band(s => s.LastMaxDiskPercent) },
            osEol = new { count = eol.Sum(x => x.value), items = eol },
            uptime = new { h24 = Pct(cr.Where(x => x.CheckedAt >= since24).Select(x => x.Status).ToList()), d7 = Pct(cr.Select(x => x.Status).ToList()) },
            outages = new { count = outs.Count, minutes = Math.Round(outMin), daily = outDaily, worst },
            fleet,
            capacity,
            rising,
            heatmap = new { rows = heatRows, data = heatData }
        });
    }

    // Destek sonu (EOL) OS kalıpları — basit ve genişletilebilir
    private static readonly string[] EolPatterns =
    {
        "2003", "2008", "2012", "Windows 7", "Windows XP",
        "CentOS Linux 6", "CentOS Linux 7", "CentOS 6", "CentOS 7",
        "Red Hat Enterprise Linux 6", "Red Hat Enterprise Linux 7",
        "Ubuntu 14", "Ubuntu 16", "Ubuntu 18", "Debian 8", "Debian 9", "Debian 10"
    };

    private async Task<List<MonitoredService>> HealthServicesAsync() =>
        await _db.Services.AsNoTracking()
            .Where(s => s.Enabled && (s.Type == ServiceType.WindowsHealth || s.Type == ServiceType.LinuxHealth))
            .ToListAsync();

    /// <summary>Bir widget'a/pasta dilimine tıklanınca: ilgili sunucu listesini + (kaynak metrikse) 7 günlük trendi döner.</summary>
    [HttpGet]
    public async Task<IActionResult> Detail(string source, string? value, int days = 7)
    {
        days = days switch { <= 7 => 7, <= 31 => 30, <= 92 => 90, <= 186 => 180, _ => 365 };
        if (!CanView()) return Forbid();
        var all = await HealthServicesAsync();
        source = (source ?? "").ToLowerInvariant();

        IEnumerable<MonitoredService> sel = source switch
        {
            "up" => all.Where(s => s.LastStatus == 0),
            "down" => all.Where(s => s.LastStatus == 1),
            "error" => all.Where(s => s.LastStatus == 2),
            "disk_full" => all.Where(s => s.LastMaxDiskPercent >= 85),
            "os_kind" => all.Where(s => string.Equals(s.OsKind, value, StringComparison.OrdinalIgnoreCase)),
            "os_version" => all.Where(s => string.Equals(s.OsName, value, StringComparison.OrdinalIgnoreCase)),
            "tag" => all.Where(s => MonitoredService.SplitKeywords(s.Keyword).Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase))),
            _ => all   // total_servers, cpu, ram, disk, avg_* → tüm sağlık sunucuları
        };

        var statusText = new[] { "Up", "Down", "Hata" };
        var servers = sel.OrderBy(s => s.Name).Select(s => new
        {
            name = s.Name,
            target = s.Target,
            os = s.OsName ?? (s.Type == ServiceType.WindowsHealth ? "Windows" : "Linux"),
            status = s.LastStatus >= 0 && s.LastStatus < 3 ? statusText[s.LastStatus] : "?",
            cpu = s.LastCpuPercent,
            ram = s.LastRamPercent,
            disk = s.LastMaxDiskPercent,
            capacity = s.CapacityInfo,
            lastChecked = s.LastCheckedAt
        }).ToList();

        // 7 günlük trend (yalnız kaynak/ortalama widget'larında anlamlı)
        object? trend = null;
        var metric = source.StartsWith("avg_") ? source[4..] : source; // cpu/ram/disk
        if (metric is "cpu" or "ram" or "disk")
        {
            var ids = sel.Select(s => s.Id).ToHashSet();
            var since = DateTime.UtcNow.AddDays(-days);
            var rows = await _db.HealthMetrics.AsNoTracking()
                .Where(m => m.CheckedAt >= since && ids.Contains(m.ServiceId))
                .Select(m => new { m.CheckedAt, m.CpuPercent, m.RamPercent, m.MaxDiskPercent })
                .ToListAsync();

            // Aralığa göre gruplama: <=30 gün → günlük, <=180 → haftalık, daha uzun → aylık
            DateTime BucketStart(DateTime d) => days <= 30 ? d.Date
                : days <= 180 ? d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7))   // haftanın başı (Pzt)
                : new DateTime(d.Year, d.Month, 1);
            string Label(DateTime d) => days <= 180 ? d.ToString("dd.MM") : d.ToString("MM.yyyy");

            var points = rows.GroupBy(r => BucketStart(r.CheckedAt.ToLocalTime())).OrderBy(g => g.Key).Select(g => new
            {
                day = Label(g.Key),
                value = Math.Round(metric switch
                {
                    "cpu" => g.Where(x => x.CpuPercent.HasValue).Select(x => x.CpuPercent!.Value).DefaultIfEmpty(0).Average(),
                    "ram" => g.Where(x => x.RamPercent.HasValue).Select(x => x.RamPercent!.Value).DefaultIfEmpty(0).Average(),
                    _ => g.Where(x => x.MaxDiskPercent.HasValue).Select(x => x.MaxDiskPercent!.Value).DefaultIfEmpty(0).Average()
                }, 1)
            }).ToList();
            if (points.Count > 0) trend = new { metric, days, points };
        }

        return Json(new { count = servers.Count, servers, trend });
    }

    public record WidgetDto(int Id, string Type, string Source, string? Title, string? ConfigJson, int X, int Y, int W, int H);

    /// <summary>Düzeni (konum/boyut + ekleme/silme) topluca kaydeder. Yalnız admin.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLayout([FromBody] List<WidgetDto> widgets)
    {
        if (!CanEdit()) return Forbid();
        widgets ??= new();
        var keepIds = widgets.Where(w => w.Id > 0).Select(w => w.Id).ToHashSet();
        var existing = await _db.StatWidgets.ToListAsync();

        // Silinenler
        foreach (var e in existing.Where(e => !keepIds.Contains(e.Id)))
            _db.StatWidgets.Remove(e);

        int order = 0;
        foreach (var w in widgets)
        {
            var ent = w.Id > 0 ? existing.FirstOrDefault(e => e.Id == w.Id) : null;
            if (ent == null) { ent = new StatWidget(); _db.StatWidgets.Add(ent); }
            ent.Type = w.Type; ent.Source = w.Source; ent.Title = w.Title; ent.ConfigJson = w.ConfigJson;
            ent.X = w.X; ent.Y = w.Y; ent.W = w.W; ent.H = w.H; ent.SortOrder = order++;
        }
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    /// <summary>Düzeni varsayılana sıfırlar (tüm widget'ları siler → Index yeniden tohumlar). Yalnız admin.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetLayout()
    {
        if (!CanEdit()) return Forbid();
        _db.StatWidgets.RemoveRange(await _db.StatWidgets.ToListAsync());
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    private static List<StatWidget> DefaultWidgets() => new()
    {
        new() { Type="counter",  Source="total_servers", X=0, Y=0, W=3, H=2, SortOrder=0 },
        new() { Type="counter",  Source="up",            X=3, Y=0, W=3, H=2, SortOrder=1 },
        new() { Type="counter",  Source="down",          X=6, Y=0, W=3, H=2, SortOrder=2 },
        new() { Type="counter",  Source="error",         X=9, Y=0, W=3, H=2, SortOrder=3 },
        new() { Type="resource", Source="cpu",           X=0, Y=2, W=4, H=3, SortOrder=4 },
        new() { Type="resource", Source="ram",           X=4, Y=2, W=4, H=3, SortOrder=5 },
        new() { Type="resource", Source="disk",          X=8, Y=2, W=4, H=3, SortOrder=6 },
        new() { Type="pie",      Source="os_kind",       X=0, Y=5, W=4, H=4, SortOrder=7 },
        new() { Type="pie",      Source="os_version",    X=4, Y=5, W=4, H=4, SortOrder=8 },
        new() { Type="pie",      Source="tag",           X=8, Y=5, W=4, H=4, SortOrder=9 },
        new() { Type="gauge",    Source="avg_cpu",       X=0, Y=9, W=4, H=3, SortOrder=10 },
        new() { Type="gauge",    Source="avg_ram",       X=4, Y=9, W=4, H=3, SortOrder=11 },
        new() { Type="gauge",    Source="avg_disk",      X=8, Y=9, W=4, H=3, SortOrder=12 },
        new() { Type="fleet",    Source="fleet",         X=0, Y=12, W=12, H=4, SortOrder=13 },
        new() { Type="top",      Source="top",           X=0, Y=16, W=6, H=5, SortOrder=14 },
        new() { Type="critical", Source="critical",      X=6, Y=16, W=6, H=2, SortOrder=15 },
        new() { Type="uptime",   Source="uptime",        X=6, Y=18, W=6, H=3, SortOrder=16 },
        new() { Type="histogram",Source="histogram",     X=0, Y=21, W=6, H=4, SortOrder=17 },
        new() { Type="capacity", Source="capacity",      X=6, Y=21, W=6, H=4, SortOrder=18 },
        new() { Type="outage",   Source="outage",        X=0, Y=25, W=6, H=4, SortOrder=19 },
        new() { Type="rising",   Source="rising",        X=6, Y=25, W=6, H=4, SortOrder=20 },
        new() { Type="os_eol",   Source="os_eol",        X=0, Y=29, W=3, H=4, SortOrder=21 },
        new() { Type="heatmap",  Source="heatmap",       X=3, Y=29, W=9, H=5, SortOrder=22 },
    };
}
