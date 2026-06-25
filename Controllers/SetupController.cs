using Microsoft.AspNetCore.Mvc;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>İlk kurulum sihirbazı. Uygulama yapılandırılmamışsa (bootstrap.json yok) tüm istekler buraya yönlenir.
/// Faz A: kurulum modu algılama + iskelet. Faz C: tam sihirbaz (DB/şirket/admin/SMTP adımları).</summary>
public class SetupController : Controller
{
    private readonly BootstrapConfig _cfg;
    public SetupController(BootstrapConfig cfg) => _cfg = cfg;

    [HttpGet]
    public IActionResult Index()
    {
        // Zaten yapılandırılmışsa kurulum ekranı gösterilmez
        if (_cfg.Configured) return Redirect("/");
        return View();
    }
}
