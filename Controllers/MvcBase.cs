using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>İzin kontrolü olan MVC sayfaları için ortak taban.</summary>
public abstract class MvcBase : Controller
{
    protected bool Can(string perm) => User.Can(perm);

    protected IActionResult Denied()
    {
        // Yetkisiz erişim girişimini denetim kaydına yaz (PCI DSS 10.2.1.4, NIST AU-2)
        try
        {
            HttpContext.RequestServices.GetService<AuditService>()?
                .LogAsync("access.denied", HttpContext.Request.Path, "Yetkisiz erişim girişimi", false)
                .GetAwaiter().GetResult();
        }
        catch { /* loglama bu akışı bozmasın */ }

        TempData["Error"] = "Bu işlem/sayfa için yetkiniz yok. Lütfen yöneticinizle iletişime geçin.";
        return RedirectToAction("About", "Home");
    }
}
