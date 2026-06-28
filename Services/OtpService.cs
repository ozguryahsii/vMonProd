using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using vMonitor.Data;
using vMonitor.Models;

namespace vMonitor.Services;

/// <summary>İki faktörlü giriş (OTP): şifre doğrulandıktan sonra tek kullanımlık kod üretir, seçili kanaldan
/// (Email/SMS/WhatsApp) kullanıcıya gönderir ve doğrular. Bekleyen kodlar bellekte (token ile), 5 dk geçerli.</summary>
public class OtpService
{
    public record Pending(string Sam, bool IsLocal, bool IsAdmin, string DisplayName, byte[] CodeHash, DateTime Expires, string? ReturnUrl, int Attempts);

    private static readonly ConcurrentDictionary<string, Pending> _pending = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const int MaxAttempts = 5;

    private readonly EmailService _email;
    private readonly SmsService _sms;
    private readonly AppDbContext _db;
    private readonly ILogger<OtpService> _logger;

    public OtpService(EmailService email, SmsService sms, AppDbContext db, ILogger<OtpService> logger)
    { _email = email; _sms = sms; _db = db; _logger = logger; }

    private static byte[] Hash(string code) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("vMon.otp." + code));

    /// <summary>Kod üretip saklar; (token, code) döner. Kod ayrıca SendAsync ile gönderilir.</summary>
    public (string token, string code) Create(AppUser user, bool isAdmin, string displayName, string? returnUrl)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var token = Guid.NewGuid().ToString("N");
        _pending[token] = new Pending(user.Sam, user.IsLocal, isAdmin, displayName, Hash(code), DateTime.UtcNow.Add(Ttl), returnUrl, 0);
        foreach (var kv in _pending) if (DateTime.UtcNow > kv.Value.Expires) _pending.TryRemove(kv.Key, out _);
        return (token, code);
    }

    public Pending? Peek(string? token) => (!string.IsNullOrEmpty(token) && _pending.TryGetValue(token, out var p)) ? p : null;

    public (bool ok, Pending? pending, string? error) Verify(string? token, string? code)
    {
        if (string.IsNullOrWhiteSpace(token) || !_pending.TryGetValue(token, out var p))
            return (false, null, "Oturum bulunamadı. Lütfen tekrar giriş yapın.");
        if (DateTime.UtcNow > p.Expires) { _pending.TryRemove(token, out _); return (false, null, "Kodun süresi doldu. Tekrar giriş yapın."); }
        if (p.Attempts >= MaxAttempts) { _pending.TryRemove(token, out _); return (false, null, "Çok fazla hatalı deneme. Tekrar giriş yapın."); }
        if (!CryptographicOperations.FixedTimeEquals(Hash(code ?? ""), p.CodeHash))
        {
            _pending[token] = p with { Attempts = p.Attempts + 1 };
            return (false, null, "Kod hatalı.");
        }
        _pending.TryRemove(token, out _);
        return (true, p, null);
    }

    /// <summary>Kodu seçili kanaldan kullanıcının iletişim bilgisine gönderir.</summary>
    public async Task<(bool ok, string error)> SendAsync(MonitorSettings settings, AppUser user, string code, CancellationToken ct = default)
    {
        var channel = (settings.OtpChannel ?? "Email").Trim();
        var text = $"vMon giris dogrulama kodunuz: {code} (5 dk gecerli)";
        try
        {
            if (channel.Equals("Email", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(user.Email)) return (false, "E-posta adresiniz tanımlı değil.");
                await _email.SendToAsync(settings, user.Email!, "vMon doğrulama kodu",
                    $"<p>vMon giriş doğrulama kodunuz:</p><h2 style='letter-spacing:3px'>{code}</h2><p>Kod 5 dakika geçerlidir.</p>", ct);
                return (true, "");
            }
            if (string.IsNullOrWhiteSpace(user.Phone)) return (false, "Telefon numaranız tanımlı değil.");
            var kind = channel.Equals("Whatsapp", StringComparison.OrdinalIgnoreCase) ? "Whatsapp" : "Sms";
            var p = await _db.SmsProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Enabled && x.Kind == kind, ct);
            if (p == null) return (false, $"Aktif {kind} kanalı yok (Ayarlar → Bildirim Kanalları).");
            var (sok, msg) = await _sms.SendViaIntegrationAsync(p, new[] { user.Phone! }, text, ct);
            return sok ? (true, "") : (false, msg);
        }
        catch (Exception ex) { _logger.LogError(ex, "OTP gönderilemedi"); return (false, ex.GetBaseException().Message); }
    }
}
