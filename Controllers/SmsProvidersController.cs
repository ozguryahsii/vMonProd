using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>UI'dan tanımlanabilen genel HTTP SMS sağlayıcıları — yalnızca admin.
/// Kod değişikliği olmadan yeni sağlayıcı eklemeyi sağlar.</summary>
public class SmsProvidersController : MvcBase
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly SmsService _sms;
    private readonly AuditService _audit;
    public SmsProvidersController(AppDbContext db, SettingsService settings, SmsService sms, AuditService audit)
    { _db = db; _settings = settings; _sms = sms; _audit = audit; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (User?.Identity?.IsAuthenticated == true && !User.IsAppAdmin())
            context.Result = Denied();
        base.OnActionExecuting(context);
    }

    public async Task<IActionResult> Index()
        => View(await _db.SmsProviders.AsNoTracking().OrderBy(p => p.Name).ToListAsync());

    public IActionResult Create(string? kind)
    {
        // "Voice" ve "Ivr" birleştirildi → tek "Ivr" (Sesli Arama / IVR). Eski Voice istekleri Ivr'a düşer.
        var allowed = new[] { "Sms", "Whatsapp", "Ivr" };
        if (string.Equals(kind, "Voice", StringComparison.OrdinalIgnoreCase)) kind = "Ivr";
        var k = allowed.FirstOrDefault(a => a.Equals(kind, StringComparison.OrdinalIgnoreCase)) ?? "Sms";
        return View("Form", new SmsProvider { Kind = k });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var p = await _db.SmsProviders.FindAsync(id);
        if (p == null) return NotFound();
        return View("Form", p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(SmsProvider model, string? newPassword, string? newApiKey)
    {
        ModelState.Remove(nameof(SmsProvider.PasswordEncrypted));
        ModelState.Remove(nameof(SmsProvider.ApiKeyEncrypted));
        model.Username ??= "";

        // Kanal türü doğrulama ("Voice" kaldırıldı → "Ivr" ile birleşti)
        if (string.Equals(model.Kind, "Voice", StringComparison.OrdinalIgnoreCase)) model.Kind = "Ivr";
        var allowedKinds = new[] { "Sms", "Whatsapp", "Ivr" };
        if (string.IsNullOrWhiteSpace(model.Kind) || !allowedKinds.Contains(model.Kind, StringComparer.OrdinalIgnoreCase))
            model.Kind = "Sms";

        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError("", "Entegrasyon adı zorunlu.");
        if (string.Equals(model.Name?.Trim(), "Twilio", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError("", "'Twilio' adı yerleşiktir; farklı bir ad kullanın.");
        if (string.IsNullOrWhiteSpace(model.Url))
            ModelState.AddModelError("", "URL zorunlu.");
        // Aynı ad başka kayıtta var mı
        if (await _db.SmsProviders.AnyAsync(p => p.Id != model.Id && p.Name == model.Name))
            ModelState.AddModelError("", "Bu adda bir sağlayıcı zaten var.");
        if (!ModelState.IsValid) return View("Form", model);

        var isNew = model.Id == 0;
        if (isNew)
        {
            model.PasswordEncrypted = string.IsNullOrEmpty(newPassword) ? "" : CryptoHelper.Encrypt(newPassword);
            model.ApiKeyEncrypted = string.IsNullOrEmpty(newApiKey) ? "" : CryptoHelper.Encrypt(newApiKey);
            _db.SmsProviders.Add(model);
        }
        else
        {
            var ex = await _db.SmsProviders.FindAsync(model.Id);
            if (ex == null) return NotFound();
            ex.Name = model.Name.Trim();
            ex.Kind = model.Kind;
            ex.Recipients = model.Recipients;
            ex.TemplateSid = model.TemplateSid;
            ex.Method = model.Method;
            ex.Url = model.Url;
            ex.ContentType = model.ContentType;
            ex.Body = model.Body;
            ex.Headers = model.Headers;
            ex.AuthType = model.AuthType;
            ex.Username = model.Username;
            ex.Sender = model.Sender;
            ex.SuccessContains = model.SuccessContains;
            ex.Enabled = model.Enabled;
            if (!string.IsNullOrEmpty(newPassword)) ex.PasswordEncrypted = CryptoHelper.Encrypt(newPassword);
            if (!string.IsNullOrEmpty(newApiKey)) ex.ApiKeyEncrypted = CryptoHelper.Encrypt(newApiKey);
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(isNew ? "integration.create" : "integration.update", model.Name, model.Kind);
        TempData["Message"] = "Entegrasyon kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.SmsProviders.FindAsync(id);
        if (p != null) { _db.SmsProviders.Remove(p); await _db.SaveChangesAsync(); await _audit.LogAsync("integration.delete", p.Name); TempData["Message"] = "Entegrasyon silindi."; }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Entegrasyonu aktif/pasif yapar (liste üzerindeki toggle).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, string? returnUrl)
    {
        var p = await _db.SmsProviders.FindAsync(id);
        if (p != null)
        {
            p.Enabled = !p.Enabled;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("integration.toggle", p.Name, p.Enabled ? "aktif" : "pasif");
            TempData["Message"] = $"'{p.Name}' {(p.Enabled ? "aktif" : "pasif")} yapıldı.";
        }
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Bu sağlayıcıyla bir test SMS gönderir (kayıtlı ayar üzerinden).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(int id, string to)
    {
        var p = await _db.SmsProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (string.IsNullOrWhiteSpace(to)) { TempData["Error"] = "Test için bir alıcı girin."; return RedirectToAction(nameof(Index)); }
        // Tür fark etmeksizin entegrasyonun şablonlu HTTP isteğiyle test gönder
        var (ok, msg) = await _sms.SendViaIntegrationAsync(p, new[] { to.Trim() }, "vMon test mesajı ✅");
        await _audit.LogAsync("integration.test", p.Name, msg, ok);
        TempData[ok ? "Message" : "Error"] = (ok ? "✅ " : "❌ ") + msg;
        return RedirectToAction(nameof(Index));
    }
}
