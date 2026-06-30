using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
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
    private readonly SyslogService _syslog;

    public AuditService(AppDbContext db, IHttpContextAccessor http, ILogger<AuditService> logger, IWebHostEnvironment env, SyslogService syslog)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _env = env;
        _syslog = syslog;
    }

    // Hash-zinciri yazımını serileştirir (eşzamanlı denetim yazımlarında zincir bozulmasın).
    private static readonly SemaphoreSlim _chainLock = new(1, 1);

    /// <summary>Bir kaydın kanonik (değişmez sıralı) metin temsili — hash girdisi.</summary>
    private static string Canonical(AuditLog a) =>
        string.Join("", a.At.ToString("o"), a.User, a.Ip, a.Action, a.Target, a.Detail, a.Success ? "1" : "0");

    /// <summary>SHA-256(öncekiHash + kanonik) → hex. Önceki yoksa "GENESIS" kullanılır.</summary>
    private static string ComputeHash(string? prevHash, AuditLog a) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((prevHash ?? "GENESIS") + "" + Canonical(a))));

    /// <summary>Denetim zincirinin bütünlüğünü doğrular: her kaydın hash'i yeniden hesaplanıp karşılaştırılır;
    /// zincir bağı (PrevHash) kontrol edilir. Herhangi bir değişiklik/silme tespit edilir. (Retention ile en eski
    /// kayıtların silinmesi normaldir; doğrulama mevcut en eski kayıttan başlar.)</summary>
    public static async Task<(bool ok, string message, int? badId)> VerifyChainAsync(AppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.AuditLogs.AsNoTracking().Where(x => x.Hash != null).OrderBy(x => x.Id).ToListAsync(ct);
        if (rows.Count == 0) return (true, "Henüz hash-zincirli kayıt yok.", null);
        string? prev = rows[0].PrevHash; // ilk kaydın öncesi retention ile silinmiş olabilir → kabul
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0 && r.PrevHash != prev) return (false, $"#{r.Id} kaydında zincir kopması (silme/ekleme).", r.Id);
            if (ComputeHash(r.PrevHash, r) != r.Hash) return (false, $"#{r.Id} kaydı değiştirilmiş (hash uyuşmuyor).", r.Id);
            prev = r.Hash;
        }
        return (true, $"{rows.Count} kayıt doğrulandı — bütünlük sağlam.", null);
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

            var entry = new AuditLog
            {
                At = DateTime.UtcNow,
                User = user ?? "?",
                Ip = ip,
                Action = action,
                Target = target,
                Detail = detail,
                Success = success
            };
            // Değiştirilemezlik zinciri: önceki kaydın hash'ini al, bu kaydın hash'ini hesapla. Yarış olmasın diye serileştir.
            await _chainLock.WaitAsync(ct);
            try
            {
                var prevHash = await _db.AuditLogs.AsNoTracking().OrderByDescending(x => x.Id)
                    .Select(x => x.Hash).FirstOrDefaultAsync(ct);
                entry.PrevHash = prevHash;
                entry.Hash = ComputeHash(prevHash, entry);
                _db.AuditLogs.Add(entry);
                await _db.SaveChangesAsync(ct);
            }
            finally { _chainLock.Release(); }

            _syslog.Forward(entry);   // SIEM/syslog'a ilet (fire-and-forget; açıksa)
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
                    $"{DateTime.UtcNow:o}\t{action}\t{target}\t{ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
            }
            catch { /* dosyaya da yazılamıyorsa sessiz geç */ }
        }
    }
}
