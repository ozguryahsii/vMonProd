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
        /// <summary>Lisans Fazı L1: kurulum key olmadan İLERLEYEMEZ (Basic/ücretsiz dahil).
        /// DB adımından ÖNCE doğrulanır — Basic yalnız SQLite kurabilir.</summary>
        public string LicenseKey { get; set; } = "";
        public string Provider { get; set; } = "Sqlite";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 0;
        public string Database { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool UseSsl { get; set; } = false;
        public bool TrustServerCertificate { get; set; } = false;
        public string ConnectionStringRaw { get; set; } = "";  // gelişmiş: ADO.NET veya JDBC URL
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
            ConnectionStringRaw = f.ConnectionStringRaw?.Trim() ?? "",
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

    /// <summary>Lisans key doğrulama (adım 0 — DB seçiminden önce zorunlu). Paket bilgisi ve
    /// SQLite kısıtı (Basic) buradan döner; sihirbaz DB seçeneklerini buna göre daraltır.</summary>
    [HttpPost]
    public IActionResult ValidateLicense(SetupForm f)
    {
        if (!LicenseService.TryParse(f.LicenseKey, out var info, out var err))
            return Json(new { ok = false, message = err });
        if (info!.IsExpired)
            return Json(new { ok = false, message = $"Bu lisansın süresi dolmuş (bitiş: {info.ExpiresAt:dd.MM.yyyy})." });
        return Json(new
        {
            ok = true,
            edition = info.Edition.ToString(),
            company = info.Company,
            expires = info.ExpiresAt.ToString("dd.MM.yyyy"),
            daysLeft = info.DaysLeft,
            sqliteOnly = info.SqliteOnly,
            message = $"{info.Edition} lisansı doğrulandı ✅ ({info.Company} — bitiş {info.ExpiresAt:dd.MM.yyyy})"
        });
    }

    /// <summary>Veritabanı bağlantı testi (adım 1 ilerlemeden önce zorunlu).</summary>
    [HttpPost]
    public async Task<IActionResult> TestDb(SetupForm f, CancellationToken ct)
    {
        try
        {
            var c = ToConfig(f);
            if (c.Provider != DbProviderKind.Sqlite && string.IsNullOrWhiteSpace(c.ConnectionStringRaw))
            {
                if (string.IsNullOrWhiteSpace(c.Host)) return Json(new { ok = false, message = "Sunucu (Host) zorunlu." });
                if (string.IsNullOrWhiteSpace(c.Database)) return Json(new { ok = false, message = c.Provider == DbProviderKind.Oracle ? "Service Name zorunlu." : "Veritabanı adı zorunlu." });
            }
            var pass = ResolvePlainPassword(f, c);
            await using var ctx = BuildContext(c, pass);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                // CanConnectAsync gerçek hatayı yutar → doğrudan bağlantı aç ki tam SQL hatası (login/DB yok/SSL) yüzeye çıksın.
                var conn = ctx.Database.GetDbConnection();
                await conn.OpenAsync(cts.Token);
                await conn.CloseAsync();
                return Json(new { ok = true, message = "Bağlantı başarılı ✅" });
            }
            catch (OperationCanceledException) { return Json(new { ok = false, message = "Bağlantı zaman aşımı (12 sn) — sunucuya erişilemiyor. Firewall, host/port ve SQL Server Browser servisini kontrol edin." }); }
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, message = "Hata: " + Trim(ex) });
        }
    }

    /// <summary>Kurulumun oluşturacağı şema (CREATE) betiğini üretir — DB'ye DOKUNMAZ, yalnız DDL döndürür.
    /// DBA'nın "hangi scriptleri çalıştırıyor" sorusunu yanıtlamak ve ORA-00902 gibi tip hatalarını teşhis için.</summary>
    [HttpPost]
    public IActionResult SchemaScript(SetupForm f)
    {
        try
        {
            var c = ToConfig(f);
            var pass = ResolvePlainPassword(f, c);
            using var ctx = BuildContext(c, pass);
            // GenerateCreateScript modeli seçilen sağlayıcının SQL üreticisiyle DDL'e çevirir (bağlantı gerektirmez).
            var script = ctx.Database.GenerateCreateScript();
            var header = $"-- vMon şema betiği ({c.Provider}) — kurulumda EnsureCreated bunu çalıştırır.\r\n"
                       + "-- Bu betik yalnızca üretildi; SUNUCUDA ÇALIŞTIRILMADI. DBA incelemesi içindir.\r\n\r\n";
            return Content(header + script, "text/plain; charset=utf-8");
        }
        catch (Exception ex)
        {
            return Content("-- Betik üretilemedi: " + ex.GetBaseException().Message, "text/plain; charset=utf-8");
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
            // Lisans Fazı L1: key olmadan kurulum TAMAMLANAMAZ; Basic paket yalnız SQLite kurabilir.
            if (!LicenseService.TryParse(f.LicenseKey, out var lic, out var licErr))
                return Json(new { ok = false, message = "Lisans: " + licErr });
            if (lic!.IsExpired)
                return Json(new { ok = false, message = $"Lisansın süresi dolmuş (bitiş: {lic.ExpiresAt:dd.MM.yyyy})." });
            Enum.TryParse<DbProviderKind>(f.Provider, true, out var provKind);
            if (lic.SqliteOnly && provKind != DbProviderKind.Sqlite)
                return Json(new { ok = false, message = "Basic (ücretsiz) lisans yalnız SQLite veritabanını destekler. Diğer veritabanları için Standard/Enterprise lisans gerekir." });

            if (string.IsNullOrWhiteSpace(f.CompanyName)) return Json(new { ok = false, message = "Şirket adı zorunlu." });
            if (string.IsNullOrWhiteSpace(f.AdminUsers)) return Json(new { ok = false, message = "Yönetici kullanıcı adı zorunlu." });
            // Parola politikası (kurulumda varsayılan: en az 12 karakter + karmaşıklık — PCI 8.3.6 / NIST 800-63B)
            var (pwOk, pwErr) = PasswordHasher.ValidatePolicy(f.AdminPassword, 12, true);
            if (!pwOk) return Json(new { ok = false, message = "Yönetici şifresi: " + pwErr });

            var c = ToConfig(f);
            var pass = ResolvePlainPassword(f, c);

            // 1) Şemayı kur + ayarları seed et (seçilen DB üzerinde)
            await using (var ctx = BuildContext(c, pass))
            {
                await ctx.Database.EnsureCreatedAsync(ct);
                if (c.Provider == DbProviderKind.Sqlite)
                    DbSchemaHelper.EnsureSchema(ctx, _logger);
                else
                {
                    // EnsureCreated, şemada tek bir (ör. önceki başarısız denemeden kalan) tablo görünce
                    // HİÇBİR tabloyu oluşturmaz → sonraki sorgu ORA-00942 (table does not exist) verir.
                    // Eksik tabloları/kolonları tek tek tamamla → yarım/kirli şemada da kurulum tamamlanır.
                    await SchemaSync.EnsureTablesAsync(ctx, c.Provider, _logger, ct);
                    await SchemaSync.EnsureColumnsAsync(ctx, c.Provider, _logger, ct);
                }

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
            c.LicenseKey = f.LicenseKey.Trim();   // lisans DB'den önce gerektiği için bootstrap'ta durur
            if (c.Provider != DbProviderKind.Sqlite)
            {
                if (!string.IsNullOrWhiteSpace(c.ConnectionStringRaw))
                {
                    // Ham/JDBC bağlantı dizesi: şifre içerebilir → şifreli sakla, diğer alanları kullanma
                    c.UseVault = false;
                    c.ConnectionStringRawEncrypted = _secrets.Protect(c.ConnectionStringRaw);
                    c.PasswordEncrypted = "";
                }
                else if (f.UseVault)
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

    /// <summary>Yeni yapılandırmayla başlamak için uygulamayı yeniden başlatır.
    /// IIS (InProcess): StopApplication → sonraki istekte worker yeniden başlar.
    /// Windows Service: StopApplication yalnız durdurur → kurtarma (recovery) için sıfırdan-farklı kodla çık (SCM yeniden başlatır).</summary>
    [HttpPost]
    public IActionResult Restart()
    {
        if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
            _ = Task.Run(async () => { await Task.Delay(1500); Environment.Exit(1); });
        else
            _life.StopApplication();
        return Json(new { ok = true });
    }

    private static string Trim(Exception ex)
    {
        var m = ex.GetBaseException().Message;
        return m.Length > 300 ? m[..300] : m;
    }
}
