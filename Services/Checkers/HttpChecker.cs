using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>Endpoint URL kontrolü. Target = tam URL.
/// Extra = beklenen durum kodu (boşsa 2xx/3xx kabul edilir).</summary>
public class HttpChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Http;

    protected override async Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        var handler = new HttpClientHandler();
        if (service.IgnoreCertErrors)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        using var client = new HttpClient(handler);
        client.Timeout = Timeout.InfiniteTimeSpan; // timeout'u CheckerBase'in CTS'i yönetiyor

        if (credential != null)
        {
            var raw = $"{PlainUsername(credential)}:{PlainPassword(credential)}";
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw)));
        }

        using var resp = await client.GetAsync(service.Target, HttpCompletionOption.ResponseHeadersRead, ct);
        var code = (int)resp.StatusCode;

        if (int.TryParse(service.Extra, out var expected))
            return code == expected ? null : $"Beklenen {expected}, gelen {code}";

        return code < 400 ? null : $"HTTP {code}";
    }
}
