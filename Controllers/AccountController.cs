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
    private readonly OtpService _otp;

    public AccountController(SettingsService settings, LdapAuthService ldap, IWebHostEnvironment env, AppDbContext db, AuditService audit, OtpService otp)
    {
        _settings = settings;
        _ldap = ldap;
        _env = env;
        _db = db;
        _audit = audit;
        _otp = otp;
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
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
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
                return await ProceedAsync(localUser, settings.IsAdmin(localUser.Sam),
                    localUser.DisplayName ?? localUser.Sam, returnUrl, settings);
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
        // LDAP'tan gelen e-postayı sakla (OTP e-posta kanalı + Kullanıcılar ekranı). Elle girilmişse koru/güncelle.
        if (!string.IsNullOrWhiteSpace(result.Email)) appUser.Email = result.Email;
        return await ProceedAsync(appUser, isAdmin, result.DisplayName ?? username, returnUrl, settings);
    }

    /// <summary>Şifre doğrulandıktan sonraki adım: OTP kapalıysa doğrudan giriş; açıksa kod üret+gönder ve OTP ekranına yönlendir.</summary>
    private async Task<IActionResult> ProceedAsync(AppUser appUser, bool isAdmin, string displayName, string? returnUrl, MonitorSettings settings)
    {
        if (!settings.OtpEnabled)
            return await CompleteLoginAsync(appUser, isAdmin, displayName, returnUrl);

        // OTP gerekli → yeni LDAP kullanıcısı verify adımında bulunabilsin diye önce kaydet
        if (appUser.Id == 0) { await _db.SaveChangesAsync(); }

        var (token, code) = _otp.Create(appUser, isAdmin, displayName, returnUrl);
        var (ok, err) = await _otp.SendAsync(settings, appUser, code);
        if (!ok)
        {
            await _audit.LogAsync("login.otp.failsend", appUser.Sam, err, false);
            ViewBag.Error = "Doğrulama kodu gönderilemedi: " + err + " (Profil bilgilerinizi/kanalı kontrol edin.)";
            ViewBag.ReturnUrl = returnUrl; return View("Login");
        }
        Response.Cookies.Append("vmon_otp", token, new CookieOptions
        { HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromMinutes(6), Path = "/" });
        await _audit.LogAsync("login.otp.sent", appUser.Sam, $"Kanal: {settings.OtpChannel}", true);
        return RedirectToAction(nameof(Otp));
    }

    [HttpGet]
    public IActionResult Otp()
    {
        var p = _otp.Peek(Request.Cookies["vmon_otp"]);
        if (p == null) return RedirectToAction(nameof(Login));
        ViewBag.Channel = _settings.GetAsync().GetAwaiter().GetResult().OtpChannel;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(string? code)
    {
        var token = Request.Cookies["vmon_otp"];
        var (ok, p, err) = _otp.Verify(token, code);
        if (!ok)
        {
            if (p == null && _otp.Peek(token) == null) { Response.Cookies.Delete("vmon_otp"); return RedirectToAction(nameof(Login)); }
            ViewBag.Error = err;
            ViewBag.Channel = (await _settings.GetAsync()).OtpChannel;
            return View("Otp");
        }
        Response.Cookies.Delete("vmon_otp");
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Sam == p!.Sam);
        if (user == null) return RedirectToAction(nameof(Login));
        await _audit.LogAsync("login.otp.ok", user.Sam, "OTP doğrulandı", true);
        return await CompleteLoginAsync(user, p!.IsAdmin, p!.DisplayName, p!.ReturnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendOtp()
    {
        var p = _otp.Peek(Request.Cookies["vmon_otp"]);
        if (p == null) return RedirectToAction(nameof(Login));
        var settings = await _settings.GetAsync();
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Sam == p.Sam);
        if (user == null) return RedirectToAction(nameof(Login));
        var (token, code) = _otp.Create(user, p.IsAdmin, p.DisplayName, p.ReturnUrl);
        var (ok, err) = await _otp.SendAsync(settings, user, code);
        Response.Cookies.Append("vmon_otp", token, new CookieOptions
        { HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromMinutes(6), Path = "/" });
        ViewBag.Channel = settings.OtpChannel;
        ViewBag.Error = ok ? null : ("Kod gönderilemedi: " + err);
        ViewBag.Info = ok ? "Yeni kod gönderildi." : null;
        return View("Otp");
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

    /// <summary>Oturum açmış kullanıcının kendi iletişim bilgileri (OTP için e-posta/telefon).</summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var sam = User.FindFirst("sam")?.Value;
        var u = await _db.AppUsers.FirstOrDefaultAsync(x => x.Sam == sam);
        if (u == null) return RedirectToAction("Index", "Home");
        var st = await _settings.GetAsync();
        ViewBag.MinPasswordLength = st.MinPasswordLength;
        ViewBag.RequireComplexity = st.RequirePasswordComplexity;
        return View(u);
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string? email, string? phone, string? currentPassword, string? newPassword, string? confirmPassword)
    {
        var sam = User.FindFirst("sam")?.Value;
        var u = await _db.AppUsers.FirstOrDefaultAsync(x => x.Sam == sam);
        if (u == null) return RedirectToAction(nameof(Profile));

        u.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        u.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        // Yerel kullanıcı parola değişimi (opsiyonel): yeni parola girildiyse mevcut doğrula + politika uygula
        if (u.IsLocal && !string.IsNullOrEmpty(newPassword))
        {
            if (!PasswordHasher.Verify(currentPassword ?? "", u.PasswordHash))
            { TempData["Error"] = "Mevcut şifre hatalı."; return RedirectToAction(nameof(Profile)); }
            if (newPassword != confirmPassword)
            { TempData["Error"] = "Yeni şifre ile tekrarı eşleşmiyor."; return RedirectToAction(nameof(Profile)); }
            var settings = await _settings.GetAsync();
            var (ok, err) = PasswordHasher.ValidatePolicy(newPassword, settings.MinPasswordLength, settings.RequirePasswordComplexity);
            if (!ok) { TempData["Error"] = err; return RedirectToAction(nameof(Profile)); }
            // Tekrar kullanım engeli (PCI 8.3.7): yeni parola mevcut veya son N parolayla aynı olamaz
            var history = (u.PasswordHistory ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (PasswordHasher.Verify(newPassword, u.PasswordHash) || history.Any(h => PasswordHasher.Verify(newPassword, h)))
            { TempData["Error"] = $"Yeni parola son {settings.PasswordHistoryCount} parolanızdan farklı olmalı."; return RedirectToAction(nameof(Profile)); }
            if (settings.PasswordHistoryCount > 0 && !string.IsNullOrEmpty(u.PasswordHash))
            {
                history.Insert(0, u.PasswordHash!);   // eski parolayı geçmişe ekle
                u.PasswordHistory = string.Join("\n", history.Take(settings.PasswordHistoryCount));
            }
            u.PasswordHash = PasswordHasher.Hash(newPassword);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("user.password.change", u.Sam, "Yerel kullanıcı parolasını değiştirdi", true);
            TempData["Message"] = "Profil ve şifre güncellendi.";
            return RedirectToAction(nameof(Profile));
        }

        await _db.SaveChangesAsync();
        TempData["Message"] = "Profil güncellendi.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("logout", User.FindFirst("sam")?.Value ?? User.Identity?.Name);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
