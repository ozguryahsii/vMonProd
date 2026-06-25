using System.Net;
using DnsClient;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>DNS: hedef DNS sunucusuna doğrudan sorgu atar (OS resolver bypass).
/// Target = DNS sunucu IP/host, Extra = çözülecek test hostname'i.</summary>
public class DnsChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Dns;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(service.Extra)) return "Test edilecek hostname (Extra) tanımlı değil";

        if (!IPAddress.TryParse(service.Target, out var serverIp))
        {
            var resolved = await System.Net.Dns.GetHostAddressesAsync(service.Target, ct);
            if (resolved.Length == 0) return "DNS sunucu adresi çözülemedi";
            serverIp = resolved[0];
        }

        var options = new LookupClientOptions(new IPEndPoint(serverIp, service.Port ?? 53))
        {
            Timeout = TimeSpan.FromSeconds(service.TimeoutSeconds),
            UseCache = false,
            Retries = 0
        };
        var client = new LookupClient(options);
        var result = await client.QueryAsync(service.Extra, QueryType.A, cancellationToken: ct);

        if (result.HasError) return $"DNS hatası: {result.ErrorMessage}";
        if (!result.Answers.Any()) return $"'{service.Extra}' için kayıt dönmedi";
        return null;
    }
}
