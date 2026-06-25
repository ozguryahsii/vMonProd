using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>Güvenlik denetim kaydı görüntüleme — yalnızca uygulama adminleri.
/// NOT: Eylem türü filtresi parametresi "act" olarak adlandırıldı; "action" adı MVC route
/// token'ı {action} ile çakışıp her zaman "Index" değerini alıyordu (ekran hep boş geliyordu).</summary>
public class AuditController : MvcBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    public AuditController(AppDbContext db, AuditService audit) { _db = db; _audit = audit; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (User?.Identity?.IsAuthenticated == true && !User.IsAppAdmin())
            context.Result = Denied();
        base.OnActionExecuting(context);
    }

    public async Task<IActionResult> Index(string? q, string? act, int days = 0, int take = 1000)
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        take = Math.Clamp(take, 50, 5000);

        // Denetim kaydına erişimin kendisi de loglanır (PCI DSS 10.2.1.3)
        await _audit.LogAsync("audit.view", null,
            (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(act) && days <= 0)
                ? "Denetim kaydı görüntülendi" : $"Denetim kaydı görüntülendi (filtre: q='{q}', act='{act}', gün={days})");

        static string Esc(string s) => s.Replace("'", "''");
        var where = new List<string>();
        if (days > 0)
        {
            var since = DateTime.Now.AddDays(-Math.Min(days, 3650)).ToString("yyyy-MM-dd HH:mm:ss");
            where.Add($"At >= '{since}'");
        }
        if (!string.IsNullOrWhiteSpace(act))
            where.Add($"Action = '{Esc(act.Trim())}'");
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = Esc(q.Trim());
            where.Add($"(User LIKE '%{t}%' OR IFNULL(Target,'') LIKE '%{t}%' OR IFNULL(Detail,'') LIKE '%{t}%' OR IFNULL(Ip,'') LIKE '%{t}%')");
        }
        var whereSql = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";

        var rows = new List<AuditLog>();
        var actions = new List<string>();

        using (var conn = new SqliteConnection(_db.Database.GetDbConnection().ConnectionString))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT Id, At, User, Ip, Action, Target, Detail, Success FROM AuditLogs{whereSql} ORDER BY Id DESC LIMIT {take}";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    rows.Add(new AuditLog
                    {
                        Id = rd.GetInt32(0),
                        At = ParseDt(rd.IsDBNull(1) ? null : rd.GetString(1)),
                        User = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        Ip = rd.IsDBNull(3) ? null : rd.GetString(3),
                        Action = rd.IsDBNull(4) ? "" : rd.GetString(4),
                        Target = rd.IsDBNull(5) ? null : rd.GetString(5),
                        Detail = rd.IsDBNull(6) ? null : rd.GetString(6),
                        Success = !rd.IsDBNull(7) && Convert.ToInt64(rd.GetValue(7)) != 0
                    });
                }
            }
            using (var cmd2 = conn.CreateCommand())
            {
                cmd2.CommandText = "SELECT DISTINCT Action FROM AuditLogs ORDER BY Action";
                using var rd2 = cmd2.ExecuteReader();
                while (rd2.Read())
                    if (!rd2.IsDBNull(0)) actions.Add(rd2.GetString(0));
            }
        }

        ViewBag.Actions = actions;
        ViewBag.Q = q;
        ViewBag.Action = act;
        ViewBag.Days = days > 0 ? days.ToString() : "";
        return View("Index", rows);
    }

    private static DateTime ParseDt(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
}
