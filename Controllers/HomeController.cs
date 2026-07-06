using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using vMonitor.Models;

namespace vMonitor.Controllers;

public class HomeController : MvcBase
{
    private readonly ILogger<HomeController> _logger;
    public HomeController(ILogger<HomeController> logger) => _logger = logger;

    // Klasik arayüz emekli: kök adres doğrudan yeni tasarıma (React SPA) gider.
    public IActionResult Index() => Redirect("/app/dashboard");

    public IActionResult About() => View();

    /// <summary>Hata sayfası: gerçek istisnayı yalnızca LOGLAR; kullanıcıya bilgi sızdırmamak için sadece
    /// genel mesaj + bir referans (TraceId) gösterir. Ayrıntı uygulama loglarında / Event Viewer'dadır.</summary>
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var ex = feature?.Error;
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        if (ex != null)
            _logger.LogError(ex, "İşlenmeyen hata. Yol: {Path} TraceId: {TraceId}", feature?.Path, traceId);
        Response.StatusCode = 500;
        return Content($"Bir hata oluştu. Lütfen sistem yöneticisine başvurun.\nReferans: {traceId}",
            "text/plain; charset=utf-8");
    }
}
