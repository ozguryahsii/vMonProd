using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>SMTP bildirimi (MailKit): bağlantı güvenliği tek yerden seçilir — Yok / STARTTLS / SSL(465).
/// Kimlik doğrulama açıksa kullanıcı/şifre uygulanır. Port kullanıcı tarafından girilir (güvenlik seçimine
/// göre önerilir). System.Net.Mail 465'i desteklemediği için MailKit kullanılır.</summary>
public class EmailService
{
    private readonly ILogger<EmailService> _logger;
    public EmailService(ILogger<EmailService> logger) => _logger = logger;

    private static SecureSocketOptions Security(MonitorSettings s) => (s.SmtpSecurity ?? "none").ToLowerInvariant() switch
    {
        "starttls" => SecureSocketOptions.StartTls,
        "ssl" => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.None
    };

    private async Task SendCoreAsync(MonitorSettings s, IEnumerable<string> to, string subject, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.SmtpHost))
            throw new InvalidOperationException("SMTP sunucusu tanımlı değil (Ayarlar sayfasından girin).");
        var toList = to.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        if (toList.Count == 0) throw new InvalidOperationException("Email alıcısı tanımlı değil.");

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("vMon", s.MailFrom));
        foreach (var r in toList) msg.To.Add(MailboxAddress.Parse(r));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = body }.ToMessageBody();

        using var client = new SmtpClient();
        var port = s.SmtpPort > 0 ? s.SmtpPort : 25;
        await client.ConnectAsync(s.SmtpHost, port, Security(s), ct);
        if (s.SmtpUseAuth && !string.IsNullOrWhiteSpace(s.SmtpUsername))
        {
            var pwd = string.IsNullOrWhiteSpace(s.SmtpPasswordEncrypted) ? "" : CryptoHelper.Decrypt(s.SmtpPasswordEncrypted);
            await client.AuthenticateAsync(s.SmtpUsername, pwd, ct);
        }
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    public Task SendAsync(MonitorSettings settings, string subject, string body, CancellationToken ct = default)
    {
        var recipients = settings.MailRecipients
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _logger.LogInformation("Email gönderiliyor: {Subject} -> {Recipients}", subject, settings.MailRecipients);
        return SendCoreAsync(settings, recipients, subject, body, ct);
    }

    /// <summary>Belirli bir adrese e-posta gönderir (OTP gibi kullanıcıya özel mesajlar için).</summary>
    public Task SendToAsync(MonitorSettings settings, string toAddress, string subject, string body, CancellationToken ct = default)
        => SendCoreAsync(settings, new[] { toAddress }, subject, body, ct);

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
