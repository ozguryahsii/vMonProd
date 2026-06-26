using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>İlk kurulum sihirbazı. Uygulama yapılandırılmamışsa (bootstrap.json yok) tüm istekler buraya yönlenir.
/// Adımlar: 1) Veritabanı (+ bağlantı testi) 2) Şirket 3) Admin 4) SMTP (opsiyonel, test ile) → Bitir.
/// Kurulum öncesi (oturum yok) olduğundan POST'lar antiforgery'den muaftır.</summary>
[IgnoreAntiforgeryToken]
public class SetupController : Controller
{
    private readonly BootstrapService _bootstrap;
    private readonly BootstrapConfig _cfg;
    private readonly ISecretProtector _secrets;
    private readonly EmailService _email;
    private readonly IWebHostEnvironment _env;
    private readonly IHostApplicationLifetime _life;
    private readonly ILogger<SetupController> _logger;

    public SetupController(BootstrapService bootstrap, BootstrapConfig cfg, ISecretProtector secrets,
        EmailService email, IWebHostEnvironment env, IHostApplicationLifetime life, ILogger<SetupController> logger)
    {
        _bootstrap = bootstrap; _cfg = cfg; _secrets = secrets; _email = email;
        _env = env; _life = life; _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (_cfg.Configured) return Redirect("/");
        return View();
    }

    // ---- Sihirbaz POST modeli ----
    public class SetupForm
    {
        public string Provider { get; set; } = "Sqlite";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 0;
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool UseSsl { get; set; } = false;
        public bool TrustServerCertificate { get; set; } = false;
        // Vault (opsiyonel)
        public bool UseVault { get; set; } = false;
        public string VaultUrl { get; set; } = "";
        public string VaultToken { get; set; } = "";
        public string VaultUserKey { get; set; } = "";
        public string VaultKey { get; set; } = "";
        // Diğer adımlar
        public string CompanyName { get; set; } = "";
        public string AdminUsers { get; set; } = "";     // yerel admin kullanıcı adı
        public string AdminPassword { get; set; } = "";  // yerel admin şifresi
        public bool SmtpEnabled { get; set; } = false;
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 25;
        public string MailFrom { get; set; } = "";
        public string MailRecipients { get; set; } = "";
        public string SmtpTestTo { get; set; } = "";
    }

    private BootstrapConfig ToConfig(SetupForm f)
    {
        Enum.TryParse<DbProviderKind>(f.Provider, true, out var kind);
        var c = new BootstrapConfig
        {
            Provider = kind,
            Host = f.Host?.Trim() ?? "",
            Port = f.Port,
            Database = f.Database?.Trim() ?? "",
            Username = f.Username?.Trim() ?? "",
            UseSsl = f.UseSsl,
            TrustServerCertificate = f.TrustServerCertificate,
            UseVault = f.UseVault,
            VaultUrl = f.VaultUrl?.Trim() ?? "",
            VaultUserKey = f.VaultUserKey?.Trim() ?? "",
            VaultKey = f.VaultKey?.Trim() ?? ""
        };
        if (kind == DbProviderKind.Sqlite)
            c.SqlitePath = Path.Combine(_env.ContentRootPath, "Data", "monitoring.db");
        return c;
    }

    /// <summary>Düz şifreyi çözer: Vault açıksa Vault'tan, değilse formdaki şifre.</summary>
    private string ResolvePlainPassword(SetupForm f, BootstrapConfig c)
    {
        if (c.Provider == DbProviderKind.Sqlite) return "";
        if (f.UseVault)
        {
            // Vault token'ı geçici olarak şifreleyip ResolvePassword ile çöz
            c.VaultTokenEncrypted = _secrets.Protect(f.VaultToken?.Trim() ?? "");
            return DbProviderConfig.ResolvePassword(c, _secrets);
        }
        return f.Password ?? "";
    }

    private AppDbContext BuildContext(BootstrapConfig c, string plainPassword)
    {
        // SQLite: veritabanı dosyasının klasörü yoksa oluştur (Error 14 'unable to open database file' önlenir)
        if (c.Provider == DbProviderKind.Sqlite && !string.IsNullOrEmpty(c.SqlitePath))
        {
            var dir = Path.GetDirectoryName(c.SqlitePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        var connStr = DbProviderConfig.BuildConnectionString(c, plainPassword);
        var ob = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderConfig.Apply(ob, c, connStr);
        return new AppDbContext(ob.Options);
    }

    /// <summary>Veritabanı bağlantı testi (adım 1 ilerlemeden önce zorunlu).</summary>
    [HttpPost]
    public async Task<IActionResult> TestDb(SetupForm f, CancellationToken ct)
    {
        try
        {
            var c = ToConfig(f);
            if (c.Provider != DbProviderKind.Sqlite)
            {
                if (string.IsNullOrWhiteSpace(c.Host)) return Json(new { ok = false, message = "Sunucu (Host) zorunlu." });
                if (string.IsNullOrWhiteSpace(c.Database)) return Json(new { ok = false, message = c.Provider == DbProviderKind.Oracle ? "Service Name zorunlu." : "Veritabanı adı zorunlu." });
            }
            var pass = ResolvePlainPassword(f, c);
            await using var ctx = BuildContext(c, pass);
            var can = await ctx.Database.CanConnectAsync(ct);
            return can
                ? Json(new { ok = true, message = "Bağlantı başarılı ✅" })
                : Json(new { ok = false, message = "Bağlanılamadı — sunucu/veritabanı/kullanıcı bilgilerini kontrol edin. (Veritabanı henüz yoksa önce oluşturun.)" });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Hata: " + Trim(ex) });
        }
    }

    /// <summary>SMTP testi (SMTP doldurulduysa ilerlemeden önce zorunlu).</summary>
    [HttpPost]
    public async Task<IActionResult> TestSmtp(SetupForm f, CancellationToken ct)
    {
        try
        {
            var to = string.IsNullOrWhiteSpace(f.SmtpTestTo) ? f.MailRecipients : f.SmtpTestTo;
            if (string.IsNullOrWhiteSpace(to)) return Json(new { ok = false, message = "Test için bir alıcı e-posta girin." });
            var ms = new MonitorSettings
            {
                EmailEnabled = true,
                SmtpHost = f.SmtpHost?.Trim() ?? "",
                SmtpPort = f.SmtpPort > 0 ? f.SmtpPort : 25,
                MailFrom = string.IsNullOrWhiteSpace(f.MailFrom) ? "vmon@localhost" : f.MailFrom.Trim(),
                MailRecipients = to
            };
            await _email.SendAsync(ms, "vMon kurulum testi", "Bu bir vMon kurulum SMTP test e-postasıdır. ✅", ct);
            return Json(new { ok = true, message = "Test e-postası gönderildi ✅ (gelen kutusunu kontrol edin)" });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "SMTP hatası: " + Trim(ex) });
        }
    }

    /// <summary>Kurulumu tamamla: şemayı kur, ayarları seed et, bootstrap.json yaz.</summary>
    [HttpPost]
    public async Task<IActionResult> Complete(SetupForm f, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(f.CompanyName)) return Json(new { ok = false, message = "Şirket adı zorunlu." });
            if (string.IsNullOrWhiteSpace(f.AdminUsers)) return Json(new { ok = false, message = "Yönetici kullanıcı adı zorunlu." });
            if (string.IsNullOrWhiteSpace(f.AdminPassword) || f.AdminPassword.Length < 8)
                return Json(new { ok = false, message = "Yönetici şifresi en az 8 karakter olmalı." });

            var c = ToConfig(f);
            var pass = ResolvePlainPassword(f, c);

            // 1) Şemayı kur + ayarları seed et (seçilen DB üzerinde)
            await using (var ctx = BuildContext(c, pass))
            {
                await ctx.Database.EnsureCreatedAsync(ct);
                if (c.Provider == DbProviderKind.Sqlite)
                    DbSchemaHelper.EnsureSchema(ctx, _logger);

                // Yerel admin kullanıcı (şifreli) — kurulumdan sonra şifreli giriş zorunlu
                var adminSam = f.AdminUsers.Trim();
                if (!await ctx.AppUsers.AnyAsync(u => u.Sam == adminSam, ct))
                {
                    ctx.AppUsers.Add(new AppUser
                    {
                        Sam = adminSam,
                        DisplayName = adminSam,
                        IsLocal = true,
                        IsActive = true,
                        PasswordHash = PasswordHasher.Hash(f.AdminPassword),
                        PermissionsCsv = ""
                    });
                    await ctx.SaveChangesAsync(ct);
                }

                var ss = new SettingsService(ctx);
                var ms = await ss.GetAsync(ct);
                ms.CompanyName = f.CompanyName.Trim();
                ms.AdminUsers = adminSam;
                ms.AuthEnabled = true;   // yerel admin oluşturuldu → giriş ekranı + şifre zorunlu
                if (f.SmtpEnabled && !string.IsNullOrWhiteSpace(f.SmtpHost))
                {
                    ms.EmailEnabled = true;
                    ms.SmtpHost = f.SmtpHost.Trim();
                    ms.SmtpPort = f.SmtpPort > 0 ? f.SmtpPort : 25;
                    ms.MailFrom = string.IsNullOrWhiteSpace(f.MailFrom) ? "vmon@localhost" : f.MailFrom.Trim();
                    ms.MailRecipients = f.MailRecipients?.Trim() ?? "";
                }
                await ss.SaveAsync(ms, ct);
            }

            // 2) bootstrap.json yaz (artık yapılandırıldı). Şifre/Vault güvenli saklanır.
            c.Configured = true;
            if (c.Provider != DbProviderKind.Sqlite)
            {
                if (f.UseVault)
                {
                    c.UseVault = true;
                    c.VaultTokenEncrypted = _secrets.Protect(f.VaultToken?.Trim() ?? "");
                    c.PasswordEncrypted = "";
                }
                else
                {
                    c.UseVault = false;
                    c.PasswordEncrypted = _secrets.Protect(pass);
                }
            }
            _bootstrap.Save(c);

            return Json(new { ok = true, message = "Kurulum tamamlandı ✅" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kurulum tamamlanamadı");
            return Json(new { ok = false, message = "Kurulum hatası: " + Trim(ex) });
        }
    }

    /// <summary>Yeni yapılandırmayla başlamak için uygulamayı yeniden başlatır (IIS InProcess otomatik kaldırır).</summary>
    [HttpPost]
    public IActionResult Restart()
    {
        _life.StopApplication();
        return Json(new { ok = true });
    }

    private static string Trim(Exception ex)
    {
        var m = ex.GetBaseException().Message;
        return m.Length > 300 ? m[..300] : m;
    }
}
