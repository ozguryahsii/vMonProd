using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using vMonitor.Models;

namespace vMonitor.Controllers;

public class HomeController : MvcBase
{
    private readonly ILogger<HomeController> _logger;
    public HomeController(ILogger<HomeController> logger) => _logger = logger;

    // Ayrı "Dashboard" ekranı kaldırıldı; ana dashboard artık Dashboard'lar içindeki sabit "Hepsi" sekmesi.
    public IActionResult Index() => RedirectToAction("Index", "Dashboards");

    public IActionResult About() => View();

    /// <summary>Hata sayfası: gerçek istisnayı LOGLAR ve özetini gösterir (kapıya takılmadan görünür olsun diye
    /// erişim listesinde açıktır). Böylece "login'e dönüyor" gibi görünen gizli hatalar teşhis edilebilir.</summary>
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var ex = feature?.Error;
        if (ex != null)
            _logger.LogError(ex, "İşlenmeyen hata. Yol: {Path}", feature?.Path);
        var detail = ex == null
            ? "Bir hata oluştu. Lütfen logları kontrol edin."
            : $"Bir hata oluştu.\n\nYol: {feature?.Path}\nHata: {ex.GetType().Name}: {ex.GetBaseException().Message}\n\n(Ayrıntı uygulama loglarında / Event Viewer'da.)";
        Response.StatusCode = 500;
        return Content(detail, "text/plain; charset=utf-8");
    }
}
