using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;
using vMonitor.Services.Checkers;

var builder = WebApplication.CreateBuilder(args);

// Windows Service olarak da çalışabilsin (IIS altında no-op). Hem IIS hem servis dağıtımı desteklenir.
builder.Host.UseWindowsService();

// HTTPS zorunluluğu: IIS (TLS) veya servis+sertifika → true (varsayılan); düz-HTTP servis (ters proxy arkası) → false.
// false iken: HTTPS yönlendirme/HSTS kapanır ve oturum çerezi Secure=Always yerine SameAsRequest olur (HTTP'de login çalışır).
var requireHttps = builder.Configuration.GetValue("Hosting:RequireHttps", true);

// Kestrel sürüm başlığını gizle (bilgi ifşası)
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Ters proxy (nginx/IIS) arkasında gerçek şema/IP'yi al (servis modunda TLS proxy'de sonlanırsa)
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear(); o.KnownProxies.Clear();   // iç ağ proxy'sine güven
});

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
        o.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
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

// ---- Veritabanı sağlayıcısı (çoklu-DB) ----
// Önyükleme: Data/bootstrap.json hangi sağlayıcı + bağlantı? Yoksa eski SQLite kurulumundan geriye-uyum.
// Yapılandırılmadıysa kurulum (Setup) moduna girilir; tüm istekler /Setup'a yönlendirilir.
var bootstrap = new BootstrapService(builder.Environment);
var bcfg = bootstrap.EnsureConfig();
var secrets = new DpapiSecretProtector();
builder.Services.AddSingleton(bootstrap);
builder.Services.AddSingleton(bcfg);
builder.Services.AddSingleton<ISecretProtector>(secrets);

if (bcfg.Configured)
{
    var dbPass = DbProviderConfig.ResolvePassword(bcfg, secrets);
    var connStr = DbProviderConfig.BuildConnectionString(bcfg, dbPass);
    builder.Services.AddDbContext<AppDbContext>(o => DbProviderConfig.Apply(o, bcfg, connStr));
}
else
{
    // Kurulum modu: DI grafiği çözülsün diye geçici scratch SQLite (gerçek izleme yapılmaz; middleware /Setup'a yönlendirir).
    var scratch = Path.Combine(builder.Environment.ContentRootPath, "Data", "setup-temp.db");
    builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={scratch}"));
}

builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<SmsService>();
builder.Services.AddScoped<WhatsappService>();
builder.Services.AddScoped<OtpService>();
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

// İzleme arka plan servisi yalnızca yapılandırılmış kurulumda çalışır (Setup modunda değil)
if (bcfg.Configured)
    builder.Services.AddHostedService<MonitoringBackgroundService>();

var app = builder.Build();

// Create database on startup + manuel şema adımları
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Setup modunda HİÇBİR DB işlemi yapılmaz (uygulama her koşulda başlar; kullanıcı /Setup'ta yapılandırır).
    // Yapılandırılmış modda bile DB hazırlığı try/catch ile sarılır — DB erişilemese dahi uygulama 500.30 vermez.
    if (bcfg.Configured)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();   // sıfır kurulum: model'den tam şema (tüm sağlayıcılar)

            // GÜVENLİ yükseltme: model'de olup DB'de eksik olan kolonları ekle (tüm sağlayıcılar; yalnız EKLEME → veri kaybı yok)
            SchemaSync.EnsureColumnsAsync(db, bcfg.Provider, logger).GetAwaiter().GetResult();

            if (bcfg.Provider == DbProviderKind.Sqlite)
                DbSchemaHelper.EnsureSchema(db, logger);   // mevcut SQLite kurulumları: legacy CREATE/ALTER + veri-fix (idempotent)

            try
            {
                TwilioChannelMigration.RunAsync(db, scope.ServiceProvider.GetRequiredService<SettingsService>(), logger)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex) { logger.LogError(ex, "Twilio kanal taşıması atlandı."); }

            try
            {
                var s = scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync().GetAwaiter().GetResult();
                VaultClient.TrustInternalCertificates = s.TrustInternalTlsCertificates;
            }
            catch (Exception ex) { logger.LogWarning(ex, "TLS güven ayarı okunamadı, güvenli varsayılan kullanılıyor."); }

            try
            {
                scope.ServiceProvider.GetRequiredService<AuditService>()
                    .LogAsync("app.start", "vMon", "Uygulama başlatıldı.", true, user: "sistem").GetAwaiter().GetResult();
            }
            catch (Exception ex) { logger.LogError(ex, "Açılış denetim kaydı yazılamadı."); }
        }
        catch (Exception ex)
        {
            // DB açılışta hazırlanamadı (erişilemez/izin yok) — uygulama yine de ayakta kalır, hata loglanır.
            logger.LogError(ex, "Açılış veritabanı hazırlığı başarısız — uygulama başlatılıyor (DB sonra erişilebilir olabilir).");
        }
    }
}

// Ters proxy başlıklarını uygula (servis modu + nginx/IIS TLS sonlandırma)
app.UseForwardedHeaders();

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

// HTTPS yönlendirme/HSTS yalnızca HTTPS zorunluyken (IIS-TLS veya servis+sertifika). Düz-HTTP servis modunda kapalı.
if (requireHttps)
{
    if (!app.Environment.IsDevelopment()) app.UseHsts();
    app.UseHttpsRedirection();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Kurulum kapısı: uygulama henüz yapılandırılmadıysa (bootstrap yok) tüm istekleri /Setup'a yönlendir.
// (Yalnızca Setup modunda kayıtlanır; yapılandırılmış kurulumlarda hiç çalışmaz.)
if (!bcfg.Configured)
{
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        bool allow = path.StartsWithSegments("/Setup")
                  || path.StartsWithSegments("/lib") || path.StartsWithSegments("/css")
                  || path.StartsWithSegments("/js") || path.Equals("/favicon.ico");
        if (!allow) { ctx.Response.Redirect("/Setup"); return; }
        await next();
    });
}

// Erişim kapısı: AuthEnabled ise, giriş yapmamış kullanıcıyı login'e yönlendir.
// Statik dosyalar (UseStaticFiles yukarıda) ve /Account herkese açıktır.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    bool open = path.StartsWithSegments("/Account")
                || path.StartsWithSegments("/Setup")          // kurulum sihirbazı (yapılandırma öncesi)
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
