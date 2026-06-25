using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>Güvenlik denetim kaydı yazıcısı (PCI DSS 10.2/10.3, ISO 27001 A.8.15, NIST AU-2/AU-3).
/// Kullanıcı kimliği ve IP, geçerli HTTP isteğinden otomatik alınır.</summary>
public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditService> _logger;
    private readonly IWebHostEnvironment _env;

    public AuditService(AppDbContext db, IHttpContextAccessor http, ILogger<AuditService> logger, IWebHostEnvironment env)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _env = env;
    }

    /// <summary>Bir güvenlik olayını kaydeder. Hata olsa bile çağıran işlemi bozmaz.</summary>
    public async Task LogAsync(string action, string? target = null, string? detail = null, bool success = true,
        string? user = null, string? ip = null, CancellationToken ct = default)
    {
        try
        {
            var ctx = _http.HttpContext;
            user ??= ctx?.User?.FindFirst("sam")?.Value
                     ?? ctx?.User?.Identity?.Name
                     ?? (ctx?.User?.Identity?.IsAuthenticated == true ? "?" : "anonim");
            ip ??= ctx?.Connection?.RemoteIpAddress?.ToString();

            _db.AuditLogs.Add(new AuditLog
            {
                At = DateTime.Now,
                User = user ?? "?",
                Ip = ip,
                Action = action,
                Target = target,
                Detail = detail,
                Success = success
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Denetim kaydı yazılamazsa uygulamayı kilitlemeyiz; sunucu loguna + dosyaya düşürürüz.
            _logger.LogError(ex, "Denetim kaydı yazılamadı: {Action} {Target}", action, target);
            try
            {
                var dir = Path.Combine(_env.ContentRootPath, "Data");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "audit-error.log"),
                    $"{DateTime.Now:o}\t{action}\t{target}\t{ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
            catch { /* dosyaya da yazılamıyorsa sessiz geç */ }
        }
    }
}
