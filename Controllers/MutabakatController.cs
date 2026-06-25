using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>Envanter mutabakatı — uygulamanın geri kalanından tamamen bağımsız.
/// Ayarlar'dan açık değilse erişilemez; ayrıca "mutabakat.view" yetkisi gerekir.</summary>
public class MutabakatController : MvcBase
{
    private readonly SettingsService _settings;
    private readonly MutabakatService _mut;
    private readonly AuditService _audit;
    public MutabakatController(SettingsService settings, MutabakatService mut, AuditService audit)
    { _settings = settings; _mut = mut; _audit = audit; }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var s = await _settings.GetAsync();
        if (!s.MutabakatEnabled) { context.Result = NotFound(); return; }   // kapalıyken hiç görünmez
        if (!User.Can(Perms.MutabakatView)) { context.Result = Denied(); return; }
        await next();
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await FillNamesAsync();
        return View((MutabakatResult?)null);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Compare(IFormFile? ourFile, IFormFile? vendorFile, IFormFile? vendorPrevFile,
        string? ourMonth, string? vendorMonth, string? vendorPrevMonth)
    {
        var s = await _settings.GetAsync();
        await FillNamesAsync(s);

        if (ourFile == null || ourFile.Length == 0 || vendorFile == null || vendorFile.Length == 0)
        {
            TempData["Error"] = "Lütfen hem bizim envanter dosyamızı hem de firma listesini yükleyin.";
            return RedirectToAction(nameof(Index));
        }
        if (!Ok(ourFile) || !Ok(vendorFile) || (vendorPrevFile != null && vendorPrevFile.Length > 0 && !Ok(vendorPrevFile)))
        {
            TempData["Error"] = "Yalnızca .csv veya .xlsx dosyaları desteklenir.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            List<InvServer> ours, vendor;
            List<InvServer>? prev = null;
            using (var ms = await Buffer(ourFile)) ours = MutabakatService.ParseOur(ms, ourFile.FileName);
            using (var ms = await Buffer(vendorFile)) vendor = MutabakatService.ParseVendor(ms, vendorFile.FileName);
            if (vendorPrevFile != null && vendorPrevFile.Length > 0)
                using (var ms = await Buffer(vendorPrevFile)) prev = MutabakatService.ParseVendor(ms, vendorPrevFile.FileName);

            var result = _mut.Compare(ours, vendor, prev,
                s.MutabakatOwnCompany, s.MutabakatVendorCompany,
                ourMonth ?? "", vendorMonth ?? "", vendorPrevMonth ?? "");

            await _audit.LogAsync("mutabakat.compare", null,
                $"Mutabakat: bizim={ours.Count}, firma={vendor.Count}, firma-önceki={(prev?.Count ?? 0)}");

            return View("Index", result);
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Dosyalar okunamadı/karşılaştırılamadı: " + ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    private static bool Ok(IFormFile f)
    {
        var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
        return ext is ".csv" or ".xlsx";
    }

    private static async Task<MemoryStream> Buffer(IFormFile f)
    {
        var ms = new MemoryStream();
        await f.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }

    private async Task FillNamesAsync(MonitorSettings? s = null)
    {
        s ??= await _settings.GetAsync();
        ViewBag.OwnName = string.IsNullOrWhiteSpace(s.MutabakatOwnCompany) ? "Bizim Envanter" : s.MutabakatOwnCompany;
        ViewBag.VendorName = string.IsNullOrWhiteSpace(s.MutabakatVendorCompany) ? "Hizmet Aldığımız Firma" : s.MutabakatVendorCompany;
    }
}
