using Microsoft.AspNetCore.Mvc;
using vMonitor.Models;

namespace vMonitor.Controllers;

public class HomeController : MvcBase
{
    // Ayrı "Dashboard" ekranı kaldırıldı; ana dashboard artık Dashboard'lar içindeki sabit "Hepsi" sekmesi.
    public IActionResult Index() => RedirectToAction("Index", "Dashboards");

    public IActionResult About() => View();

    public IActionResult Error() => Content("Bir hata oluştu. Lütfen logları kontrol edin.", "text/plain; charset=utf-8");
}
