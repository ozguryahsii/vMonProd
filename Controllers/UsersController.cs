using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>Kullanıcı ve granüler yetki yönetimi — yalnızca uygulama adminleri.</summary>
public class UsersController : MvcBase
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly LdapAuthService _ldap;
    private readonly AuditService _audit;
    public UsersController(AppDbContext db, SettingsService settings, LdapAuthService ldap, AuditService audit)
    { _db = db; _settings = settings; _ldap = ldap; _audit = audit; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Açık modda (oturum kapalı) veya admin ise izin ver; değilse reddet
        if (User?.Identity?.IsAuthenticated == true && !User.IsAppAdmin())
            context.Result = Denied();
        base.OnActionExecuting(context);
    }

    public async Task<IActionResult> Index()
    {
        var users = await _db.AppUsers.AsNoTracking().OrderBy(u => u.Sam).ToListAsync();
        ViewBag.AdminUsers = (await _settings.GetAsync()).AdminUsers;
        return View(users);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var u = await _db.AppUsers.FindAsync(id);
        if (u == null) return NotFound();
        var settings = await _settings.GetAsync();
        ViewBag.IsAdmin = settings.IsAdmin(u.Sam);
        return View(u);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string[]? perms, string? phone)
    {
        var u = await _db.AppUsers.FindAsync(id);
        if (u == null) return NotFound();
        var valid = Perms.All.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        u.PermissionsCsv = string.Join(",", (perms ?? Array.Empty<string>()).Where(p => valid.Contains(p)).Distinct());
        u.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        await _db.SaveChangesAsync();
        await _audit.LogAsync("user.permissions", u.Sam, "Yetkiler: " + (string.IsNullOrEmpty(u.PermissionsCsv) ? "(yok)" : u.PermissionsCsv));
        TempData["Message"] = $"{u.DisplayName ?? u.Sam} yetkileri güncellendi. (Kullanıcı bir sonraki girişinde geçerli olur.)";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync()
    {
        var settings = await _settings.GetAsync();
        Credential? syncCred = settings.LdapSyncCredentialId.HasValue
            ? await _db.Credentials.AsNoTracking().FirstOrDefaultAsync(c => c.Id == settings.LdapSyncCredentialId.Value)
            : null;
        var (error, members) = await Task.Run(() => _ldap.ListGroupMembers(settings, syncCred));
        if (error != null)
        {
            TempData["Error"] = "Senkronizasyon başarısız: " + error;
            return RedirectToAction(nameof(Index));
        }

        var existing = await _db.AppUsers.ToDictionaryAsync(u => u.Sam, StringComparer.OrdinalIgnoreCase);
        var inGroup = members.Select(m => m.Sam).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0, reactivated = 0;
        foreach (var m in members)
        {
            if (existing.TryGetValue(m.Sam, out var u))
            {
                if (!string.IsNullOrWhiteSpace(m.DisplayName)) u.DisplayName = m.DisplayName;
                if (!u.IsActive) { u.IsActive = true; reactivated++; }
            }
            else
            {
                _db.AppUsers.Add(new AppUser { Sam = m.Sam, DisplayName = m.DisplayName, PermissionsCsv = Perms.DashboardsView, IsActive = true });
                added++;
            }
        }

        // Gruptan düşenleri pasifleştir (silmeyiz — denetim izi ve geçmiş için saklanır)
        int deactivated = 0;
        foreach (var u in existing.Values)
            if (u.IsActive && !inGroup.Contains(u.Sam)) { u.IsActive = false; deactivated++; }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("user.sync", null,
            $"{members.Count} grup üyesi; {added} yeni, {reactivated} yeniden etkin, {deactivated} pasifleştirildi.");
        TempData["Message"] = $"LDAP senkronizasyonu tamam: {members.Count} grup üyesi ({added} yeni eklendi, {deactivated} pasifleştirildi). Yeni kullanıcılara varsayılan olarak yalnızca görüntüleme yetkisi verildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var u = await _db.AppUsers.FindAsync(id);
        if (u != null)
        {
            _db.AppUsers.Remove(u);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("user.delete", u.Sam);
            TempData["Message"] = "Kullanıcı kaydı silindi.";
        }
        return RedirectToAction(nameof(Index));
    }
}
