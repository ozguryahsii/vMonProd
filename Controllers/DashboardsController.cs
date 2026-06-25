using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Controllers;

public class DashboardsController : MvcBase
{
    private readonly AppDbContext _db;
    public DashboardsController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        if (!Can(Perms.DashboardsView)) return Denied();
        return View(await _db.Dashboards.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        if (!Can(Perms.DashboardsManage)) return Denied();
        await LoadServicesAsync();
        return View("Form", new DashboardDef());
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!Can(Perms.DashboardsManage)) return Denied();
        var dash = await _db.Dashboards.FindAsync(id);
        if (dash == null) return NotFound();
        await LoadServicesAsync();
        return View("Form", dash);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(DashboardDef model, int[]? serviceIds)
    {
        if (!Can(Perms.DashboardsManage)) return Denied();
        model.ServiceIdsCsv = serviceIds is { Length: > 0 } ? string.Join(",", serviceIds) : null;
        model.TypeFilter = string.IsNullOrWhiteSpace(model.TypeFilter) ? null : model.TypeFilter;
        model.KeywordFilter = string.IsNullOrWhiteSpace(model.KeywordFilter) ? null : model.KeywordFilter.Trim();
        if (!ModelState.IsValid)
        {
            await LoadServicesAsync();
            return View("Form", model);
        }

        if (model.Id == 0)
        {
            _db.Dashboards.Add(model);
        }
        else
        {
            var existing = await _db.Dashboards.FindAsync(model.Id);
            if (existing == null) return NotFound();
            existing.Name = model.Name;
            existing.ServiceIdsCsv = model.ServiceIdsCsv;
            existing.TypeFilter = string.IsNullOrWhiteSpace(model.TypeFilter) ? null : model.TypeFilter;
            existing.KeywordFilter = string.IsNullOrWhiteSpace(model.KeywordFilter) ? null : model.KeywordFilter.Trim();
            existing.SortOrder = model.SortOrder;
        }
        await _db.SaveChangesAsync();
        TempData["Message"] = "Dashboard kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!Can(Perms.DashboardsManage)) return Denied();
        var dash = await _db.Dashboards.FindAsync(id);
        if (dash != null)
        {
            _db.Dashboards.Remove(dash);
            await _db.SaveChangesAsync();
            TempData["Message"] = "Dashboard silindi.";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Dashboard görüntüleme sayfası — kart grid + canlı grafik.</summary>
    [Route("Dashboards/View/{id:int}")]
    public async Task<IActionResult> ViewBoard(int id)
    {
        if (!Can(Perms.DashboardsView)) return Denied();
        var dash = await _db.Dashboards.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (dash == null) return NotFound();
        return View("ViewBoard", dash);
    }

    private async Task LoadServicesAsync() =>
        ViewBag.AllServices = await _db.Services.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
}
