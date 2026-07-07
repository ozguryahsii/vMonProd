using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>SSL sertifika izleme (SLLTracker mirası — iç/dış çift kontrol).
/// Target = alan adı (SNI). DIŞ kontrol: Google DoH ile public IP çözülür (iç ağın split-horizon
/// DNS'i bypass edilir — gerçekten DIŞARIDAN görünen sertifika alınır); DoH erişilemezse sistem DNS'e
/// düşülür. İÇ kontrol (Extra = host[:port], opsiyonel): sunucuya doğrudan bağlanılır, SNI yine Target.
/// İki sertifikanın THUMBPRINT'i karşılaştırılır — farklıysa ERROR ("sunucuda değişti ama F5/LB'de eski
/// kaldı" senaryosu). Kalan gün grafiğe yazılır; eşik (ResponseTimeThresholdMs, GÜN, varsayılan 30)
/// altına inince veya süresi dolunca ERROR üretir. Sertifika alınamazsa DOWN.</summary>
public class SslCertificateChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.SslCertificate;
    private const int DefaultWarnDays = 30;

    public sealed record CertInfo(string CommonName, string Issuer, DateTime NotAfter, string Thumbprint, int DaysRemaining, string Via);

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        // Hostname ayıkla (kullanıcı https://... yapıştırmış olabilir)
        var host = (service.Target ?? "").Trim();
        if (host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            host = new Uri(host.Contains("://") ? host : "https://" + host).Host;
        if (string.IsNullOrWhiteSpace(host)) return "Alan adı (Hedef) tanımlı değil";
        var port = service.Port ?? 443;

        // ---- DIŞ kontrol ----
        var publicIp = await ResolvePublicIpAsync(host, ct);
        var ext = await GetCertificateAsync(host, publicIp ?? host, port,
            publicIp != null ? "dış (public DNS)" : "dış (sistem DNS)", ct);
        OverrideResponseValue = ext.DaysRemaining;   // kalan gün grafiğe

        // ---- İÇ kontrol (opsiyonel) ----
        CertInfo? inn = null;
        if (!string.IsNullOrWhiteSpace(service.Extra))
        {
            var (inHost, inPort) = ParseHostPort(service.Extra!, port);
            try { inn = await GetCertificateAsync(host, inHost, inPort, "iç", ct); }
            catch (Exception ex)
            {
                IsThresholdError = true;
                return $"İç kontrol başarısız ({service.Extra}): {ex.GetBaseException().Message} — dış sertifika: {ext.CommonName}, {ext.DaysRemaining} gün";
            }
        }

        // ---- Değerlendirme ----
        if (inn != null && !string.Equals(inn.Thumbprint, ext.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            IsThresholdError = true;
            return $"İç ve dış sertifika FARKLI — iç: bitiş {inn.NotAfter:dd.MM.yyyy} ({inn.DaysRemaining} gün), " +
                   $"dış: bitiş {ext.NotAfter:dd.MM.yyyy} ({ext.DaysRemaining} gün). Sunucuda yenilenen sertifika F5/LB'ye taşınmamış olabilir.";
        }

        if (ext.DaysRemaining < 0)
        {
            IsThresholdError = true;
            return $"Sertifikanın SÜRESİ DOLMUŞ ({ext.NotAfter:dd.MM.yyyy}) — {ext.CommonName}";
        }

        var warnDays = service.ResponseTimeThresholdMs is > 0 ? service.ResponseTimeThresholdMs.Value : DefaultWarnDays;
        if (ext.DaysRemaining <= warnDays)
        {
            IsThresholdError = true;
            return $"Sertifika {ext.DaysRemaining} gün içinde bitiyor ({ext.NotAfter:dd.MM.yyyy}, eşik {warnDays} gün) — {ext.CommonName}";
        }

        return null;
    }

    /// <summary>SNI=sniHost ile connectHost:port'a bağlanıp sunucu sertifikasını okur (zincir doğrulaması
    /// yapılmaz — amaç sertifikanın kendisini incelemek).</summary>
    public static async Task<CertInfo> GetCertificateAsync(string sniHost, string connectHost, int port, string via, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(connectHost, port, ct);
        await using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = sniHost }, ct);

        var remote = ssl.RemoteCertificate ?? throw new InvalidOperationException("Sunucu sertifika döndürmedi");
        using var cert = new X509Certificate2(remote);

        var cn = cert.Subject.Split(',').Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))?[3..] ?? cert.Subject;
        var issuer = cert.Issuer.Split(',').Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))?[3..] ?? cert.Issuer;
        var days = (int)(cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;
        return new CertInfo(cn, issuer, cert.NotAfter, cert.Thumbprint, days, via);
    }

    /// <summary>Google DNS-over-HTTPS ile public A kaydını çözer (split-horizon bypass). Erişilemezse null
    /// (kapalı ağda sistem DNS'e düşülür — sonuç yine üretilir, 'via' etiketi bunu belirtir).</summary>
    public static async Task<string?> ResolvePublicIpAsync(string hostname, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/dns-json");
            var json = await http.GetStringAsync($"https://dns.google/resolve?name={Uri.EscapeDataString(hostname)}&type=A", ct);
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.TryGetProperty("Answer", out var answers))
                foreach (var a in answers.EnumerateArray())
                    if (a.TryGetProperty("type", out var t) && t.GetInt32() == 1
                        && a.TryGetProperty("data", out var ip) && IPAddress.TryParse(ip.GetString(), out _))
                        return ip.GetString();
            return null;
        }
        catch { return null; }
    }

    /// <summary>"host" veya "host:port" biçimini ayrıştırır (port yoksa varsayılan kullanılır).</summary>
    public static (string host, int port) ParseHostPort(string value, int defaultPort)
    {
        var v = value.Trim();
        var i = v.LastIndexOf(':');
        if (i > 0 && int.TryParse(v[(i + 1)..], out var p) && p is > 0 and < 65536)
            return (v[..i], p);
        return (v, defaultPort);
    }
}
