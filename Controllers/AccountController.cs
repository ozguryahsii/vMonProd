using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

public class AccountController : Controller
{
    private readonly SettingsService _settings;
    private readonly LdapAuthService _ldap;
    private readonly IWebHostEnvironment _env;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public AccountController(SettingsService settings, LdapAuthService ldap, IWebHostEnvironment env, AppDbContext db, AuditService audit)
    {
        _settings = settings;
        _ldap = ldap;
        _env = env;
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.HasLogo = LogoPath(await _settings.GetAsync()) != null;
        return View();
    }

    /// <summary>Giriş ekranı logosunu Data klasöründen servis eder (oturum öncesi erişilebilir).</summary>
    [HttpGet]
    public async Task<IActionResult> Logo()
    {
        var path = LogoPath(await _settings.GetAsync());
        if (path == null) return NotFound();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var ct = ext switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        // Eski SVG logolar için bile script çalışmasını engelle
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, ct);
    }

    private string? LogoPath(MonitorSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.LoginLogoFile)) return null;
        var path = Path.Combine(_env.ContentRootPath, "Data", s.LoginLogoFile);
        return System.IO.File.Exists(path) ? path : null;
    }

    // Kaba kuvvet koruması: kullanıcı adı + IP başına ardışık başarısız denemeler (PCI DSS 8.3.4).
    // Eşik ve süre Ayarlar'dan yönetilir (varsayılan 10 deneme / 30 dk).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime FirstAt)> _loginGate = new();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var settings = await _settings.GetAsync();
        var maxAttempts = settings.MaxLoginAttempts;
        var lockWindow = TimeSpan.FromMinutes(settings.LockoutMinutes);
        var gateKey = (username ?? "").Trim().ToLowerInvariant() + "|" + ip;

        if (_loginGate.TryGetValue(gateKey, out var st) && st.Count >= maxAttempts && DateTime.UtcNow - st.FirstAt < lockWindow)
        {
            await _audit.LogAsync("login.locked", username, $"Hesap geçici kilitli ({maxAttempts} başarısız deneme).", false);
            ViewBag.Error = $"Çok fazla başarısız deneme. Hesap {settings.LockoutMinutes} dakika kilitlendi.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // ---- Yerel (LDAP olmayan) kullanıcı: kurulumda oluşturulan admin. LDAP'tan önce denenir. ----
        var uname = (username ?? "").Trim();
        var localUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.IsLocal && u.Sam.ToLower() == uname.ToLower());
        if (localUser != null)
        {
            if (!localUser.IsActive)
            {
                await _audit.LogAsync("login.denied", uname, "Yerel kullanıcı pasif.", false);
                ViewBag.Error = "Hesabınız pasif durumda.";
                ViewBag.ReturnUrl = returnUrl; return View();
            }
            if (PasswordHasher.Verify(password, localUser.PasswordHash))
            {
                _loginGate.TryRemove(gateKey, out _);
                return await CompleteLoginAsync(localUser, settings.IsAdmin(localUser.Sam),
                    localUser.DisplayName ?? localUser.Sam, returnUrl);
            }
            var le = _loginGate.TryGetValue(gateKey, out var lex) && DateTime.UtcNow - lex.FirstAt < lockWindow
                ? (lex.Count + 1, lex.FirstAt) : (1, DateTime.UtcNow);
            _loginGate[gateKey] = le;
            await _audit.LogAsync("login.fail", uname, "Yerel kullanıcı şifresi hatalı.", false);
            ViewBag.Error = "Kullanıcı adı veya şifre hatalı.";
            ViewBag.ReturnUrl = returnUrl; ViewBag.Username = username; return View();
        }

        var result = _ldap.Validate(settings, username, password);

        if (!result.Success)
        {
            // Pencere dolduysa sayacı sıfırla, değilse artır
            var entry = _loginGate.TryGetValue(gateKey, out var e) && DateTime.UtcNow - e.FirstAt < lockWindow
                ? (e.Count + 1, e.FirstAt) : (1, DateTime.UtcNow);
            _loginGate[gateKey] = entry;
            await _audit.LogAsync("login.fail", username, result.Error, false);
            ViewBag.Error = result.Error;
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Username = username;
            return View();
        }
        _loginGate.TryRemove(gateKey, out _);

        var sam = result.Sam ?? username;
        var isAdmin = settings.IsAdmin(sam);

        // Kullanıcıyı kaydet/güncelle; yeni kullanıcıya varsayılan izin (yalnızca görüntüleme)
        var appUser = await _db.AppUsers.FirstOrDefaultAsync(u => u.Sam == sam);
        if (appUser == null)
        {
            appUser = new AppUser { Sam = sam, PermissionsCsv = Perms.DashboardsView };
            _db.AppUsers.Add(appUser);
        }

        // Pasifleştirilmiş (AD güvenlik grubundan düşmüş) kullanıcı giriş yapamaz (PCI DSS 8.2.4-8.2.5)
        if (appUser.Id != 0 && !appUser.IsActive)
        {
            await _audit.LogAsync("login.denied", sam, "Kullanıcı pasif (güvenlik grubundan düşmüş).", false);
            ViewBag.Error = "Hesabınız pasif durumda. Lütfen yöneticinizle iletişime geçin.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        appUser.DisplayName = result.DisplayName ?? username;
        return await CompleteLoginAsync(appUser, isAdmin, result.DisplayName ?? username, returnUrl);
    }

    /// <summary>Başarılı kimlik doğrulama sonrası ortak adımlar: claim'ler + oturum + tema/dil çerezi + denetim + yönlendirme.
    /// Hem yerel kullanıcı hem LDAP başarısında çağrılır.</summary>
    private async Task<IActionResult> CompleteLoginAsync(AppUser appUser, bool isAdmin, string displayName, string? returnUrl)
    {
        appUser.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, displayName),
            new("sam", appUser.Sam)
        };
        if (isAdmin)
            claims.Add(new Claim("admin", "true"));
        else
            foreach (var p in appUser.Permissions())
                claims.Add(new Claim("perm", p));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
        WritePrefCookies(appUser.Theme, appUser.Language);
        await _audit.LogAsync("login.success", appUser.Sam, isAdmin ? "Admin girişi" : "Kullanıcı girişi", true, user: appUser.Sam);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    private void WritePrefCookies(string? theme, string? lang)
    {
        var opt = new CookieOptions { Expires = DateTimeOffset.Now.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, Path = "/" };
        Response.Cookies.Append("vmon_theme", theme == "dark" ? "dark" : "light", opt);
        Response.Cookies.Append("vmon_lang", lang == "en" ? "en" : "tr", opt);
    }

    /// <summary>Kullanıcının tema/dil tercihini kaydeder (menüden çağrılır). Çerez + DB.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPreference(string? theme, string? lang)
    {
        theme = theme == "dark" ? "dark" : (theme == "light" ? "light" : null);
        lang = lang == "en" ? "en" : (lang == "tr" ? "tr" : null);

        var sam = User.FindFirst("sam")?.Value;
        if (!string.IsNullOrWhiteSpace(sam))
        {
            var u = await _db.AppUsers.FirstOrDefaultAsync(x => x.Sam == sam);
            if (u != null)
            {
                if (theme != null) u.Theme = theme;
                if (lang != null) u.Language = lang;
                await _db.SaveChangesAsync();
            }
        }
        // Çerezleri güncel değerlerle yaz (anonim/açık modda da çalışsın)
        WritePrefCookies(theme ?? Request.Cookies["vmon_theme"], lang ?? Request.Cookies["vmon_lang"]);
        return Ok(new { ok = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("logout", User.FindFirst("sam")?.Value ?? User.Identity?.Name);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
