using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>SMS bildirimi. İki sağlayıcı türü: yerleşik "Twilio" (Ayarlar'daki SID/Token/From) ve
/// UI'dan tanımlanan genel HTTP sağlayıcıları (SmsProviders tablosu — şablonlu URL/gövde).
/// Token'lar DPAPI ile çözülür, asla düz saklanmaz.</summary>
public class SmsService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ILogger<SmsService> _logger;
    private readonly AppDbContext _db;
    public SmsService(ILogger<SmsService> logger, AppDbContext db) { _logger = logger; _db = db; }

    public static string[] ParseRecipients(string? csv) =>
        (csv ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Aktif sağlayıcıya göre SMS gönderir. (ok, mesaj) döner; uygulamayı bozmaz.</summary>
    public async Task<(bool ok, string message)> SendAsync(MonitorSettings s, IEnumerable<string> recipients, string body, CancellationToken ct = default)
    {
        if (!s.SmsEnabled) return (false, "SMS bildirimi kapalı.");
        var toList = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
        if (toList.Count == 0) return (false, "SMS alıcısı tanımlı değil.");

        var provider = string.IsNullOrWhiteSpace(s.SmsProvider) ? "Twilio" : s.SmsProvider.Trim();
        if (provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
            return await SendViaTwilioAsync(s, toList, body, ct);

        var custom = await _db.SmsProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Name == provider, ct);
        if (custom == null) return (false, $"SMS sağlayıcısı '{provider}' bulunamadı.");
        if (!custom.Enabled) return (false, $"SMS sağlayıcısı '{provider}' pasif.");
        return await SendViaCustomAsync(custom, toList, body, ct);
    }

    // ---- Yerleşik: Twilio ----
    private async Task<(bool ok, string message)> SendViaTwilioAsync(MonitorSettings s, List<string> toList, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.SmsAccountSid) || string.IsNullOrWhiteSpace(s.SmsAuthTokenEncrypted) || string.IsNullOrWhiteSpace(s.SmsFrom))
            return (false, "Twilio ayarları eksik (SID / Token / Gönderen).");
        string token;
        try { token = CryptoHelper.Decrypt(s.SmsAuthTokenEncrypted); }
        catch { return (false, "Twilio token çözülemedi (sunucu değişmiş olabilir; tekrar girin)."); }

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{s.SmsAccountSid}/Messages.json";
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.SmsAccountSid}:{token}"));
        int sent = 0; string? lastErr = null;
        foreach (var to in toList)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["To"] = to, ["From"] = s.SmsFrom, ["Body"] = body });
                using var resp = await Http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) sent++;
                else lastErr = await Snippet(resp);
            }
            catch (Exception ex) { lastErr = ex.Message; }
        }
        return Result(sent, toList.Count, lastErr);
    }

    /// <summary>Herhangi bir kanal türü (SMS/WhatsApp/Voice/IVR) için tanımlı bir entegrasyonla
    /// şablonlu HTTP isteği gönderir. CheckRunner özel entegrasyonları bununla tetikler.</summary>
    public async Task<(bool ok, string message)> SendViaIntegrationAsync(SmsProvider p, IEnumerable<string> recipients, string body, CancellationToken ct = default)
    {
        var toList = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
        if (toList.Count == 0) return (false, "Alıcı tanımlı değil.");
        return await SendViaCustomAsync(p, toList, body, ct);
    }

    // ---- Genel: UI'dan tanımlı HTTP sağlayıcı ----
    private async Task<(bool ok, string message)> SendViaCustomAsync(SmsProvider p, List<string> toList, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Url)) return (false, "Sağlayıcı URL'i tanımlı değil.");
        string pass = SafeDecrypt(p.PasswordEncrypted), apikey = SafeDecrypt(p.ApiKeyEncrypted);
        int sent = 0; string? lastErr = null;

        foreach (var to in toList)
        {
            try
            {
                // URL'de değerler URL-encode edilir
                var url = Subst(p.Url, to, p.Sender, body, p.Username, pass, apikey, Uri.EscapeDataString);
                var method = p.Method?.ToUpperInvariant() == "POST" ? HttpMethod.Post : HttpMethod.Get;
                using var req = new HttpRequestMessage(method, url);

                if (method == HttpMethod.Post && !string.IsNullOrWhiteSpace(p.Body))
                {
                    if (string.Equals(p.ContentType, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        var json = Subst(p.Body, to, p.Sender, body, p.Username, pass, apikey, JsonEsc);
                        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    }
                    else
                    {
                        var form = Subst(p.Body, to, p.Sender, body, p.Username, pass, apikey, Uri.EscapeDataString);
                        req.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");
                    }
                }

                // Kimlik doğrulama kısayolu
                if (string.Equals(p.AuthType, "basic", StringComparison.OrdinalIgnoreCase))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{p.Username}:{pass}")));
                else if (string.Equals(p.AuthType, "bearer", StringComparison.OrdinalIgnoreCase))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                        string.IsNullOrEmpty(apikey) ? pass : apikey);

                // Ek başlıklar
                if (!string.IsNullOrWhiteSpace(p.Headers))
                    foreach (var line in p.Headers.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var i = line.IndexOf(':');
                        if (i <= 0) continue;
                        var key = line[..i].Trim();
                        var val = Subst(line[(i + 1)..].Trim(), to, p.Sender, body, p.Username, pass, apikey, x => x);
                        if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                        { try { req.Headers.TryAddWithoutValidation("Authorization", val); } catch { } }
                        else req.Headers.TryAddWithoutValidation(key, val);
                    }

                using var resp = await Http.SendAsync(req, ct);
                var respBody = await resp.Content.ReadAsStringAsync(ct);
                bool ok = resp.IsSuccessStatusCode &&
                          (string.IsNullOrWhiteSpace(p.SuccessContains) || respBody.Contains(p.SuccessContains, StringComparison.OrdinalIgnoreCase));
                if (ok) sent++;
                else lastErr = $"HTTP {(int)resp.StatusCode}: {(respBody.Length > 200 ? respBody[..200] : respBody)}";
            }
            catch (Exception ex) { lastErr = ex.Message; }
        }
        return Result(sent, toList.Count, lastErr);
    }

    private (bool ok, string message) Result(int sent, int total, string? lastErr)
    {
        if (sent == 0) { _logger.LogWarning("SMS gönderilemedi: {Err}", lastErr); return (false, lastErr ?? "Gönderilemedi."); }
        return (true, $"{sent}/{total} alıcıya gönderildi" + (sent < total && lastErr != null ? $" (bazıları başarısız: {lastErr})" : "."));
    }

    private static string Subst(string tpl, string to, string from, string msg, string user, string pass, string apikey, Func<string, string> enc) =>
        tpl.Replace("{to}", enc(to)).Replace("{from}", enc(from)).Replace("{message}", enc(msg))
           .Replace("{user}", enc(user)).Replace("{password}", enc(pass)).Replace("{apikey}", enc(apikey));

    private static string JsonEsc(string s) => System.Text.Json.JsonEncodedText.Encode(s).ToString();
    private static string SafeDecrypt(string enc) { try { return CryptoHelper.Decrypt(enc); } catch { return ""; } }
    private static async Task<string> Snippet(HttpResponseMessage resp)
    {
        var b = await resp.Content.ReadAsStringAsync();
        return $"HTTP {(int)resp.StatusCode}: {(b.Length > 200 ? b[..200] : b)}";
    }

    // --- Alarm metinleri ---
    public static string DownText(MonitoredService svc, string? error) =>
        $"vMon ⛔ DOWN: {svc.Name} ({svc.Target}{(svc.Port.HasValue ? ":" + svc.Port : "")}) - {Trim(error)}";
    public static string ErrorText(MonitoredService svc, string? error) =>
        $"vMon ⚠️ UYARI: {svc.Name} - {Trim(error)}";
    public static string RecoveredText(MonitoredService svc) =>
        $"vMon ✅ DÜZELDİ: {svc.Name} ({svc.Target}{(svc.Port.HasValue ? ":" + svc.Port : "")})";
    private static string Trim(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : (s.Length > 120 ? s[..120] : s);
}
