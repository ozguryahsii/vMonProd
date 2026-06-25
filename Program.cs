using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;
using vMonitor.Services.Checkers;

var builder = WebApplication.CreateBuilder(args);

// Kestrel sürüm başlığını gizle (bilgi ifşası)
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Tüm POST/PUT/DELETE isteklerinde CSRF token doğrula (form + API)
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));

// API fetch'leri token'ı header ile gönderir
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    // SameAsRequest: HTTPS'te Secure, HTTP'de exception fırlatmaz (HTTPS redirect + HSTS zaten zorlar)
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.HttpOnly = true;
});

// Cookie tabanlı oturum (LDAP login için)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/Login";
        // PCI DSS 8.2.8: 15 dk hareketsizlikte yeniden kimlik doğrulama (kayan süre = boşta zaman aşımı)
        o.ExpireTimeSpan = TimeSpan.FromMinutes(15);
        o.SlidingExpiration = true;
        o.Cookie.Name = "vMon.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;   // yalnızca HTTPS
        o.Cookie.SameSite = SameSiteMode.Strict;             // CSRF'e karşı

        // Yetkiler her istekte VERİTABANINDAN canlı doğrulanır: admin bir kullanıcının yetkisini
        // aldığında oturumu açık olsa bile bir sonraki istekte etkili olur (yeniden giriş gerekmez).
        o.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async ctx =>
            {
                var sam = ctx.Principal?.FindFirst("sam")?.Value;
                if (string.IsNullOrWhiteSpace(sam)) return;
                try
                {
                    var sp = ctx.HttpContext.RequestServices;
                    var db = sp.GetRequiredService<AppDbContext>();
                    var settings = await sp.GetRequiredService<SettingsService>().GetAsync();
                    var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Sam == sam);

                    // Kullanıcı silinmiş veya pasifleştirilmişse oturumu sonlandır
                    if (user == null || !user.IsActive)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    // Principal'ı güncel yetkilerle yeniden kur (bu isteğin User'ı bunu kullanır)
                    var isAdmin = settings.IsAdmin(sam);
                    var id = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
                    id.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName ?? sam));
                    id.AddClaim(new Claim("sam", sam));
                    if (isAdmin) id.AddClaim(new Claim("admin", "true"));
                    else foreach (var p in user.Permissions()) id.AddClaim(new Claim("perm", p));
                    ctx.ReplacePrincipal(new ClaimsPrincipal(id));
                }
                catch
                {
                    // DB erişilemezse herkesi kilitlemeyiz; mevcut principal'ı koru
                }
            }
        };
    });
builder.Services.AddScoped<LdapAuthService>();
builder.Services.AddHttpContextAccessor();

// SQLite database — Data/monitoring.db (uygulama klasörü içinde)
var dbFolder = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dbFolder);
var dbPath = Path.Combine(dbFolder, "monitoring.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<SmsService>();
builder.Services.AddScoped<WhatsappService>();
builder.Services.AddScoped<CheckRunner>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<MutabakatService>();

// Checker'lar
builder.Services.AddScoped<IServiceChecker, HttpChecker>();
builder.Services.AddScoped<IServiceChecker, TcpChecker>();
builder.Services.AddScoped<IServiceChecker, MySqlChecker>();
builder.Services.AddScoped<IServiceChecker, MsSqlChecker>();
builder.Services.AddScoped<IServiceChecker, OracleChecker>();
builder.Services.AddScoped<IServiceChecker, LdapChecker>();
builder.Services.AddScoped<IServiceChecker, DnsChecker>();
builder.Services.AddScoped<IServiceChecker, SftpChecker>();
builder.Services.AddScoped<IServiceChecker, DhcpChecker>();
builder.Services.AddScoped<IServiceChecker, SmtpChecker>();
builder.Services.AddScoped<IServiceChecker, ImapChecker>();
builder.Services.AddScoped<IServiceChecker, PingChecker>();
builder.Services.AddScoped<IServiceChecker, WindowsHealthChecker>();
builder.Services.AddScoped<IServiceChecker, LinuxHealthChecker>();
builder.Services.AddScoped<IServiceChecker, WindowsServiceChecker>();
builder.Services.AddScoped<IServiceChecker, LinuxServiceChecker>();

builder.Services.AddHostedService<MonitoringBackgroundService>();

var app = builder.Build();

// Create database on startup + manuel şema adımları
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    db.Database.EnsureCreated();
    DbSchemaHelper.EnsureSchema(db, logger);

    // Yerleşik Twilio SMS/WhatsApp ayarlarını "Bildirim Kanalları" entegrasyonlarına bir kez taşı
    try
    {
        TwilioChannelMigration.RunAsync(db, scope.ServiceProvider.GetRequiredService<SettingsService>(), logger)
            .GetAwaiter().GetResult();
    }
    catch (Exception ex) { logger.LogError(ex, "Twilio kanal taşıması atlandı."); }

    // TLS güven ayarını giden istemcilere uygula (Vault) — açılışta bir kez
    try
    {
        var s = scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync().GetAwaiter().GetResult();
        VaultClient.TrustInternalCertificates = s.TrustInternalTlsCertificates;
    }
    catch (Exception ex) { logger.LogWarning(ex, "TLS güven ayarı okunamadı, güvenli varsayılan kullanılıyor."); }

    // Denetim kaydı yazma yolunu açılışta sına — başarısız olursa Data\audit-error.log oluşur
    try
    {
        scope.ServiceProvider.GetRequiredService<AuditService>()
            .LogAsync("app.start", "vMon", "Uygulama başlatıldı.", true, user: "sistem").GetAwaiter().GetResult();
    }
    catch (Exception ex) { logger.LogError(ex, "Açılış denetim kaydı yazılamadı."); }
}

// Güvenlik başlıkları (tüm yanıtlara)
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["X-Permitted-Cross-Domain-Policies"] = "none";
    h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    h["Content-Security-Policy"] = "frame-ancestors 'none'";
    h.Remove("X-Powered-By");

    // Dinamik (kimlikli) sayfalar önbelleğe alınmasın — araya giren proxy/WAF eski içerik göstermesin.
    // Statik varlıklar (lib/css/js/font/logo) hariç; onlar asp-append-version ile zaten sürümlenir.
    var p = ctx.Request.Path.Value ?? "";
    bool isStatic = p.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
                 || p.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
                 || p.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
                 || p.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
                 || p.StartsWith("/Account/Logo", StringComparison.OrdinalIgnoreCase);
    if (!isStatic)
    {
        h["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        h["Pragma"] = "no-cache";
        h["Expires"] = "0";
    }
    await next();
});

// API route'ları için düz metin (genel) hata — iç detay sızdırmadan
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseExceptionHandler(errApp =>
        errApp.Run(async ctx =>
        {
            var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            app.Logger.LogError(feature?.Error, "API hatası: {Path}", ctx.Request.Path);
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync("İşlem sırasında bir hata oluştu.");
        })));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Erişim kapısı: AuthEnabled ise, giriş yapmamış kullanıcıyı login'e yönlendir.
// Statik dosyalar (UseStaticFiles yukarıda) ve /Account herkese açıktır.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    bool open = path.StartsWithSegments("/Account")
                || path.StartsWithSegments("/lib")
                || path.StartsWithSegments("/css")
                || path.StartsWithSegments("/js")
                || path.StartsWithSegments("/api/whatsapp")   // gelen WhatsApp webhook'u (kendi gizli anahtarıyla doğrulanır)
                || path.Value == "/favicon.ico";
    if (open)
    {
        await next();
        return;
    }

    var settings = await ctx.RequestServices.GetRequiredService<SettingsService>().GetAsync();
    if (!settings.AuthEnabled || ctx.User?.Identity?.IsAuthenticated == true)
    {
        await next();
        return;
    }

    if (path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.WriteAsync("Oturum gerekli. Lütfen giriş yapın.");
        return;
    }
    ctx.Response.Redirect("/Account/Login?returnUrl=" +
        Uri.EscapeDataString(path + ctx.Request.QueryString));
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
