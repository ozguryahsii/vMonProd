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
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    public StatisticsController(AppDbContext db, Services.SettingsService settings, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    { _db = db; _settings = settings; _cache = cache; }

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

    /// <summary>Tüm istatistik verisini tek seferde döner; widget'lar buradan beslenir. Sonuç ~20 sn önbelleklenir
    /// (ağır toplama her istekte değil, en çok 20 sn'de bir çalışır → sayfa hızlı açılır, çok sekme/yenileme ucuzdur).</summary>
    [HttpGet]
    public async Task<IActionResult> Data()
    {
        if (!CanView()) return Forbid();
        var payload = await _cache.GetOrCreateAsync("stats:data", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20);
            return BuildDataAsync();
        });
        return Json(payload);
    }

    private async Task<object> BuildDataAsync()
    {
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

        // Uptime — satır yüklemeden COUNT sorgularıyla (indeksli, hızlı)
        var since24 = now.AddDays(-1);
        var since7 = now.AddDays(-7);
        int total7 = await _db.CheckResults.CountAsync(r => ids.Contains(r.ServiceId) && r.CheckedAt >= since7);
        int up7 = await _db.CheckResults.CountAsync(r => ids.Contains(r.ServiceId) && r.CheckedAt >= since7 && r.Status == 0);
        int total24 = await _db.CheckResults.CountAsync(r => ids.Contains(r.ServiceId) && r.CheckedAt >= since24);
        int up24 = await _db.CheckResults.CountAsync(r => ids.Contains(r.ServiceId) && r.CheckedAt >= since24 && r.Status == 0);
        double upH24 = total24 == 0 ? 100 : Math.Round(100.0 * up24 / total24, 2);
        double upD7 = total7 == 0 ? 100 : Math.Round(100.0 * up7 / total7, 2);

        // Kesinti özeti (Outages, son 7g)
        var outs = await _db.Outages.AsNoTracking()
            .Where(o => ids.Contains(o.ServiceId) && (o.EndedAt == null || o.EndedAt >= now.AddDays(-7) || o.StartedAt >= now.AddDays(-7)))
            .Select(o => new { o.ServiceId, o.StartedAt, o.EndedAt }).ToListAsync();
        double outMin = outs.Sum(o => ((o.EndedAt ?? now) - o.StartedAt).TotalMinutes);
        var outDaily = outs.GroupBy(o => o.StartedAt.ToLocalTime().Date).OrderBy(g => g.Key)
            .Select(g => new { day = g.Key.ToString("dd.MM"), value = g.Count() }).ToList();
        var worst = outs.GroupBy(o => o.ServiceId).Select(g => new { name = idName.GetValueOrDefault(g.Key, "?"), value = g.Count() })
            .OrderByDescending(x => x.value).Take(5).ToList();

        // Zaman serileri — DB-TARAFI toplama (satırları belleğe yüklemeden). Gün × sunucu özeti.
        var since30 = now.AddDays(-30);
        var daily = await _db.HealthMetrics.AsNoTracking()
            .Where(m => ids.Contains(m.ServiceId) && m.CheckedAt >= since30)
            .GroupBy(m => new { m.ServiceId, Day = m.CheckedAt.Date })
            .Select(g => new
            {
                g.Key.ServiceId,
                g.Key.Day,
                Cpu = g.Average(x => x.CpuPercent),
                Ram = g.Average(x => x.RamPercent),
                Disk = g.Average(x => x.MaxDiskPercent),
                Cores = g.Max(x => x.CpuCores),
                RamUsed = g.Average(x => x.RamUsedGb),
                RamTotal = g.Max(x => x.RamTotalGb),
                DiskUsed = g.Average(x => x.DiskUsedGb),
                DiskTotal = g.Max(x => x.DiskTotalGb)
            }).ToListAsync();

        var fleet = daily.GroupBy(x => x.Day).OrderBy(g => g.Key).Select(g => new
        {
            day = g.Key.ToString("dd.MM"),
            cpu = Math.Round(g.Where(x => x.Cpu.HasValue).Select(x => x.Cpu!.Value).DefaultIfEmpty(0).Average(), 1),
            ram = Math.Round(g.Where(x => x.Ram.HasValue).Select(x => x.Ram!.Value).DefaultIfEmpty(0).Average(), 1),
            disk = Math.Round(g.Where(x => x.Disk.HasValue).Select(x => x.Disk!.Value).DefaultIfEmpty(0).Average(), 1)
        }).ToList();

        var capacity = daily.GroupBy(x => x.Day).OrderBy(g => g.Key).Select(g => new
        {
            day = g.Key.ToString("dd.MM"),
            cpuUsed = Math.Round(g.Sum(x => (x.Cores ?? 0) * (x.Cpu ?? 0) / 100.0), 1),
            cpuAlloc = g.Sum(x => x.Cores ?? 0),
            ramUsed = Math.Round(g.Sum(x => x.RamUsed ?? 0), 1),
            ramAlloc = Math.Round(g.Sum(x => x.RamTotal ?? 0), 1),
            diskUsed = Math.Round(g.Sum(x => x.DiskUsed ?? 0), 1),
            diskAlloc = Math.Round(g.Sum(x => x.DiskTotal ?? 0), 1)
        }).ToList();

        // Kapasite kullanımı ARTAN: son ~2 gün ort. vs ~7 gün önce ort.; +%10 puandan fazla
        List<object> Rising(IEnumerable<(int id, DateTime day, double v)> rows)
        {
            var tmp = new List<(string name, double from, double to, double delta)>();
            foreach (var g in rows.GroupBy(r => r.id))
            {
                var recent = g.Where(r => r.day >= now.AddDays(-2).Date).Select(r => r.v).ToList();
                var past = g.Where(r => r.day <= now.AddDays(-6).Date && r.day >= now.AddDays(-9).Date).Select(r => r.v).ToList();
                if (recent.Count == 0 || past.Count == 0) continue;
                double rr = recent.Average(), pp = past.Average(), d = rr - pp;
                if (d >= 10) tmp.Add((idName.GetValueOrDefault(g.Key, "?"), Math.Round(pp, 1), Math.Round(rr, 1), Math.Round(d, 1)));
            }
            return tmp.OrderByDescending(x => x.delta).Select(x => (object)new { name = x.name, from = x.from, to = x.to, delta = x.delta }).ToList();
        }
        var rising = new
        {
            cpu = Rising(daily.Where(x => x.Cpu.HasValue).Select(x => (x.ServiceId, x.Day, x.Cpu!.Value))),
            ram = Rising(daily.Where(x => x.Ram.HasValue).Select(x => (x.ServiceId, x.Day, x.Ram!.Value))),
            disk = Rising(daily.Where(x => x.Disk.HasValue).Select(x => (x.ServiceId, x.Day, x.Disk!.Value)))
        };

        // Isı haritası: son 24s, saat bazında DB-tarafı ortalama; en yoğun 24 sunucu
        var hourly = await _db.HealthMetrics.AsNoTracking()
            .Where(m => ids.Contains(m.ServiceId) && m.CheckedAt >= since24 && m.CpuPercent != null)
            .GroupBy(m => new { m.ServiceId, Hour = m.CheckedAt.Hour })
            .Select(g => new { g.Key.ServiceId, g.Key.Hour, Cpu = g.Average(x => x.CpuPercent) })
            .ToListAsync();
        var topIds = hourly.GroupBy(x => x.ServiceId).Select(g => new { id = g.Key, avg = g.Average(x => x.Cpu ?? 0) })
            .OrderByDescending(x => x.avg).Take(24).Select(x => x.id).ToList();
        var heatRows = topIds.Select(id => idName.GetValueOrDefault(id, "?")).ToList();
        var heatData = new List<int[]>();
        for (int yi = 0; yi < topIds.Count; yi++)
            foreach (var hg in hourly.Where(x => x.ServiceId == topIds[yi]))
                heatData.Add(new[] { hg.Hour, yi, (int)Math.Round(hg.Cpu ?? 0) });

        return new
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
            uptime = new { h24 = upH24, d7 = upD7 },
            outages = new { count = outs.Count, minutes = Math.Round(outMin), daily = outDaily, worst },
            fleet,
            capacity,
            rising,
            heatmap = new { rows = heatRows, data = heatData }
        };
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

        // Histogram bandı: source "hist_cpu|hist_ram|hist_disk", value "40-60"
        if (source.StartsWith("hist_"))
        {
            var metric = source[5..];
            Func<MonitoredService, double?> hsel = metric == "ram" ? s => s.LastRamPercent
                : metric == "disk" ? s => s.LastMaxDiskPercent : s => s.LastCpuPercent;
            var parts = (value ?? "").Split('-');
            int lo = 0, hi = 100;
            if (parts.Length == 2) { int.TryParse(parts[0], out lo); int.TryParse(parts[1], out hi); }
            var inBand = all.Where(s => hsel(s).HasValue && hsel(s)!.Value >= lo && (hi >= 100 ? hsel(s)!.Value <= hi : hsel(s)!.Value < hi)).ToList();
            return Json(new
            {
                count = inBand.Count,
                servers = inBand.OrderByDescending(hsel).Select(s => new
                {
                    name = s.Name, target = s.Target, os = s.OsName ?? (s.Type == ServiceType.WindowsHealth ? "Windows" : "Linux"),
                    status = s.LastStatus is >= 0 and < 3 ? new[] { "Up", "Down", "Hata" }[s.LastStatus] : "?",
                    cpu = s.LastCpuPercent, ram = s.LastRamPercent, disk = s.LastMaxDiskPercent,
                    capacity = s.CapacityInfo, lastChecked = s.LastCheckedAt
                }).ToList(),
                trend = (object?)null
            });
        }

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
