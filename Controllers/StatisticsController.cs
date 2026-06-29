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
        var svc = await _db.Services.AsNoTracking().Where(s => s.Enabled).ToListAsync();

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

        return Json(new
        {
            counts = new { total, up, down, error },
            cpu = new { used = Math.Round(usedCores, 1), alloc = Math.Round(allocCores, 1), unit = "çekirdek" },
            ram = new { used = Math.Round(ramUsed, 1), alloc = Math.Round(ramAlloc, 1), unit = "GB" },
            disk = new { used = Math.Round(diskUsed, 1), alloc = Math.Round(diskAlloc, 1), unit = "GB" },
            avg = new { cpu = AvgOrNull(svc.Select(s => s.LastCpuPercent)), ram = AvgOrNull(svc.Select(s => s.LastRamPercent)), disk = AvgOrNull(svc.Select(s => s.LastMaxDiskPercent)) },
            osKind,
            osVersion,
            tags
        });
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
    };
}
