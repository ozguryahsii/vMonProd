using Microsoft.AspNetCore.Mvc;
using vMonitor.Models;

namespace vMonitor.Controllers;

public class ReportsController : MvcBase
{
    public IActionResult Index() => Can(Perms.DashboardsView) ? View() : Denied();
}
