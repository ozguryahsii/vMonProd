using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Services;

namespace vMonitor.Controllers;

public class SettingsController : Controller
{
    private readonly SettingsService _settings;
    private readonly IWebHostEnvironment _env;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly BackupService _backup;
    private readonly IHostApplicationLifetime _life;
    public SettingsController(SettingsService settings, IWebHostEnvironment env, AppDbContext db, AuditService audit, BackupService backup, IHostApplicationLifetime life)
    {
        _settings = settings;
        _env = env;
        _db = db;
        _audit = audit;
        _backup = backup;
        _life = life;
    }

    /// <summary>Admin değilse (ve oturum açık modundaysa) erişimi reddet.</summary>
    private bool IsAllowed(MonitorSettings settings)
    {
        // Oturum açma kapalıysa herkes erişebilir (açık mod)
        if (User?.Identity?.IsAuthenticated != true) return true;
        // Canlı admin listesine göre yetki — adminlikten çıkarma anında etkili olur
        return settings.IsAdmin(User.FindFirst("sam")?.Value);
    }

    private IActionResult Denied()
    {
        TempData["Error"] = "Ayarlar sayfasına erişim yetkiniz yok (yalnızca uygulama adminleri).";
        return RedirectToAction("Index", "Home");
    }

    public async Task<IActionResult> Index()
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();
        await LoadCredentialsAsync();
        ViewBag.BackupIsSqlite = _backup.IsSqlite;
        ViewBag.Backups = _backup.List(settings.BackupPath);
        return View(settings);
    }

    // ---------------- Yedekleme (SQLite) ----------------

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BackupNow()
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();
        var (file, error) = await _backup.BackupNowAsync(settings.BackupPath, settings.BackupRetentionCount);
        if (error != null) { TempData["Error"] = "Yedek alınamadı: " + error; }
        else { TempData["Message"] = "Yedek alındı: " + file; await _audit.LogAsync("backup.create", null, file); }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadBackup(string file)
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();
        var path = _backup.SafeBackupPath(settings.BackupPath, file);
        if (path == null) return NotFound();
        await _audit.LogAsync("backup.download", null, Path.GetFileName(path));
        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBackup(string file)
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();
        var path = _backup.SafeBackupPath(settings.BackupPath, file);
        if (path != null) { try { System.IO.File.Delete(path); await _audit.LogAsync("backup.delete", null, Path.GetFileName(path)); } catch { } }
        TempData["Message"] = "Yedek silindi.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Klasördeki bir yedeği aktif DB'nin üzerine geri yükler, sonra uygulamayı yeniden başlatır.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreExisting(string file)
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();
        var path = _backup.SafeBackupPath(settings.BackupPath, file);
        if (path == null) { TempData["Error"] = "Yedek bulunamadı."; return RedirectToAction(nameof(Index)); }
        return await DoRestore(path, Path.GetFileName(path));
    }

    /// <summary>Yüklenen bir .db dosyasını aktif DB'nin üzerine geri yükler (eski makineden taşıma için).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(1_073_741_824)]
    public async Task<IActionResult> RestoreUpload(IFormFile? dbFile)
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();
        if (dbFile == null || dbFile.Length == 0) { TempData["Error"] = "Dosya seçilmedi."; return RedirectToAction(nameof(Index)); }
        var temp = Path.Combine(Path.GetTempPath(), "vmon-restore-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var fs = System.IO.File.Create(temp)) await dbFile.CopyToAsync(fs);
            return await DoRestore(temp, dbFile.FileName);
        }
        finally { try { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); } catch { } }
    }

    private async Task<IActionResult> DoRestore(string sourcePath, string label)
    {
        var (ok, error) = await _backup.RestoreAsync(sourcePath);
        if (!ok) { TempData["Error"] = "Geri yükleme başarısız: " + error; return RedirectToAction(nameof(Index)); }
        await _audit.LogAsync("backup.restore", null, label, true);
        // Geri yüklemeden sonra önbellekler/şema güncel olsun diye yeniden başlat
        if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
            _ = Task.Run(async () => { await Task.Delay(1500); Environment.Exit(1); });
        else
            _ = Task.Run(async () => { await Task.Delay(1500); _life.StopApplication(); });
        TempData["Message"] = $"Geri yükleme tamam ({label}). Uygulama yeniden başlatılıyor — birkaç saniye içinde tekrar deneyin.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadCredentialsAsync()
    {
        ViewBag.Credentials = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
            await _db.Credentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        var integrations = await _db.SmsProviders.AsNoTracking()
            .OrderBy(p => p.Kind).ThenBy(p => p.Name).ToListAsync();
        ViewBag.Integrations = integrations;
        ViewBag.SmsProviderNames = integrations.Select(p => p.Name).OrderBy(n => n).ToList();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MonitorSettings model, string? newSmsToken, string? newWhatsappToken)
    {
        var current = await _settings.GetAsync();
        if (!IsAllowed(current)) return Denied();

        // Bu form tamamen elle yönetilir; boş bırakılan (opsiyonel) string alanlar "zorunlu" sayılıp
        // kaydı engellemesin diye model doğrulamasını temizliyoruz. Sayısal alanlar yüklemede zaten
        // güvenli aralığa kırpılır. (Aksi halde her yeni ayar alanı eklendiğinde Kaydet sessizce başarısız oluyordu.)
        ModelState.Clear();

        // Logo alanı bu formda taşınmaz (ayrı yükleme formu) — mevcut değeri koru
        model.LoginLogoFile = current.LoginLogoFile;

        // SMS Auth Token: yeni girilmediyse mevcut (şifreli) değer korunur; girildiyse DPAPI ile şifrele
        model.SmsAuthTokenEncrypted = string.IsNullOrWhiteSpace(newSmsToken)
            ? current.SmsAuthTokenEncrypted
            : CryptoHelper.Encrypt(newSmsToken.Trim());
        // WhatsApp Auth Token: aynı mantık
        model.WhatsappAuthTokenEncrypted = string.IsNullOrWhiteSpace(newWhatsappToken)
            ? current.WhatsappAuthTokenEncrypted
            : CryptoHelper.Encrypt(newWhatsappToken.Trim());

        await _settings.SaveAsync(model);
        // Giden TLS güven ayarını anında uygula (Vault istemcisi)
        VaultClient.TrustInternalCertificates = model.TrustInternalTlsCertificates;
        await _audit.LogAsync("settings.save", null,
            $"TLS güven={model.TrustInternalTlsCertificates}, kilit={model.MaxLoginAttempts}/{model.LockoutMinutes}dk, denetim saklama={model.AuditRetentionDays}g, auth={model.AuthEnabled}");
        TempData["Message"] = "Ayarlar kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadLogo(IFormFile? logoFile)
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();

        if (logoFile == null || logoFile.Length == 0)
        {
            TempData["Error"] = "Dosya seçilmedi.";
            return RedirectToAction(nameof(Index));
        }

        var ext = Path.GetExtension(logoFile.FileName).ToLowerInvariant();
        // SVG kabul edilmez (gömülü script ile XSS riski) — yalnızca raster görüntüler
        var allowed = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
        if (!allowed.Contains(ext))
        {
            TempData["Error"] = "Geçersiz dosya türü. İzin verilenler: PNG, JPG, GIF, WEBP.";
            return RedirectToAction(nameof(Index));
        }
        if (logoFile.Length > 2 * 1024 * 1024)
        {
            TempData["Error"] = "Logo en fazla 2 MB olabilir.";
            return RedirectToAction(nameof(Index));
        }

        var dataDir = Path.Combine(_env.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDir);

        // Eski logoları temizle (uzantı değişmiş olabilir)
        foreach (var old in Directory.GetFiles(dataDir, "login-logo.*"))
            try { System.IO.File.Delete(old); } catch { }

        var fileName = "login-logo" + ext;
        using (var fs = System.IO.File.Create(Path.Combine(dataDir, fileName)))
            await logoFile.CopyToAsync(fs);

        settings.LoginLogoFile = fileName;
        await _settings.SaveAsync(settings);
        await _audit.LogAsync("settings.logo", fileName, "Giriş ekranı logosu güncellendi.");
        TempData["Message"] = "Giriş ekranı logosu güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLogo()
    {
        var settings = await _settings.GetAsync();
        if (!IsAllowed(settings)) return Denied();

        var dataDir = Path.Combine(_env.ContentRootPath, "Data");
        foreach (var old in Directory.GetFiles(dataDir, "login-logo.*"))
            try { System.IO.File.Delete(old); } catch { }

        settings.LoginLogoFile = "";
        await _settings.SaveAsync(settings);
        await _audit.LogAsync("settings.logo", null, "Giriş ekranı logosu kaldırıldı.");
        TempData["Message"] = "Giriş ekranı logosu kaldırıldı.";
        return RedirectToAction(nameof(Index));
    }
}
