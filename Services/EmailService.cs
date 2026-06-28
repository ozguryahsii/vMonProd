using System.Net.Mail;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>SMTP Relay üzerinden bildirim — authentication yok, EnableSsl=false.</summary>
public class EmailService
{
    private readonly ILogger<EmailService> _logger;
    public EmailService(ILogger<EmailService> logger) => _logger = logger;

    public async Task SendAsync(MonitorSettings settings, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            throw new InvalidOperationException("SMTP sunucusu tanımlı değil (Ayarlar sayfasından girin).");

        var recipients = settings.MailRecipients
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (recipients.Length == 0)
            throw new InvalidOperationException("Email alıcısı tanımlı değil.");

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = false,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        using var msg = new MailMessage
        {
            From = new MailAddress(settings.MailFrom, "vMon"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        foreach (var r in recipients) msg.To.Add(r);

        await client.SendMailAsync(msg, ct);
        _logger.LogInformation("Email gönderildi: {Subject} -> {Recipients}", subject, settings.MailRecipients);
    }

    /// <summary>Belirli bir adrese e-posta gönderir (OTP gibi kullanıcıya özel mesajlar için).</summary>
    public async Task SendToAsync(MonitorSettings settings, string toAddress, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            throw new InvalidOperationException("SMTP sunucusu tanımlı değil (Ayarlar sayfasından girin).");
        if (string.IsNullOrWhiteSpace(toAddress))
            throw new InvalidOperationException("Alıcı e-posta adresi yok.");

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = false,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        using var msg = new MailMessage
        {
            From = new MailAddress(settings.MailFrom, "vMon"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        msg.To.Add(toAddress.Trim());
        await client.SendMailAsync(msg, ct);
    }

    public Task SendDownAlertAsync(MonitorSettings settings, MonitoredService svc, string? error, CancellationToken ct = default)
    {
        var body = $@"<h3 style='color:#dc3545'>⛔ Servis DOWN: {svc.Name}</h3>
<table cellpadding='6' style='border-collapse:collapse;font-family:sans-serif'>
<tr><td><b>Servis</b></td><td>{svc.Name}</td></tr>
<tr><td><b>Tip</b></td><td>{svc.Type}</td></tr>
<tr><td><b>Hedef</b></td><td>{svc.Target}{(svc.Port.HasValue ? ":" + svc.Port : "")}</td></tr>
<tr><td><b>Zaman</b></td><td>{DateTime.Now:dd.MM.yyyy HH:mm:ss}</td></tr>
<tr><td><b>Hata</b></td><td>{System.Net.WebUtility.HtmlEncode(error ?? "-")}</td></tr>
</table><p style='color:#888;font-size:12px'>vMon otomatik bildirimi</p>";
        return SendAsync(settings, $"[vMon] DOWN: {svc.Name}", body, ct);
    }

    public Task SendErrorAlertAsync(MonitorSettings settings, MonitoredService svc, string? error, CancellationToken ct = default)
    {
        var body = $@"<h3 style='color:#f97316'>⚠️ Servis UYARI (Eşik Aşıldı): {svc.Name}</h3>
<table cellpadding='6' style='border-collapse:collapse;font-family:sans-serif'>
<tr><td><b>Servis</b></td><td>{svc.Name}</td></tr>
<tr><td><b>Tip</b></td><td>{svc.Type}</td></tr>
<tr><td><b>Hedef</b></td><td>{svc.Target}{(svc.Port.HasValue ? ":" + svc.Port : "")}</td></tr>
<tr><td><b>Zaman</b></td><td>{DateTime.Now:dd.MM.yyyy HH:mm:ss}</td></tr>
<tr><td><b>Uyarı</b></td><td>{System.Net.WebUtility.HtmlEncode(error ?? "-")}</td></tr>
</table><p style='color:#888;font-size:12px'>vMon otomatik bildirimi — bu bir erişim kesintisi değil, eşik/uyarı durumudur.</p>";
        return SendAsync(settings, $"[vMon] ERROR: {svc.Name}", body, ct);
    }

    public Task SendRecoveredAlertAsync(MonitorSettings settings, MonitoredService svc, TimeSpan? outageDuration, CancellationToken ct = default)
    {
        var durationText = outageDuration.HasValue
            ? $"{(int)outageDuration.Value.TotalHours:D2}:{outageDuration.Value.Minutes:D2}:{outageDuration.Value.Seconds:D2}"
            : "-";
        var body = $@"<h3 style='color:#198754'>✅ Servis DÜZELDİ: {svc.Name}</h3>
<table cellpadding='6' style='border-collapse:collapse;font-family:sans-serif'>
<tr><td><b>Servis</b></td><td>{svc.Name}</td></tr>
<tr><td><b>Hedef</b></td><td>{svc.Target}{(svc.Port.HasValue ? ":" + svc.Port : "")}</td></tr>
<tr><td><b>Zaman</b></td><td>{DateTime.Now:dd.MM.yyyy HH:mm:ss}</td></tr>
<tr><td><b>Kesinti süresi</b></td><td>{durationText}</td></tr>
</table><p style='color:#888;font-size:12px'>vMon otomatik bildirimi</p>";
        return SendAsync(settings, $"[vMon] RECOVERED: {svc.Name}", body, ct);
    }
}
