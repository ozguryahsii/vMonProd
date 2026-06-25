using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

public class CredentialsController : MvcBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    public CredentialsController(AppDbContext db, AuditService audit) { _db = db; _audit = audit; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!Can(Perms.CredentialsManage)) context.Result = Denied();
        base.OnActionExecuting(context);
    }

    public async Task<IActionResult> Index()
    {
        var creds = await _db.Credentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        var usage = await _db.Services.AsNoTracking()
            .Where(s => s.CredentialId != null)
            .GroupBy(s => s.CredentialId!.Value)
            .Select(g => new { CredId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CredId, x => x.Count);
        ViewBag.Usage = usage;
        return View(creds);
    }

    public IActionResult Create() => View("Form", new Credential());

    public async Task<IActionResult> Edit(int id)
    {
        var cred = await _db.Credentials.FindAsync(id);
        if (cred == null) return NotFound();
        return View("Form", cred);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Credential model, string? newPassword, string? newVaultToken)
    {
        ModelState.Remove(nameof(Credential.PasswordEncrypted));
        ModelState.Remove(nameof(Credential.VaultTokenEncrypted));
        // Username non-nullable string olduğu için framework otomatik zorunlu sayıyor;
        // Vault kimliğinde kullanıcı adı secret'tan gelir — doğrulamayı kendimiz yapıyoruz.
        ModelState.Remove(nameof(Credential.Username));
        model.Username ??= "";

        if (model.SourceType == CredentialSource.Manual)
        {
            if (string.IsNullOrWhiteSpace(model.Username))
                ModelState.AddModelError("", "Kullanıcı adı zorunlu.");
            if (model.Id == 0 && string.IsNullOrEmpty(newPassword))
                ModelState.AddModelError("", "Yeni kayıt için şifre zorunlu.");
        }
        else // Vault — kullanıcı adı ve şifre secret'tan çekilir
        {
            if (string.IsNullOrWhiteSpace(model.VaultUrl))
                ModelState.AddModelError("", "Vault secret URL'i zorunlu.");
            if (string.IsNullOrWhiteSpace(model.VaultKey))
                ModelState.AddModelError("", "Secret içindeki şifre anahtarının adı zorunlu.");
            if (string.IsNullOrWhiteSpace(model.VaultUserKey))
                ModelState.AddModelError("", "Secret içindeki kullanıcı adı anahtarının adı zorunlu.");
            if (model.Id == 0 && string.IsNullOrEmpty(newVaultToken))
                ModelState.AddModelError("", "Yeni Vault kaydı için token zorunlu.");
            model.Username = ""; // kullanıcı adı Vault'tan gelir, kartta tutulmaz
        }
        if (!ModelState.IsValid) return View("Form", model);

        var isNew = model.Id == 0;
        if (model.Id == 0)
        {
            model.PasswordEncrypted = model.SourceType == CredentialSource.Manual
                ? CryptoHelper.Encrypt(newPassword!) : "";
            model.VaultTokenEncrypted = model.SourceType == CredentialSource.Vault
                ? CryptoHelper.Encrypt(newVaultToken!) : null;
            _db.Credentials.Add(model);
        }
        else
        {
            var existing = await _db.Credentials.FindAsync(model.Id);
            if (existing == null) return NotFound();
            existing.Name = model.Name;
            existing.Username = model.Username;
            existing.Domain = model.Domain;
            existing.Description = model.Description;
            existing.SourceType = model.SourceType;
            existing.VaultUrl = model.SourceType == CredentialSource.Vault ? model.VaultUrl?.Trim() : null;
            existing.VaultKey = model.SourceType == CredentialSource.Vault ? model.VaultKey?.Trim() : null;
            existing.VaultUserKey = model.SourceType == CredentialSource.Vault ? model.VaultUserKey?.Trim() : null;
            if (model.SourceType == CredentialSource.Manual && !string.IsNullOrEmpty(newPassword))
                existing.PasswordEncrypted = CryptoHelper.Encrypt(newPassword);
            if (model.SourceType == CredentialSource.Vault && !string.IsNullOrEmpty(newVaultToken))
                existing.VaultTokenEncrypted = CryptoHelper.Encrypt(newVaultToken);
            if (model.SourceType == CredentialSource.Vault && string.IsNullOrWhiteSpace(existing.VaultTokenEncrypted))
            {
                ModelState.AddModelError("", "Bu kayıt için saklı Vault token yok — token girin.");
                return View("Form", model);
            }
            VaultClient.Invalidate(existing.Id); // önbellekteki eski şifreyi düşür
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(isNew ? "credential.create" : "credential.update", model.Name,
            $"Kaynak: {model.SourceType}");
        TempData["Message"] = "Kimlik bilgisi kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var inUse = await _db.Services.AnyAsync(s => s.CredentialId == id);
        if (inUse)
        {
            TempData["Error"] = "Bu kimlik bilgisi bir veya daha fazla serviste kullanılıyor, önce o servislerden kaldırın.";
            return RedirectToAction(nameof(Index));
        }
        var cred = await _db.Credentials.FindAsync(id);
        if (cred != null)
        {
            _db.Credentials.Remove(cred);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("credential.delete", cred.Name);
            TempData["Message"] = "Kimlik bilgisi silindi.";
        }
        return RedirectToAction(nameof(Index));
    }
}
