using System.Net.Http.Headers;
using System.Text;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>WhatsApp bildirimi (Faz 3: tek yön) — Twilio üzerinden. Twilio'da WhatsApp, SMS ile aynı
/// Messages API'dir; numaraların başına "whatsapp:" gelir. Token DPAPI ile çözülür.
/// NOT: İş-başlatımlı mesajlar 24 saat penceresi dışında ONAYLI ŞABLON ister (üretim). Sandbox'ta
/// kullanıcı katıldıktan sonra 24 saat serbest metin gönderilebilir.</summary>
public class WhatsappService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ILogger<WhatsappService> _logger;
    public WhatsappService(ILogger<WhatsappService> logger) => _logger = logger;

    public static string[] ParseRecipients(string? csv) =>
        (csv ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Eşleştirme için telefonu sadeleştirir: "whatsapp:", boşluk, +, tire vb. atılır → yalnız rakamlar.</summary>
    public static string NormalizePhone(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)) s = s["whatsapp:".Length..];
        return new string(s.Where(char.IsDigit).ToArray());
    }

    private static string Wa(string n)
    {
        n = n.Trim();
        return n.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? n : "whatsapp:" + n;
    }

    public async Task<(bool ok, string message)> SendAsync(MonitorSettings s, IEnumerable<string> recipients, string body, CancellationToken ct = default)
    {
        if (!s.WhatsappEnabled) return (false, "WhatsApp bildirimi kapalı.");
        if (string.IsNullOrWhiteSpace(s.WhatsappAccountSid) || string.IsNullOrWhiteSpace(s.WhatsappAuthTokenEncrypted) || string.IsNullOrWhiteSpace(s.WhatsappFrom))
            return (false, "WhatsApp ayarları eksik (SID / Token / Gönderen).");

        var toList = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
        if (toList.Count == 0) return (false, "WhatsApp alıcısı tanımlı değil.");

        string token;
        try { token = CryptoHelper.Decrypt(s.WhatsappAuthTokenEncrypted); }
        catch { return (false, "WhatsApp token çözülemedi (sunucu değişmiş olabilir; tekrar girin)."); }

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{s.WhatsappAccountSid}/Messages.json";
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.WhatsappAccountSid}:{token}"));
        var from = Wa(s.WhatsappFrom);
        int sent = 0; string? lastErr = null;

        foreach (var to in toList)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["To"] = Wa(to),
                    ["From"] = from,
                    ["Body"] = body
                });
                using var resp = await Http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) sent++;
                else
                {
                    var b = await resp.Content.ReadAsStringAsync(ct);
                    lastErr = $"HTTP {(int)resp.StatusCode}: {(b.Length > 200 ? b[..200] : b)}";
                }
            }
            catch (Exception ex) { lastErr = ex.Message; }
        }

        if (sent == 0) { _logger.LogWarning("WhatsApp gönderilemedi: {Err}", lastErr); return (false, lastErr ?? "Gönderilemedi."); }
        return (true, $"{sent}/{toList.Count} alıcıya gönderildi" + (sent < toList.Count && lastErr != null ? $" (bazıları başarısız: {lastErr})" : "."));
    }

    /// <summary>Onaylı şablon (Content SID) ile gönderim — butonlar şablondan gelir.
    /// vars: {"1":servisAdı,"2":hata,"3":zaman,"4":kesintiSüresi}</summary>
    public async Task<(bool ok, string message)> SendTemplateAsync(MonitorSettings s, IEnumerable<string> recipients,
        string contentSid, IReadOnlyDictionary<string, string> vars, CancellationToken ct = default)
    {
        if (!s.WhatsappEnabled) return (false, "WhatsApp bildirimi kapalı.");
        if (string.IsNullOrWhiteSpace(s.WhatsappAccountSid) || string.IsNullOrWhiteSpace(s.WhatsappAuthTokenEncrypted) || string.IsNullOrWhiteSpace(s.WhatsappFrom))
            return (false, "WhatsApp ayarları eksik (SID / Token / Gönderen).");
        if (string.IsNullOrWhiteSpace(contentSid)) return (false, "Şablon (Content SID) tanımlı değil.");

        var toList = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
        if (toList.Count == 0) return (false, "WhatsApp alıcısı tanımlı değil.");

        string token;
        try { token = CryptoHelper.Decrypt(s.WhatsappAuthTokenEncrypted); }
        catch { return (false, "WhatsApp token çözülemedi."); }

        var contentVars = System.Text.Json.JsonSerializer.Serialize(vars);
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{s.WhatsappAccountSid}/Messages.json";
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s.WhatsappAccountSid}:{token}"));
        var from = Wa(s.WhatsappFrom);
        int sent = 0; string? lastErr = null;

        foreach (var to in toList)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["To"] = Wa(to),
                    ["From"] = from,
                    ["ContentSid"] = contentSid,
                    ["ContentVariables"] = contentVars
                });
                using var resp = await Http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) sent++;
                else { var b = await resp.Content.ReadAsStringAsync(ct); lastErr = $"HTTP {(int)resp.StatusCode}: {(b.Length > 200 ? b[..200] : b)}"; }
            }
            catch (Exception ex) { lastErr = ex.Message; }
        }
        if (sent == 0) { _logger.LogWarning("WhatsApp şablon gönderilemedi: {Err}", lastErr); return (false, lastErr ?? "Gönderilemedi."); }
        return (true, $"{sent}/{toList.Count} alıcıya gönderildi" + (sent < toList.Count && lastErr != null ? $" (bazıları başarısız: {lastErr})" : "."));
    }

    /// <summary>Entegrasyonun kendi Twilio kimlik bilgileriyle (SID/şifreli token/gönderen) onaylı şablon gönderir.
    /// Bildirim Kanalları'ndaki "Twilio WhatsApp" entegrasyonu butonlu interaktif alarm için bunu kullanır.</summary>
    public async Task<(bool ok, string message)> SendTemplateRawAsync(string accountSid, string tokenEncrypted, string from,
        IEnumerable<string> recipients, string contentSid, IReadOnlyDictionary<string, string> vars, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(tokenEncrypted) || string.IsNullOrWhiteSpace(from))
            return (false, "WhatsApp entegrasyonu eksik (SID / Token / Gönderen).");
        if (string.IsNullOrWhiteSpace(contentSid)) return (false, "Şablon (Content SID) tanımlı değil.");

        var toList = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
        if (toList.Count == 0) return (false, "WhatsApp alıcısı tanımlı değil.");

        string token;
        try { token = CryptoHelper.Decrypt(tokenEncrypted); }
        catch { return (false, "WhatsApp token çözülemedi."); }

        var contentVars = System.Text.Json.JsonSerializer.Serialize(vars);
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountSid}:{token}"));
        var fromWa = Wa(from);
        int sent = 0; string? lastErr = null;

        foreach (var to in toList)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["To"] = Wa(to),
                    ["From"] = fromWa,
                    ["ContentSid"] = contentSid,
                    ["ContentVariables"] = contentVars
                });
                using var resp = await Http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) sent++;
                else { var b = await resp.Content.ReadAsStringAsync(ct); lastErr = $"HTTP {(int)resp.StatusCode}: {(b.Length > 200 ? b[..200] : b)}"; }
            }
            catch (Exception ex) { lastErr = ex.Message; }
        }
        if (sent == 0) { _logger.LogWarning("WhatsApp şablon (entegrasyon) gönderilemedi: {Err}", lastErr); return (false, lastErr ?? "Gönderilemedi."); }
        return (true, $"{sent}/{toList.Count} alıcıya gönderildi.");
    }

    public static string DownText(MonitoredService svc, string? error) =>
        $"⛔ *DOWN*: {svc.Name}\n{svc.Target}{(svc.Port.HasValue ? ":" + svc.Port : "")}\n{Trim(error)}";
    public static string ErrorText(MonitoredService svc, string? error) =>
        $"⚠️ *UYARI*: {svc.Name}\n{Trim(error)}";
    public static string RecoveredText(MonitoredService svc) =>
        $"✅ *DÜZELDİ*: {svc.Name}\n{svc.Target}{(svc.Port.HasValue ? ":" + svc.Port : "")}";
    private static string Trim(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : (s.Length > 200 ? s[..200] : s);
}
