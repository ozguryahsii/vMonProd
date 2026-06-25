using Microsoft.EntityFrameworkCore;
using vMonitor.Models;
using vMonitor.Services;

namespace vMonitor.Data;

/// <summary>Yerleşik Twilio SMS/WhatsApp ayarlarını, "Bildirim Kanalları" entegrasyonlarına bir kez taşır.
/// Twilio aslında basit bir HTTP POST'tur (Basic auth + To/From/Body) — generic HTTP entegrasyon modeline
/// birebir uyar. Şifreli token blob'u (DPAPI) doğrudan kopyalanır (aynı CryptoHelper ile çözülür).
/// Taşıma sonrası yerleşik genel anahtarlar (SmsEnabled/WhatsappEnabled) kapatılır → çift gönderim olmaz.</summary>
public static class TwilioChannelMigration
{
    public static async Task RunAsync(AppDbContext db, SettingsService settingsSvc, ILogger logger)
    {
        try
        {
            var s = await settingsSvc.GetAsync();

            // Backfill (her açılışta idempotent): eski WhatsApp şablonunu (Content SID) "Twilio WhatsApp"
            // entegrasyonuna taşı. Faz 2'de şablon alanı taşınmadığı için butonlu bildirim düz metne dönmüştü.
            if (!string.IsNullOrWhiteSpace(s.WhatsappAlarmTemplateSid))
            {
                var wa = await db.SmsProviders.FirstOrDefaultAsync(p => p.Name == "Twilio WhatsApp");
                if (wa != null && string.IsNullOrWhiteSpace(wa.TemplateSid))
                {
                    wa.TemplateSid = s.WhatsappAlarmTemplateSid;
                    await db.SaveChangesAsync();
                    logger.LogInformation("WhatsApp şablonu (Content SID) Twilio WhatsApp entegrasyonuna geri yüklendi.");
                }
            }

            if (s.TwilioChannelsMigrated) return;

            bool createdAny = false;

            // --- SMS (Twilio yerleşik) ---
            if (!string.IsNullOrWhiteSpace(s.SmsAccountSid) &&
                !await db.SmsProviders.AnyAsync(p => p.Name == "Twilio SMS"))
            {
                db.SmsProviders.Add(new SmsProvider
                {
                    Kind = "Sms",
                    Name = "Twilio SMS",
                    Method = "POST",
                    Url = $"https://api.twilio.com/2010-04-01/Accounts/{s.SmsAccountSid}/Messages.json",
                    ContentType = "form",
                    Body = "To={to}&From={from}&Body={message}",
                    AuthType = "basic",
                    Username = s.SmsAccountSid,
                    PasswordEncrypted = s.SmsAuthTokenEncrypted ?? "",
                    Sender = s.SmsFrom ?? "",
                    Recipients = s.SmsRecipients,
                    Enabled = s.SmsEnabled
                });
                createdAny = true;
            }

            // --- WhatsApp (Twilio yerleşik, tek yön serbest metin) ---
            if (!string.IsNullOrWhiteSpace(s.WhatsappAccountSid) &&
                !await db.SmsProviders.AnyAsync(p => p.Name == "Twilio WhatsApp"))
            {
                db.SmsProviders.Add(new SmsProvider
                {
                    Kind = "Whatsapp",
                    Name = "Twilio WhatsApp",
                    Method = "POST",
                    Url = $"https://api.twilio.com/2010-04-01/Accounts/{s.WhatsappAccountSid}/Messages.json",
                    ContentType = "form",
                    Body = "To=whatsapp:{to}&From=whatsapp:{from}&Body={message}",
                    AuthType = "basic",
                    Username = s.WhatsappAccountSid,
                    PasswordEncrypted = s.WhatsappAuthTokenEncrypted ?? "",
                    Sender = s.WhatsappFrom ?? "",
                    Recipients = s.WhatsappRecipients,
                    TemplateSid = s.WhatsappAlarmTemplateSid,
                    Enabled = s.WhatsappEnabled
                });
                createdAny = true;
            }

            if (createdAny) await db.SaveChangesAsync();

            // Yerleşik genel anahtarları kapat (entegrasyonlar artık gönderiyor) + bayrağı işaretle
            s.SmsEnabled = false;
            s.WhatsappEnabled = false;
            s.TwilioChannelsMigrated = true;
            await settingsSvc.SaveAsync(s);

            if (createdAny) logger.LogInformation("Twilio SMS/WhatsApp ayarları Bildirim Kanalları'na taşındı.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Twilio kanal taşıması başarısız (atlandı).");
        }
    }
}
