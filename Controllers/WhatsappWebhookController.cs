using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Controllers;

/// <summary>WhatsApp gelen mesaj/buton webhook'u (Twilio çağırır). Kimlik doğrulama: URL'deki ?key=
/// gizli anahtarı. Kullanıcı butona basınca: numara→kullanıcı eşlenir, alarm.manage yetkisi denetlenir,
/// en son açık alarm oturumundaki servise start/stop/restart uygulanır, sonuç WhatsApp'a (TwiML) döner.
/// Bu uç internetten erişilebilir olmalıdır (DMZ/reverse-proxy); Program.cs erişim kapısında muaftır.</summary>
[Route("api/whatsapp")]
public class WhatsappWebhookController : Controller
{
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    public WhatsappWebhookController(SettingsService settings, AppDbContext db, AuditService audit)
    { _settings = settings; _db = db; _audit = audit; }

    [HttpPost("inbound")]
    [Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryToken]
    public async Task<IActionResult> Inbound([FromQuery] string? key, [FromForm] string? From,
        [FromForm] string? Body, [FromForm] string? ButtonText, [FromForm] string? ButtonPayload)
    {
        var s = await _settings.GetAsync();
        if (string.IsNullOrEmpty(s.WhatsappWebhookSecret) || !string.Equals(key, s.WhatsappWebhookSecret, StringComparison.Ordinal))
            return StatusCode(403, "forbidden");

        var phone = WhatsappService.NormalizePhone(From);
        var raw = !string.IsNullOrWhiteSpace(ButtonPayload) ? ButtonPayload
                : !string.IsNullOrWhiteSpace(ButtonText) ? ButtonText : Body;
        var action = MapAction(raw);

        if (action == null)
            return Twiml("Komutu anlayamadım. Lütfen alarm mesajındaki Başlat / Yeniden Başlat / Durdur butonlarını kullanın.");

        // Numarayı kullanıcıya eşle
        var users = await _db.AppUsers.AsNoTracking().Where(u => u.Phone != null && u.Phone != "").ToListAsync();
        var user = users.FirstOrDefault(u => WhatsappService.NormalizePhone(u.Phone) == phone);
        if (user == null)
        {
            await _audit.LogAsync("whatsapp.denied", phone, "Tanımsız numaradan komut", false, user: "anonim", ip: phone);
            return Twiml("Bu numara sistemde tanımlı değil. Lütfen yöneticinizle iletişime geçin.");
        }
        if (!user.IsActive) return Twiml("Hesabınız pasif durumda.");

        bool can = s.IsAdmin(user.Sam) || user.Permissions().Contains(Perms.AlarmManage);
        if (!can)
        {
            await _audit.LogAsync("whatsapp.denied", user.Sam, $"Yetkisiz komut: {action}", false, user: user.Sam, ip: phone);
            return Twiml("Bu işlem için yetkiniz yok (yalnızca bilgilendirme alıyorsunuz).");
        }

        // En son açık alarm oturumu (son 2 saat)
        var since = DateTime.UtcNow.AddMinutes(-120);
        var session = await _db.AlarmSessions
            .Where(a => a.Phone == phone && a.HandledAt == null && a.CreatedAt >= since)
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        if (session == null)
            return Twiml("İşlem yapılacak aktif bir alarm bulunamadı (zaman aşmış olabilir).");

        var svc = await _db.Services.Include(x => x.Credential).FirstOrDefaultAsync(x => x.Id == session.ServiceId);
        if (svc == null) return Twiml("İlgili servis bulunamadı.");

        if (svc.Type != ServiceType.WindowsServiceControl && svc.Type != ServiceType.LinuxServiceControl)
        {
            session.HandledAt = DateTime.UtcNow; session.Action = action; session.Result = "kontrol edilemez tip";
            await _db.SaveChangesAsync();
            return Twiml($"'{svc.Name}' servisi uzaktan kontrol edilemiyor (servis tipi uygun değil).");
        }

        ServiceControl.ActionResult res;
        if (svc.Type == ServiceType.WindowsServiceControl)
            res = await Task.Run(() => ServiceControl.WindowsAction(svc, svc.Credential, action));
        else
        {
            if (svc.Credential == null) return Twiml($"'{svc.Name}' için kimlik bilgisi tanımlı değil.");
            res = await Task.Run(() => ServiceControl.LinuxAction(svc, svc.Credential, action));
        }

        session.HandledAt = DateTime.UtcNow; session.Action = action; session.Result = res.Message;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("whatsapp.action", svc.Name, $"{user.Sam} (WhatsApp): {action} → {res.Message}", res.Ok, user: user.Sam, ip: phone);

        var actName = action == "start" ? "başlatma" : action == "stop" ? "durdurma" : "yeniden başlatma";
        return Twiml((res.Ok ? "✅ " : "❌ ") + $"{svc.Name} — {actName}: {res.Message}");
    }

    private static string? MapAction(string? raw)
    {
        var t = (raw ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return null;
        if (t.Contains("restart") || t.Contains("yeniden") || t.Contains("3")) return "restart";
        if (t.Contains("stop") || t.Contains("durdur") || t.Contains("2")) return "stop";
        if (t.Contains("start") || t.Contains("başlat") || t.Contains("baslat") || t.Contains("1")) return "start";
        return null;
    }

    private ContentResult Twiml(string message)
    {
        var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response><Message>{System.Security.SecurityElement.Escape(message)}</Message></Response>";
        return Content(xml, "text/xml");
    }
}
